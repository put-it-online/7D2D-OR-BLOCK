using HarmonyLib;
using System.Collections.Generic;

// ---------------------------------------------------------------------------
// Logic Relay metadata store
// ---------------------------------------------------------------------------
/// <summary>
/// Holds Logic Relay metadata (secondParentPosition, hasSecondParent, mode) that
/// is read from TileEntity chunk data before InitializePowerData runs.
/// Keyed by world position of the Logic Relay TileEntity.
/// </summary>
public static class LogicRelayMetadataStore
{
    private static readonly Dictionary<Vector3i, LogicRelayMetadata> _store
        = new Dictionary<Vector3i, LogicRelayMetadata>();

    public static void Store(Vector3i pos, LogicRelayMetadata meta)
    {
        _store[pos] = meta;
    }

    public static bool TryGet(Vector3i pos, out LogicRelayMetadata meta)
    {
        return _store.TryGetValue(pos, out meta);
    }

    public static void Remove(Vector3i pos)
    {
        _store.Remove(pos);
    }
}

public struct LogicRelayMetadata
{
    public bool HasSecondParent;
    public Vector3i SecondParentPosition;
    public LogicRelayMode Mode;
    public bool HasPrimaryParent;
    public Vector3i PrimaryParentPosition;
}

// ---------------------------------------------------------------------------
// TileEntityPowered.write() — persist Logic Relay metadata in chunk data
// ---------------------------------------------------------------------------
/// <summary>
/// Appends Logic Relay metadata (hasSecondParent flag, secondParentPosition, mode)
/// after the normal TileEntityPowered write for Logic Relay blocks.
///
/// This is the only place Logic Relay extra data is persisted. The power.dat file
/// (power item serialization) intentionally carries NO extra Logic Relay bytes —
/// writing extra bytes there caused stream corruption because PowerConsumer.read()
/// (which handles the relay's second duplicate occurrence during load) only reads
/// the base fields and leaves any extra bytes unconsumed, corrupting all
/// subsequent power item reads.
/// </summary>
[HarmonyPatch(typeof(TileEntityPowered))]
[HarmonyPatch("write")]
public class TileEntityPowered_Write_Patch
{
    static void Postfix(
        TileEntityPowered __instance,
        System.IO.BinaryWriter _bw,
        TileEntity.StreamModeWrite _eStreamMode)
    {
        // Only persist to chunk file, not to client/server network streams
        if (_eStreamMode != TileEntity.StreamModeWrite.Persistency)
            return;

        Block block = __instance.blockValue.Block;
        if (!block.Properties.Values.ContainsKey("IsLogicRelay") ||
            block.Properties.Values["IsLogicRelay"] != "true")
            return;

        // Find the PowerItemLogicRelay for this position
        PowerItem powerItem = PowerManager.Instance.GetPowerItemByWorldPos(__instance.ToWorldPos());
        if (powerItem is PowerItemLogicRelay logicRelay)
        {
            _bw.Write(logicRelay.HasSecondParent);
            if (logicRelay.HasSecondParent)
            {
                _bw.Write(logicRelay.SecondParentPosition.x);
                _bw.Write(logicRelay.SecondParentPosition.y);
                _bw.Write(logicRelay.SecondParentPosition.z);
            }
            _bw.Write((byte)logicRelay.Mode);

            // Primary parent position (new field — appended after existing fields)
            bool hasPrimaryParent = logicRelay.Parent != null;
            _bw.Write(hasPrimaryParent);
            if (hasPrimaryParent)
            {
                _bw.Write(logicRelay.Parent.Position.x);
                _bw.Write(logicRelay.Parent.Position.y);
                _bw.Write(logicRelay.Parent.Position.z);
            }

            Log.Out("[LogicRelay] TileEntityPowered.write: saved Logic Relay metadata at "
                + __instance.ToWorldPos()
                + " hasSecondParent=" + logicRelay.HasSecondParent
                + " mode=" + logicRelay.Mode);
        }
        else
        {
            // No Logic Relay yet (e.g. relay not yet wired) — write defaults so
            // the read side can always consume the same number of bytes.
            _bw.Write(false); // hasSecondParent
            _bw.Write((byte)LogicRelayMode.OR); // mode
            _bw.Write(false); // hasPrimaryParent (new field default)
        }
    }
}

