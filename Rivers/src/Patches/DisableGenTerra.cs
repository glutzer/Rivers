using HarmonyLib;
using Vintagestory.ServerMods;

public class DisableGenTerra
{
    [HarmonyPatch(typeof(GenTerra))]
    [HarmonyPatch("StartServerSide")]
    public static class GenTerraDisable
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return false;
        }
    }
}