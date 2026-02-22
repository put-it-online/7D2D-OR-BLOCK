using HarmonyLib;
using UnityEngine;

/// <summary>
/// Patch block activation to handle the disconnect action.
/// When the player interacts with the OR gate while crouching (sneak + activate),
/// it disconnects all inputs. Normal interaction does the default behavior (wire tool).
/// </summary>
[HarmonyPatch(typeof(Block))]
[HarmonyPatch("OnBlockActivated")]
[HarmonyPatch(new[] { typeof(string), typeof(WorldBase), typeof(int),
    typeof(Vector3i), typeof(BlockValue), typeof(EntityPlayerLocal) })]
public class Block_OnBlockActivated_Patch
{
    static bool Prefix(string _commandName, WorldBase _world, int _cIdx,
        Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player,
        ref bool __result)
    {
        Block block = _blockValue.Block;
        if (!block.Properties.Values.ContainsKey("IsORGate") ||
            block.Properties.Values["IsORGate"] != "true")
            return true; // not our block

        // Check if player is crouching/sneaking — this triggers disconnect
        // movementInput is a public field on EntityPlayerLocal (type MovementInput)
        // MovementInput.down is a public bool field (all lowercase)
        if (_player.movementInput.down && _commandName == "activate")
        {
            PowerItem powerItem = PowerManager.Instance.GetPowerItemByWorldPos(_blockPos);
            if (powerItem is PowerItemORGate orGate)
            {
                orGate.DisconnectAllInputs();
                // GameManager.ShowTooltip is a static method:
                // public static void ShowTooltip(EntityPlayerLocal _player, string _text, ...)
                GameManager.ShowTooltip(_player, "OR Gate: All inputs disconnected");
                __result = true;
                return false; // skip original
            }
        }

        return true; // let vanilla handle
    }
}
