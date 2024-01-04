using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

public class TectonicPlate
{
    public ICoreServerAPI sapi;

    public int zoneSize; // Distance between each zone center.
    public int zonesInPlate; // Number of zones in plate.
    public int zonePadding; // Minimum distance a center can be from the edge of a cell.
    public int minSegments;

    public int plateSize; // Plate size in blocks.
    public Vec2d localPlateCenterPosition = new(); // Local center (plate size / 2).
    public Vec2d globalPlateStart = new();

    public TectonicZone[,] zones; // All regions in the plate.

    public LCGRandom rand;

    // CONFIG.
    public float riverGrowth;
    public int riverSpawnChance;
    public int riverSplitChance;
    public int lakeChance;
    public int segmentsInRiver;
    public double segmentOffset;

    public int maxZoneTraversal;

    public float riverSpeed;
    public float oceanThreshold;

    public TectonicPlate(ICoreServerAPI sapi, int plateX, int plateZ)
    {
        this.sapi = sapi;

        // Zone config.
        zoneSize = RiverConfig.Loaded.zoneSize;
        zonesInPlate = RiverConfig.Loaded.zonesInPlate;
        zonePadding = RiverConfig.Loaded.zonePadding;
        minSegments = RiverConfig.Loaded.minSegments;

        // River config.
        riverGrowth = RiverConfig.Loaded.riverGrowth;
        riverSpawnChance = RiverConfig.Loaded.riverSpawnChance;
        riverSplitChance = RiverConfig.Loaded.riverSplitChance;
        lakeChance = RiverConfig.Loaded.lakeChance;
        segmentsInRiver = RiverConfig.Loaded.segmentsInRiver;
        segmentOffset = RiverConfig.Loaded.segmentOffset;

        maxZoneTraversal = RiverConfig.Loaded.maxZoneTraversal;

        riverSpeed = RiverConfig.Loaded.riverSpeed;
        oceanThreshold = RiverConfig.Loaded.oceanThreshold;

        plateSize = zoneSize * zonesInPlate;
        localPlateCenterPosition.X = plateSize / 2;
        localPlateCenterPosition.Y = plateSize / 2;

        zones = new TectonicZone[zonesInPlate, zonesInPlate];
        rand = new LCGRandom(sapi.WorldManager.Seed);

        // Initialize all zones.
        GenerateZones(plateX, plateZ);
    }

