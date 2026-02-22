# Save/Load Investigation Notes

## Current Status
- No more NullReferenceException crash on load
- AND mode works correctly during gameplay
- OR mode works correctly
- Disconnect works (wires removed visually + persists)
- Toggle Mode works (radial menu with both options)
- **BUG: Connection 1 (primary parent) is lost on save/reload**

## Bug: Primary parent connection lost on load

After saving and reloading, the first parent connection is gone. The second parent (restored via `ORGateMetadataStore` chunk data) survives, but the primary parent does not re-link.

## How the dual save system works

The OR gate data is split across two persistence systems:

### 1. `power.dat` (PowerManager save file)
- Stores the vanilla power graph: nodes, parent-child links, positions
- Written by `PowerItem.write()` / read by `PowerItem.read()`
- The gate appears under BOTH parents' Children lists (because both parents have it in their `Children`)
- On load, each parent deserializes its children and calls `AddPowerNode` for each

### 2. Chunk TileEntity data
- Stores block tile entity state per chunk
- Written by `TileEntityPowered.write()` / read by `TileEntityPowered.read()`
- Our patches append OR gate metadata (hasSecondParent, secondParentPosition, mode) here
- Read into `ORGateMetadataStore` during chunk load, applied later in `InitializePowerData`

### Why the split exists

Previously, `PowerItemORGate.write()` appended extra bytes (secondParentPosition + mode) after `base.write()` in power.dat. This caused stream corruption:

1. The gate is serialized under both parents' Children lists in power.dat
2. On load, the second occurrence is created as a plain `PowerConsumer` (not `PowerItemORGate`)
3. `PowerConsumer.read()` only reads base fields, leaving the extra bytes unconsumed
4. Those stale bytes corrupt the NEXT item read from the stream
5. The mode byte (0 or 1) was misread as `PowerTrigger.TriggerType`, turning a motion detector into a PressurePlate
6. `MotionSensorController.Update()` then cast to `PowerPressurePlate`, got null, and threw NullReferenceException every frame

The fix moved OR gate metadata to TileEntity chunk data (which is NOT duplicated like power.dat) and stripped all extra bytes from `PowerItemORGate.write()`/`read()`.

## Load sequence (order of events)

Understanding the load order is critical for debugging connection 1 loss:

```
1. PowerManager.LoadPowerManager()
   - Reads power.dat
   - For each PowerSource, reads Children recursively
   - Each child calls PowerItem.CreateItem() → creates PowerConsumer (not PowerItemORGate)
   - Calls AddPowerNode(node, parent) for each
   - Our AddPowerNode patch skips duplicates (same position already in dict)
   - At this point: gate is a plain PowerConsumer with Parent set to first parent

   Postfix: PowerManager_LoadPowerManager_Patch
   - Iterates all power items, calls RestoreSecondParent() on any PowerItemORGate
   - But at this point, no PowerItemORGate exists yet — only plain PowerConsumer
   - This is a safety net, not the primary restore mechanism

2. Chunk loading → TileEntity deserialization
   - TileEntityPowered.read() fires for each powered block in the chunk
   - Our TileEntityPowered_Read_Patch reads OR gate metadata from chunk data
   - Stores in ORGateMetadataStore keyed by world position

3. TileEntityPowered.InitializePowerData() fires for each TileEntity
   - Our patch checks if block has IsLogicRelay property
   - If PowerItem is a plain PowerConsumer (from power.dat), upgrades to PowerItemORGate:
     a. Collects children from old item
     b. Calls RemovePowerNode(oldItem) — removes from dict, parent's Children, orphans children
     c. Calls CleanPhantomChildren(gatePos) — removes stale nodes from all Children lists
     d. Creates new PowerItemORGate, sets Position and BlockID
     e. Calls AddPowerNode(orGate) — adds to dict (but with NO parent!)
     f. Re-attaches children via SetParent(child, orGate)
     g. Calls ApplyMetadata() — sets Mode, HasSecondParent, SecondParentPosition
     h. Calls RestoreSecondParent() — looks up second parent by position, wires it
```

