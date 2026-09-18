using System;
using System.Collections.Generic;
using UnityEngine;

namespace McValheim
{
    public class ValheimSampler : ITerrainSampler
    {
        public const int heightOffset = 32;

        private readonly Dictionary<long, int> heightCache = new Dictionary<long, int>();
        private readonly Dictionary<long, ValheimBiome> biomeCache = new Dictionary<long, ValheimBiome>();

        public void sample(int worldX, int worldZ, out int surfaceY, out ValheimBiome biome)
        {
            var key = ((long)worldX << 32) ^ (uint)worldZ;

            if (!heightCache.TryGetValue(key, out surfaceY))
            {
                var height = WorldGenerator.instance.GetHeight(worldX, worldZ);
                surfaceY = Mathf.RoundToInt(height) + heightOffset;
                heightCache[key] = surfaceY;
            }

            if (!biomeCache.TryGetValue(key, out biome))
            {
                biome = translate(WorldGenerator.instance.GetBiome(worldX, worldZ));
                biomeCache[key] = biome;
            }
        }

        public void clearCache()
        {
            heightCache.Clear();
            biomeCache.Clear();
        }

        public static ValheimBiome translate(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.Meadows: return ValheimBiome.meadows;
                case Heightmap.Biome.Swamp: return ValheimBiome.swamp;
                case Heightmap.Biome.Mountain: return ValheimBiome.mountain;
                case Heightmap.Biome.BlackForest: return ValheimBiome.blackForest;
                case Heightmap.Biome.Plains: return ValheimBiome.plains;
                case Heightmap.Biome.AshLands: return ValheimBiome.ashLands;
                case Heightmap.Biome.DeepNorth: return ValheimBiome.deepNorth;
                case Heightmap.Biome.Ocean: return ValheimBiome.ocean;
                case Heightmap.Biome.Mistlands: return ValheimBiome.mistlands;
                default: return ValheimBiome.none;
            }
        }
    }

    public class Telemetry
    {
        public bool inWorld;
        public string worldName = "";
        public int day;
        public string biome = "";
        public int health;
        public int maxHealth;
        public int spawnX;
        public int spawnZ;
        public int timeOfDay;
        public bool raining;
        public List<MobSnapshot> mobs = new List<MobSnapshot>();

        public static Telemetry capture()
        {
            var net = ZNet.instance;
            var player = Player.m_localPlayer;
            var env = EnvMan.instance;
            if (net == null || player == null || env == null) return new Telemetry { inWorld = false };

            var pos = player.transform.position;
            var current = env.GetCurrentEnvironment();
            return new Telemetry
            {
                inWorld = true,
                worldName = net.GetWorldName(),
                day = env.GetDay(),
                biome = player.GetCurrentBiome().ToString(),
                health = Mathf.RoundToInt(player.GetHealth()),
                maxHealth = Mathf.RoundToInt(player.GetMaxHealth()),
                spawnX = Mathf.RoundToInt(pos.x),
                spawnZ = Mathf.RoundToInt(pos.z),
                timeOfDay = Clock.minecraftTime(env.GetDayFraction()),
                raining = current != null && current.m_isWet,
                mobs = captureMobs()
            };
        }

        private static List<MobSnapshot> captureMobs()
        {
            var list = new List<MobSnapshot>();
            foreach (var character in Character.GetAllCharacters())
            {
                if (character == null) continue;

                var transform = character.transform;
                var asPlayer = character as Player;

                list.Add(new MobSnapshot
                {
                    valheimId = character.GetInstanceID(),
                    prefab = character.gameObject.name,
                    isPlayer = asPlayer != null,
                    playerName = asPlayer != null ? asPlayer.GetPlayerName() : "",
                    x = transform.position.x,
                    y = transform.position.y + ValheimSampler.heightOffset + 1,
                    z = transform.position.z,
                    yaw = transform.rotation.eulerAngles.y
                });
            }
            return list;
        }

        public string describe()
        {
            if (!inWorld) return "Valheim is at the main menu";
            var weather = raining ? "  raining" : "";
            return worldName + "  Day " + day + "  " + biome + "  " + health + "/" + maxHealth + " hp" + weather;
        }
    }
}
