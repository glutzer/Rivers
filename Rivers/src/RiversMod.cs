using HarmonyLib;
using Vintagestory.API.Common;

public class RiversMod : ModSystem
{
    public Harmony harmony;
    public static bool patched = false;
    public bool devEnvironment = true;

    public override double ExecuteOrder()
    {
        return 0;
    }

    public override void StartPre(ICoreAPI api)
    {
        if (!patched)
        {
            harmony = new Harmony("rivers");
            harmony.PatchAll();
            patched = true;
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
}
