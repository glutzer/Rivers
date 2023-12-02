using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

/// <summary>
/// Fixes hot springs and such not emitting particles.
/// </summary>
public class BlockParticlePatches
{
    [HarmonyPatch(typeof(BlockBehaviorSteaming), "ShouldReceiveClientParticleTicks")]
    public static class ShouldReceiveClientParticleTicksPrefix
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result, ref EnumHandling handling)
        {
            handling = EnumHandling.Handled;
            __result = true;
            return false;
        }
    }

    /// <summary>
    /// If of type handled it will always use that result unless prevent subsequent has been called already.
    /// </summary>
    [HarmonyPatch(typeof(Block), "ShouldReceiveClientParticleTicks")]
    public static class BlockShouldReceiveClientParticleTicksPrefix
    {
        [HarmonyPrefix]
        public static bool Prefix(Block __instance, ref bool __result, IWorldAccessor world, IPlayer player, BlockPos pos, ref bool isWindAffected)
        {
            bool result = true;
            bool preventDefault = false;
            isWindAffected = false;

            foreach (BlockBehavior behavior in __instance.BlockBehaviors)
            {
                EnumHandling handled = EnumHandling.PassThrough;

                bool behaviorResult = behavior.ShouldReceiveClientParticleTicks(world, player, pos, ref handled);
                if (handled != EnumHandling.PassThrough)
                {
                    if (handled == EnumHandling.Handled)
                    {
                        __result = behaviorResult;
                        return false;
                    }

                    result &= behaviorResult;
                    preventDefault = true;
                }

                if (handled == EnumHandling.PreventSubsequent)
                {
                    __result = result;
                    return false;
                }
            }

            if (preventDefault) return result;

            if (__instance.ParticleProperties != null && __instance.ParticleProperties.Length > 0)
            {
                for (int i = 0; i < __instance.ParticleProperties.Length; i++) isWindAffected |= __instance.ParticleProperties[0].WindAffectednes > 0;

                __result = true;
                return false;
            }

            __result = false;
            return false;
        }
    }
}