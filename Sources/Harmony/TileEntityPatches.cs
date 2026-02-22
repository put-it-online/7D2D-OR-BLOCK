using HarmonyLib;

/// <summary>
/// Patch TileEntityPowered.InitializePowerData() to create PowerItemORGate
/// for our Logic Relay block instead of the default PowerConsumer.
/// </summary>
[HarmonyPatch(typeof(TileEntityPowered))]
[HarmonyPatch("InitializePowerData")]
public class TileEntityPowered_InitializePowerData_Patch
{
    static void Postfix(TileEntityPowered __instance)
    {
        BlockValue blockValue = __instance.blockValue;
        Block block = blockValue.Block;

        if (block.Properties.Values.ContainsKey("IsLogicRelay") &&
            block.Properties.Values["IsLogicRelay"] == "true")
        {
            if (__instance.PowerItem != null && !(__instance.PowerItem is PowerItemORGate))
            {
                PowerItemORGate orGate = new PowerItemORGate();
                orGate.Position = __instance.PowerItem.Position;
                orGate.BlockID = __instance.PowerItem.BlockID;

                PowerManager.Instance.RemovePowerNode(__instance.PowerItem);

                __instance.PowerItem = orGate;
                orGate.TileEntity = __instance;

                PowerManager.Instance.AddPowerNode(orGate);

                Log.Out("[ORBlock] Created PowerItemORGate at " + orGate.Position);
            }
        }
    }
}
