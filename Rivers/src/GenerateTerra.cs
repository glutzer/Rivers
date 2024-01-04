using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.ServerMods.NoObf;

namespace Vintagestory.ServerMods
{
    public class GenerateTerra : ModStdWorldGen
    {
        public RiverGenerator riverGenerator;
        public int chunksInPlate;
        public int chunksInZone;
        public int aboveSeaLevel;
        public int baseSeaLevel;
        public int heightBoost;
        public float topFactor;
        public RiverSample[,] samples = new RiverSample[32, 32];

        public int chunksize = 32;

        public ICoreServerAPI sapi;

        public const double terrainDistortionMultiplier = 4.0;
        public const double terrainDistortionThreshold = 40.0;
        public const double geoDistortionMultiplier = 10.0;
        public const double geoDistortionThreshold = 10.0;
        public const double maxDistortionAmount = (55 + 40 + 30 + 10) * SimplexNoiseOctave.MAX_VALUE_2D_WARP;

        public int maxThreads;

        public LandformsWorldProperty landforms;
        public Dictionary<int, LerpedWeightedIndex2DMap> landformMapByRegion = new(10);
        public int regionMapSize;
        public float noiseScale;
        public int terrainGenOctaves = 9;

        public NewNormalizedSimplexFractalNoise terrainNoise;
        public SimplexNoise distort2dx;
        public SimplexNoise distort2dz;
        public NormalizedSimplexNoise geoUpheavalNoise;
        public WeightedTaper[] taperMap;

        public Noise valleyNoise = new(0, 0.0008f, 2);
        public Noise floorNoise = new(0, 0.0008f, 1);
        public double maxValleyWidth;

        public int riverFloorBase;
        public double riverFloorVariation;

        public struct ThreadLocalTempData
        {
            public double[] LerpedAmplitudes;
            public double[] LerpedThresholds;
        }
        public ThreadLocal<ThreadLocalTempData> tempDataThreadLocal;

        public struct WeightedTaper
        {
            public float terrainYPos;
            public float weight;
        }

        public struct ColumnResult
        {
            public BitArray columnBlockSolidities;
            public int waterBlockId;
        }
        public ColumnResult[] columnResults;
        public int[] borderIndicesByCardinal;

        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }

        public override double ExecuteOrder()
        {
            return 0;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            api.Event.ServerRunPhase(EnumServerRunPhase.ModsAndConfigReady, LoadGamePre);
            api.Event.InitWorldGenerator(InitWorldGen, "standard");
            api.Event.ChunkColumnGeneration(OnChunkColumnGen, EnumWorldGenPass.Terrain, "standard");
        }

        private void LoadGamePre()
        {
            if (sapi.WorldManager.SaveGame.WorldType != "standard") return;

            TerraGenConfig.seaLevel = (int)(0.4313725490196078 * sapi.WorldManager.MapSizeY);
            sapi.WorldManager.SetSeaLevel(TerraGenConfig.seaLevel);
        }

        Type landType;

