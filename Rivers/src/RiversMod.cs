using HarmonyLib;
using Vintagestory.API.Common;

public class RiversMod : ModSystem
{
    public Harmony harmony;
    public static bool patched = false;
    public bool patchedLocal = false;
    public bool devEnvironment = false;

    public override double ExecuteOrder()
    {
        return 0;
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass("BlockRiverWaterWheel", typeof(BlockWaterWheel));
        api.RegisterBlockEntityClass("BERiverWaterWheel", typeof(BEWaterWheel));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorRiverWaterWheel", typeof(BEBehaviorWaterWheel));
    }

    public override void StartPre(ICoreAPI api)
    {
        if (!patched)
        {
            harmony = new Harmony("rivers");
            harmony.PatchAll();
            patched = true;
            patchedLocal = true;
        }

        string cfgFileName = "rivers.json";
        try
        {
            RiverConfig fromDisk;
            if ((fromDisk = api.LoadModConfig<RiverConfig>(cfgFileName)) == null || devEnvironment)
            {
                api.StoreModConfig(RiverConfig.Loaded, cfgFileName);
            }
            else
            {
                RiverConfig.Loaded = fromDisk;
            }
        }
        catch
        {
            api.StoreModConfig(RiverConfig.Loaded, cfgFileName);
        }
    }

    public override void Dispose()
    {
        ChunkTesselatorManagerPatch.bottomChunk = null;
        BlockLayersPatches.flowVectorsX = null;
        BlockLayersPatches.flowVectorsZ = null;
        if (patchedLocal)
        {
            harmony.UnpatchAll("rivers");
            patched = false;
            patchedLocal = false;
        }
    }
}