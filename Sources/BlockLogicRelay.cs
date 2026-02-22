using UnityEngine;

/// <summary>
/// Custom Block class for the Logic Relay.
/// Provides activation commands (Toggle Mode, Disconnect) and activation text.
/// The game maps Class="LogicRelay" in blocks.xml to this class (prefixes "Block").
/// </summary>
public class BlockLogicRelay : BlockPowered
{
    private readonly BlockActivationCommand[] cmds = new BlockActivationCommand[]
    {
        new BlockActivationCommand("toggle_mode", "electric_switch", false, false),
        new BlockActivationCommand("disconnect", "electric_disconnect", false, false)
    };

    public override BlockActivationCommand[] GetBlockActivationCommands(
        WorldBase _world,
        BlockValue _blockValue,
        int _clrIdx,
        Vector3i _blockPos,
        EntityAlive _entityFocusing)
    {
        bool isUsable = _world.IsMyLandProtectedBlock(_blockPos,
            _world.GetGameManager().GetPersistentLocalPlayer());

        cmds[0].enabled = isUsable; // toggle_mode
        cmds[1].enabled = isUsable; // disconnect

        return cmds;
    }

    public override string GetActivationText(
        WorldBase _world,
        BlockValue _blockValue,
        int _clrIdx,
        Vector3i _blockPos,
        EntityAlive _entityFocusing)
    {
        PowerItem powerItem = PowerManager.Instance.GetPowerItemByWorldPos(_blockPos);
        if (powerItem is PowerItemORGate orGate)
        {
            string mode = orGate.Mode == GateMode.OR ? "OR" : "AND";
            string input1 = orGate.Parent != null ? "Connected" : "Empty";
            string input2 = orGate.SecondParent != null ? "Connected" : "Empty";
            string output = orGate.IsOutputPowered() ? "ON" : "OFF";
            return string.Format("Logic Relay [Mode: {0}] [Input 1: {1}] [Input 2: {2}] [Output: {3}]",
                mode, input1, input2, output);
        }
        return "Logic Relay";
    }

    public override bool OnBlockActivated(
        WorldBase _world,
        int _cIdx,
        Vector3i _blockPos,
        BlockValue _blockValue,
        EntityPlayerLocal _player)
    {
        // Default activation: toggle mode
        return OnBlockActivated("toggle_mode", _world, _cIdx, _blockPos, _blockValue, _player);
    }

    public override bool OnBlockActivated(
        string _commandName,
        WorldBase _world,
        int _cIdx,
        Vector3i _blockPos,
        BlockValue _blockValue,
        EntityPlayerLocal _player)
    {
        PowerItem powerItem = PowerManager.Instance.GetPowerItemByWorldPos(_blockPos);
        if (!(powerItem is PowerItemORGate orGate))
            return false;

        switch (_commandName)
        {
            case "toggle_mode":
                orGate.ToggleMode();
                string modeName = orGate.Mode == GateMode.OR ? "OR" : "AND";
                GameManager.ShowTooltip(_player, "Logic Relay: Mode set to " + modeName);
                return true;

            case "disconnect":
                orGate.DisconnectAllInputs();
                GameManager.ShowTooltip(_player, "Logic Relay: All inputs disconnected");
                return true;

            default:
                return false;
        }
    }
}
