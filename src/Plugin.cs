using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace McValheim
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim.app")]
    [BepInProcess("valheim_server.exe")]
    [BepInProcess("valheim_server.x86_64")]
    public class Plugin : BaseUnityPlugin
    {
        public const string pluginGuid = "it.hostd.mcvalheim";
        public const string pluginName = "McValheim";
        public const string pluginVersion = "0.1.0";

        public static Plugin instance;

        private ConfigEntry<int> port;
        private ConfigEntry<bool> bridgeChat;
        private Server server;
        private ValheimSampler sampler;
        private Harmony harmony;
        private bool started;

        private void Awake()
        {
            instance = this;
            port = Config.Bind("server", "port", 25565, "TCP port the Minecraft server listens on");
            bridgeChat = Config.Bind("server", "bridgeChat", true, "Mirror Valheim chat in to Minecraft and back");

            sampler = new ValheimSampler();
            server = new Server(sampler, message => Logger.LogInfo(message));

            harmony = new Harmony(pluginGuid);
            harmony.PatchAll(typeof(ChatPatch));
        }

        private void Update()
        {
            var snapshot = Telemetry.capture();

            if (!started && snapshot.inWorld)
            {
                server.start(port.Value);
                started = true;
            }

            if (started && !snapshot.inWorld)
            {
                server.stop();
                sampler.clearCache();
                started = false;
                return;
            }

            if (!started) return;

            server.tick(snapshot);

            string line;
            while (server.chatToValheim.TryDequeue(out line))
            {
                if (Chat.instance == null) continue;
                Chat.instance.AddString("[minecraft] " + line);
            }
        }

        private void OnDestroy()
        {
            if (started) server.stop();
            harmony.UnpatchSelf();
        }

        public void onValheimChat(string user, string text)
        {
            if (!started || !bridgeChat.Value) return;
            if (user.StartsWith("[minecraft]")) return;
            server.chatToMinecraft.Enqueue("<" + user + "> " + text);
        }
    }

    [HarmonyPatch(typeof(Chat), "OnNewChatMessage")]
    public static class ChatPatch
    {
        private static void Postfix(UserInfo sender, string text)
        {
            if (Plugin.instance == null || sender == null) return;
            Plugin.instance.onValheimChat(sender.GetDisplayName(), text);
        }
    }
}
