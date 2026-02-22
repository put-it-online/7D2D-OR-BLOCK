# Save/Load Connection Fix Design

## Problem

Two bugs after save/reload:

1. **Primary parent connection lost** — Wire 1 disappears completely, hover shows Input 1 as Empty
2. **Second parent wire invisible** — Input 2 is functionally connected (hover shows Connected) but the visual wire is not drawn

## Root Causes

### Bug 1: Primary parent lost

In `TileEntityPowered_InitializePowerData_Patch`, the upgrade from `PowerConsumer` to `PowerItemORGate` destroys the primary parent link:

1. `oldItem.Parent` is set correctly (from power.dat loading)
2. `RemovePowerNode(oldItem)` removes oldItem from parent's Children and nulls parent references
3. New `PowerItemORGate` is created and added via `AddPowerNode(orGate)` with no parent
4. Nobody restores `orGate.Parent` to the original parent

### Bug 2: Second parent wire invisible

`RestoreSecondParent()` sets the `SecondParent` reference and adds the gate to `secondParent.Children`, but doesn't create a `wireDataList` entry on the second parent's TileEntity. Wire visuals are driven by `wireDataList`.

## Design

### Fix 1: Capture and restore primary parent

In `TileEntityPatches.cs`, `InitializePowerData` postfix:

```csharp
PowerItem originalParent = oldItem.Parent;  // capture BEFORE RemovePowerNode

// ... existing RemovePowerNode, CleanPhantomChildren, create orGate, AddPowerNode ...

// Restore primary parent
if (originalParent != null)
{
    PowerManager.Instance.SetParent(orGate, originalParent);
}
```

`SetParent` will go through `PowerManager_SetParent_Patch`. Since `orGate.Parent` is null at that point, the patch lets vanilla SetParent run, which sets `orGate.Parent = originalParent` and adds the gate to `originalParent.Children`.

### Fix 2: Wire visual for second parent

Requires investigation into `CheckForNewWires()` and `wireDataList` to understand how wire visuals are created. After understanding the mechanism, add wire data for the second parent in `RestoreSecondParent()` or `ApplyMetadata()`.

Also need to verify whether `CheckForNewWires` handles parent 1's wire after our `SetParent` fix, or if we need explicit wire data for parent 1 too.

## Approach chosen

Approach A from brainstorming: capture `oldItem.Parent` before upgrade, restore with `SetParent` after. Minimal code, low risk, no persistence format changes.

## Files to modify

| File | Change |
|------|--------|
| `Sources/Harmony/TileEntityPatches.cs` | Add parent capture + SetParent call in InitializePowerData postfix |
| `Sources/PowerItemORGate.cs` | Possibly add wire data creation in RestoreSecondParent() |

## Out of scope

- No changes to persistence format (power.dat or chunk data)
- No backward compatibility concerns
- No changes to ActivationPatches.cs or PowerManagerPatches.cs