        public void InitWorldGen()
        {
            aboveSeaLevel = sapi.WorldManager.MapSizeY - TerraGenConfig.seaLevel;
            baseSeaLevel = TerraGenConfig.seaLevel;

            chunksInPlate = RiverConfig.Loaded.zonesInPlate * RiverConfig.Loaded.zoneSize / 32;
            chunksInZone = RiverConfig.Loaded.zoneSize / 32;

            heightBoost = RiverConfig.Loaded.heightBoost;
            topFactor = RiverConfig.Loaded.topFactor;

            maxValleyWidth = RiverConfig.Loaded.maxValleyWidth;

            riverFloorBase = RiverConfig.Loaded.riverFloorBase + baseSeaLevel - 1;
            riverFloorVariation = RiverConfig.Loaded.riverFloorVariation;

            riverGenerator = new RiverGenerator();

            Type[] types = AccessTools.GetTypesFromAssembly(Assembly.GetAssembly(typeof(NoiseBase)));
            foreach (Type type in types)
            {
                if (type.Name == "NoiseLandforms")
                {
                    landType = type;
                    break;
                }
            }
            if (landType == null) throw new Exception("NoiseLandforms type not found.");

            LoadGlobalConfig(sapi);
            landformMapByRegion.Clear();

            maxThreads = Math.Min(Environment.ProcessorCount, sapi.Server.Config.HostedMode ? 4 : 10);

            regionMapSize = (int)Math.Ceiling((double)sapi.WorldManager.MapSizeX / sapi.WorldManager.RegionSize);
            noiseScale = Math.Max(1, sapi.WorldManager.MapSizeY / 256f);
            terrainGenOctaves = TerraGenConfig.GetTerrainOctaveCount(sapi.WorldManager.MapSizeY);

            terrainNoise = NewNormalizedSimplexFractalNoise.FromDefaultOctaves(
                terrainGenOctaves, 0.0005 * NewSimplexNoiseLayer.OldToNewFrequency / noiseScale, 0.9, sapi.WorldManager.Seed
            );

            distort2dx = new SimplexNoise(
                new double[] { 55, 40, 30, 10 },
                ScaleAdjustedFreqs(new double[] { 1 / 5.0, 1 / 2.50, 1 / 1.250, 1 / 0.65 }, noiseScale),
                sapi.World.Seed + 9876 + 0
            );

            distort2dz = new SimplexNoise(
                new double[] { 55, 40, 30, 10 },
                ScaleAdjustedFreqs(new double[] { 1 / 5.0, 1 / 2.50, 1 / 1.250, 1 / 0.65 }, noiseScale),
                sapi.World.Seed + 9876 + 2
            );

            geoUpheavalNoise = new NormalizedSimplexNoise(
                new double[] { 55, 40, 30, 15, 7, 4 },
                ScaleAdjustedFreqs(new double[] {
                    1.0 / 5.5,
                    1.1 / 2.75,
                    1.2 / 1.375,
                    1.2 / 0.715,
                    1.2 / 0.45,
                    1.2 / 0.25
                }, noiseScale),
                sapi.World.Seed + 9876 + 1
            );

            tempDataThreadLocal = new ThreadLocal<ThreadLocalTempData>(() => new ThreadLocalTempData
            {
                LerpedAmplitudes = new double[terrainGenOctaves],
                LerpedThresholds = new double[terrainGenOctaves]
            });
            columnResults = new ColumnResult[chunksize * chunksize];
            taperMap = new WeightedTaper[chunksize * chunksize];
            for (int i = 0; i < chunksize * chunksize; i++) columnResults[i].columnBlockSolidities = new BitArray(sapi.WorldManager.MapSizeY);

            borderIndicesByCardinal = new int[8];
            borderIndicesByCardinal[Cardinal.NorthEast.Index] = (chunksize - 1) * chunksize + 0;
            borderIndicesByCardinal[Cardinal.SouthEast.Index] = 0 + 0;
            borderIndicesByCardinal[Cardinal.SouthWest.Index] = 0 + chunksize - 1;
            borderIndicesByCardinal[Cardinal.NorthWest.Index] = (chunksize - 1) * chunksize + chunksize - 1;
        }

        public static double[] ScaleAdjustedFreqs(double[] vs, float horizontalScale)
        {
            for (int i = 0; i < vs.Length; i++)
            {
                vs[i] /= horizontalScale;
            }

            return vs;
        }

