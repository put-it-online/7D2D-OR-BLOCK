using HarmonyLib;

/// <summary>
/// Patch the TileEntity creation to use our PowerItemORGate for our block type.
///
/// When a powered block is placed, TileEntityPowered.InitializePowerData() creates
/// the PowerItem. We patch this to create our custom type for our block.
///
/// NOTE: The exact method name may differ between game versions. Common candidates:
/// - TileEntityPowered.InitializePowerData()
/// - TileEntityPoweredBlock.CreatePowerItem()
/// - TileEntityPowered.SetValuesFromBlock()
///
/// After decompiling, verify the correct method and adjust this patch.
/// The key is: wherever the game does `new PowerConsumer()` for our block,
/// we replace it with `new PowerItemORGate()`.
/// </summary>
[HarmonyPatch(typeof(TileEntityPowered))]
[HarmonyPatch("InitializePowerData")]
public class TileEntityPowered_InitializePowerData_Patch
{
    static void Postfix(TileEntityPowered __instance)
    {
        // Check if this tile entity belongs to our OR gate block
        BlockValue blockValue = __instance.blockValue;
        Block block = blockValue.Block;

        if (block.Properties.Values.ContainsKey("IsORGate") &&
            block.Properties.Values["IsORGate"] == "true")
        {
            // Replace the PowerItem with our custom OR gate version
            // We need to swap out the PowerConsumer that was created
            if (__instance.PowerItem != null && !(__instance.PowerItem is PowerItemORGate))
            {
                PowerItemORGate orGate = new PowerItemORGate();
                // Copy over essential properties from the existing power item
                orGate.Position = __instance.PowerItem.Position;
                orGate.BlockID = __instance.PowerItem.BlockID;

                // Remove old power item from power manager
                PowerManager.Instance.RemovePowerNode(__instance.PowerItem);

                // Set our new power item
                __instance.PowerItem = orGate;
                orGate.TileEntity = __instance;

                // Register with power manager
                PowerManager.Instance.AddPowerNode(orGate);

                Log.Out("[ORBlock] Created PowerItemORGate at " + orGate.Position);
            }
        }
    }
}

/// <summary>
/// Patch to handle the activation/interaction text for the OR gate block.
/// Shows "OR Gate: Input 1 [status] | Input 2 [status]" when looking at the block.
/// </summary>
[HarmonyPatch(typeof(Block))]
[HarmonyPatch("GetActivationText")]
public class Block_GetActivationText_Patch
{
    static void Postfix(Block __instance, ref string __result,
        WorldBase _world, BlockValue _blockValue, int _clrIdx,
        Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        if (__instance.Properties.Values.ContainsKey("IsORGate") &&
            __instance.Properties.Values["IsORGate"] == "true")
        {
            PowerItem powerItem = PowerManager.Instance.GetPowerItemByWorldPos(_blockPos);
            if (powerItem is PowerItemORGate orGate)
            {
                string input1 = orGate.Parent != null ? "Connected" : "Empty";
                string input2 = orGate.SecondParent != null ? "Connected" : "Empty";
                string output = orGate.IsEitherInputPowered() ? "ON" : "OFF";
                __result = string.Format("OR Gate [Input 1: {0}] [Input 2: {1}] [Output: {2}]",
                    input1, input2, output);
            }
        }
    }
}
