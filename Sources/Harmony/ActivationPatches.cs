using HarmonyLib;
using UnityEngine;

/// <summary>
/// Provides activation commands for Logic Relay blocks via Harmony patches.
/// The game cannot find custom Block classes in mod DLLs (Type.GetType only searches
/// Assembly-CSharp.dll), so we patch BlockPowered methods instead of using a custom class.
/// </summary>

[HarmonyPatch(typeof(BlockPowered))]
[HarmonyPatch("GetBlockActivationCommands")]
public class BlockPowered_GetBlockActivationCommands_Patch
{
    static readonly BlockActivationCommand[] relayCmds = new BlockActivationCommand[]
    {
        new BlockActivationCommand("toggle_mode", "electric_switch", false, false),
        new BlockActivationCommand("disconnect", "electric_disconnect", false, false)
    };

    static bool Prefix(
        BlockPowered __instance,
        ref BlockActivationCommand[] __result,
        WorldBase _world,
        BlockValue _blockValue,
        int _clrIdx,
        Vector3i _blockPos,
        EntityAlive _entityFocusing)
    {
        Block block = _blockValue.Block;
        if (!block.Properties.Values.ContainsKey("IsLogicRelay") ||
            block.Properties.Values["IsLogicRelay"] != "true")
            return true;

        bool isUsable = _world.IsMyLandProtectedBlock(_blockPos,
            _world.GetGameManager().GetPersistentLocalPlayer());

        relayCmds[0].enabled = isUsable;
        relayCmds[1].enabled = isUsable;

        __result = relayCmds;
        return false;
    }
}

[HarmonyPatch(typeof(BlockPowered))]
[HarmonyPatch("GetActivationText")]
public class BlockPowered_GetActivationText_Patch
{
    static bool Prefix(
        BlockPowered __instance,
        ref string __result,
        WorldBase _world,
        BlockValue _blockValue,
        int _clrIdx,
        Vector3i _blockPos,
        EntityAlive _entityFocusing)
    {
        Block block = _blockValue.Block;
        if (!block.Properties.Values.ContainsKey("IsLogicRelay") ||
            block.Properties.Values["IsLogicRelay"] != "true")
            return true;

        PowerItem powerItem = PowerManager.Instance.GetPowerItemByWorldPos(_blockPos);
        if (powerItem is PowerItemORGate orGate)
        {
            string mode = orGate.Mode == GateMode.OR ? "OR" : "AND";
            string input1 = orGate.Parent != null ? "Connected" : "Empty";
            string input2 = orGate.SecondParent != null ? "Connected" : "Empty";
            string output = orGate.IsOutputPowered() ? "ON" : "OFF";
            __result = string.Format("Logic Relay [Mode: {0}] [Input 1: {1}] [Input 2: {2}] [Output: {3}]",
                mode, input1, input2, output);
        }
        else
        {
            __result = "Logic Relay";
        }
        return false;
    }
}

[HarmonyPatch(typeof(BlockPowered))]
[HarmonyPatch("OnBlockActivated")]
[HarmonyPatch(new[] { typeof(string), typeof(WorldBase), typeof(int), typeof(Vector3i), typeof(BlockValue), typeof(EntityPlayerLocal) })]
public class BlockPowered_OnBlockActivated_Patch
{
    static bool Prefix(
        BlockPowered __instance,
        ref bool __result,
        string _commandName,
        WorldBase _world,
        int _cIdx,
        Vector3i _blockPos,
        BlockValue _blockValue,
        EntityPlayerLocal _player)
    {
        Block block = _blockValue.Block;
        if (!block.Properties.Values.ContainsKey("IsLogicRelay") ||
            block.Properties.Values["IsLogicRelay"] != "true")
            return true;

        PowerItem powerItem = PowerManager.Instance.GetPowerItemByWorldPos(_blockPos);
        if (!(powerItem is PowerItemORGate orGate))
        {
            __result = false;
            return false;
        }

        switch (_commandName)
        {
            case "toggle_mode":
                orGate.ToggleMode();
                string modeName = orGate.Mode == GateMode.OR ? "OR" : "AND";
                GameManager.ShowTooltip(_player, "Logic Relay: Mode set to " + modeName);
                __result = true;
                return false;

            case "disconnect":
                orGate.DisconnectAllInputs();
                GameManager.ShowTooltip(_player, "Logic Relay: All inputs disconnected");
                __result = true;
                return false;

            default:
                __result = false;
                return false;
        }
    }
}