// ---------------------------------------------------------------------------
// TileEntityPowered.read() — restore Logic Relay metadata from chunk data
// ---------------------------------------------------------------------------
/// <summary>
/// Reads the Logic Relay metadata that was appended by TileEntityPowered_Write_Patch
/// and stores it in LogicRelayMetadataStore so that InitializePowerData can apply
/// it when creating the PowerItemLogicRelay.
/// </summary>
[HarmonyPatch(typeof(TileEntityPowered))]
[HarmonyPatch("read")]
public class TileEntityPowered_Read_Patch
{
    static void Postfix(
        TileEntityPowered __instance,
        System.IO.BinaryReader _br,
        TileEntity.StreamModeRead _eStreamMode)
    {
        // Only restore from chunk file, not from client/server network streams
        if (_eStreamMode != TileEntity.StreamModeRead.Persistency)
            return;

        Block block = __instance.blockValue.Block;
        if (!block.Properties.Values.ContainsKey("IsLogicRelay") ||
            block.Properties.Values["IsLogicRelay"] != "true")
            return;

        // Guard: check there are enough bytes left to read our metadata.
        // Old saves without Logic Relay metadata will have no extra bytes here.
        try
        {
            bool hasSecondParent = _br.ReadBoolean();
            Vector3i secondParentPos = Vector3i.zero;
            if (hasSecondParent)
            {
                int x = _br.ReadInt32();
                int y = _br.ReadInt32();
                int z = _br.ReadInt32();
                secondParentPos = new Vector3i(x, y, z);
            }
            LogicRelayMode mode = (LogicRelayMode)_br.ReadByte();

            // New fields — nested try-catch for backward compatibility with old saves
            bool hasPrimaryParent = false;
            Vector3i primaryParentPos = Vector3i.zero;
            try
            {
                hasPrimaryParent = _br.ReadBoolean();
                if (hasPrimaryParent)
                {
                    int px = _br.ReadInt32();
                    int py = _br.ReadInt32();
                    int pz = _br.ReadInt32();
                    primaryParentPos = new Vector3i(px, py, pz);
                }
            }
            catch
            {
                // Old save without primary parent position — fall back to oldItem.Parent
            }

            Vector3i worldPos = __instance.ToWorldPos();
            LogicRelayMetadataStore.Store(worldPos, new LogicRelayMetadata
            {
                HasSecondParent = hasSecondParent,
                SecondParentPosition = secondParentPos,
                Mode = mode,
                HasPrimaryParent = hasPrimaryParent,
                PrimaryParentPosition = primaryParentPos
            });

            Log.Out("[LogicRelay] TileEntityPowered.read: loaded Logic Relay metadata at "
                + worldPos
                + " hasSecondParent=" + hasSecondParent
                + " hasPrimaryParent=" + hasPrimaryParent
                + " mode=" + mode);
        }
        catch
        {
            // Old save format without Logic Relay metadata — ignore gracefully.
            // The relay will have default values (no second parent, OR mode).
            Log.Out("[LogicRelay] TileEntityPowered.read: no Logic Relay metadata found (old save?) at "
                + __instance.ToWorldPos());
        }
    }
}

