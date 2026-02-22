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

    // Position of second parent, used for save/load restoration.
    // NOTE: These fields are no longer written to power.dat (which caused stream
    // corruption because PowerConsumer.read() didn't consume our extra bytes).
    // They are now persisted via TileEntityPowered chunk data through
    // TileEntityPowered_ReadWrite_Patch in TileEntityPatches.cs.
    public Vector3i SecondParentPosition { get; set; } = Vector3i.zero;
    public bool HasSecondParent { get; set; } = false;

    // Gate mode: OR (default) or AND
    public GateMode Mode { get; set; } = GateMode.OR;

    /// <summary>
    /// Returns true if a given parent input is "on" for gate logic purposes.
    ///
    /// For PowerTrigger parents (motion detectors, switches, pressure plates, etc.),
    /// we check IsActive rather than IsPowered. IsPowered only means "electricity
    /// is flowing through the device from the generator" — it is true for ALL devices
    /// downstream of an active generator, regardless of whether the sensor has fired.
    /// IsActive correctly reflects whether the trigger has been activated.
    ///
    /// For non-trigger parents (generators, relays, etc.), IsPowered is the right check.
    /// </summary>
    private static bool IsParentActive(PowerItem parent)
    {
        if (parent == null)
            return false;
        if (parent is PowerTrigger trigger)
            return trigger.IsActive;
        return parent.IsPowered;
    }

    /// <summary>
    /// Check if the gate output should be powered, based on current mode.
    /// Uses IsActive for trigger-type parents so that AND mode correctly requires
    /// BOTH triggers to be active, not just both receiving electricity.
    /// </summary>
    public bool IsOutputPowered()
    {
        bool parent1Active = IsParentActive(Parent);
        bool parent2Active = IsParentActive(SecondParent);

        if (Mode == GateMode.AND)
        {
            // AND: both inputs must be connected AND active
            return Parent != null && parent1Active
                && SecondParent != null && parent2Active;
        }

        // OR: either input active
        return parent1Active || parent2Active;
    }

    /// <summary>
    /// Override HandlePowerUpdate to apply gate logic directly.
    ///
    /// The base PowerConsumer.HandlePowerUpdate does:
    ///   bool flag = isPowered && isOn;
    ///   TileEntity.Activate(flag);
    ///
    /// The isPowered FIELD is always true when any generator supplies power through
    /// this node — it does NOT reflect whether both trigger inputs are active.
    /// We must override this entirely to use IsOutputPowered() as the activation flag.
    ///
    /// We also must control child propagation: in AND mode, children should only
    /// receive HandlePowerUpdate(true) when the gate output is on.
    /// </summary>
    public override void HandlePowerUpdate(bool isOn)
    {
        bool outputOn = isOn && IsOutputPowered();

        Log.Out("[ORBlock] HandlePowerUpdate: mode=" + Mode
            + " parent1Active=" + IsParentActive(Parent)
            + " parent2Active=" + IsParentActive(SecondParent)
            + " isPowered=" + isPowered
            + " isOn=" + isOn
            + " outputOn=" + outputOn);

        if (TileEntity != null)
        {
            TileEntity.Activate(outputOn);
            if (outputOn && lastActivate != outputOn)
            {
                TileEntity.ActivateOnce();
            }
            TileEntity.SetModified();
        }
        lastActivate = outputOn;

        // Propagate to children with the gate output state
        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].HandlePowerUpdate(outputOn);
        }
    }

    /// <summary>
    /// Controls whether HandlePowerReceived propagates power to children.
    /// The base PowerItem.HandlePowerReceived checks PowerChildren() before
    /// recursing into the Children list. By returning IsOutputPowered() here,
    /// AND mode correctly blocks child power-flow when only one input is powered.
    /// </summary>
    public override bool PowerChildren()
    {
        return IsOutputPowered();
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
        HasSecondParent = true;
        SecondParentPosition = parent.Position;
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
        HasSecondParent = false;
        SecondParentPosition = Vector3i.zero;
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
    /// Serialize to power.dat.
    ///
    /// IMPORTANT: We intentionally write ONLY the base PowerConsumer data here.
    /// Do NOT write any extra bytes (secondParentPosition, mode) after base.write().
    ///
    /// Root cause of the NullReferenceException crash (fixed here):
    ///   When an OR gate is connected to two parents, vanilla PowerItem.write()
    ///   serialises the gate into BOTH parents' Children lists in power.dat.
    ///   On load, the second occurrence is read by CreateItem(Consumer) which
    ///   creates a plain PowerConsumer. PowerConsumer.read() only reads base bytes
    ///   and returns — leaving any extra bytes we wrote unconsumed in the stream.
    ///   Those stale bytes are then misread as the NEXT item's fields, corrupting
    ///   every power item loaded afterwards (including the motion detectors).
    ///   Specifically, the extra mode byte (0=OR, 1=AND) was misread as
    ///   PowerTrigger.TriggerType, making the motion detector appear to be a
    ///   PressurePlate instead of a Motion sensor. The MotionSensorController
    ///   then called set_IsTriggered, which cast PowerItem to PowerPressurePlate,
    ///   got null, and threw a NullReferenceException every frame.
    ///
    /// The OR gate metadata (secondParentPosition, mode) is now persisted
    /// exclusively through TileEntityPowered chunk data via
    /// TileEntityPowered_ReadWrite_Patch in TileEntityPatches.cs.
    /// </summary>
    public override void write(BinaryWriter _bw)
    {
        base.write(_bw);
        // No extra bytes — see summary above.
    }

    /// <summary>
    /// Deserialize from power.dat.
    /// Only reads base PowerConsumer data; extra OR gate data is loaded
    /// from TileEntity chunk data in TileEntityPowered_ReadWrite_Patch.
    /// </summary>
    public override void read(BinaryReader _br, byte _version)
    {
        base.read(_br, _version);
        // No extra bytes — extra data comes from TileEntity chunk persistence.
    }

    /// <summary>
    /// Called after all power items are loaded to restore the second parent reference.
    /// Uses the SecondParentPosition that was loaded from TileEntity chunk data.
    /// </summary>
    public void RestoreSecondParent()
    {
        if (!HasSecondParent) return;
        PowerItem item = PowerManager.Instance.GetPowerItemByWorldPos(SecondParentPosition);
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
            Log.Warning("[ORBlock] Could not restore second parent at " + SecondParentPosition);
            HasSecondParent = false;
            SecondParentPosition = Vector3i.zero;
        }
    }
}
