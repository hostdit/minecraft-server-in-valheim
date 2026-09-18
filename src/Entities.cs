using System;
using System.Collections.Generic;

namespace McValheim
{
    public struct MobSnapshot
    {
        public int valheimId;
        public string prefab;
        public bool isPlayer;
        public string playerName;
        public double x;
        public double y;
        public double z;
        public float yaw;
    }

    public static class Clock
    {
        public static int minecraftTime(float dayFraction)
        {
            var ticks = (int)Math.Round(dayFraction * 24000.0) + 18000;
            ticks %= 24000;
            if (ticks < 0) ticks += 24000;
            return ticks;
        }
    }

    public static class Ids
    {
        public static Guid stableUuid(int valheimId)
        {
            return new Guid(valheimId, (short)0x5643, (short)0x4d43, 0, 0, 0, 0, 0, 0, 0, 0);
        }
    }

    public static class MobTypes
    {
        public const byte skeleton = 51;
        public const byte spider = 52;
        public const byte zombie = 54;
        public const byte slime = 55;
        public const byte ghast = 56;
        public const byte zombiePigman = 57;
        public const byte enderman = 58;
        public const byte silverfish = 60;
        public const byte blaze = 61;
        public const byte enderDragon = 63;
        public const byte wither = 64;
        public const byte bat = 65;
        public const byte guardian = 68;
        public const byte pig = 90;
        public const byte cow = 92;
        public const byte squid = 94;
        public const byte wolf = 95;
        public const byte ironGolem = 99;
        public const byte horse = 100;
        public const byte rabbit = 101;
        public const byte villager = 120;

        private static readonly Dictionary<string, byte> table = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            { "Greyling", zombie },
            { "Greydwarf", zombie },
            { "Greydwarf_Elite", zombie },
            { "Greydwarf_Shaman", zombie },
            { "Draugr", zombie },
            { "Draugr_Elite", zombie },
            { "Draugr_Ranged", zombie },
            { "Skeleton", skeleton },
            { "Skeleton_Poison", skeleton },
            { "Goblin", zombiePigman },
            { "GoblinBrute", zombiePigman },
            { "GoblinShaman", zombiePigman },
            { "Blob", slime },
            { "BlobElite", slime },
            { "Bonemass", slime },
            { "Troll", ironGolem },
            { "StoneGolem", ironGolem },
            { "gd_king", ironGolem },
            { "Surtling", blaze },
            { "Ghost", ghast },
            { "Hatchling", ghast },
            { "Gjall", ghast },
            { "Wraith", enderman },
            { "Wolf", wolf },
            { "Wolf_cub", wolf },
            { "Fenring", wolf },
            { "Fenring_Cultist", wolf },
            { "Ulv", wolf },
            { "Boar", pig },
            { "Boar_piggy", pig },
            { "Lox", cow },
            { "Lox_Calf", cow },
            { "Deer", horse },
            { "Eikthyr", horse },
            { "Hare", rabbit },
            { "Neck", silverfish },
            { "Leech", silverfish },
            { "Tick", spider },
            { "Seeker", spider },
            { "SeekerBrood", spider },
            { "SeekerQueen", spider },
            { "Serpent", guardian },
            { "Fish1", squid },
            { "Fish2", squid },
            { "Fish3", squid },
            { "Fish4_cave", squid },
            { "Bat", bat },
            { "Crow", bat },
            { "Seagal", bat },
            { "Dverger", villager },
            { "Dragon", enderDragon },
            { "GoblinKing", wither }
        };

        public static byte forPrefab(string prefab)
        {
            var name = clean(prefab);
            byte type;
            return table.TryGetValue(name, out type) ? type : zombie;
        }

        public static string clean(string prefab)
        {
            if (prefab == null) return "";
            var cut = prefab.IndexOf("(Clone)", StringComparison.Ordinal);
            return cut >= 0 ? prefab.Substring(0, cut) : prefab;
        }
    }
}
