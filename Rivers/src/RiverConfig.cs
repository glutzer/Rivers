public class RiverConfig
{
    public static RiverConfig Loaded { get; set; } = new RiverConfig();

    public bool boulders = true;

    // New values.
    public float minSize = 8;
    public float maxSize = 40;

    public int error = 1;

    public int minLength = 150;
    public int lengthVariation = 300;

    // Very large for lots of rivers in vanilla.
    public int zoneSize = 256;
    public int zonesInPlate = 128;
    public int minSegments = 2;

    public float riverGrowth = 2; // Amount river will grow traversing 1 zone.
    public int riverSpawnChance = 10; // Chance for river to spawn at the edge of the coast.
    public int riverSplitChance = 70; // Chance for river to split at the center of a region.
    public int lakeChance = 15; // Chance for ends of rivers to spawn a lake.
    public int segmentsInRiver = 2; // How many segments each river is composed of.
    public double segmentOffset = 50; // How much to offset each segment along the river line.

    public double riverDepth = 0.025; // Based on the square root of the river size.
    public double baseDepth = 0.1; // Minimum depth.

    public int heightBoost = 7;
    public float topFactor = 1;

    public int riverOctaves = 2;
    public float riverFrequency = 0.0075f;
    public float riverLacunarity = 3;
    public float riverGain = 0.3f;
    public int riverStrength = 12;

    public float riverSpeed = 8;

    public double maxValleyWidth = 200;

    public int riverFloorBase = -1;
    public double riverFloorVariation = 4;

    public float oceanThreshold = 30f;

    public float wheelSpeedMultiplier = 0.5f;
    public float wheelTorqueMultiplier = 1;

    public bool removeGravityBlocks = true;

    public bool clientParticles = true;
}