        public void OnChunkColumnGen(IChunkColumnGenerateRequest request)
        {
            if (request.RequiresChunkBorderSmoothing)
            {
                ushort[][] neibHeightMaps = request.NeighbourTerrainHeight;

                if (neibHeightMaps[Cardinal.North.Index] != null)
                {
                    neibHeightMaps[Cardinal.NorthEast.Index] = null;
                    neibHeightMaps[Cardinal.NorthWest.Index] = null;
                }
                if (neibHeightMaps[Cardinal.East.Index] != null)
                {
                    neibHeightMaps[Cardinal.NorthEast.Index] = null;
                    neibHeightMaps[Cardinal.SouthEast.Index] = null;
                }
                if (neibHeightMaps[Cardinal.South.Index] != null)
                {
                    neibHeightMaps[Cardinal.SouthWest.Index] = null;
                    neibHeightMaps[Cardinal.SouthEast.Index] = null;
                }
                if (neibHeightMaps[Cardinal.West.Index] != null)
                {
                    neibHeightMaps[Cardinal.SouthWest.Index] = null;
                    neibHeightMaps[Cardinal.NorthWest.Index] = null;
                }

                string sides = "";
                for (int i = 0; i < Cardinal.ALL.Length; i++)
                {
                    var neibMap = neibHeightMaps[i];
                    if (neibMap == null) continue;

                    sides += Cardinal.ALL[i].Code + "_";
                }

                for (int localX = 0; localX < chunksize; localX++)
                {
                    borderIndicesByCardinal[Cardinal.North.Index] = (chunksize - 1) * chunksize + localX;
                    borderIndicesByCardinal[Cardinal.South.Index] = 0 + localX;

                    for (int localZ = 0; localZ < chunksize; localZ++)
                    {
                        double sumWeight = 0;
                        double yPos = 0;
                        float maxWeight = 0;

                        borderIndicesByCardinal[Cardinal.East.Index] = localZ * chunksize + 0;
                        borderIndicesByCardinal[Cardinal.West.Index] = localZ * chunksize + chunksize - 1;

                        for (int i = 0; i < Cardinal.ALL.Length; i++)
                        {
                            ushort[] neibMap = neibHeightMaps[i];
                            if (neibMap == null) continue;

                            float distToEdge = 0;

                            switch (i)
                            {
                                case 0:
                                    distToEdge = (float)localZ / chunksize;
                                    break;
                                case 1:
                                    distToEdge = 1 - (float)localX / chunksize + (float)localZ / chunksize;
                                    break;
                                case 2:
                                    distToEdge = 1 - (float)localX / chunksize;
                                    break;
                                case 3:
                                    distToEdge = 1 - (float)localX / chunksize + (1 - (float)localZ / chunksize);
                                    break;
                                case 4:
                                    distToEdge = 1 - (float)localZ / chunksize;
                                    break;
                                case 5:
                                    distToEdge = (float)localX / chunksize + 1 - (float)localZ / chunksize;
                                    break;
                                case 6:
                                    distToEdge = (float)localX / chunksize;
                                    break;
                                case 7:
                                    distToEdge = (float)localX / chunksize + (float)localZ / chunksize;
                                    break;
                            }

                            float cardinalWeight = (float)Math.Pow((float)(1 - GameMath.Clamp(distToEdge, 0, 1)), 2);
                            float neibYPos = neibMap[borderIndicesByCardinal[i]] + 0.5f;

                            yPos += neibYPos * Math.Max(0.0001, cardinalWeight);
                            sumWeight += cardinalWeight;
                            maxWeight = Math.Max(maxWeight, cardinalWeight);
                        }

                        taperMap[localZ * chunksize + localX] = new WeightedTaper() { terrainYPos = (float)(yPos / Math.Max(0.0001, sumWeight)), weight = maxWeight };
                    }
                }
            }

            Generate(request.Chunks, request.ChunkX, request.ChunkZ, request.RequiresChunkBorderSmoothing);
        }

