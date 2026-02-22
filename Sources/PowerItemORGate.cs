using System.IO;
using UnityEngine;

public enum GateMode : byte
{
    OR = 0,
    AND = 1
}

public class PowerItemORGate : PowerConsumer
{
    // The second input connection (vanilla only supports one parent)
    public PowerItem SecondParent { get; set; }

    // Position of second parent, used for save/load restoration
    private Vector3i secondParentPosition = Vector3i.zero;
    private bool hasSecondParent = false;

    // Gate mode: OR (default) or AND
    public GateMode Mode { get; set; } = GateMode.OR;

    /// <summary>
    /// Controls whether HandlePowerReceived propagates power to children.
    /// The base PowerItem.HandlePowerReceived checks PowerChildren() before
    /// recursing into the Children list. By returning IsOutputPowered() here,
    /// AND mode correctly blocks child power when only one input is powered.
    /// </summary>
    public override bool PowerChildren()
    {
        return IsOutputPowered();
    }

    /// <summary>
    /// Check if the gate output should be powered, based on current mode.
    /// </summary>
    public bool IsOutputPowered()
    {
        bool parent1Powered = (Parent != null && Parent.IsPowered);
        bool parent2Powered = (SecondParent != null && SecondParent.IsPowered);

        if (Mode == GateMode.AND)
        {
            // AND: both inputs must be connected AND powered
            return (Parent != null && Parent.IsPowered)
                && (SecondParent != null && SecondParent.IsPowered);
        }

        // OR: either input powered
        return parent1Powered || parent2Powered;
    }

    /// <summary>
    /// Toggle between OR and AND mode.
    /// </summary>
    public void ToggleMode()
    {
        Mode = (Mode == GateMode.OR) ? GateMode.AND : GateMode.OR;
        SendHasLocalChangesToRoot();
    }

    /// <summary>
    /// Connect the second parent. Called from our Harmony patch on SetParent.
    /// </summary>
    public void SetSecondParent(PowerItem parent)
    {
        if (SecondParent != null)
        {
            RemoveSecondParent();
        }
        SecondParent = parent;
        hasSecondParent = true;
        secondParentPosition = parent.Position;
        SendHasLocalChangesToRoot();
    }

    /// <summary>
    /// Disconnect the second parent.
    /// </summary>
    public void RemoveSecondParent()
    {
        if (SecondParent == null) return;

        if (SecondParent.Children.Contains(this))
        {
            SecondParent.Children.Remove(this);
        }

        SecondParent = null;
        hasSecondParent = false;
        secondParentPosition = Vector3i.zero;
        SendHasLocalChangesToRoot();
    }

    /// <summary>
    /// Disconnect ALL inputs (both parents). Called from the block activation menu.
    /// Mirrors the vanilla RemoveParentWithWiringTool pattern: updates the power
    /// graph, rebuilds wire data, sends network packets, and redraws wires.
    /// </summary>
    public void DisconnectAllInputs()
    {
        // Capture parent TileEntity refs before disconnect nullifies them
        TileEntityPowered firstParentTE = Parent != null ? Parent.TileEntity : null;
        TileEntityPowered secondParentTE = SecondParent != null ? SecondParent.TileEntity : null;

        // Disconnect second parent (custom graph cleanup)
        RemoveSecondParent();

        // Disconnect first parent (vanilla graph cleanup: Children.Remove,
        // Parent = null, Circuits.Add, HandleDisconnect, SendHasLocalChangesToRoot)
        if (Parent != null)
        {
            PowerManager.Instance.RemoveParent(this);
        }

        // Full wire visual/network cleanup for both parents.
        // RemoveParent partially handles the first parent but misses SendWireData
        // and RemoveWires. We redo the full sequence for both parents.
        if (firstParentTE != null)
        {
            firstParentTE.CreateWireDataFromPowerItem();
            firstParentTE.SendWireData();
            firstParentTE.RemoveWires();
            firstParentTE.DrawWires();
        }
        if (secondParentTE != null)
        {
            secondParentTE.CreateWireDataFromPowerItem();
            secondParentTE.SendWireData();
            secondParentTE.RemoveWires();
            secondParentTE.DrawWires();
        }

        // Mark our own tile entity as modified so the disconnect persists on save
        if (TileEntity != null)
        {
            TileEntity.SetModified();
        }
    }

    /// <summary>
    /// Serialize: write our extra data after the base class data.
    /// </summary>
    public override void write(BinaryWriter _bw)
    {
        base.write(_bw);
        _bw.Write(hasSecondParent);
        if (hasSecondParent)
        {
            _bw.Write(secondParentPosition.x);
            _bw.Write(secondParentPosition.y);
            _bw.Write(secondParentPosition.z);
        }
        _bw.Write((byte)Mode);
    }

    /// <summary>
    /// Deserialize: read our extra data after the base class data.
    /// </summary>
    public override void read(BinaryReader _br, byte _version)
    {
        base.read(_br, _version);
        hasSecondParent = _br.ReadBoolean();
        if (hasSecondParent)
        {
            int x = _br.ReadInt32();
            int y = _br.ReadInt32();
            int z = _br.ReadInt32();
            secondParentPosition = new Vector3i(x, y, z);
        }
        // Read gate mode (defaults to OR if not present for backwards compat)
        if (_br.BaseStream.Position < _br.BaseStream.Length)
        {
            Mode = (GateMode)_br.ReadByte();
        }
        else
        {
            Mode = GateMode.OR;
        }
    }

    /// <summary>
    /// Called after all power items are loaded to restore the second parent reference.
    /// </summary>
    public void RestoreSecondParent()
    {
        if (!hasSecondParent) return;
        PowerItem item = PowerManager.Instance.GetPowerItemByWorldPos(secondParentPosition);
        if (item != null)
        {
            SecondParent = item;
            if (!item.Children.Contains(this))
            {
                item.Children.Add(this);
            }
        }
        else
        {
            Log.Warning("[ORBlock] Could not restore second parent at " + secondParentPosition);
            hasSecondParent = false;
            secondParentPosition = Vector3i.zero;
        }
    }
}
