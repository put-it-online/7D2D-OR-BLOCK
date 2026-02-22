# Save/Load Timing Fix Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix chunk load order timing bug where both parents resolve to the same block after save/reload, and fix invisible gate-to-child wires.

**Architecture:** Store primary parent position in chunk metadata alongside existing secondary parent data. On load, restore both parents from stored positions instead of relying on `oldItem.Parent` (which gets overwritten by CheckForNewWires). Also rebuild the gate's own wireDataList after children re-attach.

**Tech Stack:** C# / Harmony patches / 7 Days to Die modding API

**Subagent instructions:** See `docs/subagent-instructions.md` for critical build/patch rules. Use `bash build.sh` to build (NOT `dotnet build`). Use `game-developer` subagent type for all implementation tasks.

---

### Task 1: Add PrimaryParentPosition to metadata struct and write patch

**Files:**
- Modify: `Sources/Harmony/TileEntityPatches.cs`

**Step 1: Add fields to ORGateMetadata struct (line 33-38)**

Change:
```csharp
public struct ORGateMetadata
{
    public bool HasSecondParent;
    public Vector3i SecondParentPosition;
    public GateMode Mode;
}
```

To:
```csharp
public struct ORGateMetadata
{
    public bool HasSecondParent;
    public Vector3i SecondParentPosition;
    public GateMode Mode;
    public bool HasPrimaryParent;
    public Vector3i PrimaryParentPosition;
}
```

**Step 2: Write primary parent position in TileEntityPowered_Write_Patch (lines 74-87)**

After the existing `_bw.Write((byte)orGate.Mode);` line (line 83), add primary parent write:

```csharp
            _bw.Write((byte)orGate.Mode);

            // Primary parent position (new field — appended after existing fields)
            bool hasPrimaryParent = orGate.Parent != null;
            _bw.Write(hasPrimaryParent);
            if (hasPrimaryParent)
            {
                _bw.Write(orGate.Parent.Position.x);
                _bw.Write(orGate.Parent.Position.y);
                _bw.Write(orGate.Parent.Position.z);
            }

            Log.Out("[ORBlock] TileEntityPowered.write: saved ORGate metadata at "
```

**Step 3: Write primary parent in the else branch too (lines 89-95)**

The else branch writes defaults when no OR gate exists yet. After `_bw.Write((byte)GateMode.OR);` (line 94), add:

```csharp
            _bw.Write(false); // hasSecondParent
            _bw.Write((byte)GateMode.OR); // mode
            _bw.Write(false); // hasPrimaryParent (new field default)
```

**Step 4: Build and commit**

Run: `bash build.sh`

```bash
git add Sources/Harmony/TileEntityPatches.cs
git commit -m "feat: add primary parent position to chunk metadata write"
```

---

### Task 2: Read primary parent position with backward-compatible migration

**Files:**
- Modify: `Sources/Harmony/TileEntityPatches.cs`

**Step 1: Read primary parent in TileEntityPowered_Read_Patch (lines 127-159)**

After the `GateMode mode = (GateMode)_br.ReadByte();` line (line 138) and before the `ORGateMetadataStore.Store` call, add a nested try-catch to read the new fields:

```csharp
            GateMode mode = (GateMode)_br.ReadByte();

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
            ORGateMetadataStore.Store(worldPos, new ORGateMetadata
            {
                HasSecondParent = hasSecondParent,
                SecondParentPosition = secondParentPos,
                Mode = mode,
                HasPrimaryParent = hasPrimaryParent,
                PrimaryParentPosition = primaryParentPos
            });

            Log.Out("[ORBlock] TileEntityPowered.read: loaded ORGate metadata at "
                + worldPos
                + " hasSecondParent=" + hasSecondParent
                + " hasPrimaryParent=" + hasPrimaryParent
                + " mode=" + mode);
```

**Step 2: Build and commit**

Run: `bash build.sh`

```bash
git add Sources/Harmony/TileEntityPatches.cs
git commit -m "feat: read primary parent position from chunk metadata with migration"
```

---

### Task 3: Restore primary parent from stored position + rebuild gate wires

**Files:**
- Modify: `Sources/Harmony/TileEntityPatches.cs`

**Step 1: Move primary parent restoration into ApplyMetadata**

The current primary parent restoration at lines 296-304 uses `originalParent` (captured from `oldItem.Parent`). Replace it with metadata-driven restoration inside `ApplyMetadata`, with `originalParent` as fallback.

