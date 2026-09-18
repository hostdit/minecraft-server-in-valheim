using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace McValheim
{
    public enum Stage
    {
        handshake,
        status,
        login,
        awaitingSpawn,
        play
    }

    public class Session
    {
        public readonly TcpClient client;
        public readonly NetworkStream stream;
        public readonly PacketStream packets = new PacketStream();
        public readonly object sendLock = new object();
        public readonly HashSet<long> loaded = new HashSet<long>();
        public readonly HashSet<int> knownEntities = new HashSet<int>();

        public Stage stage = Stage.handshake;
        public string name = "";
        public string uuid = "";
        public int keepAliveToken;
        public DateTime lastKeepAlive = DateTime.UtcNow;
        public DateTime lastTimeSync = DateTime.MinValue;
        public int lastHealth = -1;
        public int lastRaining = -1;

        private int centerXValue;
        private int centerZValue;

        public int centerX => Volatile.Read(ref centerXValue);
        public int centerZ => Volatile.Read(ref centerZValue);

        public Session(TcpClient client)
        {
            this.client = client;
            stream = client.GetStream();
        }

        public void setCenter(int chunkX, int chunkZ)
        {
            Volatile.Write(ref centerXValue, chunkX);
            Volatile.Write(ref centerZValue, chunkZ);
        }

        public void send(PacketWriter writer)
        {
            var bytes = writer.frame();
            lock (sendLock)
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
        }

        public void close()
        {
            try { client.Close(); } catch (Exception) { }
        }
    }

    public class Server
    {
        public const int protocolVersion = 47;
        public const int maxPlayers = 10;
        public const int viewRadius = 5;
        public const int spawnRadius = 2;
        public const int chunkBudgetPerTick = 4;
        public const int keepAliveSeconds = 15;
        public const int entitySyncMillis = 100;
        public const int timeSyncSeconds = 2;
        public const int firstEntityId = 100;

        private readonly Action<string> log;
        private readonly ITerrainSampler sampler;
        private readonly List<Session> sessions = new List<Session>();
        private readonly object sessionLock = new object();

        public readonly ConcurrentQueue<string> chatToValheim = new ConcurrentQueue<string>();
        public readonly ConcurrentQueue<string> chatToMinecraft = new ConcurrentQueue<string>();

        private TcpListener listener;
        private Thread acceptThread;
        private volatile bool running;
        private Telemetry telemetry = new Telemetry { inWorld = false };
        private readonly Dictionary<int, int> entityIds = new Dictionary<int, int>();
        private readonly HashSet<int> playerIds = new HashSet<int>();
        private int nextEntityId = firstEntityId;
        private DateTime lastEntitySync = DateTime.MinValue;
        private static readonly string favicon = loadFavicon();

        public Server(ITerrainSampler sampler, Action<string> log)
        {
            this.sampler = sampler;
            this.log = log;
        }

        public int playerCount
        {
            get { lock (sessionLock) { return countPlaying(); } }
        }

        public void start(int port)
        {
            if (running) throw new InvalidOperationException("server already running");

            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            running = true;

            acceptThread = new Thread(acceptLoop) { IsBackground = true, Name = "mc-accept" };
            acceptThread.Start();

            log("listening on " + port + " in pid " + System.Diagnostics.Process.GetCurrentProcess().Id);
        }

        public void stop()
        {
            if (!running) return;
            running = false;

            lock (sessionLock)
            {
                foreach (var session in sessions)
                {
                    if (session.stage == Stage.play) sendDisconnect(session, "Valheim closed the world");
                    session.close();
                }
                sessions.Clear();
            }

            entityIds.Clear();
            playerIds.Clear();
            nextEntityId = firstEntityId;

            listener.Stop();
            log("stopped");
        }

        public void tick(Telemetry snapshot)
        {
            telemetry = snapshot;

            List<Session> current;
            lock (sessionLock) current = new List<Session>(sessions);

            var budget = chunkBudgetPerTick;
            var now = DateTime.UtcNow;

            foreach (var session in current)
            {
                if (session.stage == Stage.awaitingSpawn)
                {
                    budget = streamChunks(session, spawnRadius, budget);
                    if (loadedEnough(session, spawnRadius)) finishJoin(session);
                    continue;
                }

                if (session.stage != Stage.play) continue;

                budget = streamChunks(session, viewRadius, budget);
                syncVitals(session, snapshot, now);

                if ((now - session.lastKeepAlive).TotalSeconds >= keepAliveSeconds)
                {
                    session.lastKeepAlive = now;
                    session.keepAliveToken++;
                    var keepAlive = new PacketWriter(0x00);
                    keepAlive.writeVarInt(session.keepAliveToken);
                    trySend(session, keepAlive);
                }
            }

            if ((now - lastEntitySync).TotalMilliseconds >= entitySyncMillis)
            {
                lastEntitySync = now;
                syncEntities(current, snapshot);
            }

            string line;
            while (chatToMinecraft.TryDequeue(out line)) broadcast(line);
        }

        public void broadcast(string text)
        {
            List<Session> current;
            lock (sessionLock) current = new List<Session>(sessions);

            foreach (var session in current)
            {
                if (session.stage != Stage.play) continue;
                var chat = new PacketWriter(0x02);
                chat.writeString("{\"text\":\"" + escape(text) + "\"}");
                chat.writeByte(0);
                trySend(session, chat);
            }
        }

        private void acceptLoop()
        {
            while (running)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (SocketException) { return; }
                catch (ObjectDisposedException) { return; }

                var session = new Session(client);
                lock (sessionLock) sessions.Add(session);

                var thread = new Thread(() => sessionLoop(session)) { IsBackground = true, Name = "mc-session" };
                thread.Start();
            }
        }

        private void sessionLoop(Session session)
        {
            var buffer = new byte[4096];
            try
            {
                while (running)
                {
                    var read = session.stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;

                    session.packets.feed(buffer, read);

                    byte[] body;
                    while ((body = session.packets.next()) != null) handle(session, new PacketReader(body));
                }
            }
            catch (ProtocolException error)
            {
                log("protocol error from " + describe(session) + ": " + error.Message);
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                var wasPlaying = session.stage == Stage.play;
                lock (sessionLock) sessions.Remove(session);
                session.close();
                if (wasPlaying) log(session.name + " left");
            }
        }

        private void handle(Session session, PacketReader reader)
        {
            switch (session.stage)
            {
                case Stage.handshake:
                    handleHandshake(session, reader);
                    return;
                case Stage.status:
                    handleStatus(session, reader);
                    return;
                case Stage.login:
                    handleLogin(session, reader);
                    return;
                default:
                    handlePlay(session, reader);
                    return;
            }
        }

        private void handleHandshake(Session session, PacketReader reader)
        {
            if (reader.packetId != 0x00) throw new ProtocolException("expected handshake, got " + reader.packetId);

            reader.readVarInt();
            reader.readString();
            reader.readUShort();
            var next = reader.readVarInt();

            if (next == 1) session.stage = Stage.status;
            else if (next == 2) session.stage = Stage.login;
            else throw new ProtocolException("unknown next state " + next);
        }

        private void handleStatus(Session session, PacketReader reader)
        {
            if (reader.packetId == 0x00)
            {
                var response = new PacketWriter(0x00);
                response.writeString(statusJson());
                session.send(response);
                return;
            }

            if (reader.packetId == 0x01)
            {
                var token = reader.readLong();
                var pong = new PacketWriter(0x01);
                pong.writeLong(token);
                session.send(pong);
                return;
            }

            throw new ProtocolException("unknown status packet " + reader.packetId);
        }

        private void handleLogin(Session session, PacketReader reader)
        {
            if (reader.packetId != 0x00) throw new ProtocolException("expected login start, got " + reader.packetId);

            session.name = reader.readString();
            session.uuid = Guid.NewGuid().ToString();

            var success = new PacketWriter(0x02);
            success.writeString(session.uuid);
            success.writeString(session.name);
            session.send(success);

            var snapshot = telemetry;
            if (!snapshot.inWorld)
            {
                sendDisconnect(session, "Load a Valheim world first");
                session.close();
                return;
            }

            var join = new PacketWriter(0x01);
            join.writeInt(1);
            join.writeByte(0);
            join.writeByte(0);
            join.writeByte(0);
            join.writeByte(maxPlayers);
            join.writeString("flat");
            join.writeBool(false);
            session.send(join);

            var spawn = new PacketWriter(0x05);
            spawn.writePosition(snapshot.spawnX, ChunkBuilder.seaLevel + 10, snapshot.spawnZ);
            session.send(spawn);

            var abilities = new PacketWriter(0x39);
            abilities.writeByte(0x05);
            abilities.writeFloat(0.05f);
            abilities.writeFloat(0.1f);
            session.send(abilities);

            session.setCenter(floorDiv(snapshot.spawnX, 16), floorDiv(snapshot.spawnZ, 16));
            session.stage = Stage.awaitingSpawn;

            log(session.name + " joined at " + snapshot.spawnX + ", " + snapshot.spawnZ);
        }

        private void handlePlay(Session session, PacketReader reader)
        {
            if (reader.packetId == 0x01)
            {
                var text = reader.readString();
                chatToValheim.Enqueue(session.name + ": " + text);
                broadcast("<" + session.name + "> " + text);
                return;
            }

            if (reader.packetId == 0x04 || reader.packetId == 0x06)
            {
                var x = reader.readDouble();
                reader.readDouble();
                var z = reader.readDouble();
                session.setCenter(floorDiv((int)Math.Floor(x), 16), floorDiv((int)Math.Floor(z), 16));
            }
        }

        private int streamChunks(Session session, int radius, int budget)
        {
            if (budget <= 0) return 0;

            var cx = session.centerX;
            var cz = session.centerZ;

            for (var ring = 0; ring <= radius && budget > 0; ring++)
            {
                for (var dx = -ring; dx <= ring && budget > 0; dx++)
                {
                    for (var dz = -ring; dz <= ring && budget > 0; dz++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring) continue;

                        var chunkX = cx + dx;
                        var chunkZ = cz + dz;
                        var key = ((long)chunkX << 32) ^ (uint)chunkZ;
                        if (session.loaded.Contains(key)) continue;

                        var payload = ChunkBuilder.build(chunkX, chunkZ, sampler);
                        var packet = new PacketWriter(0x21);
                        packet.writeInt(payload.chunkX);
                        packet.writeInt(payload.chunkZ);
                        packet.writeBool(true);
                        packet.writeUShort(payload.bitmask);
                        packet.writeVarInt(payload.data.Length);
                        packet.writeBytes(payload.data);

                        if (!trySend(session, packet)) return 0;

                        session.loaded.Add(key);
                        budget--;
                    }
                }
            }

            return budget;
        }

        private void syncEntities(List<Session> current, Telemetry snapshot)
        {
            var live = new HashSet<int>();
            foreach (var mob in snapshot.mobs) live.Add(mob.valheimId);

            var gone = new List<int>();
            foreach (var pair in entityIds) if (!live.Contains(pair.Key)) gone.Add(pair.Key);

            if (gone.Count > 0)
            {
                var removed = new List<int>();
                var departed = new List<Guid>();
                foreach (var valheimId in gone)
                {
                    removed.Add(entityIds[valheimId]);
                    entityIds.Remove(valheimId);
                    if (playerIds.Remove(valheimId)) departed.Add(Ids.stableUuid(valheimId));
                }

                var destroy = new PacketWriter(0x13);
                destroy.writeVarInt(removed.Count);
                foreach (var entityId in removed) destroy.writeVarInt(entityId);

                PacketWriter unlist = null;
                if (departed.Count > 0)
                {
                    unlist = new PacketWriter(0x38);
                    unlist.writeVarInt(4);
                    unlist.writeVarInt(departed.Count);
                    foreach (var id in departed) unlist.writeUuid(id);
                }

                foreach (var session in current)
                {
                    if (session.stage != Stage.play) continue;
                    foreach (var entityId in removed) session.knownEntities.Remove(entityId);
                    if (!trySend(session, destroy)) continue;
                    if (unlist != null) trySend(session, unlist);
                }
            }

            foreach (var mob in snapshot.mobs)
            {
                int entityId;
                if (!entityIds.TryGetValue(mob.valheimId, out entityId))
                {
                    entityId = nextEntityId++;
                    entityIds[mob.valheimId] = entityId;
                    if (mob.isPlayer) playerIds.Add(mob.valheimId);
                }

                foreach (var session in current)
                {
                    if (session.stage != Stage.play) continue;

                    if (session.knownEntities.Add(entityId))
                    {
                        if (mob.isPlayer) spawnPlayer(session, entityId, mob);
                        else spawnMob(session, entityId, mob);
                        continue;
                    }

                    var move = new PacketWriter(0x18);
                    move.writeVarInt(entityId);
                    move.writeFixedPoint(mob.x);
                    move.writeFixedPoint(mob.y);
                    move.writeFixedPoint(mob.z);
                    move.writeAngle(mob.yaw);
                    move.writeAngle(0f);
                    move.writeBool(true);
                    if (!trySend(session, move)) continue;

                    var head = new PacketWriter(0x19);
                    head.writeVarInt(entityId);
                    head.writeAngle(mob.yaw);
                    trySend(session, head);
                }
            }
        }

        private void spawnMob(Session session, int entityId, MobSnapshot mob)
        {
            var packet = new PacketWriter(0x0F);
            packet.writeVarInt(entityId);
            packet.writeByte(MobTypes.forPrefab(mob.prefab));
            packet.writeFixedPoint(mob.x);
            packet.writeFixedPoint(mob.y);
            packet.writeFixedPoint(mob.z);
            packet.writeAngle(mob.yaw);
            packet.writeAngle(0f);
            packet.writeAngle(mob.yaw);
            packet.writeShort(0);
            packet.writeShort(0);
            packet.writeShort(0);
            packet.writeByte(0x7F);
            trySend(session, packet);
        }

        private void spawnPlayer(Session session, int entityId, MobSnapshot mob)
        {
            var id = Ids.stableUuid(mob.valheimId);
            var label = mob.playerName.Length > 0 ? mob.playerName : "Viking";
            if (label.Length > 16) label = label.Substring(0, 16);

            var listItem = new PacketWriter(0x38);
            listItem.writeVarInt(0);
            listItem.writeVarInt(1);
            listItem.writeUuid(id);
            listItem.writeString(label);
            listItem.writeVarInt(0);
            listItem.writeVarInt(0);
            listItem.writeVarInt(0);
            listItem.writeBool(false);
            if (!trySend(session, listItem)) return;

            var packet = new PacketWriter(0x0C);
            packet.writeVarInt(entityId);
            packet.writeUuid(id);
            packet.writeFixedPoint(mob.x);
            packet.writeFixedPoint(mob.y);
            packet.writeFixedPoint(mob.z);
            packet.writeAngle(mob.yaw);
            packet.writeAngle(0f);
            packet.writeShort(0);
            packet.writeByte(0x7F);
            trySend(session, packet);
        }

        private void syncVitals(Session session, Telemetry snapshot, DateTime now)
        {
            var hearts = snapshot.maxHealth > 0 ? (int)Math.Round(20.0 * snapshot.health / snapshot.maxHealth) : 20;
            hearts = Math.Max(1, hearts);
            if (hearts != session.lastHealth)
            {
                session.lastHealth = hearts;
                var health = new PacketWriter(0x06);
                health.writeFloat(hearts);
                health.writeVarInt(20);
                health.writeFloat(5f);
                trySend(session, health);
            }

            var raining = snapshot.raining ? 1 : 0;
            if (raining != session.lastRaining)
            {
                session.lastRaining = raining;
                var weather = new PacketWriter(0x2B);
                weather.writeByte((byte)(snapshot.raining ? 2 : 1));
                weather.writeFloat(0f);
                trySend(session, weather);
            }

            if ((now - session.lastTimeSync).TotalSeconds >= timeSyncSeconds)
            {
                session.lastTimeSync = now;
                var time = new PacketWriter(0x03);
                time.writeLong(snapshot.day * 24000L + snapshot.timeOfDay);
                time.writeLong(snapshot.timeOfDay);
                trySend(session, time);
            }
        }

        private bool loadedEnough(Session session, int radius)
        {
            var side = radius * 2 + 1;
            return session.loaded.Count >= side * side;
        }

        private void finishJoin(Session session)
        {
            var snapshot = telemetry;
            int surfaceY;
            ValheimBiome biome;
            sampler.sample(snapshot.spawnX, snapshot.spawnZ, out surfaceY, out biome);

            var look = new PacketWriter(0x08);
            look.writeDouble(snapshot.spawnX + 0.5);
            look.writeDouble(surfaceY + 2);
            look.writeDouble(snapshot.spawnZ + 0.5);
            look.writeFloat(0f);
            look.writeFloat(0f);
            look.writeByte(0);

            if (!trySend(session, look)) return;

            session.stage = Stage.play;
            session.lastKeepAlive = DateTime.UtcNow;
            broadcast(session.name + " joined the world");
        }

        private void sendDisconnect(Session session, string reason)
        {
            var packet = new PacketWriter(0x40);
            packet.writeString("{\"text\":\"" + escape(reason) + "\"}");
            trySend(session, packet);
        }

        private bool trySend(Session session, PacketWriter writer)
        {
            try
            {
                session.send(writer);
                return true;
            }
            catch (IOException)
            {
                session.close();
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        private string statusJson()
        {
            var snapshot = telemetry;
            var builder = new StringBuilder();
            builder.Append("{\"version\":{\"name\":\"Valheim\",\"protocol\":").Append(protocolVersion).Append("},");
            builder.Append("\"players\":{\"max\":").Append(maxPlayers).Append(",\"online\":").Append(playerCount).Append(",\"sample\":[]},");
            builder.Append("\"description\":{\"text\":\"").Append(escape(snapshot.describe())).Append("\"}");
            if (favicon != null) builder.Append(",\"favicon\":\"").Append(favicon).Append("\"");
            builder.Append("}");
            return builder.ToString();
        }

        private static string loadFavicon()
        {
            using (var stream = typeof(Server).Assembly.GetManifestResourceStream("McValheim.favicon.png"))
            {
                if (stream == null) return null;
                var memory = new MemoryStream();
                stream.CopyTo(memory);
                return "data:image/png;base64," + Convert.ToBase64String(memory.ToArray());
            }
        }

        private int countPlaying()
        {
            var count = 0;
            foreach (var session in sessions) if (session.stage == Stage.play) count++;
            return count;
        }

        private static string describe(Session session)
        {
            return session.name.Length > 0 ? session.name : session.client.Client.RemoteEndPoint.ToString();
        }

        private static int floorDiv(int value, int divisor)
        {
            var quotient = value / divisor;
            if (value % divisor != 0 && ((value < 0) != (divisor < 0))) quotient--;
            return quotient;
        }

        private static string escape(string text)
        {
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
        }
    }
}
