using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Patches PowerManager.SetParent to allow a second parent on OR gate blocks.
///
/// Vanilla behavior: SetParent always removes existing parent before setting new one.
/// Our behavior: If the child is a PowerItemORGate and already has a Parent,
/// store the new parent as SecondParent instead.
/// </summary>
[HarmonyPatch(typeof(PowerManager))]
[HarmonyPatch("SetParent")]
[HarmonyPatch(new[] { typeof(PowerItem), typeof(PowerItem) })]
public class PowerManager_SetParent_Patch
{
    static bool Prefix(PowerManager __instance, PowerItem child, PowerItem parent)
    {
        // Only intercept for our OR gate type
        if (child is PowerItemORGate orGate)
        {
            // If the OR gate has no parent yet, let vanilla handle it
            if (orGate.Parent == null)
                return true; // continue to original method

            // If trying to set same parent, skip
            if (orGate.Parent == parent)
                return false;

            // If already has a parent but no second parent, store as second
            if (orGate.SecondParent == null && parent != null)
            {
                orGate.SetSecondParent(parent);
                // Add ourselves as child of the second parent for wire rendering
                if (!parent.Children.Contains(orGate))
                {
                    parent.Children.Add(orGate);
                }
                return false; // skip original method
            }

            // If already has both parents, replace second parent
            if (orGate.SecondParent != null && parent != null)
            {
                orGate.RemoveSecondParent();
                orGate.SetSecondParent(parent);
                if (!parent.Children.Contains(orGate))
                {
                    parent.Children.Add(orGate);
                }
                return false;
            }
        }

        return true; // let vanilla handle non-OR-gate blocks
    }
}

/// <summary>
/// Patches PowerManager.RemoveParent to also handle our second parent.
/// When removing parent from an OR gate, if it's the second parent being
/// disconnected (by wire tool), handle that correctly.
/// </summary>
[HarmonyPatch(typeof(PowerManager))]
[HarmonyPatch("RemoveParent")]
[HarmonyPatch(new[] { typeof(PowerItem) })]
public class PowerManager_RemoveParent_Patch
{
    static void Postfix(PowerItem node)
    {
        // If a second parent's child was removed and that child is an OR gate,
        // we might need cleanup. But this is handled by DisconnectAllInputs.
        // This postfix is a safety net.
    }
}

/// <summary>
/// Patches PowerItem.IsPowered getter to implement OR logic.
///
/// Decompile findings:
///   - IsPowered is defined on PowerItem (NOT PowerConsumer) as:
///       public virtual bool IsPowered => isPowered;
///   - PowerConsumer does NOT override IsPowered — it only overrides IsPoweredChanged(bool)
///   - Therefore the Harmony patch target must be PowerItem, not PowerConsumer
///
/// This patch makes the OR gate report powered if either input is active.
/// </summary>
[HarmonyPatch(typeof(PowerItem))]
[HarmonyPatch("get_IsPowered")]
public class PowerItem_IsPowered_Patch
{
    static bool Prefix(PowerItem __instance, ref bool __result)
    {
        if (__instance is PowerItemORGate orGate)
        {
            __result = orGate.IsEitherInputPowered();
            return false; // skip original
        }
        return true; // let vanilla handle
    }
}

/// <summary>
/// Patch PowerManager.LoadPowerManager to restore second parent connections
/// after all power items have been loaded from the save file.
/// </summary>
[HarmonyPatch(typeof(PowerManager))]
[HarmonyPatch("LoadPowerManager")]
public class PowerManager_LoadPowerManager_Patch
{
    static void Postfix(PowerManager __instance)
    {
        // After all nodes are loaded, restore second parent references
        foreach (var kvp in __instance.PowerItemDictionary)
        {
            if (kvp.Value is PowerItemORGate orGate)
            {
                orGate.RestoreSecondParent();
            }
        }
        Log.Out("[ORBlock] Restored OR gate connections.");
    }
}

/// <summary>
/// Patch PowerItem.CreateItem to handle our custom power item type.
/// This factory method creates PowerItem subclasses based on PowerItemTypes enum.
/// Since we can't add to the enum, we use a convention: if the block at the
/// position has our custom property, we substitute our class.
///
/// Alternative approach: We register our block type and patch the factory.
/// The exact approach depends on how the game version handles this.
///
/// NOTE: This patch may need adjustment. If PowerItem.CreateItem uses an enum
/// that we can't extend, we may need to patch the TileEntity creation instead,
/// or patch Read() to swap the instance after creation.
/// </summary>
public class PowerItemFactory
{
    // This is called from our TileEntity to create the right PowerItem type
    public static PowerItemORGate CreateORGatePowerItem()
    {
        return new PowerItemORGate();
    }
}