// ---------------------------------------------------------------------------
// TileEntityPowered.InitializePowerData() — create PowerItemLogicRelay for Logic Relay
// ---------------------------------------------------------------------------
/// <summary>
/// Patch TileEntityPowered.InitializePowerData() to create PowerItemLogicRelay
/// for our Logic Relay block instead of the default PowerConsumer.
///
/// This Postfix runs after the vanilla method has already set PowerItem to the
/// node loaded from power.dat (or created a fresh one). If the block is a
/// Logic Relay and the current PowerItem is not yet a PowerItemLogicRelay, we:
///   1. Remove the stale power node (plain PowerConsumer from power.dat)
///   2. Clean up any phantom duplicate nodes left in parent Children lists
///      (artefacts from the second duplicate read during power.dat loading)
///   3. Create a fresh PowerItemLogicRelay and register it
///   4. Apply secondParent + mode metadata from LogicRelayMetadataStore
///      (populated by TileEntityPowered_Read_Patch from chunk data)
///
/// The second-parent restoration from LogicRelayMetadataStore must happen AFTER
/// all TileEntities initialize (so the target parent's PowerItem exists).
/// RestoreSecondParent() is called from PowerManager_LoadPowerManager_Patch
/// Postfix, which runs after the power graph is fully loaded. However, for
/// the chunk-load path, we call RestoreSecondParent immediately since all
/// items should already be in the PowerItemDictionary by the time chunks load.
/// </summary>
[HarmonyPatch(typeof(TileEntityPowered))]
[HarmonyPatch("InitializePowerData")]
public class TileEntityPowered_InitializePowerData_Patch
{
    static void Postfix(TileEntityPowered __instance)
    {
        BlockValue blockValue = __instance.blockValue;
        Block block = blockValue.Block;

        // ---------------------------------------------------------------
        // Migration guard: fix corrupted PowerTrigger.TriggerType
        // ---------------------------------------------------------------
        // OLD saves have extra bytes in power.dat written by the previous
        // PowerItemLogicRelay.write() (secondParentPosition + mode). These are
        // not consumed by PowerConsumer.read(), leaving stale bytes in the
        // stream that corrupt the TriggerType field of subsequent
        // PowerTrigger objects (e.g. motion detectors).
        //
        // Concretely: the extra 'mode' byte (0=OR, 1=AND) was misread as
        // PowerTrigger.TriggerType, making the motion detector look like a
        // Switch (0) or PressurePlate (1) instead of Motion (3).
        // TileEntityPoweredTrigger.set_IsTriggered then cast to
        // PowerPressurePlate, got null, and threw NullReferenceException.
        //
        // Fix: if this TileEntity is a TileEntityPoweredTrigger and its
        // PowerItem.TriggerType disagrees with the block-derived TriggerType
        // stored on the TileEntity itself (which is loaded from CHUNK data,
        // not the corrupted power.dat), correct the PowerItem's TriggerType.
        //
        // This guard runs once per TileEntityPoweredTrigger on world load and
        // is a no-op on clean saves where the types already agree.
        if (__instance is TileEntityPoweredTrigger triggerTE)
        {
            if (triggerTE.PowerItem is PowerTrigger pt
                && pt.TriggerType != triggerTE.TriggerType)
            {
                Log.Out("[LogicRelay] InitializePowerData: correcting corrupted TriggerType at "
                    + __instance.ToWorldPos()
                    + " from " + pt.TriggerType
                    + " to " + triggerTE.TriggerType
                    + " (power.dat stream corruption from old save format)");
                pt.TriggerType = triggerTE.TriggerType;
            }
            return; // TileEntityPoweredTrigger is not a Logic Relay — done.
        }

        if (!block.Properties.Values.ContainsKey("IsLogicRelay") ||
            block.Properties.Values["IsLogicRelay"] != "true")
            return;

        if (__instance.PowerItem == null)
        {
            Log.Out("[LogicRelay] InitializePowerData: PowerItem is null for Logic Relay at "
                + __instance.ToWorldPos() + " — skipping (not server?)");
            return;
        }

        if (__instance.PowerItem is PowerItemLogicRelay)
        {
            // Already upgraded — just ensure metadata is applied
            ApplyMetadata((PowerItemLogicRelay)__instance.PowerItem, __instance.ToWorldPos());
            return;
        }

        // The PowerItem is a plain PowerConsumer (loaded from power.dat).
        // Replace it with a PowerItemLogicRelay.

        PowerItem oldItem = __instance.PowerItem;
        Vector3i gatePos = __instance.ToWorldPos();
        PowerItem originalParent = oldItem.Parent;  // capture before RemovePowerNode destroys it

        Log.Out("[LogicRelay] InitializePowerData: upgrading PowerConsumer to PowerItemLogicRelay at "
            + gatePos);

        // Step 1: Collect children from the old item so we can re-attach them.
        // We make a copy because RemovePowerNode will modify the Children list.
        var childrenToKeep = new System.Collections.Generic.List<PowerItem>(oldItem.Children);

        // Step 2: Remove the old node from the power graph.
        // RemovePowerNode removes oldItem from:
        //   - its primary parent's Children list
        //   - PowerItemDictionary
        //   - Circuits / PowerSources / PowerTriggers lists
        //   - It also orphans oldItem's children (SetParent(child, null) for each)
        PowerManager.Instance.RemovePowerNode(oldItem);

        // Step 3: Clean up phantom duplicate nodes that may have been added to
        // parent Children lists during the power.dat double-read.
        //
        // When the Logic Relay is serialized under TWO parents in power.dat, the
        // second occurrence is read as a fresh PowerConsumer (pc2). Before our
        // AddPowerNode patch can fire, PowerItem.read() calls SetParent(pc2,
        // primaryParent) which adds pc2 to primaryParent.Children. After our
        // Postfix removes pc1 (the first occurrence via RemovePowerNode), pc2
        // remains in primaryParent.Children. This phantom blocks CheckForNewWires
        // from re-linking the real logicRelay.
        //
        // We purge all nodes at gatePos from every power item's Children list.
        CleanPhantomChildren(gatePos);

        // Step 4: Create and register the PowerItemLogicRelay.
        PowerItemLogicRelay logicRelay = new PowerItemLogicRelay();
        logicRelay.Position = gatePos;
        logicRelay.BlockID = oldItem.BlockID;

        __instance.PowerItem = logicRelay;
        logicRelay.TileEntity = __instance;

        PowerManager.Instance.AddPowerNode(logicRelay);

        // Step 5: Re-attach children that were under the old item.
        // RemovePowerNode orphaned them into Circuits. We set logicRelay as their parent.
        foreach (PowerItem child in childrenToKeep)
        {
            // Only re-attach if the child is still in the system and not already
            // parented to something else
            if (PowerManager.Instance.GetPowerItemByWorldPos(child.Position) != null
                && child.Parent == null)
            {
                PowerManager.Instance.SetParent(child, logicRelay);
            }
        }

        // Rebuild the relay's own wireDataList for child wires (e.g., relay -> speaker)
        RebuildParentWires(logicRelay, "relay children");

        // Step 6: Apply Logic Relay metadata (secondParent, mode) from chunk save data.
        // Pass originalParent as fallback for old saves that lack stored primary parent position.
        ApplyMetadata(logicRelay, gatePos, originalParent);

        Log.Out("[LogicRelay] InitializePowerData: PowerItemLogicRelay created at " + gatePos
            + " mode=" + logicRelay.Mode
            + " hasSecondParent=" + logicRelay.HasSecondParent);
    }

