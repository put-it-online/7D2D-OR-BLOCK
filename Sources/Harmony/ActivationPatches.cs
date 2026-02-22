using HarmonyLib;

/// <summary>
/// Provides activation commands for Logic Relay blocks via Harmony patches.
/// The game cannot find custom Block classes in mod DLLs (Type.GetType only searches
/// Assembly-CSharp.dll), so we patch Block/BlockPowered methods instead of using a
/// custom class. Each patch checks the IsLogicRelay property before acting, leaving
/// all other blocks unaffected.
///
/// Method ownership in Assembly-CSharp.dll:
///   Block.OnBlockActivated(WorldBase, int, Vector3i, BlockValue, EntityPlayerLocal) — bool overload
///   Block.HasBlockActivationCommands(...)
///   Block.GetBlockActivationCommands(...)
///   Block.GetActivationText(...)
///   Block.OnBlockActivated(string, WorldBase, int, Vector3i, BlockValue, EntityPlayerLocal) — command overload
///
///   BlockPowered overrides: HasBlockActivationCommands, GetBlockActivationCommands,
///                           GetActivationText, and the string-command OnBlockActivated.
///   BlockPowered does NOT override the bool-return OnBlockActivated — that lives only
///   on Block. Patching typeof(BlockPowered) for a method it does not declare causes
///   Harmony to throw at patch time, which is why that patch must target typeof(Block).
///
/// Radial menu flow:
///   E pressed → HasBlockActivationCommands returns true
///             → GetBlockActivationCommands provides command list
///             → player selects → OnBlockActivated(string, ...) handles command
///   The bool-return OnBlockActivated is for immediate single-press interactions
///   (loot containers, pickup). It is NOT the entry point for the radial menu.
/// </summary>

// ---------------------------------------------------------------------------
// 1. Bool-return OnBlockActivated — MUST patch typeof(Block), not BlockPowered.
//    BlockPowered does not override this method; Harmony would throw if we
//    targeted BlockPowered for a method that only exists on Block.
//    Returning true here is not needed for the radial menu (HasBlockActivation-
//    Commands drives that), but the patch is kept as a no-op guard so the block
//    does not accidentally trigger pickup/loot behaviour on E press.
// ---------------------------------------------------------------------------
/// <summary>
/// Intercepts the immediate E-press handler on Block (not BlockPowered).
/// For Logic Relay blocks, returns false to skip the default pickup / loot logic
/// and lets the radial menu path (driven by HasBlockActivationCommands) take over.
/// </summary>
[HarmonyPatch(typeof(Block))]
[HarmonyPatch("OnBlockActivated")]
[HarmonyPatch(new[] { typeof(WorldBase), typeof(int), typeof(Vector3i), typeof(BlockValue), typeof(EntityPlayerLocal) })]
public class Block_OnBlockActivated_Bool_Patch
{
    static bool Prefix(
        Block __instance,
        ref bool __result,
        WorldBase _world,
        int _clrIdx,
        Vector3i _blockPos,
        BlockValue _blockValue,
        EntityPlayerLocal _player)
    {
        Block block = _blockValue.Block;
        if (!block.Properties.Values.ContainsKey("IsLogicRelay") ||
            block.Properties.Values["IsLogicRelay"] != "true")
            return true;

        Log.Out("[LogicRelay] Block_OnBlockActivated_Bool_Patch fired at " + _blockPos);

        // Let the original method run — it contains the logic that checks
        // HasBlockActivationCommands and opens the radial menu.
        return true;
    }
}

// ---------------------------------------------------------------------------
// 2. HasBlockActivationCommands — patch on BlockPowered (which overrides it).
//    BlockPowered always returns true, which would also work for us, but we
//    patch it anyway so we can log that the method fires and confirm patching
//    succeeded. Returning true is what causes the game to open the radial menu.
// ---------------------------------------------------------------------------
/// <summary>
/// Ensures the game knows the Logic Relay block has activation commands,
/// which is what triggers the radial menu on E press.
/// </summary>
[HarmonyPatch(typeof(BlockPowered))]
[HarmonyPatch("HasBlockActivationCommands")]
public class BlockPowered_HasBlockActivationCommands_Patch
{
    static bool Prefix(
        BlockPowered __instance,
        ref bool __result,
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

        Log.Out("[LogicRelay] HasBlockActivationCommands fired at " + _blockPos);
        __result = true;
        return false;
    }
}

