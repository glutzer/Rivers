using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

/// <summary>
/// Transpiler results not guaranteed.
/// </summary>
public class LiquidTesselatorPatch
{
    public static void TesselateFlow(float[] upFlowVectors, TCTCache vars)
    {
        TCTCacheTwo varsTwo = (TCTCacheTwo)vars;

        if (varsTwo.flowVectorsX != null) //Check if the chunk can even have a river
        {
            float xFlow = varsTwo.flowVectorsX[vars.posZ % 32 * 32 + vars.posX % 32]; //These are normalized vectors multiplied by the speed?
            float zFlow = varsTwo.flowVectorsZ[vars.posZ % 32 * 32 + vars.posX % 32]; //Z * 32 + X, 2d index

            if (xFlow != 0 || zFlow != 0)
            {
                upFlowVectors[0] = xFlow;
                upFlowVectors[1] = zFlow;
                upFlowVectors[2] = xFlow;
                upFlowVectors[3] = zFlow;
                upFlowVectors[4] = xFlow;
                upFlowVectors[5] = zFlow;
                upFlowVectors[6] = xFlow;
                upFlowVectors[7] = zFlow;
            }
        }
    }

    [HarmonyPatch(typeof(LiquidTesselator))]
    [HarmonyPatch("Tesselate")]
    public static class LiquidTesselateTranspiler
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            List<CodeInstruction> code = new(instructions);
            int insertionIndex = -1;

            //Need label if it needs to jump to something
            //Label nextLabel = il.DefineLabel();

            for (int i = 4; i < code.Count - 4; i++) //-1 since checking i + 1
            {
                if (code[i].opcode == OpCodes.Ldloc_S && code[i + 1].opcode == OpCodes.Ldnull && code[i + 2].opcode == OpCodes.Call && code[i + 2].operand == AccessTools.Method(typeof(Vec3i), "op_Inequality"))
                {
                    insertionIndex = i;
                    //code[i].labels.Add(nextLabel); //+1 since insertion is after
                    break;
                }
            }

            List<CodeInstruction> ins = new()
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(LiquidTesselator), "upFlowVectors")),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LiquidTesselatorPatch), "TesselateFlow"))
            };

            if (insertionIndex != -1)
            {
                code.InsertRange(insertionIndex, ins);
            }

            return code;
        }
    }
}
