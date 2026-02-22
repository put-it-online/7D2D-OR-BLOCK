using HarmonyLib;
using System.Collections.Generic;

// ---------------------------------------------------------------------------
// PowerManager.AddPowerNode — duplicate-key guard (Bug 3 fix)
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
/// Fix: if the position is already registered we skip the dictionary insert
/// (the node is already there) but still proceed with SetParent so the second
/// parent connection is re-established exactly as it was wired.
/// </summary>
[HarmonyPatch(typeof(PowerManager), "AddPowerNode")]
public class PowerManager_AddPowerNode_Patch
{
    // Signature:  void AddPowerNode(PowerItem node, PowerItem parent = null)
    static bool Prefix(
        PowerManager __instance,
        PowerItem node,
        PowerItem parent,
        List<PowerItem> ___Circuits,
        Dictionary<Vector3i, PowerItem> ___PowerItemDictionary)
    {
        // Only intercept when the position is already registered — this is
        // exactly the duplicate-child situation caused by two parents each
        // serialising the OR gate in their Children list.
        if (!___PowerItemDictionary.ContainsKey(node.Position))
            return true; // first occurrence — let the original method run

        // Duplicate occurrence (second parent loading the same OR gate node).
        // The node is already in the dictionary and in Circuits; we must NOT
        // add it a second time. We only need to wire up the parent relationship
        // so the second parent link is restored.
        Log.Out("[ORBlock] AddPowerNode: skipping duplicate at " + node.Position
                + " (restoring second-parent link to "
                + (parent != null ? parent.Position.ToString() : "null") + ")");

        if (parent != null)
        {
            PowerItem existing = ___PowerItemDictionary[node.Position];

            // Add the gate to this parent's Children list if not already there.
            if (!parent.Children.Contains(existing))
                parent.Children.Add(existing);

            // If the gate already has a primary parent, treat this as the
            // second parent. The SecondParent reference will be set by
            // RestoreSecondParent() in the LoadPowerManager Postfix below,
            // which resolves references from the saved position data.
        }

        return false; // skip the original AddPowerNode
    }
}

// ---------------------------------------------------------------------------
// PowerManager.SetParent — second-parent support
// ---------------------------------------------------------------------------
/// <summary>
/// Allows a PowerItemORGate to accept a second parent without replacing the
/// first. Vanilla SetParent would overwrite the existing parent; this prefix
/// intercepts that for OR-gate nodes and routes to SetSecondParent instead.
/// </summary>
[HarmonyPatch(typeof(PowerManager))]
[HarmonyPatch("SetParent")]
[HarmonyPatch(new[] { typeof(PowerItem), typeof(PowerItem) })]
public class PowerManager_SetParent_Patch
{
    static bool Prefix(PowerManager __instance, PowerItem child, PowerItem parent)
    {
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
/// This resolves the saved second-parent position back to a live reference
/// (the AddPowerNode duplicate guard ensures the node is already in the
/// dictionary at this point).
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
        Log.Out("[ORBlock] LoadPowerManager: restored " + restored + " Logic Relay second-parent connection(s).");
    }
}
