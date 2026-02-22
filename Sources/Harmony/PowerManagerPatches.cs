using HarmonyLib;
using UnityEngine;

/// <summary>
/// Patches PowerManager.SetParent to allow a second parent on Logic Relay blocks.
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
            if (orGate.Parent == null)
                return true;

            if (orGate.Parent == parent)
                return false;

            if (orGate.SecondParent == null && parent != null)
            {
                orGate.SetSecondParent(parent);
                if (!parent.Children.Contains(orGate))
                {
                    parent.Children.Add(orGate);
                }
                return false;
            }

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

        return true;
    }
}

/// <summary>
/// Safety net for RemoveParent on Logic Relay blocks.
/// </summary>
[HarmonyPatch(typeof(PowerManager))]
[HarmonyPatch("RemoveParent")]
[HarmonyPatch(new[] { typeof(PowerItem) })]
public class PowerManager_RemoveParent_Patch
{
    static void Postfix(PowerItem node)
    {
        // Safety net — main cleanup handled by DisconnectAllInputs
    }
}

/// <summary>
/// Patches PowerItem.IsPowered getter to implement OR/AND logic.
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

/// <summary>
/// Patch PowerManager.LoadPowerManager to restore second parent connections after load.
/// </summary>
[HarmonyPatch(typeof(PowerManager))]
[HarmonyPatch("LoadPowerManager")]
public class PowerManager_LoadPowerManager_Patch
{
    static void Postfix(PowerManager __instance)
    {
        foreach (var kvp in __instance.PowerItemDictionary)
        {
            if (kvp.Value is PowerItemORGate orGate)
            {
                orGate.RestoreSecondParent();
            }
        }
        Log.Out("[ORBlock] Restored Logic Relay connections.");
    }
}

/// <summary>
/// Factory helper for creating PowerItemORGate instances.
/// </summary>
public class PowerItemFactory
{
    public static PowerItemORGate CreateORGatePowerItem()
    {
        return new PowerItemORGate();
    }
}