## Why connection 1 is lost — the likely cause

Looking at step 3e: `AddPowerNode(orGate)` is called with **no parent argument**. The vanilla `AddPowerNode(node, parent=null)` adds the node to `PowerItemDictionary` and `Circuits` but does NOT set `node.Parent`. The primary parent link is lost because:

1. The old `PowerConsumer` had `Parent` set (from power.dat loading in step 1)
2. `RemovePowerNode(oldItem)` in step 3b removes the old item from the parent's Children
3. The new `PowerItemORGate` is added with `parent=null`
4. Nobody calls `SetParent(orGate, originalParent)` to restore the primary parent link
5. `RestoreSecondParent()` restores connection 2, but connection 1 has no equivalent restore

### What needs to happen

Before calling `RemovePowerNode(oldItem)` in `InitializePowerData`, capture `oldItem.Parent`. After creating the new `PowerItemORGate` and adding it via `AddPowerNode`, call `SetParent(orGate, originalParent)` to restore the primary parent link.

Something like:
```csharp
PowerItem originalParent = oldItem.Parent;  // capture before RemovePowerNode nulls it

// ... RemovePowerNode, CleanPhantomChildren, create orGate, AddPowerNode ...

// Restore primary parent
if (originalParent != null)
{
    PowerManager.Instance.SetParent(orGate, originalParent);
}
```

### Alternative: rely on CheckForNewWires

The current code comments mention that `CheckForNewWires()` (which runs in `TileEntityPowered.OnReadComplete` after `InitializePowerData`) should re-link the gate to its primary parent via the parent's `TileEntity.wireDataList`. But this may not work if:
- `CleanPhantomChildren` removed the gate from the parent's Children list
- The parent's TileEntity hasn't initialized yet
- `CheckForNewWires` only creates wires, not power connections

### Alternative: store primary parent position in chunk data too

Currently only `secondParentPosition` is stored in chunk data. If we also stored `primaryParentPosition`, we could restore both connections in `ApplyMetadata()` the same way `RestoreSecondParent()` works. This would make the gate independent of power.dat parent links.

## Key methods to decompile for further investigation

- `PowerManager.RemovePowerNode(PowerItem)` — what exactly does it clean up?
- `TileEntityPowered.OnReadComplete()` — does it call CheckForNewWires?
- `TileEntityPowered.CheckForNewWires()` — does it restore Parent links or just draw wires?
- `PowerManager.AddPowerNode(PowerItem, PowerItem)` — does it handle null parent correctly?

## Architecture reference

### Files and their responsibilities

| File | Purpose |
|------|---------|
| `PowerItemORGate.cs` | Power logic class: dual inputs, OR/AND modes, HandlePowerUpdate override |
| `ActivationPatches.cs` | Radial menu: HasBlockActivationCommands, GetBlockActivationCommands, GetActivationText, OnBlockActivated(string) |
| `PowerManagerPatches.cs` | Power graph: AddPowerNode duplicate guard, SetParent second-parent routing, IsPowered override, LoadPowerManager postfix |
| `TileEntityPatches.cs` | Persistence: TileEntity write/read for OR gate metadata, InitializePowerData for PowerConsumer→PowerItemORGate upgrade, ORGateMetadataStore, phantom cleanup |

### Power propagation paths

Two passes run every 0.16s from `PowerSource.Update()`:

**Pass 1 — HandlePowerReceived (power budget accounting):**
```
PowerSource.HandleSendPower()
  → child.HandlePowerReceived(ref power)
    → sets isPowered FIELD based on power budget
    → checks PowerChildren() — our override returns IsOutputPowered()
    → if true: recurse into gate's Children
```