Remove the current primary parent block (lines 296-304):
```csharp
        // Restore primary parent link that was destroyed by RemovePowerNode.
        // Must happen before children re-attach so the gate is properly parented.
        if (originalParent != null)
        {
            PowerManager.Instance.SetParent(orGate, originalParent);
            Log.Out("[ORBlock] InitializePowerData: restored primary parent at "
                + originalParent.Position + " for gate at " + gatePos);
            RebuildParentWires(originalParent, "primary parent");
        }
```

And pass `originalParent` to `ApplyMetadata` instead. Change the call from:
```csharp
        ApplyMetadata(orGate, gatePos);
```
To:
```csharp
        ApplyMetadata(orGate, gatePos, originalParent);
```

**Step 2: Update ApplyMetadata to restore primary parent**

Change the `ApplyMetadata` signature and add primary parent restoration:

```csharp
    private static void ApplyMetadata(PowerItemORGate orGate, Vector3i gatePos, PowerItem fallbackParent = null)
    {
        if (ORGateMetadataStore.TryGet(gatePos, out ORGateMetadata meta))
        {
            orGate.Mode = meta.Mode;
            orGate.HasSecondParent = meta.HasSecondParent;
            orGate.SecondParentPosition = meta.SecondParentPosition;
            ORGateMetadataStore.Remove(gatePos);

            // Restore primary parent from stored position (immune to CheckForNewWires timing)
            if (meta.HasPrimaryParent)
            {
                PowerItem primaryParent = PowerManager.Instance.GetPowerItemByWorldPos(meta.PrimaryParentPosition);
                if (primaryParent != null)
                {
                    PowerManager.Instance.SetParent(orGate, primaryParent);
                    RebuildParentWires(primaryParent, "primary parent");
                    Log.Out("[ORBlock] ApplyMetadata: restored primary parent from stored position at "
                        + meta.PrimaryParentPosition);
                }
                else
                {
                    Log.Warning("[ORBlock] ApplyMetadata: could not find primary parent at "
                        + meta.PrimaryParentPosition);
                }
            }
            else if (fallbackParent != null)
            {
                // Old save without stored primary parent — use captured oldItem.Parent
                PowerManager.Instance.SetParent(orGate, fallbackParent);
                RebuildParentWires(fallbackParent, "primary parent (fallback)");
                Log.Out("[ORBlock] ApplyMetadata: restored primary parent from fallback at "
                    + fallbackParent.Position);
            }

            // Restore second parent
            if (meta.HasSecondParent)
            {
                orGate.RestoreSecondParent();
                RebuildParentWires(orGate.SecondParent, "second parent");
            }

            Log.Out("[ORBlock] ApplyMetadata: applied metadata at " + gatePos
                + " mode=" + meta.Mode
                + " hasSecondParent=" + meta.HasSecondParent
                + " hasPrimaryParent=" + meta.HasPrimaryParent);
        }
        else if (fallbackParent != null)
        {
            // No metadata at all — just restore the fallback parent
            PowerManager.Instance.SetParent(orGate, fallbackParent);
            RebuildParentWires(fallbackParent, "primary parent (no metadata)");
        }
    }
```

Also update the "already upgraded" call at line 247 to pass null for fallbackParent (signature has default):
```csharp
            ApplyMetadata((PowerItemORGate)__instance.PowerItem, __instance.ToWorldPos());
```
This is unchanged — the default `null` applies.

**Step 3: Rebuild gate's own wires after children re-attach**

After the children re-attach loop (lines 306-317) and before the `ApplyMetadata` call, add a wire rebuild for the gate itself:

```csharp
        // Rebuild the gate's own wireDataList for child wires (e.g., gate → speaker)
        RebuildParentWires(orGate, "gate children");
```

**Step 4: Build and commit**

Run: `bash build.sh`

```bash
git add Sources/Harmony/TileEntityPatches.cs
git commit -m "fix: restore parents from stored positions, rebuild gate child wires"
```

---

### Task 4: In-game verification

**Preconditions:** Tasks 1-3 complete, mod built successfully.

**Step 1: Test save/load**

1. Launch game, load world with two-parent OR gate in AND mode
2. Check F1 console for `[ORBlock]` log lines confirming:
   - `restored primary parent from stored position at <pos1>` (NOT same as second parent)
   - `rebuilt wire data for second parent at <pos2>` (different from primary)
   - `rebuilt wire data for gate children`
3. Verify hover text shows both Input 1 and Input 2 as Connected
4. Verify all three wires visible: MC1→gate, MC2→gate, gate→speaker
5. Verify AND mode: only activates when BOTH motion cameras detect

**Step 2: Test backward compatibility**

1. If possible, test with a save from before this fix (old format without primaryParentPosition)
2. Should fall back to `oldItem.Parent` with `(fallback)` in log
3. Timing bug may still occur on old saves (expected — new saves will be immune)