    /// <summary>
    /// Removes all phantom (stale/orphaned) child entries pointing to gatePos
    /// from every power item's Children list in the entire power graph.
    ///
    /// These phantoms arise because the power.dat second-occurrence read calls
    /// SetParent on a freshly-deserialized but never-registered PowerConsumer,
    /// inserting it into a parent's Children list before our AddPowerNode patch
    /// can intercept it.
    /// </summary>
    private static void CleanPhantomChildren(Vector3i gatePos)
    {
        PowerItem realNode = PowerManager.Instance.GetPowerItemByWorldPos(gatePos);

        foreach (var kvp in PowerManager.Instance.PowerItemDictionary)
        {
            var item = kvp.Value;
            // Remove any child whose position matches gatePos but is NOT the
            // real registered node (i.e., it's a phantom/stale object).
            for (int i = item.Children.Count - 1; i >= 0; i--)
            {
                PowerItem child = item.Children[i];
                if (child.Position == gatePos && child != realNode)
                {
                    item.Children.RemoveAt(i);
                    Log.Out("[LogicRelay] CleanPhantomChildren: removed phantom child at "
                        + gatePos + " from parent at " + kvp.Key);
                }
            }
        }
    }

    /// <summary>
    /// Rebuilds wire visuals on a parent's TileEntity after its Children list changed.
    /// This ensures the visual wire from parent to Logic Relay is drawn after load.
    /// </summary>
    private static void RebuildParentWires(PowerItem parent, string label)
    {
        if (parent == null || parent.TileEntity == null)
            return;

        TileEntityPowered parentTE = parent.TileEntity;
        parentTE.CreateWireDataFromPowerItem();
        parentTE.SendWireData();
        parentTE.RemoveWires();
        parentTE.DrawWires();
        Log.Out("[LogicRelay] RebuildParentWires: rebuilt wire data for " + label
            + " at " + parent.Position);
    }