    public void GenerateZones(int plateX, int plateZ)
    {
        rand.InitPositionSeed(plateX, plateZ);

        // Width and depth in zones.
        int width = zones.GetLength(0);
        int depth = zones.GetLength(1);

        // Global plate coordinates.
        globalPlateStart.X = plateX * plateSize;
        globalPlateStart.Y = plateZ * plateSize;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                // Initializes the regions center position as a random point in the grid.
                zones[x, z] = new TectonicZone(x * zoneSize + rand.NextInt(zoneSize - zonePadding) + zonePadding,
                                               z * zoneSize + rand.NextInt(zoneSize - zonePadding) + zonePadding);

                TectonicZone zone = zones[x, z];
                zone.xIndex = x;
                zone.zIndex = z;

                // Get oceanicity here.
                int oceanPadding = 5;
                int zoneWorldX = (int)(globalPlateStart.X + zones[x, z].localZoneCenterPosition.X);
                int zoneWorldZ = (int)(globalPlateStart.Y + zones[x, z].localZoneCenterPosition.Y);
                int chunkX = zoneWorldX / 32;
                int chunkZ = zoneWorldZ / 32;
                int regionX = chunkX / 16;
                int regionZ = chunkZ / 16;

                GenMaps genMaps = sapi.ModLoader.GetModSystem<GenMaps>();
                int noiseSizeOcean = genMaps.GetField<int>("noiseSizeOcean");

                IntDataMap2D oceanMap = new()
                {
                    Size = noiseSizeOcean + 2 * oceanPadding,
                    TopLeftPadding = oceanPadding,
                    BottomRightPadding = oceanPadding,
                    Data = genMaps.GetField<MapLayerBase>("oceanGen").GenLayer(regionX * noiseSizeOcean - oceanPadding,
                                                                                    regionZ * noiseSizeOcean - oceanPadding,
                                                                                    noiseSizeOcean + 2 * oceanPadding,
                                                                                    noiseSizeOcean + 2 * oceanPadding)
                };

                int rlX = chunkX % 16;
                int rlZ = chunkZ % 16;

                int localX = zoneWorldX % 32;
                int localZ = zoneWorldZ % 32;

                float chunkBlockDelta = 1.0f / 16;

                float oceanFactor = (float)oceanMap.InnerSize / 16;
                int oceanUpLeft = oceanMap.GetUnpaddedInt((int)(rlX * oceanFactor), (int)(rlZ * oceanFactor));
                int oceanUpRight = oceanMap.GetUnpaddedInt((int)(rlX * oceanFactor + oceanFactor), (int)(rlZ * oceanFactor));
                int oceanBotLeft = oceanMap.GetUnpaddedInt((int)(rlX * oceanFactor), (int)(rlZ * oceanFactor + oceanFactor));
                int oceanBotRight = oceanMap.GetUnpaddedInt((int)(rlX * oceanFactor + oceanFactor), (int)(rlZ * oceanFactor + oceanFactor));
                float oceanicityFactor = sapi.WorldManager.MapSizeY / 256 * 0.33333f;

                double zoneOceanicity = GameMath.BiLerp(oceanUpLeft, oceanUpRight, oceanBotLeft, oceanBotRight, localX * chunkBlockDelta, localZ * chunkBlockDelta) * oceanicityFactor;
                
                zone.height = 1 - zoneOceanicity;

                // 1 oceanicity = saltwater threshold.
                if (zoneOceanicity > oceanThreshold)
                {
                    zone.ocean = true;
                    zone.height = -1;
                }
            }
        }

        // Check if a zone is coastal.
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                TectonicZone zone = zones[x, z];

                if (zone.ocean)
                {
                    zone.coastal = true;
                    continue;
                }

                for (int xz = -1; xz < 2; xz++)
                {
                    for (int zz = -1; zz < 2; zz++)
                    {
                        if (zones[Math.Clamp(x + xz, 0, zonesInPlate - 1), Math.Clamp(z + zz, 0, zonesInPlate - 1)].ocean)
                        {
                            zone.coastal = true;
                            break;
                        }
                    }

                    if (zone.coastal) break;
                }
            }
        }

        // Set zone height based on distance from ocean.
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                if (zones[x, z].ocean) continue;

                // For every ocean region, calculate distance to the center of the tile.
                double closestDistance = double.MaxValue;
                for (int j = 0; j < width; j++)
                {
                    for (int i = 0; i < depth; i++)
                    {
                        if (!zones[j, i].ocean) continue;
                        double distance = zones[x, z].localZoneCenterPosition.DistanceTo(zones[j, i].localZoneCenterPosition);
                        if (distance < closestDistance) closestDistance = distance;
                    }
                }

                zones[x, z].height = closestDistance / (plateSize / 2);
            }
        }

        // Generate rivers.
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                TectonicZone zone = zones[x, z];

                // Only generate from oceans.
                if (!zone.ocean)
                {
                    continue;
                }

                // Check if the ocean borders a coastal zone.
                bool nearLand = false;
                for (int xz = -1; xz < 2; xz++)
                {
                    for (int zz = -1; zz < 2; zz++)
                    {
                        if (zones[Math.Clamp(x + xz, 0, zonesInPlate - 1), Math.Clamp(z + zz, 0, zonesInPlate - 1)].ocean == false)
                        {
                            nearLand = true;
                            break;
                        }
                    }

                    if (nearLand) break;
                }

                // Chance to generate a river here.
                if (nearLand && rand.NextInt(100) < riverSpawnChance)
                {
                    List<River> riverList = new();
                    List<TectonicZone> pathedZones = new();
                    GenerateRiver(width, depth, zone, maxZoneTraversal, 3, null, riverList, pathedZones);

                    // Invalid number of rivers.
                    if (riverList.Count < minSegments)
                    {
                        foreach (TectonicZone pathedZone in pathedZones)
                        {
                            pathedZone.rivers.Clear();
                            pathedZone.pathedTo = false;
                        }
                        continue;
                    }

                    AssignRiverSizes(riverList);
                    BuildRiverSegments(riverList);
                    ConnectSegments(riverList);
                    ValidateSegments(riverList);
                }
            }
        }

        // No lakes in this mod.
    }

    public void AssignRiverSizes(List<River> riverList)
    {
        List<River> riverEndList = riverList.Where(river => river.end == true).ToList();

        foreach (River river in riverEndList)
        {
            AssignRiverSize(river, 1);
        }
    }

    public void AssignRiverSize(River river, float startSize)
    {
        if (river.startSize <= startSize) // If the endSize is less than the startSize this river hasn't been generated yet or a bigger river is ready to generate.
        {
            river.endSize = startSize;
            river.startSize = startSize + riverGrowth;

            if (river.parentRiver != null)
            {
                AssignRiverSize(river.parentRiver, river.startSize);
            }
        }
    }

    public void BuildRiverSegments(List<River> riverList)
    {
        // Assign segments to each river.
        foreach (River river in riverList)
        {
            river.segments = new RiverSegment[segmentsInRiver];

            Vec2d offsetVector = new Vec2d(river.endPoint.X - river.startPoint.X, river.endPoint.Y - river.startPoint.Y).Normalize();
            offsetVector = new Vec2d(-offsetVector.Y, offsetVector.X);

            for (int i = 0; i < river.segments.Length; i++) // For each segment.
            {
                double offset = -segmentOffset + rand.NextDouble() * segmentOffset * 2; // Offset segment by up to 200.

                river.segments[i] = new RiverSegment();

                if (i == 0)
                {
                    river.segments[i].startPoint = river.startPoint;
                }
                else
                {
                    river.segments[i].startPoint = river.segments[i - 1].endPoint;
                }

                if (i == segmentsInRiver - 1)
                {
                    river.segments[i].endPoint = river.endPoint;
                }
                else
                {
                    river.segments[i].endPoint = new Vec2d(
                        GameMath.Lerp(river.startPoint.X, river.endPoint.X, (double)(i + 1) / segmentsInRiver),
                        GameMath.Lerp(river.startPoint.Y, river.endPoint.Y, (double)(i + 1) / segmentsInRiver)
                        );

                    river.segments[i].endPoint.X += offset * offsetVector.X;
                    river.segments[i].endPoint.Y += offset * offsetVector.Y;
                }

                river.segments[i].river = river;

                river.segments[i].midPoint = river.segments[i].startPoint + (river.segments[i].endPoint - river.segments[i].startPoint) * 0.5;
            }
        }
    }

    public void ConnectSegments(List<River> riverList)
    {
        foreach (River river in riverList)
        {
            // Make sure all rivers flow into each other smoothly.
            if (river.parentRiver?.endSize > river.startSize)
            {
                river.startSize = river.parentRiver.endSize;
            }

            for (int i = 0; i < river.segments.Length; i++)
            {
                if (i == 0)
                {
                    if (river.parentRiver != null)
                    {
                        river.segments[i].parent = river.parentRiver.segments[river.parentRiver.segments.Length - 1]; // Make the last segment in the parent river the parent of this.
                        river.parentRiver.segments[segmentsInRiver - 1].children.Add(river.segments[i]); // Add this to the children of that river.
                    }
                    else
                    {
                        river.segments[i].parent = river.segments[i];
                    }

                    continue;
                }

                river.segments[i].parent = river.segments[i - 1]; // Make the parent the last segment.
                river.segments[i - 1].children.Add(river.segments[i]); // Add this to its children.
            }
        }

        // If the river segment has no children, make it its own child.
        foreach (River river in riverList)
        {
            for (int i = 0; i < river.segments.Length; i++)
            {
                if (river.segments[i].children.Count == 0)
                {
                    river.segments[i].children.Add(river.segments[i]);
                }
            }
        }
    }

    // Checks if a curve is too steep to interpolate.
    public void ValidateSegments(List<River> riverList)
    {
        foreach (River river in riverList)
        {
            foreach (RiverSegment segment in river.segments)
            {
                // If it's the last segment invalidate it.
                if (segment.children[0] == segment)
                {
                    segment.parentInvalid = true;
                    continue;
                }

                if (segment.parent == segment)
                {
                    segment.parentInvalid = true;
                    continue;
                }

                int index = river.segments.IndexOf(segment);

                if (index == 0)
                {
                    float projection = RiverMath.GetProjection(segment.startPoint, segment.midPoint, river.parentRiver.segments[segmentsInRiver - 1].midPoint);

                    if (projection < 0.2 || projection > 0.8)
                    {
                        segment.parentInvalid = true;
                    }

                    continue;
                }

                float projection2 = RiverMath.GetProjection(segment.startPoint, segment.midPoint, river.segments[index - 1].midPoint);

                if (projection2 < 0.2 || projection2 > 0.8)
                {
                    segment.parentInvalid = true;
                }
            }
        }
    }

    /// <summary>
    /// Threshold is how many river steps are taken before splitting can occur.
    /// </summary>
    public void GenerateRiver(int width, int depth, TectonicZone zone, int stage, int threshold, River parentRiver, List<River> riverList, List<TectonicZone> pathedZones)
    {
        threshold--;

        if (parentRiver != null) parentRiver.speed = riverSpeed;

        pathedZones.Add(zone);

        // Get all 8 surrounding regions.
        List<TectonicZone> closeZonesUnsorted = new()
        {
            zones[Math.Clamp(zone.xIndex + 1, 0, zonesInPlate - 1), zone.zIndex],
            zones[Math.Clamp(zone.xIndex - 1, 0, zonesInPlate - 1), zone.zIndex],
            zones[zone.xIndex, Math.Clamp(zone.zIndex + 1, 0, zonesInPlate - 1)],
            zones[zone.xIndex, Math.Clamp(zone.zIndex - 1, 0, zonesInPlate - 1)]
        };

        // Filter out matching zones.
        List<TectonicZone> closeZones = new();
        foreach (TectonicZone tecZone in closeZonesUnsorted)
        {
            if (closeZones.Contains(tecZone)) continue;
            closeZones.Add(tecZone);
        }

        // Get regions that are higher, sorts height difference from highest to lowest.
        closeZones = closeZones
            .Where(closeZone => closeZone.height > zone.height && closeZone.pathedTo == false)
            .OrderByDescending(closeRegion => closeRegion.height - zone.height)
            .ToList();

        // Don't split until threshold met. Don't split at the start. Only split if 2 valid zones.
        if (closeZones.Count > 1 && rand.NextInt(100) < riverSplitChance && threshold < 0 && stage > 0)
        {
            River newRiver = new(zone.localZoneCenterPosition, closeZones[0].localZoneCenterPosition);
            River secondRiver = new(zone.localZoneCenterPosition, closeZones[1].localZoneCenterPosition);

            closeZones[0].pathedTo = true;
            closeZones[1].pathedTo = true;

            newRiver.parentRiver = parentRiver;
            secondRiver.parentRiver = parentRiver;

            riverList.Add(newRiver);
            riverList.Add(secondRiver);

            zone.rivers.Add(newRiver);
            zone.rivers.Add(secondRiver);

            GenerateRiver(width, depth, closeZones[0], stage - 1, threshold, newRiver, riverList, pathedZones);
            GenerateRiver(width, depth, closeZones[1], stage - 1, threshold, secondRiver, riverList, pathedZones);
        }
        else if (closeZones.Count > 0 && stage > 0)
        {
            River newRiver = new(zone.localZoneCenterPosition, closeZones[0].localZoneCenterPosition);

            closeZones[0].pathedTo = true;

            newRiver.parentRiver = parentRiver;

            riverList.Add(newRiver);

            zone.rivers.Add(newRiver);

            GenerateRiver(width, depth, closeZones[0], stage - 1, threshold, newRiver, riverList, pathedZones);
        }
        else if (parentRiver != null)
        {
            parentRiver.end = true;

            // Need sin wave distortion for lakes?
            if (rand.NextInt(100) < lakeChance)
            {
                AddLake(zone, 0, 25, 50);
            }
        }
    }

    public void AddLake(TectonicZone zone, int offset, int minSize, int maxSize)
    {
        int lakeSize = rand.NextInt(maxSize - minSize) + minSize;
        Vec2d riverPosition = new(zone.localZoneCenterPosition.X + (-offset + rand.NextInt(offset * 2 + 1)), zone.localZoneCenterPosition.Y + (-offset + rand.NextInt(offset * 2 + 1))); // Offset it randomly up to a point.
        River lake = new(riverPosition, new Vec2d(riverPosition.X + (-lakeSize + rand.NextInt(lakeSize * 2)), riverPosition.Y + (-lakeSize + rand.NextInt(lakeSize * 2))))
        {
            startSize = lakeSize,
            endSize = lakeSize,

            segments = new RiverSegment[1]
        }; // End is 100 blocks in a random direction.
        lake.segments[0] = new RiverSegment(lake.startPoint, lake.endPoint, lake.startPoint + (lake.endPoint - lake.startPoint) / 2);
        lake.segments[0].parent = lake.segments[0];
        lake.segments[0].children.Add(lake.segments[0]);
        lake.segments[0].parentInvalid = true;

        lake.speed = 0;

        lake.segments[0].river = lake;

        lake.lake = true;

        zone.rivers.Add(lake);
    }

    /// <summary>
    /// Gets zones in a radius around a zone.
    /// </summary>
    public List<TectonicZone> GetZonesAround(int localZoneX, int localZoneZ, int radius = 1)
    {
        List<TectonicZone> zonesListerino = new();

        for (int x = -radius; x <= radius; x++)
        {
            for (int z = -radius; z <= radius; z++)
            {
                if (localZoneX + x < 0 || localZoneX + x > zonesInPlate - 1 || localZoneZ + z < 0 || localZoneZ + z > zonesInPlate - 1) continue;

                zonesListerino.Add(zones[localZoneX + x, localZoneZ + z]);
            }
        }

        return zonesListerino;
    }
}