        public void Generate(IServerChunk[] chunks, int chunkX, int chunkZ, bool requiresChunkBorderSmoothing)
        {
            landforms = landType.GetStaticField<LandformsWorldProperty>("landforms");
            IMapChunk mapChunk = chunks[0].MapChunk;

            int climateUpLeft;
            int climateUpRight;
            int climateBotLeft;
            int climateBotRight;

            int upheavalMapUpLeft = 0;
            int upheavalMapUpRight = 0;
            int upheavalMapBotLeft = 0;
            int upheavalMapBotRight = 0;

            IntDataMap2D climateMap = chunks[0].MapChunk.MapRegion.ClimateMap;
            IntDataMap2D oceanMap = chunks[0].MapChunk.MapRegion.OceanMap;
            int regionChunkSize = sapi.WorldManager.RegionSize / chunksize;
            float climateFactor = (float)climateMap.InnerSize / regionChunkSize;

            int rlX = chunkX % regionChunkSize;
            int rlZ = chunkZ % regionChunkSize;

            climateUpLeft = climateMap.GetUnpaddedInt((int)(rlX * climateFactor), (int)(rlZ * climateFactor));
            climateUpRight = climateMap.GetUnpaddedInt((int)(rlX * climateFactor + climateFactor), (int)(rlZ * climateFactor));
            climateBotLeft = climateMap.GetUnpaddedInt((int)(rlX * climateFactor), (int)(rlZ * climateFactor + climateFactor));
            climateBotRight = climateMap.GetUnpaddedInt((int)(rlX * climateFactor + climateFactor), (int)(rlZ * climateFactor + climateFactor));

            int oceanUpLeft = 0;
            int oceanUpRight = 0;
            int oceanBotLeft = 0;
            int oceanBotRight = 0;
            if (oceanMap != null && oceanMap.Data.Length > 0)
            {
                float oceanFactor = (float)oceanMap.InnerSize / regionChunkSize;
                oceanUpLeft = oceanMap.GetUnpaddedInt((int)(rlX * oceanFactor), (int)(rlZ * oceanFactor));
                oceanUpRight = oceanMap.GetUnpaddedInt((int)(rlX * oceanFactor + oceanFactor), (int)(rlZ * oceanFactor));
                oceanBotLeft = oceanMap.GetUnpaddedInt((int)(rlX * oceanFactor), (int)(rlZ * oceanFactor + oceanFactor));
                oceanBotRight = oceanMap.GetUnpaddedInt((int)(rlX * oceanFactor + oceanFactor), (int)(rlZ * oceanFactor + oceanFactor));
            }

            IntDataMap2D upheavalMap = chunks[0].MapChunk.MapRegion.UpheavelMap;
            if (upheavalMap != null)
            {
                float ufac = (float)upheavalMap.InnerSize / regionChunkSize;
                upheavalMapUpLeft = upheavalMap.GetUnpaddedInt((int)(rlX * ufac), (int)(rlZ * ufac));
                upheavalMapUpRight = upheavalMap.GetUnpaddedInt((int)(rlX * ufac + ufac), (int)(rlZ * ufac));
                upheavalMapBotLeft = upheavalMap.GetUnpaddedInt((int)(rlX * ufac), (int)(rlZ * ufac + ufac));
                upheavalMapBotRight = upheavalMap.GetUnpaddedInt((int)(rlX * ufac + ufac), (int)(rlZ * ufac + ufac));
            }

            int rockId = GlobalConfig.defaultRockId;
            float oceanicityFac = sapi.WorldManager.MapSizeY / 256 * 0.33333f;

            IntDataMap2D landformMap = mapChunk.MapRegion.LandformMap;
            float chunkPixelSize = landformMap.InnerSize / regionChunkSize;
            float baseX = chunkX % regionChunkSize * chunkPixelSize;
            float baseZ = chunkZ % regionChunkSize * chunkPixelSize;

            LerpedWeightedIndex2DMap landLerpMap = GetOrLoadLerpedLandformMap(chunks[0].MapChunk, chunkX / regionChunkSize, chunkZ / regionChunkSize);

            double[] octNoiseX0, octNoiseX1, octNoiseX2, octNoiseX3;
            double[] octThX0, octThX1, octThX2, octThX3;

            GetInterpolatedOctaves(landLerpMap[baseX, baseZ], out octNoiseX0, out octThX0);
            GetInterpolatedOctaves(landLerpMap[baseX + chunkPixelSize, baseZ], out octNoiseX1, out octThX1);
            GetInterpolatedOctaves(landLerpMap[baseX, baseZ + chunkPixelSize], out octNoiseX2, out octThX2);
            GetInterpolatedOctaves(landLerpMap[baseX + chunkPixelSize, baseZ + chunkPixelSize], out octNoiseX3, out octThX3);

            ushort[] rainHeightMap = chunks[0].MapChunk.RainHeightMap;
            ushort[] terrainHeightMap = chunks[0].MapChunk.WorldGenTerrainHeightMap;

            int mapSizeY = sapi.WorldManager.MapSizeY;
            int mapSizeYm2 = sapi.WorldManager.MapSizeY - 2;
            int taperThreshold = (int)(mapSizeY * 0.9f);
            double geoUpheavalAmplitude = 255;

            float chunkBlockDelta = 1.0f / chunksize;
            float chunkPixelBlockStep = chunkPixelSize * chunkBlockDelta;
            double verticalNoiseRelativeFrequency = 0.5 / TerraGenConfig.terrainNoiseVerticalScale;

            int plateX = chunkX / chunksInPlate;
            int plateZ = chunkZ / chunksInPlate;
            TectonicPlate plate = ObjectCacheUtil.GetOrCreate(sapi, plateX.ToString() + "+" + plateZ.ToString(), () =>
            {
                return new TectonicPlate(sapi, plateX, plateZ);
            });
            int localChunkX = chunkX % chunksInPlate;
            int localChunkZ = chunkZ % chunksInPlate;
            int localZoneX = localChunkX / chunksInZone;
            int localZoneZ = localChunkZ / chunksInZone;
            Vec2d plateStart = plate.globalPlateStart;
            List<TectonicZone> riverZoneList = plate.GetZonesAround(localZoneX, localZoneZ, 2);
            List<RiverSegment> validSegments = new();
            Vec2d localStart = new(localChunkX * chunksize, localChunkZ * chunksize);
            foreach (TectonicZone riverZone in riverZoneList)
            {
                foreach (River river in riverZone.rivers)
                {
                    if (RiverMath.DistanceToLine(localStart, river.startPoint, river.endPoint) < maxValleyWidth + 100 + 100) // Consider a river of 100 size, 100 distortion.
                    {
                        foreach (RiverSegment segment in river.segments)
                        {
                            if (RiverMath.DistanceToLine(localStart, segment.startPoint, segment.endPoint) < maxValleyWidth + 100 + 100)
                            {
                                validSegments.Add(segment); // Later check for duplicates. If the distance to another segment is too great it shouldn't have to be here.
                            }
                        }
                    }
                }
            }
            validSegments = validSegments.OrderBy(p => RiverMath.DistanceToLine(localStart, p.startPoint, p.endPoint)).ToList();

            float[] flowVectorsX = new float[32 * 32];
            float[] flowVectorsZ = new float[32 * 32];
            ushort[] riverDistance = new ushort[32 * 32];
            bool riverBank = false;

            Parallel.For(0, chunksize * chunksize, new ParallelOptions() { MaxDegreeOfParallelism = maxThreads }, chunkIndex2d => {
                int localX = chunkIndex2d % chunksize;
                int localZ = chunkIndex2d / chunksize;
                int worldX = chunkX * chunksize + localX;
                int worldZ = chunkZ * chunksize + localZ;

                samples[localX, localZ] = riverGenerator.SampleRiver(validSegments, localStart.X + localX, localStart.Y + localZ);
                if (samples[localX, localZ].flowVectorX > -100)
                {
                    flowVectorsX[chunkIndex2d] = samples[localX, localZ].flowVectorX;
                    flowVectorsZ[chunkIndex2d] = samples[localX, localZ].flowVectorZ;
                    riverBank = true;
                }

                riverDistance[chunkIndex2d] = (ushort)samples[localX, localZ].riverDistance;

                BitArray columnBlockSolidities = columnResults[chunkIndex2d].columnBlockSolidities;
                double[] lerpedAmps = tempDataThreadLocal.Value.LerpedAmplitudes;
                double[] lerpedThresholds = tempDataThreadLocal.Value.LerpedThresholds;

                WeightedIndex[] columnWeightedIndices = landLerpMap[baseX + localX * chunkPixelBlockStep, baseZ + localZ * chunkPixelBlockStep];
                for (int i = 0; i < terrainGenOctaves; i++)
                {
                    lerpedAmps[i] = GameMath.BiLerp(octNoiseX0[i], octNoiseX1[i], octNoiseX2[i], octNoiseX3[i], localX * chunkBlockDelta, localZ * chunkBlockDelta);
                    lerpedThresholds[i] = GameMath.BiLerp(octThX0[i], octThX1[i], octThX2[i], octThX3[i], localX * chunkBlockDelta, localZ * chunkBlockDelta);
                }

                VectorXZ distortion = NewDistortionNoise(worldX, worldZ);
                VectorXZ terrainDistortion = ApplyIsotropicDistortionThreshold(distortion * terrainDistortionMultiplier, terrainDistortionThreshold,
                    terrainDistortionMultiplier * maxDistortionAmount);
                VectorXZ upheavalDistortion = ApplyIsotropicDistortionThreshold(distortion * geoDistortionMultiplier, geoDistortionThreshold,
                    geoDistortionMultiplier * maxDistortionAmount);

                float upheavalStrength = GameMath.BiLerp(upheavalMapUpLeft, upheavalMapUpRight, upheavalMapBotLeft, upheavalMapBotRight, localX * chunkBlockDelta, localZ * chunkBlockDelta);
                float oceanicity = GameMath.BiLerp(oceanUpLeft, oceanUpRight, oceanBotLeft, oceanBotRight, localX * chunkBlockDelta, localZ * chunkBlockDelta) * oceanicityFac;
                float distY = oceanicity + ComputeOceanAndUpheavalDistY(upheavalStrength, worldX, worldZ, upheavalDistortion);

                columnResults[chunkIndex2d].waterBlockId = oceanicity > 1 ? GlobalConfig.saltWaterBlockId : GlobalConfig.waterBlockId;

                NewNormalizedSimplexFractalNoise.ColumnNoise columnNoise = terrainNoise.ForColumn(verticalNoiseRelativeFrequency, lerpedAmps, lerpedThresholds, worldX + terrainDistortion.X, worldZ + terrainDistortion.Z);

                WeightedTaper wTaper = taperMap[chunkIndex2d];

                int yMaximum;
                double valleyMax = maxValleyWidth * valleyNoise.GetNormalNoise(worldX, worldZ);
                if (valleyMax < 0) valleyMax = 0;

                if (valleyMax == 0)
                {
                    yMaximum = 1000;
                }
                else
                {
                    double riverLerp = Math.Clamp(RiverMath.InverseLerp(samples[localX, localZ].riverDistance, 0, valleyMax), 0, 1);
                    riverLerp = riverLerp * riverLerp * riverLerp;
                    yMaximum = (int)(riverFloorBase + riverFloorVariation * floorNoise.GetPosNoise(worldX, worldZ) + aboveSeaLevel * riverLerp);
                }

                for (int posY = 1; posY < mapSizeY - 1; posY++)
                {
                    StartSampleDisplacedYThreshold(posY + distY, mapSizeYm2, out int distortedPosYBase, out float distortedPosYSlide);

                    double threshold = 0;
                    for (int i = 0; i < columnWeightedIndices.Length; i++)
                    {
                        float[] thresholds = landforms.LandFormsByIndex[columnWeightedIndices[i].Index].TerrainYThresholds;
                        float thresholdValue = ContinueSampleDisplacedYThreshold(distortedPosYBase, distortedPosYSlide, thresholds);
                        threshold += thresholdValue * columnWeightedIndices[i].Weight;
                    }

                    double geoUpheavalTaper = ComputeGeoUpheavalTaper(posY, distY, taperThreshold, geoUpheavalAmplitude, mapSizeY);
                    threshold += geoUpheavalTaper;

                    if (requiresChunkBorderSmoothing)
                    {
                        double th = posY > wTaper.terrainYPos ? 1 : -1;

                        float yDiff = Math.Abs(posY - wTaper.terrainYPos);
                        double noise = yDiff > 10 ? 0 : distort2dx.Noise(-(chunkX * chunksize + localX) / 10.0, posY / 10.0, -(chunkZ * chunksize + localZ) / 10.0) / Math.Max(1, yDiff / 2.0);

                        noise *= GameMath.Clamp(2 * (1 - wTaper.weight), 0, 1) * 0.1;

                        threshold = GameMath.Lerp(threshold, th + noise, wTaper.weight);
                    }

                    double noiseSign;
                    if (!(threshold < columnNoise.BoundMax)) noiseSign = double.NegativeInfinity;
                    else if (threshold <= columnNoise.BoundMin) noiseSign = double.PositiveInfinity;

                    else
                    {
                        noiseSign = -NormalizedSimplexNoise.NoiseValueCurveInverse(threshold);
                        noiseSign = columnNoise.NoiseSign(posY, noiseSign);
                    }

                    columnBlockSolidities[posY] = noiseSign > 0;

                    if (posY > yMaximum) columnBlockSolidities[posY] = false;

                    /*
                    if (samples[localX, localZ].bankFactor > 0)
                    {
                        layerFullyEmpty[posY] = false;
                        layerFullySolid[posY] = false;
                    }
                    */
                }
            });

            int chunkY = 0;
            int localY = 1;
            IChunkBlocks chunkBlockData = chunks[chunkY].Data;
            chunkBlockData.SetBlockBulk(0, chunksize, chunksize, GlobalConfig.mantleBlockId);
            for (int posY = 1; posY < mapSizeY - 1; posY++)
            {
                for (int localZ = 0; localZ < chunksize; localZ++)
                {
                    int worldZ = chunkZ * chunksize + localZ;
                    for (int localX = 0; localX < chunksize; localX++)
                    {
                        int worldX = chunkX * chunksize + localX;

                        int mapIndex = ChunkIndex2d(localX, localZ);
                        int chunkIndex = ChunkIndex3d(localX, localY, localZ);

                        ColumnResult columnResult = columnResults[mapIndex];

                        RiverSample sample = samples[localX, localZ];
                        int bankFactorBlocks = (int)(sample.bankFactor * aboveSeaLevel);
                        int baseline = baseSeaLevel + heightBoost;

                        bool isSolid = columnResult.columnBlockSolidities[posY] && (posY <= baseline - bankFactorBlocks || posY >= baseline + bankFactorBlocks * topFactor);
                        int waterID = columnResult.waterBlockId;

                        if (isSolid)
                        {
                            terrainHeightMap[mapIndex] = (ushort)posY;
                            rainHeightMap[mapIndex] = (ushort)posY;
                            chunkBlockData[chunkIndex] = rockId;
                        }
                        else if (posY < TerraGenConfig.seaLevel)
                        {
                            rainHeightMap[mapIndex] = (ushort)posY;

                            int blockId;
                            if (posY == TerraGenConfig.seaLevel - 1)
                            {
                                int temp = (GameMath.BiLerpRgbColor(localX * chunkBlockDelta, localZ * chunkBlockDelta, climateUpLeft, climateUpRight, climateBotLeft, climateBotRight) >> 16) & 0xFF;
                                float distort = (float)distort2dx.Noise(worldX, worldZ) / 20f;
                                float tempF = TerraGenConfig.GetScaledAdjustedTemperatureFloat(temp, 0) + distort;
                                blockId = (tempF < TerraGenConfig.WaterFreezingTempOnGen && waterID != GlobalConfig.saltWaterBlockId) ? GlobalConfig.lakeIceBlockId : waterID;
                            }
                            else
                            {
                                blockId = waterID;
                            }

                            chunkBlockData.SetFluid(chunkIndex, blockId);
                        }
                    }
                }

                localY++;
                if (localY == chunksize)
                {
                    localY = 0;
                    chunkY++;
                    chunkBlockData = chunks[chunkY].Data;
                }
            }

            if (riverBank)
            {
                chunks[0].SetModdata<float[]>("flowVectorsX", flowVectorsX);
                chunks[0].SetModdata<float[]>("flowVectorsZ", flowVectorsZ);
            }

            chunks[0].SetModdata<ushort[]>("riverDistance", riverDistance);

            ushort yMax = 0;
            for (int i = 0; i < rainHeightMap.Length; i++)
            {
                yMax = Math.Max(yMax, rainHeightMap[i]);
            }

            chunks[0].MapChunk.YMax = yMax;
        }