    /// <summary>
    /// Applies Logic Relay metadata (secondParentPosition, mode, primaryParentPosition) from
    /// LogicRelayMetadataStore to the given logicRelay. Consumes and removes the entry from
    /// the store. Restores the primary parent from the stored position (immune to
    /// CheckForNewWires timing). Falls back to fallbackParent for old saves that lack a
    /// stored primary parent position. Also attempts immediate RestoreSecondParent if the
    /// target is already in the dict.
    /// </summary>
    private static void ApplyMetadata(PowerItemLogicRelay logicRelay, Vector3i gatePos, PowerItem fallbackParent = null)
    {
        if (LogicRelayMetadataStore.TryGet(gatePos, out LogicRelayMetadata meta))
        {
            logicRelay.Mode = meta.Mode;
            logicRelay.HasSecondParent = meta.HasSecondParent;
            logicRelay.SecondParentPosition = meta.SecondParentPosition;
            LogicRelayMetadataStore.Remove(gatePos);

            // Restore primary parent from stored position (immune to CheckForNewWires timing)
            if (meta.HasPrimaryParent)
            {
                PowerItem primaryParent = PowerManager.Instance.GetPowerItemByWorldPos(meta.PrimaryParentPosition);
                if (primaryParent != null)
                {
                    PowerManager.Instance.SetParent(logicRelay, primaryParent);
                    RebuildParentWires(primaryParent, "primary parent");
                    Log.Out("[LogicRelay] ApplyMetadata: restored primary parent from stored position at "
                        + meta.PrimaryParentPosition);
                }
                else
                {
                    Log.Warning("[LogicRelay] ApplyMetadata: could not find primary parent at "
                        + meta.PrimaryParentPosition);
                }
            }
            else if (fallbackParent != null)
            {
                // Old save without stored primary parent — use captured oldItem.Parent
                PowerManager.Instance.SetParent(logicRelay, fallbackParent);
                RebuildParentWires(fallbackParent, "primary parent (fallback)");
                Log.Out("[LogicRelay] ApplyMetadata: restored primary parent from fallback at "
                    + fallbackParent.Position);
            }

            // Restore second parent
            if (meta.HasSecondParent)
            {
                logicRelay.RestoreSecondParent();
                RebuildParentWires(logicRelay.SecondParent, "second parent");
            }

            Log.Out("[LogicRelay] ApplyMetadata: applied metadata at " + gatePos
                + " mode=" + meta.Mode
                + " hasSecondParent=" + meta.HasSecondParent
                + " hasPrimaryParent=" + meta.HasPrimaryParent);
        }
        else if (fallbackParent != null)
        {
            // No metadata at all — just restore the fallback parent
            PowerManager.Instance.SetParent(logicRelay, fallbackParent);
            RebuildParentWires(fallbackParent, "primary parent (no metadata)");
        }
    }
}
