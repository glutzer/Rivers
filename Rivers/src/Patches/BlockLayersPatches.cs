using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

public class BlockLayersPatches
{
    public static float[] flowVectorsX;
    public static float[] flowVectorsZ;

    /// <summary>
    /// Before the XZ loop, retrieve arrays.
    /// In XZ loop, check if either of the arrays != 0. If this is the case raise is 0.
    /// Nullify in postfix.
    /// </summary>
    [HarmonyPatch(typeof(GenBlockLayers))]
    [HarmonyPatch("OnChunkColumnGeneration")]
    public static class OnChunkColumnGenerationTranspiler
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            List<CodeInstruction> code = new(instructions);

            int insertionIndex = -1;
            object xOperand = null;
            object zOperand = null;

            for (int i = 4; i < code.Count - 4; i++)
            {
                if (code[i].opcode == OpCodes.Newobj && code[i].operand == typeof(BlockPos).GetConstructor(new Type[] { }) && code[i + 1].opcode == OpCodes.Stloc_S && code[i + 2].opcode == OpCodes.Ldc_I4_0)
                {
                    insertionIndex = i;
                    xOperand = code[i + 3].operand;
                    zOperand = code[i + 6].operand;
                    break;
                }
            }

            List<CodeInstruction> ins = new()
            {
                new CodeInstruction(OpCodes.Ldloc_0),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BlockLayersPatches), "SetVectors"))
            };

            if (insertionIndex != -1)
            {
                code.InsertRange(insertionIndex, ins);
            }

            //Second part

            insertionIndex = -1;
            object seaLevelOperand = null;
            Label returnLabel = il.DefineLabel();

            for (int i = 4; i < code.Count - 4; i++)
            {
                if (code[i].opcode == OpCodes.Stloc_S && code[i - 1].opcode == OpCodes.Conv_I4 && code[i - 2].opcode == OpCodes.Call && code[i - 2].operand == AccessTools.Method(typeof(Math), "Min", new Type[] { typeof(float), typeof(float) }))
                {
                    seaLevelOperand = code[i].operand;
                    insertionIndex = i + 1;
                    code[i + 1].labels.Add(returnLabel);
                    break;
                }
            }

            ins = new()
            {
                new CodeInstruction(OpCodes.Ldloc_S, xOperand),
                new CodeInstruction(OpCodes.Ldloc_S, zOperand),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BlockLayersPatches), "IsRiver")),
                new CodeInstruction(OpCodes.Brfalse_S, returnLabel),
                new CodeInstruction(OpCodes.Ldc_I4_0),
                new CodeInstruction(OpCodes.Stloc_S, seaLevelOperand)
            };

            if (insertionIndex != -1)
            {
                code.InsertRange(insertionIndex, ins);
            }

            return code;
        }
    }

    [HarmonyPatch(typeof(GenBlockLayers))]
    [HarmonyPatch("OnChunkColumnGeneration")]
    public static class OnChunkColumnGenerationPostfix
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            flowVectorsX = null;
            flowVectorsZ = null;
        }
    }

    public static void SetVectors(IServerChunk[] chunks)
    {
        flowVectorsX = chunks[0].GetModdata<float[]>("flowVectorsX");
        flowVectorsZ = chunks[0].GetModdata<float[]>("flowVectorsZ");
    }

    public static bool IsRiver(int localX, int localZ)
    {
        if (flowVectorsX == null) return false;

        if (flowVectorsX[localZ * 32 + localX] != 0 || flowVectorsZ[localZ * 32 + localX] != 0) return true;

        return false;
    }
}