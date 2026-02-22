using HarmonyLib;
using System.Collections.Generic;

// ---------------------------------------------------------------------------
// PowerManager.AddPowerNode — duplicate-key guard
// ---------------------------------------------------------------------------
/// <summary>
/// Prevents the ArgumentException "An item with the same key has already been
/// added" that occurs during world load when a PowerItemORGate is listed in the
/// Children of BOTH its parents in the save file.
///
/// Root cause: when a second parent is wired, the gate is added to that
/// parent's Children list so vanilla wire-drawing code works. On save,
/// PowerItem.write serialises every entry in Children, so the OR gate appears
/// under two different parent nodes. On load, each parent calls
/// PowerManager.AddPowerNode for the OR gate; the first call succeeds, the
/// second throws because the position key is already in PowerItemDictionary.
///
/// Fix: if the position is already registered, skip the duplicate AddPowerNode
/// entirely. Do NOT add the duplicate node to the parent's Children list —
/// the parent-child links are repaired later by CheckForNewWires() (which runs
/// in TileEntityPowered.OnReadComplete after InitializePowerData creates the
/// real PowerItemORGate) and by RestoreSecondParent() in the LoadPowerManager
/// Postfix below.
///
/// Previous behaviour (adding the existing node to the duplicate's parent)
/// caused phantom entries in parent Children lists, which in turn blocked
/// CheckForNewWires from re-linking the real gate.
/// </summary>
[HarmonyPatch(typeof(PowerManager), "AddPowerNode")]
public class PowerManager_AddPowerNode_Patch
{
    // Signature:  void AddPowerNode(PowerItem node, PowerItem parent = null)
    static bool Prefix(
        PowerItem node,
        Dictionary<Vector3i, PowerItem> ___PowerItemDictionary)
    {
        if (!___PowerItemDictionary.ContainsKey(node.Position))
            return true; // first occurrence — let the original method run

        // Duplicate: position already registered.
        // Skip entirely. Do NOT touch Children lists here — the real links are
        // established by CheckForNewWires / RestoreSecondParent after load.
        Log.Out("[ORBlock] AddPowerNode: skipping duplicate at " + node.Position);
        return false;
    }
}

// ---------------------------------------------------------------------------
// PowerManager.SetParent — second-parent support + phantom-node guard
// ---------------------------------------------------------------------------
/// <summary>
/// Two responsibilities:
///
/// 1. Allows a PowerItemORGate to accept a second parent without replacing the
///    first. Vanilla SetParent would overwrite the existing parent; this prefix
///    intercepts that for OR-gate nodes and routes to SetSecondParent instead.
///
/// 2. Phantom-node guard: during power.dat loading, when a duplicate OR gate
///    occurrence (pc2) is read by PowerItem.read(), pc2's base.read() calls
///    SetParent(pc2, primaryParent) BEFORE our AddPowerNode patch can see it.
///    This inserts pc2 into the primary parent's Children list as a phantom node.
///    We detect this by checking if the child's position is already registered
///    in PowerItemDictionary under a DIFFERENT object, and skip the SetParent.
/// </summary>
[HarmonyPatch(typeof(PowerManager))]
[HarmonyPatch("SetParent")]
[HarmonyPatch(new[] { typeof(PowerItem), typeof(PowerItem) })]
public class PowerManager_SetParent_Patch
{
    static bool Prefix(PowerItem child, PowerItem parent)
    {
        // --- Phantom-node guard ---
        // If child.Position is already in the dictionary under a DIFFERENT
        // object, this is a duplicate/phantom node from the second power.dat
        // read. Skip SetParent to prevent polluting parent's Children list.
        if (child != null && PowerManager.HasInstance)
        {
            PowerItem existing = PowerManager.Instance.GetPowerItemByWorldPos(child.Position);
            if (existing != null && existing != child)
            {
                Log.Out("[ORBlock] SetParent: blocking phantom SetParent for node at "
                    + child.Position + " (real node already registered)");
                return false; // skip — would insert a phantom into parent.Children
            }
        }

        // --- Second-parent support for PowerItemORGate ---
        if (child is PowerItemORGate orGate)
        {
            // No first parent yet — let vanilla handle it (sets Parent).
            if (orGate.Parent == null)
                return true;

            // Same parent wired again — nothing to do.
            if (orGate.Parent == parent)
                return false;

            // A second (or replacement) parent is being assigned.
            if (parent != null)
            {
                if (orGate.SecondParent != null)
                    orGate.RemoveSecondParent();

                orGate.SetSecondParent(parent);

                if (!parent.Children.Contains(orGate))
                    parent.Children.Add(orGate);

                return false;
            }
        }

        return true;
    }
}

// ---------------------------------------------------------------------------
// PowerItem.IsPowered — OR / AND gate logic
// ---------------------------------------------------------------------------
/// <summary>
/// Overrides IsPowered for PowerItemORGate so that output power is determined
/// by the gate's current OR/AND logic across both parent inputs.
/// </summary>
[HarmonyPatch(typeof(PowerItem))]
[HarmonyPatch("get_IsPowered")]
public class PowerItem_IsPowered_Patch
{
    static bool Prefix(PowerItem __instance, ref bool __result)
    {
        if (__instance is PowerItemORGate orGate)
        {
            __result = orGate.IsOutputPowered();
            return false;
        }
        return true;
    }
}

// ---------------------------------------------------------------------------
// PowerManager.LoadPowerManager — restore SecondParent references after load
// ---------------------------------------------------------------------------
/// <summary>
/// After the power save-file has been fully deserialised, iterates every
/// loaded power item and calls RestoreSecondParent() on any PowerItemORGate.
///
/// At this point in the game startup sequence, TileEntities have not yet
/// initialised (chunks load after this). The PowerItemDictionary only contains
/// plain PowerConsumer objects at this point — actual PowerItemORGate objects
/// are created later in TileEntityPowered_InitializePowerData_Patch when
/// each chunk's TileEntities initialise.
///
/// Therefore this Postfix serves as a safety net for any gate that was already
/// upgraded before this runs (e.g. in a future code path), but is not the
/// primary restore mechanism. The primary mechanism is ApplyMetadata() in
/// TileEntityPowered_InitializePowerData_Patch.
/// </summary>
[HarmonyPatch(typeof(PowerManager))]
[HarmonyPatch("LoadPowerManager")]
public class PowerManager_LoadPowerManager_Patch
{
    static void Postfix(PowerManager __instance)
    {
        int restored = 0;
        foreach (var kvp in __instance.PowerItemDictionary)
        {
            if (kvp.Value is PowerItemORGate orGate)
            {
                orGate.RestoreSecondParent();
                restored++;
            }
        }
        if (restored > 0)
        {
            Log.Out("[ORBlock] LoadPowerManager: restored " + restored
                + " Logic Relay second-parent connection(s).");
        }
    }
}
