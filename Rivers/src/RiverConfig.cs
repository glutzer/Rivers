public class RiverConfig
{
    public static RiverConfig Loaded { get; set; } = new RiverConfig();

    //Very large for lots of rivers in vanilla
    public int zoneSize = 512;
    public int zonesInPlate = 64;
    public int zonePadding = 128;
    public int maxZoneTraversal = 50;
    public int minSegments = 2;

    public int riverGrowth = 2; //Amount river will grow traversing 1 zone
    public int riverSpawnChance = 50; //Chance for river to spawn at the edge of the coast
    public int riverSplitChance = 50; //Chance for river to split at the center of a region
    public int lakeChance = 30; //Chance for ends of rivers to spawn a lake
    public int segmentsInRiver = 2; //How many segments each river is composed of
    public double segmentOffset = 75; //How much to offset each segment along the river line

    public double riverDepth = 0.018; //Based on the square root of the river size
    public double baseDepth = 0.1; //Minimum depth

    public int heightBoost = 7;
    public float topFactor = 1;

    public int riverOctaves = 2;
    public float riverFrequency = 0.005f;
    public float riverLacunarity = 3;
    public float riverGain = 0.3f;
    public int riverStrength = 15;

    public float riverSpeed = 10;

    public double maxValleyWidth = 250;
    public double riverFloorVariation = 3;
    public float oceanThreshold = 30f;
}