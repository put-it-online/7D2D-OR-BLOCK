# Save/Load Connection Fix Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix two bugs after save/reload — primary parent connection lost, and second parent wire invisible.

**Architecture:** Capture `oldItem.Parent` before the PowerConsumer→PowerItemORGate upgrade destroys it, then restore via `SetParent`. For wire visuals, rebuild `wireDataList` on both parents' TileEntities after their connections are restored. Wire visuals are driven by the parent's `wireDataList` (`List<Vector3i>` of child positions), rebuilt via `CreateWireDataFromPowerItem()`.

**Tech Stack:** C# / Harmony patches / 7 Days to Die modding API

**Subagent instructions:** See `docs/subagent-instructions.md` for critical build/patch rules. Use `bash build.sh` to build (NOT `dotnet build`). Use `game-developer` subagent type for all implementation tasks.

---

### Task 1: Restore primary parent link in InitializePowerData

**Files:**
- Modify: `Sources/Harmony/TileEntityPatches.cs:254-293` (InitializePowerData Postfix, between oldItem capture and RemovePowerNode)

**Step 1: Capture oldItem.Parent before RemovePowerNode**

In `TileEntityPowered_InitializePowerData_Patch.Postfix`, add one line after `PowerItem oldItem = __instance.PowerItem;` (line 254) and before `RemovePowerNode` (line 270):

```csharp
        PowerItem oldItem = __instance.PowerItem;
        Vector3i gatePos = __instance.ToWorldPos();
        PowerItem originalParent = oldItem.Parent;  // <-- ADD THIS LINE
```

**Step 2: Restore primary parent after AddPowerNode**

After `PowerManager.Instance.AddPowerNode(orGate);` (line 293) and before the children re-attach loop (line 297), add:

```csharp
        PowerManager.Instance.AddPowerNode(orGate);

        // Restore primary parent link that was destroyed by RemovePowerNode.
        // Must happen before children re-attach so the gate is properly parented.
        if (originalParent != null)
        {
            PowerManager.Instance.SetParent(orGate, originalParent);
            Log.Out("[ORBlock] InitializePowerData: restored primary parent at "
                + originalParent.Position + " for gate at " + gatePos);
        }
```

**Step 3: Build**

Run: `bash build.sh`
Expected: Build succeeds, ORBlock.dll copied to mod root.

**Step 4: Commit**

```bash
git add Sources/Harmony/TileEntityPatches.cs
git commit -m "fix: restore primary parent link on save/load"
```

---

### Task 2: Rebuild wire visuals for both parents after restoration

**Files:**
- Modify: `Sources/Harmony/TileEntityPatches.cs:352-371` (ApplyMetadata method)
- Modify: `Sources/Harmony/TileEntityPatches.cs:293` (after primary parent SetParent, from Task 1)

**Context:** Wire visuals are driven by each parent's `wireDataList` (`List<Vector3i>`). After `SetParent` or `RestoreSecondParent` modifies `Children`, the parent's `wireDataList` must be rebuilt. The pattern is: `CreateWireDataFromPowerItem()` → `SendWireData()` → `RemoveWires()` → `DrawWires()`. This is the same pattern used in `DisconnectAllInputs()` (PowerItemORGate.cs:187-200).

**Step 1: Add wire rebuild helper**

Add a private static helper method to `TileEntityPowered_InitializePowerData_Patch`, after the `ApplyMetadata` method (after line 371):

```csharp
    /// <summary>
    /// Rebuilds wire visuals on a parent's TileEntity after its Children list changed.
    /// This ensures the visual wire from parent to gate is drawn after load.
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
        Log.Out("[ORBlock] RebuildParentWires: rebuilt wire data for " + label
            + " at " + parent.Position);
    }
```

**Step 2: Rebuild primary parent wires after SetParent**

After the primary parent restoration block added in Task 1, add:

```csharp
        if (originalParent != null)
        {
            PowerManager.Instance.SetParent(orGate, originalParent);
            Log.Out("[ORBlock] InitializePowerData: restored primary parent at "
                + originalParent.Position + " for gate at " + gatePos);
            RebuildParentWires(originalParent, "primary parent");
        }
```

**Step 3: Rebuild second parent wires in ApplyMetadata**

In the `ApplyMetadata` method (line 352-371), after `orGate.RestoreSecondParent()` (line 364), add wire rebuild:

```csharp
            if (meta.HasSecondParent)
            {
                orGate.RestoreSecondParent();
                RebuildParentWires(orGate.SecondParent, "second parent");
            }
```

**Step 4: Build**

Run: `bash build.sh`
Expected: Build succeeds, ORBlock.dll copied to mod root.

**Step 5: Commit**

```bash
git add Sources/Harmony/TileEntityPatches.cs
git commit -m "fix: rebuild wire visuals for both parents on load"
```

---

### Task 3: In-game verification

**Preconditions:** Tasks 1-2 complete, mod built successfully.

**Step 1: Test save/load with two-parent OR gate**

1. Launch 7 Days to Die, load a world with an existing OR gate connected to two parents
2. Check F1 console for `[ORBlock]` log lines confirming:
   - `InitializePowerData: restored primary parent at <pos>`
   - `RebuildParentWires: rebuilt wire data for primary parent`
   - `ApplyMetadata: applied metadata`
   - `RebuildParentWires: rebuilt wire data for second parent`
3. Verify hover text shows both Input 1 and Input 2 as Connected
4. Verify both wires are visually drawn
5. Verify gate responds to both parents' power/triggers

**Step 2: Test mode persistence**

1. Set gate to AND mode, save, reload
2. Verify hover text shows AND mode
3. Verify AND logic works (both inputs required)

**Step 3: Test fresh wiring after load**

1. After reload, disconnect all inputs (radial menu)
2. Re-wire both parents
3. Verify both connections work and wires are visible

**Step 4: Report results**

Report which log lines appeared and whether the two bugs are fixed:
- Wire 1 visible after load?
- Wire 2 visible after load?
- Both inputs functional after load?

---

## Risk: Chunk load order timing

If parent2's chunk loads BEFORE the gate's chunk, parent2's `CheckForNewWires` may call `SetParent` on the old `PowerConsumer` before `InitializePowerData` upgrades it. This could overwrite parent1 as the primary parent. Our fix handles the common case (gate initializes with `oldItem.Parent` intact). If testing reveals this timing issue, a follow-up fix would store `primaryParentPosition` in chunk metadata alongside `secondParentPosition`.
