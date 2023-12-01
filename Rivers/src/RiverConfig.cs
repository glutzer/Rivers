public class RiverConfig
{
    public static RiverConfig Loaded { get; set; } = new RiverConfig();

    //Very large for lots of rivers in vanilla
    public int zoneSize = 512;
    public int zonesInPlate = 64;
    public int zonePadding = 100;

    public int riverGrowth = 5; //Amount river will grow traversing 1 zone
    public int riverSpawnChance = 100; //Chance for river to spawn at the edge of the coast
    public int riverSplitChance = 100; //Chance for river to split at the center of a region
    public int lakeChance = 30; //Chance for ends of rivers to spawn a lake
    public int segmentsInRiver = 3; //How many segments each river is composed of
    public double segmentOffset = 100; //How much to offset each segment along the river line

    public double riverDepth = 0.006; //Based on the square root of the river size
    public double baseDepth = 0.03; //Minimum depth
}