// ---------------------------------------------------------------------------
// 3. GetBlockActivationCommands — patch on BlockPowered (which overrides it).
//    Returns toggle_mode and disconnect commands, enabled when the player can
//    place blocks at the target position (mirrors BlockTimerRelay pattern).
// ---------------------------------------------------------------------------
/// <summary>
/// Returns the activation commands shown in the radial menu.
/// Commands are enabled when the player can place blocks at the position
/// (same check as vanilla BlockTimerRelay — blocked only in trader areas).
/// </summary>
[HarmonyPatch(typeof(BlockPowered))]
[HarmonyPatch("GetBlockActivationCommands")]
public class BlockPowered_GetBlockActivationCommands_Patch
{
    static readonly BlockActivationCommand[] relayCmds = new BlockActivationCommand[]
    {
        new BlockActivationCommand("toggle_mode", "electric_switch", false, false),
        new BlockActivationCommand("disconnect",  "x", false, false)
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

        Log.Out("[LogicRelay] GetBlockActivationCommands fired at " + _blockPos);

        bool canPlace = _world.CanPlaceBlockAt(_blockPos,
            _world.GetGameManager().GetPersistentLocalPlayer());

        relayCmds[0].enabled = canPlace;
        relayCmds[1].enabled = canPlace;

        __result = relayCmds;
        return false;
    }
}

// ---------------------------------------------------------------------------
// 4. GetActivationText — patch on BlockPowered (which overrides it).
//    Shows mode, input connection state, and output state in hover text.
// ---------------------------------------------------------------------------
/// <summary>
/// Returns the hover text shown when the player looks at the block.
/// Displays the current mode, input connection state, and output state.
/// </summary>
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

        Log.Out("[LogicRelay] GetActivationText fired at " + _blockPos);

        PowerItem powerItem = PowerManager.Instance.GetPowerItemByWorldPos(_blockPos);
        if (powerItem is PowerItemLogicRelay logicRelay)
        {
            string mode   = logicRelay.Mode == LogicRelayMode.OR ? "OR" : "AND";
            string input1 = logicRelay.Parent       != null ? "Connected" : "Empty";
            string input2 = logicRelay.SecondParent != null ? "Connected" : "Empty";
            string output = logicRelay.IsOutputPowered() ? "ON" : "OFF";
            __result = string.Format(
                "Logic Relay [Mode: {0}] [Input 1: {1}] [Input 2: {2}] [Output: {3}]",
                mode, input1, input2, output);
        }
        else
        {
            __result = "Logic Relay";
        }
        return false;
    }
}

// ---------------------------------------------------------------------------
// 5. OnBlockActivated (string command) — patch on BlockPowered (which overrides it).
//    Handles toggle_mode and disconnect commands from the radial menu.
// ---------------------------------------------------------------------------
/// <summary>
/// Handles a command selected from the radial menu.
/// "toggle_mode" switches between OR and AND logic.
/// "disconnect"  removes all wired inputs from the Logic Relay.
/// </summary>
[HarmonyPatch(typeof(BlockPowered))]
[HarmonyPatch("OnBlockActivated")]
[HarmonyPatch(new[] { typeof(string), typeof(WorldBase), typeof(int), typeof(Vector3i), typeof(BlockValue), typeof(EntityPlayerLocal) })]
public class BlockPowered_OnBlockActivated_Command_Patch
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

        Log.Out("[LogicRelay] OnBlockActivated command='" + _commandName + "' at " + _blockPos);

        PowerItem powerItem = PowerManager.Instance.GetPowerItemByWorldPos(_blockPos);
        if (!(powerItem is PowerItemLogicRelay logicRelay))
        {
            __result = false;
            return false;
        }

        switch (_commandName)
        {
            case "toggle_mode":
                logicRelay.ToggleMode();
                string modeName = logicRelay.Mode == LogicRelayMode.OR ? "OR" : "AND";
                GameManager.ShowTooltip(_player, "Logic Relay: Mode set to " + modeName);
                __result = true;
                return false;

            case "disconnect":
                logicRelay.DisconnectAllInputs();
                GameManager.ShowTooltip(_player, "Logic Relay: All inputs disconnected");
                __result = true;
                return false;

            default:
                __result = false;
                return false;
        }
    }
}
