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
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.ServerMods.NoObf;

namespace Vintagestory.ServerMods
{
    public class GenerateTerra : ModStdWorldGen
    {
        public ICoreServerAPI sapi;

        public const double terrainDistortionMultiplier = 4.0;
        public const double terrainDistortionThreshold = 40.0;
        public const double geoDistortionMultiplier = 10.0;
        public const double geoDistortionThreshold = 10.0;
        public const double maxDistortionAmount = (55 + 40 + 30 + 10) * SimplexNoiseOctave.MAX_VALUE_2D_WARP;

        public RiverGenerator riverGenerator;

        public int maxThreads;

        public LandformsWorldProperty landforms;
        public Dictionary<int, LerpedWeightedIndex2DMap> LandformMapByRegion = new(10);
        public int regionMapSize;
        public float noiseScale;
        public int terrainGenOctaves = 9;

        public NewNormalizedSimplexFractalNoise terrainNoise;
        public SimplexNoise distort2dX;
        public SimplexNoise distort2dZ;
        public NormalizedSimplexNoise geoUpheavalNoise;
        WeightedTaper[] taperMap;

        public int chunksInPlate;
        public int chunksInZone;
        public int aboveSeaLevel;
        public int baseSeaLevel;

        public struct ThreadLocalTempData
        {
            public double[] LerpedAmplitudes;
            public double[] LerpedThresholds;
            public WeightedIndex[] landformWeights;
        }
        public ThreadLocal<ThreadLocalTempData> tempDataThreadLocal;

        public struct WeightedTaper
        {
            public float TerrainYPos;
            public float Weight;
        }

        public struct ColumnResult
        {
            public BitArray ColumnBlockSolidities;
            public int WaterBlockID;
        }
        public ColumnResult[] columnResults;
        public bool[] layerFullySolid;
        public bool[] layerFullyEmpty;
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

        public void LoadGamePre()
        {
            if (sapi.WorldManager.SaveGame.WorldType != "standard") return;

            TerraGenConfig.seaLevel = (int)(0.4313725490196078 * sapi.WorldManager.MapSizeY);
            sapi.WorldManager.SetSeaLevel(TerraGenConfig.seaLevel);
        }

        public Type landType = null;

        public void InitWorldGen()
        {
            chunksInPlate = RiverConfig.Loaded.zonesInPlate * RiverConfig.Loaded.zoneSize / 32;
            chunksInZone = RiverConfig.Loaded.zoneSize / 32;

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
            LandformMapByRegion.Clear();

            maxThreads = Math.Clamp(Environment.ProcessorCount - (sapi.Server.IsDedicated ? 4 : 6), 1, sapi.Server.Config.HostedMode ? 4 : 10);  // We leave at least 4-6 threads free to avoid lag spikes due to CPU unavailability

            regionMapSize = (int)Math.Ceiling((double)sapi.WorldManager.MapSizeX / sapi.WorldManager.RegionSize);
            noiseScale = Math.Max(1, sapi.WorldManager.MapSizeY / 256f);
            terrainGenOctaves = TerraGenConfig.GetTerrainOctaveCount(sapi.WorldManager.MapSizeY);

            terrainNoise = NewNormalizedSimplexFractalNoise.FromDefaultOctaves(
                terrainGenOctaves, 0.0005 * NewSimplexNoiseLayer.OldToNewFrequency / noiseScale, 0.9, sapi.WorldManager.Seed
            );

            distort2dX = new SimplexNoise(
                new double[] { 55, 40, 30, 10 },
                ScaleAdjustedFreqs(new double[] { 1 / 5.0, 1 / 2.50, 1 / 1.250, 1 / 0.65 }, noiseScale),
                sapi.World.Seed + 9876 + 0
            );

            distort2dZ = new SimplexNoise(
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
                LerpedThresholds = new double[terrainGenOctaves],
                landformWeights = new WeightedIndex[landType.GetStaticField<LandformsWorldProperty>("landforms").LandFormsByIndex.Length]
            });

            columnResults = new ColumnResult[chunksize * chunksize];
            layerFullyEmpty = new bool[sapi.WorldManager.MapSizeY];
            layerFullySolid = new bool[sapi.WorldManager.MapSizeY];
            taperMap = new WeightedTaper[chunksize * chunksize];
            for (int i = 0; i < chunksize * chunksize; i++) columnResults[i].ColumnBlockSolidities = new BitArray(sapi.WorldManager.MapSizeY);

