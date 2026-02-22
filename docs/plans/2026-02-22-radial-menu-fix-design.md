# Radial Menu Fix Design

## Date: 2026-02-22

## Problem

The radial interaction menu does not appear when pressing E on the Logic Relay block. The menu silently opens and closes in the same frame because zero commands are enabled.

## Root Cause

`GetBlockActivationCommands` uses `IsMyLandProtectedBlock` to enable commands. This method returns `true` only when the player has a land claim block covering the target position. Without a land claim, both commands have `enabled = false`, producing 0 enabled entries. The game's `XUiC_Radial.SetCommonData` detects 0 enabled entries and immediately closes the radial window.

The vanilla `BlockTimerRelay` (used by `electrictimerrelay`) uses `CanPlaceBlockAt` instead, which returns `true` for any location not inside a trader area — no land claim required.

## Fix

### File 1: `Sources/Harmony/ActivationPatches.cs`

Change `IsMyLandProtectedBlock` to `CanPlaceBlockAt` in `BlockPowered_GetBlockActivationCommands_Patch`:

```csharp
// Before:
bool isUsable = _world.IsMyLandProtectedBlock(_blockPos,
    _world.GetGameManager().GetPersistentLocalPlayer());

// After:
bool canPlace = _world.CanPlaceBlockAt(_blockPos,
    _world.GetGameManager().GetPersistentLocalPlayer());
```

### File 2: `Config/Localization.txt`

Add missing radial menu label keys:

```
blockcommand_toggle_mode,UI,Radial blockcommand,false,false,Toggle Mode
blockcommand_disconnect,UI,Radial blockcommand,false,false,Disconnect
```

## Verification

1. Build with `bash build.sh`
2. Place Logic Relay block in open world (no land claim)
3. Press E — radial menu should appear with "Toggle Mode" and "Disconnect"
4. Select Toggle Mode — should switch between OR and AND
5. Select Disconnect — should remove all inputs
