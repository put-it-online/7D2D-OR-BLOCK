# Save/Load Timing Fix Design

## Problem

After save/reload, both parent connections resolve to the same block. The gate acts as a pass-through for one motion camera instead of combining two inputs with AND/OR logic. Wire from gate to children (speaker) is also invisible despite being functionally connected.

## Root Cause

**Chunk load order timing:** When a parent's chunk loads before the gate's chunk, that parent's `CheckForNewWires` calls `SetParent(oldPowerConsumer, parent)`. Since the gate is still a plain `PowerConsumer` (not yet upgraded to `PowerItemORGate`), vanilla `SetParent` runs and overwrites `oldItem.Parent`. If the second parent's chunk loads last, it overwrites the primary parent link. Then `InitializePowerData` captures the wrong parent, and `RestoreSecondParent` also points to the same block.

**Evidence from logs:**
```
restored primary parent at 335, 61, 482
rebuilt wire data for second parent at 335, 61, 482
```
Both parents resolved to the same position — the second parent overwrote the first.

**Speaker wire:** After PowerItem swap (PowerConsumer → PowerItemORGate), the gate's TileEntity wireDataList is never rebuilt to reflect the new PowerItem's Children list. The power connection works (children were re-attached) but the visual wire data is stale.

## Design

### 1. Store primary parent position in chunk metadata

Add `PrimaryParentPosition` to `ORGateMetadata`. Persist it in chunk data alongside the existing fields.

**New persistence format (appended after existing fields):**
```
[existing] HasSecondParent (1 byte)
[existing] SecondParentPosition (12 bytes, conditional)
[existing] Mode (1 byte)
[NEW]      HasPrimaryParent (1 byte)
[NEW]      PrimaryParentPosition (12 bytes, conditional)
```

**Backward compatibility:** Nested try-catch in read patch. Outer try-catch reads existing fields. Inner try-catch reads new primary parent fields. Old saves gracefully fall back to `oldItem.Parent`.

### 2. Restore primary parent from stored position

In `InitializePowerData`, instead of relying on `oldItem.Parent` (which may be overwritten by `CheckForNewWires`), use the stored `PrimaryParentPosition` from chunk metadata. Fall back to `oldItem.Parent` for old saves that don't have the stored position.

### 3. Rebuild gate's own wire data

After children are re-attached to the new `PowerItemORGate`, call `RebuildParentWires` (or equivalent) on the gate's own TileEntity. This rebuilds `wireDataList` from `orGate.Children`, making child wires (e.g., gate → speaker) visible.

## Files to modify

| File | Change |
|------|--------|
| `Sources/Harmony/TileEntityPatches.cs` | Add PrimaryParentPosition to metadata struct, write/read patches, ApplyMetadata, gate wire rebuild |

## Out of scope

- No changes to PowerManagerPatches.cs or PowerItemORGate.cs
- No changes to ActivationPatches.cs