            borderIndicesByCardinal = new int[8];
            borderIndicesByCardinal[Cardinal.NorthEast.Index] = (chunksize - 1) * chunksize + 0;
            borderIndicesByCardinal[Cardinal.SouthEast.Index] = 0 + 0;
            borderIndicesByCardinal[Cardinal.SouthWest.Index] = 0 + chunksize - 1;
            borderIndicesByCardinal[Cardinal.NorthWest.Index] = (chunksize - 1) * chunksize + chunksize - 1;

            aboveSeaLevel = sapi.WorldManager.MapSizeY - TerraGenConfig.seaLevel;
            baseSeaLevel = TerraGenConfig.seaLevel;
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
                ushort[][] neighbHeightMaps = request.NeighbourTerrainHeight;

                if (neighbHeightMaps[Cardinal.North.Index] != null)
                {
                    neighbHeightMaps[Cardinal.NorthEast.Index] = null;
                    neighbHeightMaps[Cardinal.NorthWest.Index] = null;
                }
                if (neighbHeightMaps[Cardinal.East.Index] != null)
                {
                    neighbHeightMaps[Cardinal.NorthEast.Index] = null;
                    neighbHeightMaps[Cardinal.SouthEast.Index] = null;
                }
                if (neighbHeightMaps[Cardinal.South.Index] != null)
                {
                    neighbHeightMaps[Cardinal.SouthWest.Index] = null;
                    neighbHeightMaps[Cardinal.SouthEast.Index] = null;
                }
                if (neighbHeightMaps[Cardinal.West.Index] != null)
                {
                    neighbHeightMaps[Cardinal.SouthWest.Index] = null;
                    neighbHeightMaps[Cardinal.NorthWest.Index] = null;
                }

                string sides = "";
                for (int i = 0; i < Cardinal.ALL.Length; i++)
                {
                    ushort[] neighbMap = neighbHeightMaps[i];
                    if (neighbMap == null) continue;

                    sides += Cardinal.ALL[i].Code + "_";
                }

                for (int dx = 0; dx < chunksize; dx++)
                {
                    borderIndicesByCardinal[Cardinal.North.Index] = (chunksize - 1) * chunksize + dx;
                    borderIndicesByCardinal[Cardinal.South.Index] = 0 + dx;

                    for (int dz = 0; dz < chunksize; dz++)
                    {
                        double sumWeight = 0;
                        double yPos = 0;
                        float maxWeight = 0;

                        borderIndicesByCardinal[Cardinal.East.Index] = dz * chunksize + 0;
                        borderIndicesByCardinal[Cardinal.West.Index] = dz * chunksize + chunksize - 1;

                        for (int i = 0; i < Cardinal.ALL.Length; i++)
                        {
                            ushort[] neighbMap = neighbHeightMaps[i];
                            if (neighbMap == null) continue;

                            float distToEdge = 0;

                            switch (i)
                            {
                                case 0: // N: Negative Z
                                    distToEdge = (float)dz / chunksize;
                                    break;
                                case 1: // NE: Positive X, negative Z
                                    distToEdge = (1 - (float)dx / chunksize) + (float)dz / chunksize;
                                    break;
                                case 2: // E: Positive X
                                    distToEdge = 1 - (float)dx / chunksize;
                                    break;
                                case 3: // SE: Positive X, positive Z
                                    distToEdge = (1 - (float)dx / chunksize) + (1 - (float)dz / chunksize);
                                    break;
                                case 4: // S: Positive Z
                                    distToEdge = 1 - (float)dz / chunksize;
                                    break;
                                case 5: // SW: Negative X, positive Z
                                    distToEdge = (float)dx / chunksize + 1 - (float)dz / chunksize;
                                    break;
                                case 6: // W: Negative X
                                    distToEdge = (float)dx / chunksize;
                                    break;
                                case 7: // Negative X, negative Z
                                    distToEdge = (float)dx / chunksize + (float)dz / chunksize;
                                    break;
                            }

                            float cardinalWeight = (float)Math.Pow((float)(1 - GameMath.Clamp(distToEdge, 0, 1)), 2);
                            float neighbYPos = neighbMap[borderIndicesByCardinal[i]] + 0.5f;

                            yPos += neighbYPos * Math.Max(0.0001, cardinalWeight);
                            sumWeight += cardinalWeight;
                            maxWeight = Math.Max(maxWeight, cardinalWeight);
                        }

                        taperMap[dz * chunksize + dx] = new WeightedTaper() { TerrainYPos = (float)(yPos / Math.Max(0.0001, sumWeight)), Weight = maxWeight };
                    }
                }
            }


