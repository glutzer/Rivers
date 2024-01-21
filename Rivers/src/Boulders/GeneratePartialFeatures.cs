using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

public class GeneratePartialFeatures : WorldGenPartial
{
    public override double ExecuteOrder() => 0.15;

    public IWorldGenBlockAccessor blockAccessor;

    public List<PartialFeature> features = new();

    // Can't be more than 1 because neighbor chunks are required.
    public override int chunkRange => 1;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;

        if (TerraGenConfig.DoDecorationPass && RiverConfig.Loaded.boulders)
        {
            sapi.Event.InitWorldGenerator(InitWorldGenerator, "standard");
            sapi.Event.ChunkColumnGeneration(ChunkColumnGeneration, EnumWorldGenPass.Vegetation, "standard");
            sapi.Event.GetWorldgenBlockAccessor(OnWorldGenBlockAccessor);
        }

        chunkRand = new LCGRandom(sapi.World.Seed);
    }

    private void OnWorldGenBlockAccessor(IChunkProviderThread chunkProvider)
    {
        blockAccessor = chunkProvider.GetBlockAccessor(true);
    }

    public void InitWorldGenerator()
    {
        // Load config into the system base.
        LoadGlobalConfig(sapi);

        FeatureFallenLog log = new(sapi)
        {
            tries = 1,
            chance = 0.2f
        };

        FeatureRiverBoulder riverBoulder = new(sapi)
        {
            hSize = 5,
            hSizeVariance = 5,
            tries = 5,
            chance = 0.1f,
            noise = new Noise(0, 0.05f, 2)
        };

        FeatureTinyBoulder tinyBoulder = new(sapi)
        {
            hSize = 5,
            hSizeVariance = 5,
            tries = 25,
            chance = 0.1f,
            noise = new Noise(0, 0.05f, 2)
        };

        features.Add(log);
        features.Add(riverBoulder);
        features.Add(tinyBoulder);
    }

    public override void GeneratePartial(IServerChunk[] chunks, int mainChunkX, int mainChunkZ, int generatingChunkX, int generatingChunkZ)
    {
        IMapChunk mapChunk = sapi.WorldManager.GetMapChunk(generatingChunkX, generatingChunkZ);

        ushort[] heightMap = mapChunk.WorldGenTerrainHeightMap;
        ushort[] riverMap = mapChunk.GetModdata<ushort[]>("riverDistance");

        int startX = generatingChunkX * chunkSize;
        int startZ = generatingChunkZ * chunkSize;

        BlockPos pos = new(0, 0, 0);

        foreach (PartialFeature feature in features)
        {
            chunkRand.InitPositionSeed(generatingChunkX, generatingChunkZ);

            for (int x = 0; x < feature.tries; x++)
            {
                if (chunkRand.NextFloat() >= feature.chance) continue;

                int randX = chunkRand.NextInt(chunkSize);
                int randZ = chunkRand.NextInt(chunkSize);

                pos.X = startX + randX;
                pos.Y = heightMap[(randZ * chunkSize) + randX] + 1;
                pos.Z = startZ + randZ;

                if (!feature.CanGenerate(randX, pos.Y, randZ, riverMap[(randZ * 32) + randX])) continue;

                int rockId = mapChunk.TopRockIdMap[(randZ * chunkSize) + randX];

                feature.Generate(pos, chunks, chunkRand, new Vec2d(mainChunkX * chunkSize, mainChunkZ * chunkSize), new Vec2d((mainChunkX * chunkSize) + chunkSize - 1, (mainChunkZ * chunkSize) + chunkSize - 1), blockAccessor, rockId);
            }
        }
    }
}

/*
public class BlockLayerPostfix
{
    [HarmonyPatch(typeof(GenBlockLayers))]
    [HarmonyPatch("OnChunkColumnGeneration")]
    public static class LogHeightPostfix
    {
        [HarmonyPostfix]
        public static void Postfix(IChunkColumnGenerateRequest request)
        {
            IMapChunk chunk = request.Chunks[0].MapChunk;

            ushort[] newMap = new ushort[32 * 32];

            chunk.WorldGenTerrainHeightMap.CopyTo(newMap, 0);

            chunk.SetModdata("shm", newMap);
        }
    }
}
*/