**Pass 2 — HandlePowerUpdate (device activation):**
```
PowerSource: foreach child: child.HandlePowerUpdate(IsOn)
  → PowerTrigger.HandlePowerUpdate: if IsActive → child.HandlePowerUpdate(isPowered && parentIsOn)
    → PowerItemORGate.HandlePowerUpdate(isOn) — our OVERRIDE
      → computes outputOn = isOn && IsOutputPowered()
      → TileEntity.Activate(outputOn)
      → propagates outputOn to children
```

### IsPowered vs IsActive

**Critical distinction for trigger-type parents (motion detectors, switches, pressure plates):**
- `IsPowered` = "electricity is flowing from the generator" — always true for all devices downstream of a running generator
- `IsActive` = "the sensor has actually triggered" — only true when the sensor fires

`IsOutputPowered()` uses `IsParentActive()` which checks `trigger.IsActive` for PowerTrigger parents and `parent.IsPowered` for non-trigger parents.

## Bugs fixed in this session

### 1. Radial menu not appearing (FIXED)
- **Cause:** `IsMyLandProtectedBlock` requires a land claim block. Without one, both commands had `enabled=false`, producing 0 enabled entries. The radial window opened and closed in the same frame.
- **Fix:** Changed to `CanPlaceBlockAt` (same as vanilla `BlockTimerRelay`). Only blocked in trader areas.

### 2. Disconnect option invisible in radial menu (FIXED)
- **Cause:** Icon name `"electric_disconnect"` doesn't exist in the game's UIAtlas. The entry was added to the radial wheel but rendered as an invisible blank slot.
- **Fix:** Changed icon to `"x"` (valid vanilla icon used by land claim "remove" command).

### 3. Disconnect command not working (FIXED)
- **Cause:** Four compounding failures: parent TileEntity wire data not rebuilt, no SendWireData network packet, gate's parentPosition not reset, second parent's TileEntity not updated.
- **Fix:** Rewrote `DisconnectAllInputs()` to mirror vanilla `RemoveParentWithWiringTool` pattern.

### 4. AND mode not working (FIXED)
- **Root cause:** `IsOutputPowered()` checked `Parent.IsPowered` which for PowerTriggers just means "has electricity from generator" (always true). The correct check is `Parent.IsActive` (has the sensor fired).
- **Secondary cause:** `HandlePowerUpdate` reads the `isPowered` FIELD directly (not the IsPowered property), and the field was always true from generator power.
- **Fix:** Added `IsParentActive()` helper (checks IsActive for triggers, IsPowered for others). Added direct `HandlePowerUpdate` override on `PowerItemORGate` instead of Harmony patching.

### 5. NullReferenceException crash on load (FIXED)
- **Cause:** `PowerItemORGate.write()` wrote extra bytes to power.dat. On load, the gate's second occurrence was read as a plain PowerConsumer, leaving extra bytes unconsumed. The mode byte was misread as `PowerTrigger.TriggerType`, corrupting the motion detector.
- **Fix:** Moved OR gate metadata to TileEntity chunk data. Stripped extra bytes from power.dat write/read. Added migration guard to fix corrupted TriggerType from old saves.

### 6. Connection 1 lost on load (OPEN)
- **Cause:** See "Why connection 1 is lost" section above.
- **Status:** Not yet fixed. Needs investigation into whether capturing `oldItem.Parent` and calling `SetParent(orGate, originalParent)` after the upgrade is sufficient.

## Localization keys

Added to `Config/Localization.txt`:
```
blockcommand_toggle_mode,UI,Radial blockcommand,false,false,Toggle Mode
blockcommand_disconnect,UI,Radial blockcommand,false,false,Disconnect
```

The radial menu constructs labels as `Localization.Get("blockcommand_" + command.text)`.

## Valid radial menu icons (from UIAtlas)

```
campfire, coin, door, dummy, electric_switch, frames, hand,
keypad, lock, map_cursor, pen, report, search, tool, unlock,
vending, wrench, x
```

The game constructs sprite names as `"ui_game_symbol_" + icon`. Using an invalid icon name causes the radial entry to render as an invisible blank slot.