            Generate(request.Chunks, request.ChunkX, request.ChunkZ, request.RequiresChunkBorderSmoothing);
        }

        public void Generate(IServerChunk[] chunks, int chunkX, int chunkZ, bool requiresChunkBorderSmoothing)
        {
            landforms = landType.GetStaticField<LandformsWorldProperty>("landforms");
            IMapChunk mapchunk = chunks[0].MapChunk;
            const int chunksize = GlobalConstants.ChunkSize;

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
                float upheavalFactor = (float)upheavalMap.InnerSize / regionChunkSize;
                upheavalMapUpLeft = upheavalMap.GetUnpaddedInt((int)(rlX * upheavalFactor), (int)(rlZ * upheavalFactor));
                upheavalMapUpRight = upheavalMap.GetUnpaddedInt((int)(rlX * upheavalFactor + upheavalFactor), (int)(rlZ * upheavalFactor));
                upheavalMapBotLeft = upheavalMap.GetUnpaddedInt((int)(rlX * upheavalFactor), (int)(rlZ * upheavalFactor + upheavalFactor));
                upheavalMapBotRight = upheavalMap.GetUnpaddedInt((int)(rlX * upheavalFactor + upheavalFactor), (int)(rlZ * upheavalFactor + upheavalFactor));
            }

            int rockID = GlobalConfig.defaultRockId;
            float oceanicityFactor = sapi.WorldManager.MapSizeY / 256 * 0.33333f; //At a mapheight of 255, submerge land by up to 85 blocks

            IntDataMap2D landformMap = mapchunk.MapRegion.LandformMap;

            //# of pixels for each chunk (probably 1, 2, or 4) in the landform map
            float chunkPixelSize = landformMap.InnerSize / regionChunkSize;

            //Start coordinates for the chunk in the region map
            float baseX = chunkX % regionChunkSize * chunkPixelSize;
            float baseZ = chunkZ % regionChunkSize * chunkPixelSize;

            LerpedWeightedIndex2DMap landLerpMap = GetOrLoadLerpedLandformMap(chunks[0].MapChunk, chunkX / regionChunkSize, chunkZ / regionChunkSize);

            //Terrain octaves
            double[] octNoiseX0, octNoiseX1, octNoiseX2, octNoiseX3;
            double[] octThX0, octThX1, octThX2, octThX3;

            WeightedIndex[] landformWeights = tempDataThreadLocal.Value.landformWeights;
            GetInterpolatedOctaves(landLerpMap.WeightsAt(baseX, baseZ, landformWeights), out octNoiseX0, out octThX0);
            GetInterpolatedOctaves(landLerpMap.WeightsAt(baseX + chunkPixelSize, baseZ, landformWeights), out octNoiseX1, out octThX1);
            GetInterpolatedOctaves(landLerpMap.WeightsAt(baseX, baseZ + chunkPixelSize, landformWeights), out octNoiseX2, out octThX2);
            GetInterpolatedOctaves(landLerpMap.WeightsAt(baseX + chunkPixelSize, baseZ + chunkPixelSize, landformWeights), out octNoiseX3, out octThX3);

            //Store heightmap in the map chunk
            ushort[] rainHeightMap = chunks[0].MapChunk.RainHeightMap;
            ushort[] terrainHeightMap = chunks[0].MapChunk.WorldGenTerrainHeightMap;

            int mapSizeY = sapi.WorldManager.MapSizeY;
            int mapSizeYm2 = sapi.WorldManager.MapSizeY - 2;
            int taperThreshold = (int)(mapSizeY * 0.9f);
            double geoUpheavalAmplitude = 255;

            float chunkBlockDelta = 1.0f / chunksize;
            float chunkPixelBlockStep = chunkPixelSize * chunkBlockDelta;
            double verticalNoiseRelativeFrequency = 0.5 / TerraGenConfig.terrainNoiseVerticalScale;
            for (int y = 0; y < layerFullySolid.Length; y++) layerFullySolid[y] = true; //Fill with true; later if any block in the layer is non-solid we will set it to false
            for (int y = 0; y < layerFullyEmpty.Length; y++) layerFullyEmpty[y] = true; //Fill with true; later if any block in the layer is non-solid we will set it to false
            layerFullyEmpty[mapSizeY - 1] = false; //The top block is always empty (air), leaving space for grass, snowlayer etc.
            LandformVariant[] landFormsByIndex = landforms.LandFormsByIndex;

            //1. Get data needed for the entire chunk here
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
                    if (RiverMath.DistanceToLine(localStart, river.startPoint, river.endPoint) < 400)
                    {
                        foreach (RiverSegment segment in river.segments)
                        {
                            if (RiverMath.DistanceToLine(localStart, segment.startPoint, segment.endPoint) < 200)
                            {
                                validSegments.Add(segment); //Later check for duplicates. If the distance to another segment is too great it shouldn't have to be here
                            }
                        }
                    }
                }
            }
            validSegments = validSegments.OrderBy(p => RiverMath.DistanceToLine(localStart, p.startPoint, p.endPoint)).ToList();

            Parallel.For(0, chunksize * chunksize, new ParallelOptions() { MaxDegreeOfParallelism = maxThreads }, chunkIndex2d => {
                int localX = chunkIndex2d % chunksize;
                int localZ = chunkIndex2d / chunksize;

                int worldX = chunkX * chunksize + localX;
                int worldZ = chunkZ * chunksize + localZ;

                BitArray columnBlockSolidities = columnResults[chunkIndex2d].ColumnBlockSolidities;

                double[] lerpedAmps = tempDataThreadLocal.Value.LerpedAmplitudes;
                double[] lerpedTh = tempDataThreadLocal.Value.LerpedThresholds;

                //Calculating these 1024 times is very costly: landLerpMap places on the heap 2 new Dictionary, 1 new SortedDictionary, and 3 new WeightedIndex[]
                WeightedIndex[] columnLandformIndexedWeights = tempDataThreadLocal.Value.landformWeights;
                landLerpMap.WeightsAt(baseX + localX * chunkPixelBlockStep, baseZ + localZ * chunkPixelBlockStep, columnLandformIndexedWeights);
                for (int i = 0; i < terrainGenOctaves; i++)
                {
                    lerpedAmps[i] = GameMath.BiLerp(octNoiseX0[i], octNoiseX1[i], octNoiseX2[i], octNoiseX3[i], localX * chunkBlockDelta, localZ * chunkBlockDelta);
                    lerpedTh[i] = GameMath.BiLerp(octThX0[i], octThX1[i], octThX2[i], octThX3[i], localX * chunkBlockDelta, localZ * chunkBlockDelta);
                }

                //Create directional compression effect
                VectorXZ dist = NewDistortionNoise(worldX, worldZ);
                VectorXZ distTerrain = ApplyIsotropicDistortionThreshold(dist * terrainDistortionMultiplier, terrainDistortionThreshold,
                    terrainDistortionMultiplier * maxDistortionAmount);
                VectorXZ distGeo = ApplyIsotropicDistortionThreshold(dist * geoDistortionMultiplier, geoDistortionThreshold,
                    geoDistortionMultiplier * maxDistortionAmount);

                //Get Y distortion from oceanicity and upheaval
                float upHeavalStrength = GameMath.BiLerp(upheavalMapUpLeft, upheavalMapUpRight, upheavalMapBotLeft, upheavalMapBotRight, localX * chunkBlockDelta, localZ * chunkBlockDelta);
                float oceanicity = GameMath.BiLerp(oceanUpLeft, oceanUpRight, oceanBotLeft, oceanBotRight, localX * chunkBlockDelta, localZ * chunkBlockDelta) * oceanicityFactor;
                float distY = oceanicity + ComputeOceanAndUpheavalDistY(upHeavalStrength, worldX, worldZ, distGeo);

                columnResults[chunkIndex2d].WaterBlockID = oceanicity > 1 ? GlobalConfig.saltWaterBlockId : GlobalConfig.waterBlockId;

                //Prepare the noise for the entire column
                NewNormalizedSimplexFractalNoise.ColumnNoise columnNoise = terrainNoise.ForColumn(verticalNoiseRelativeFrequency, lerpedAmps, lerpedTh, worldX + distTerrain.X, worldZ + distTerrain.Z);

                WeightedTaper wTaper = taperMap[chunkIndex2d];

                for (int posY = 1; posY < mapSizeY - 1; posY++)
                {
                    //Setup a lerp between threshold values, so that distortY can be applied continuously there
                    StartSampleDisplacedYThreshold(posY + distY, mapSizeYm2, out int distortedPosYBase, out float distortedPosYSlide);

                    //Value starts as the landform Y threshold
                    double threshold = 0;
                    for (int i = 0; i < columnLandformIndexedWeights.Length; i++)
                    {
                        float weight = columnLandformIndexedWeights[i].Weight;
                        if (weight == 0) continue;

                        //Sample the two values to lerp between. The value of distortedPosYBase is clamped in such a way that this always works
                        //Underflow and overflow of distortedPosY result in linear extrapolation

                        float[] thresholds = landFormsByIndex[columnLandformIndexedWeights[i].Index].TerrainYThresholds;
                        float thresholdValue = ContinueSampleDisplacedYThreshold(distortedPosYBase, distortedPosYSlide, thresholds);
                        threshold += thresholdValue * weight;
                    }

                    //Geo Upheaval modifier for threshold
                    double geoUpheavalTaper = ComputeGeoUpheavalTaper(posY, distY, taperThreshold, geoUpheavalAmplitude, mapSizeY);
                    threshold += geoUpheavalTaper;

                    if (requiresChunkBorderSmoothing)
                    {
                        double th = posY > wTaper.TerrainYPos ? 1 : -1;

                        float yDiff = Math.Abs(posY - wTaper.TerrainYPos);
                        double noise = yDiff > 10 ? 0 : distort2dX.Noise(-(chunkX * chunksize + localX) / 10.0, posY / 10.0, -(chunkZ * chunksize + localZ) / 10.0) / Math.Max(1, yDiff / 2.0);

                        noise *= GameMath.Clamp(2 * (1 - wTaper.Weight), 0, 1) * 0.1;

                        threshold = GameMath.Lerp(threshold, th + noise, wTaper.Weight);
                    }

                    //Often we don't need to calculate the noise
                    //First case also catches NaN if it were to ever happen
                    double noiseSign;
                    if (!(threshold < columnNoise.BoundMax)) noiseSign = double.NegativeInfinity;
                    else if (threshold <= columnNoise.BoundMin) noiseSign = double.PositiveInfinity;

                    //But sometimes we do
                    else
                    {
                        noiseSign = -NormalizedSimplexNoise.NoiseValueCurveInverse(threshold);
                        noiseSign = columnNoise.NoiseSign(posY, noiseSign);
                    }

                    columnBlockSolidities[posY] = noiseSign > 0;
                    layerFullyEmpty[posY] = false;
                    layerFullySolid[posY] = false;

                    //This massively breaks things right now

                    /*
                    if (noiseSign > 0) //Solid
                    {
                        columnBlockSolidities[posY] = true;
                        layerFullyEmpty[posY] = false; //Thread safe even when this is parallel
                    }
                    else
                    {
                        columnBlockSolidities[posY] = false;
                        layerFullySolid[posY] = false; //Thread safe even when this is parallel
                    }

                    */
                }
            });

            IChunkBlocks chunkBlockData = chunks[0].Data;

            //First set all the fully solid layers in bulk, as much as possible
            chunkBlockData.SetBlockBulk(0, chunksize, chunksize, GlobalConfig.mantleBlockId);
            int yBase = 1;
            for (; yBase < mapSizeY - 1; yBase++)
            {
                if (layerFullySolid[yBase])
                {
                    if (yBase % chunksize == 0)
                    {
                        chunkBlockData = chunks[yBase / chunksize].Data;
                    }

                    chunkBlockData.SetBlockBulk((yBase % chunksize) * chunksize * chunksize, chunksize, chunksize, rockID);
                }
                else break;
            }

            // Now figure out the top of the mixed layers (above yTop we have fully empty layers, i.e. air)
            int seaLevel = TerraGenConfig.seaLevel;
            int surfaceWaterId = 0;
            int yTop = mapSizeY - 2; //yTop never more than (mapSizey - 1), but leave the top block layer on the map always as air / for grass
            while (yTop >= yBase && layerFullyEmpty[yTop]) yTop--; //Decrease yTop, we don't need to generate anything for fully empty (air layers)
            if (yTop < seaLevel) yTop = seaLevel;
            yTop++; //Add back one because this is going to be the loop until limit

            float[] flowVectorsX = new float[32 * 32];
            float[] flowVectorsZ = new float[32 * 32];
            bool riverBank = false;

            //Then for the rest place blocks column by column (from yBase to yTop only; outside that range layers were already placed below, or are fully air above)
            for (int localZ = 0; localZ < chunksize; localZ++)
            {
                int worldZ = chunkZ * chunksize + localZ;
                int mapIndex = ChunkIndex2d(0, localZ);
                for (int localX = 0; localX < chunksize; localX++)
                {
                    ColumnResult columnResult = columnResults[mapIndex];
                    int waterId = columnResult.WaterBlockID;

                    //2. Get data needed for the entire column here
                    RiverSample riverSample = riverGenerator.SampleRiver(validSegments, localStart.X + localX, localStart.Y + localZ);
                    int bankFactorBlocks = (int)(riverSample.bankFactor * aboveSeaLevel);
                    int baseline = baseSeaLevel + 3;

                    if (riverSample.flowVectorX > -100)
                    {
                        flowVectorsX[localZ * 32 + localX] = riverSample.flowVectorX;
                        flowVectorsZ[localZ * 32 + localX] = riverSample.flowVectorZ;
                        riverBank = true;
                    }

                    if (yBase < seaLevel && waterId != GlobalConfig.saltWaterBlockId) //Finding the surface water / ice id, relevant only for fresh water and only if there is a non-solid block in the column below sea-level
                    {
                        int temp = (GameMath.BiLerpRgbColor(localX * chunkBlockDelta, localZ * chunkBlockDelta, climateUpLeft, climateUpRight, climateBotLeft, climateBotRight) >> 16) & 0xFF;
                        float distort = (float)distort2dX.Noise(chunkX * chunksize + localX, worldZ) / 20f;
                        float tempf = TerraGenConfig.GetScaledAdjustedTemperatureFloat(temp, 0) + distort;
                        surfaceWaterId = (tempf < TerraGenConfig.WaterFreezingTempOnGen) ? GlobalConfig.lakeIceBlockId : waterId;
                    }

                    terrainHeightMap[mapIndex] = (ushort)(yBase - 1); //Initially set the height maps to values reflecting the top of the fully solid layers
                    rainHeightMap[mapIndex] = (ushort)(yBase - 1);

                    chunkBlockData = chunks[yBase / chunksize].Data;
                    for (int posY = yBase; posY < yTop; posY++)
                    {
                        int localY = posY % chunksize;
                        int chunkIndex = ChunkIndex3d(localX, localY, localZ);

                        //3. Make sure blocks can't be placed in the river area
                        if (columnResult.ColumnBlockSolidities[posY] && (posY <= baseline - bankFactorBlocks || posY >= baseline + bankFactorBlocks)) //If isSolid
                        {
                            terrainHeightMap[mapIndex] = (ushort)posY;
                            rainHeightMap[mapIndex] = (ushort)posY;
                            chunkBlockData[chunkIndex] = rockID;
                        }
                        else if (posY < seaLevel)
                        {
                            int blockId;
                            if (posY == seaLevel - 1)
                            {
                                rainHeightMap[mapIndex] = (ushort)posY; //We only need to set the rainHeightMap on the top block
                                blockId = surfaceWaterId;
                            }
                            else
                            {
                                blockId = waterId;
                            }

                            chunkBlockData.SetFluid(chunkIndex, blockId);
                        }

                        if (localY == chunksize - 1)
                        {
                            chunkBlockData = chunks[(posY + 1) / chunksize].Data;  // Set up the next chunksBlockData value
                        }
                    }

                    mapIndex++;
                }
            }

            if (riverBank)
            {
                chunks[0].SetModdata<float[]>("flowVectorsX", flowVectorsX);
                chunks[0].SetModdata<float[]>("flowVectorsZ", flowVectorsZ);
            }

            ushort yMax = 0;
            for (int i = 0; i < rainHeightMap.Length; i++)
            {
                yMax = Math.Max(yMax, rainHeightMap[i]);
            }

            chunks[0].MapChunk.YMax = yMax;
        }

        public LerpedWeightedIndex2DMap GetOrLoadLerpedLandformMap(IMapChunk mapchunk, int regionX, int regionZ)
        {
            LandformMapByRegion.TryGetValue(regionZ * regionMapSize + regionX, out LerpedWeightedIndex2DMap map);
            if (map != null) return map;
            IntDataMap2D landformMap = mapchunk.MapRegion.LandformMap;
            map = LandformMapByRegion[regionZ * regionMapSize + regionX]
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
        public void StartSampleDisplacedYThreshold(float distortedPosY, int mapSizeYm2, out int yBase, out float ySlide)
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
            SimplexNoise.NoiseFairWarpVector(distort2dX, distort2dZ, noiseX, noiseZ, out double distX, out double distZ);
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