        public LerpedWeightedIndex2DMap GetOrLoadLerpedLandformMap(IMapChunk mapchunk, int regionX, int regionZ)
        {
            landformMapByRegion.TryGetValue(regionZ * regionMapSize + regionX, out LerpedWeightedIndex2DMap map);
            if (map != null) return map;
            IntDataMap2D landformMap = mapchunk.MapRegion.LandformMap;
            map = landformMapByRegion[regionZ * regionMapSize + regionX]
                = new LerpedWeightedIndex2DMap(landformMap.Data, landformMap.Size, TerraGenConfig.landFormSmoothingRadius, landformMap.TopLeftPadding, landformMap.BottomRightPadding);

            return map;
        }

        public void GetInterpolatedOctaves(WeightedIndex[] indices, out double[] amps, out double[] thresholds)
        {
            amps = new double[terrainGenOctaves];
            thresholds = new double[terrainGenOctaves];

            for (int octave = 0; octave < terrainGenOctaves; octave++)
            {
                double amplitude = 0;
                double threshold = 0;
                for (int i = 0; i < indices.Length; i++)
                {
                    float weight = indices[i].Weight;
                    if (weight == 0) continue;
                    LandformVariant l = landforms.LandFormsByIndex[indices[i].Index];
                    amplitude += l.TerrainOctaves[octave] * weight;
                    threshold += l.TerrainOctaveThresholds[octave] * weight;
                }

                amps[octave] = amplitude;
                thresholds[octave] = threshold;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StartSampleDisplacedYThreshold(float distortedPosY, int mapSizeYm2, out int yBase, out float ySlide)
        {
            int distortedPosYBase = (int)Math.Floor(distortedPosY);
            yBase = GameMath.Clamp(distortedPosYBase, 0, mapSizeYm2);
            ySlide = distortedPosY - distortedPosYBase;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ContinueSampleDisplacedYThreshold(int yBase, float ySlide, float[] thresholds)
        {
            return GameMath.Lerp(thresholds[yBase], thresholds[yBase + 1], ySlide);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ComputeOceanAndUpheavalDistY(float upheavalStrength, double worldX, double worldZ, VectorXZ distGeo)
        {
            float upheavalNoiseValue = (float)geoUpheavalNoise.Noise((worldX + distGeo.X) / 400.0, (worldZ + distGeo.Z) / 400.0) * 0.9f;
            float upheavalMultiplier = Math.Min(0, 0.5f - upheavalNoiseValue);
            return upheavalStrength * upheavalMultiplier;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ComputeGeoUpheavalTaper(double posY, double distY, double taperThreshold, double geoUpheavalAmplitude, double mapSizeY)
        {
            const double AMPLITUDE_MODIFIER = 40.0;
            if (posY > taperThreshold && distY < -2)
            {
                double upheavalAmount = GameMath.Clamp(-distY, posY - mapSizeY, posY);
                double ceilingDelta = posY - taperThreshold;
                return ceilingDelta * upheavalAmount / (AMPLITUDE_MODIFIER * geoUpheavalAmplitude);
            }
            return 0;
        }

        public VectorXZ NewDistortionNoise(double worldX, double worldZ)
        {
            double noiseX = worldX / 400.0;
            double noiseZ = worldZ / 400.0;
            SimplexNoise.NoiseFairWarpVector(distort2dx, distort2dz, noiseX, noiseZ, out double distX, out double distZ);
            return new VectorXZ { X = distX, Z = distZ };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static VectorXZ ApplyIsotropicDistortionThreshold(VectorXZ dist, double threshold, double maximum)
        {
            double distMagnitudeSquared = dist.X * dist.X + dist.Z * dist.Z;
            double thresholdSquared = threshold * threshold;
            if (distMagnitudeSquared <= thresholdSquared) dist.X = dist.Z = 0;
            else
            {
                double baseCurve = (distMagnitudeSquared - thresholdSquared) / distMagnitudeSquared;
                double maximumSquared = maximum * maximum;
                double baseCurveReciprocalAtMaximum = maximumSquared / (maximumSquared - thresholdSquared);
                double slide = baseCurve * baseCurveReciprocalAtMaximum;

                slide *= slide;

                double expectedOutputMaximum = maximum - threshold;
                double forceDown = slide * (expectedOutputMaximum / maximum);

                dist *= forceDown;
            }
            return dist;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ChunkIndex3d(int x, int y, int z)
        {
            return (y * chunksize + z) * chunksize + x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ChunkIndex2d(int x, int z)
        {
            return z * chunksize + x;
        }

        public struct VectorXZ
        {
            public double X, Z;
            public static VectorXZ operator *(VectorXZ a, double b) => new() { X = a.X * b, Z = a.Z * b };
        }
    }
}