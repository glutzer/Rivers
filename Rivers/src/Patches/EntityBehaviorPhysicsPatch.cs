using HarmonyLib;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

/// <summary>
/// Physics for entities in rivers.
/// Adjust speed here.
/// Might need a transpiler for performance?
/// Might need to exclude entities so they don't pile up on edges?
/// </summary>
public class EntityBehaviorPhysicsPatch
{
    [HarmonyPatch(typeof(EntityBehaviorPassivePhysics))]
    [HarmonyPatch("DoPhysics")]
    public static class DoPhysicsPrefix
    {
        [HarmonyPrefix]
        public static bool Prefix(EntityBehaviorPassivePhysics __instance, float dt, EntityPos pos)
        {
            if (__instance.entity.FeetInLiquid || __instance.entity.Swimming)
            {
                IWorldChunk chunk = __instance.entity.Api.World.BlockAccessor.GetChunk((int)pos.X / 32, 0, (int)pos.Z / 32);
                float[] flowVectorsX = chunk?.GetModdata<float[]>("flowVectorsX");

                if (flowVectorsX != null)
                {
                    float density = 300f / GameMath.Clamp(__instance.entity.MaterialDensity, 750f, 2500f) * (60 * dt); //Calculate density

                    float[] flowVectorsZ = chunk.GetModdata<float[]>("flowVectorsZ");

                    pos.Motion.Add(flowVectorsX[LocalChunkIndex2D((int)pos.X % 32, (int)pos.Z % 32)] * 0.001 * (double)density, 0, flowVectorsZ[LocalChunkIndex2D((int)pos.X % 32, (int)pos.Z % 32)] * 0.001 * (double)density);
                }
            }

            return true;
        }

        public static int LocalChunkIndex2D(int localX, int localZ)
        {
            return localZ * 32 + localX;
        }
    }

    [HarmonyPatch(typeof(EntityInLiquid))]
    [HarmonyPatch("DoApply")]
    public static class DoApplyPrefix
    {
        public static bool Prefix(EntityInLiquid __instance, float dt, Entity entity, EntityPos pos, EntityControls controls)
        {
            if (entity.Swimming && entity.Alive)
            {
                __instance.CallMethod("HandleSwimming", dt, entity, pos, controls);
            }

            Block block = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)pos.Y, (int)pos.Z, BlockLayersAccess.Fluid);
            if (block.PushVector != null)
            {
                if (block.PushVector.Y >= 0 || !entity.World.BlockAccessor.IsSideSolid((int)pos.X, (int)pos.Y - 1, (int)pos.Z, BlockFacing.UP))
                {
                    pos.Motion.Add(block.PushVector);
                }
            }

            if (entity is EntityPlayer)
            {
                IWorldChunk chunk = entity.Api.World.BlockAccessor.GetChunk((int)pos.X / 32, 0, (int)pos.Z / 32);
                float[] flowVectorsX = chunk?.GetModdata<float[]>("flowVectorsX");

                if (flowVectorsX != null)
                {
                    float density = 300f / GameMath.Clamp(entity.MaterialDensity, 750f, 2500f) * (60 * dt); //Calculate density

                    float[] flowVectorsZ = chunk.GetModdata<float[]>("flowVectorsZ");

                    pos.Motion.Add(flowVectorsX[LocalChunkIndex2D((int)pos.X % 32, (int)pos.Z % 32)] * 0.0025 * density, 0, flowVectorsZ[LocalChunkIndex2D((int)pos.X % 32, (int)pos.Z % 32)] * 0.0025 * density);
                }
            }
            
            return false;
        }

        public static int LocalChunkIndex2D(int localX, int localZ)
        {
            return localZ * 32 + localX;
        }
    }

    [HarmonyPatch(typeof(EntityBoat))]
    [HarmonyPatch("updateBoatAngleAndMotion")]
    public static class updateBoatAngleAndMotionPostfix
    {
        public static void Postfix(EntityBoat __instance)
        {
            if (__instance.ForwardSpeed != 0.0)
            {
                IWorldChunk chunk = __instance.Api.World.BlockAccessor.GetChunk((int)__instance.SidedPos.X / 32, 0, (int)__instance.SidedPos.Z / 32);
                float[] flowVectorsX = chunk?.GetModdata<float[]>("flowVectorsX");

                if (flowVectorsX != null)
                {
                    float[] flowVectorsZ = chunk.GetModdata<float[]>("flowVectorsZ");

                    __instance.SidedPos.Motion.Add(flowVectorsX[LocalChunkIndex2D((int)__instance.SidedPos.X % 32, (int)__instance.SidedPos.Z % 32)] * 0.01, 0, flowVectorsZ[LocalChunkIndex2D((int)__instance.SidedPos.X % 32, (int)__instance.SidedPos.Z % 32)] * 0.01);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LocalChunkIndex2D(int localX, int localZ)
        {
            return localZ * 32 + localX;
        }
    }
}