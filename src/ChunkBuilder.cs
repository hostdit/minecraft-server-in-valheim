using System;

namespace McValheim
{
    public enum ValheimBiome
    {
        none = 0,
        meadows = 1,
        swamp = 2,
        mountain = 4,
        blackForest = 8,
        plains = 16,
        ashLands = 32,
        deepNorth = 64,
        ocean = 256,
        mistlands = 512
    }

    public interface ITerrainSampler
    {
        void sample(int worldX, int worldZ, out int surfaceY, out ValheimBiome biome);
    }

    public struct ChunkPayload
    {
        public int chunkX;
        public int chunkZ;
        public ushort bitmask;
        public byte[] data;
    }

    public static class ChunkBuilder
    {
        public const int seaLevel = 62;
        public const int minSurface = 1;
        public const int maxSurface = 250;
        public const int sectionBytes = 8192 + 2048 + 2048;

        private const int bedrock = 7;
        private const int stone = 1;
        private const int grass = 2;
        private const int dirt = 3;
        private const int water = 9;
        private const int sand = 12;
        private const int gravel = 13;
        private const int snowBlock = 80;
        private const int netherrack = 87;
        private const int packedIce = 174;

        public static ChunkPayload build(int chunkX, int chunkZ, ITerrainSampler sampler)
        {
            var surface = new int[256];
            var biomes = new ValheimBiome[256];
            var highest = 0;

            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                {
                    int y;
                    ValheimBiome biome;
                    sampler.sample(chunkX * 16 + x, chunkZ * 16 + z, out y, out biome);
                    if (y < minSurface) y = minSurface;
                    if (y > maxSurface) y = maxSurface;
                    var i = z * 16 + x;
                    surface[i] = y;
                    biomes[i] = biome;
                    if (y > highest) highest = y;
                }
            }

            var topSection = Math.Max(highest, seaLevel) / 16;
            var sectionCount = topSection + 1;

            ushort bitmask = 0;
            for (var s = 0; s <= topSection; s++) bitmask |= (ushort)(1 << s);

            var data = new byte[sectionCount * sectionBytes + 256];
            var lightBase = sectionCount * 8192;
            var skyBase = lightBase + sectionCount * 2048;

            for (var s = 0; s < sectionCount; s++)
            {
                for (var y = 0; y < 16; y++)
                {
                    var worldY = s * 16 + y;
                    for (var z = 0; z < 16; z++)
                    {
                        for (var x = 0; x < 16; x++)
                        {
                            var column = z * 16 + x;
                            var id = blockAt(worldY, surface[column], biomes[column]);
                            if (id == 0) continue;
                            var index = (s * 4096) + (y * 256) + column;
                            var value = (ushort)(id << 4);
                            data[index * 2] = (byte)(value & 0xFF);
                            data[index * 2 + 1] = (byte)(value >> 8);
                        }
                    }
                }
            }

            for (var i = 0; i < sectionCount * 2048; i++) data[skyBase + i] = 0xFF;

            var biomeBase = data.Length - 256;
            for (var i = 0; i < 256; i++) data[biomeBase + i] = mcBiome(biomes[i]);

            return new ChunkPayload { chunkX = chunkX, chunkZ = chunkZ, bitmask = bitmask, data = data };
        }

        private static int blockAt(int y, int surfaceY, ValheimBiome biome)
        {
            if (y == 0) return bedrock;
            if (y > surfaceY) return y <= seaLevel ? water : 0;

            var submerged = surfaceY < seaLevel;
            var depth = surfaceY - y;

            if (submerged)
            {
                if (depth == 0) return biome == ValheimBiome.ocean ? gravel : sand;
                if (depth < 4) return sand;
                return stone;
            }

            switch (biome)
            {
                case ValheimBiome.mountain:
                    return depth == 0 ? snowBlock : stone;
                case ValheimBiome.deepNorth:
                    if (depth == 0) return snowBlock;
                    return depth < 4 ? packedIce : stone;
                case ValheimBiome.ashLands:
                    return depth < 4 ? netherrack : stone;
                case ValheimBiome.mistlands:
                    return stone;
                case ValheimBiome.ocean:
                    return depth < 4 ? gravel : stone;
                default:
                    if (depth == 0) return grass;
                    return depth < 4 ? dirt : stone;
            }
        }

        private static byte mcBiome(ValheimBiome biome)
        {
            switch (biome)
            {
                case ValheimBiome.meadows: return 1;
                case ValheimBiome.blackForest: return 4;
                case ValheimBiome.swamp: return 6;
                case ValheimBiome.mountain: return 12;
                case ValheimBiome.deepNorth: return 12;
                case ValheimBiome.plains: return 35;
                case ValheimBiome.mistlands: return 29;
                case ValheimBiome.ashLands: return 8;
                case ValheimBiome.ocean: return 0;
                default: return 1;
            }
        }
    }
}
