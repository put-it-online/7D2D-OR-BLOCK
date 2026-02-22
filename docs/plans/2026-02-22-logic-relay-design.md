# Logic Relay Block Design

**Date:** 2026-02-22
**Status:** Approved

## Summary

Rename the OR Gate block to "Logic Relay" and add selectable OR/AND mode via an in-game activation command menu. Fix the broken interaction system (no icon/prompt showing) by replacing Harmony patches on Block methods with a proper custom Block class.

## Problems Solved

1. **No interaction icon or prompt** — The mod patches `GetActivationText` and `OnBlockActivated` via Harmony but never implements `GetBlockActivationCommands`, so the game has no commands to display.
2. **Can't access block menu** — Without activation commands, the radial menu never appears.
3. **No OR/AND selection** — The block only supports OR logic with no way to switch.

## Architecture: Hybrid Approach

- **Custom Block class** (`BlockLogicRelay`) for all interaction/UI logic — follows vanilla patterns.
- **Harmony patches** kept only for power system internals that can't be extended via XML/Block classes.

## Rename

| Before | After |
|--------|-------|
| `ORGateBlock` (XML name) | `LogicRelayBlock` |
| `OR Gate` (display name) | `Logic Relay` |
| `IsORGate` (custom property) | `IsLogicRelay` |
| Description | Updated to reflect OR/AND capability |

Internal names (`ORBlock` namespace, `PowerItemORGate` class) stay as-is — they're not user-facing.

## Block Interaction: BlockLogicRelay

New C# class extending `BlockPowered`. Overrides:

### GetBlockActivationCommands()

Returns two commands:

| Index | Command | Icon | Default | Description |
|-------|---------|------|---------|-------------|
| 0 | `toggle_mode` | `electric_switch` | yes | Toggles between OR and AND mode |
| 1 | `disconnect` | `electric_disconnect` | no | Disconnects all inputs |

Commands are enabled based on: block has `IsLogicRelay` property, player has land claim access.

### GetActivationText()

Shows current state when looking at block:
```
Logic Relay [Mode: OR] [Input 1: Connected] [Input 2: Empty] [Output: ON]
```

### OnBlockActivated()

Dispatches commands:
- `toggle_mode`: Cycles gateMode OR↔AND on the PowerItemORGate, shows tooltip.
- `disconnect`: Calls `DisconnectAllInputs()` on the PowerItemORGate, shows tooltip.

### XML Block Definition

```xml
<block name="LogicRelayBlock">
    <property name="Extends" value="electricwirerelay" />
    <property name="Class" value="LogicRelay" />
    <property name="CreativeMode" value="Player" />
    <property name="CustomIcon" value="electricwirerelay" />
    <property name="DescriptionKey" value="LogicRelayBlockDesc" />
    <property name="IsLogicRelay" value="true" />
    <property name="RequiredPower" value="0" />
</block>
```

## Gate Mode State

### PowerItemORGate Changes

- Add `GateMode` enum: `OR = 0, AND = 1`
- Add `gateMode` field (default: OR)
- Serialize mode in `write()` / `read()` (1 extra byte)
- Update power logic:
  - **OR:** `Parent.IsPowered || SecondParent.IsPowered`
  - **AND:** `Parent != null && Parent.IsPowered && SecondParent != null && SecondParent.IsPowered`

AND mode requires both inputs connected and powered. If only one input is wired, output is always OFF.

## Harmony Patches

### Kept (power system)

| Patch | Purpose |
|-------|---------|
| `PowerManager_SetParent_Patch` | Dual parent support |
| `PowerManager_RemoveParent_Patch` | Cleanup safety net |
| `PowerItem_IsPowered_Patch` | OR/AND logic dispatch |
| `PowerManager_LoadPowerManager_Patch` | Restore second parent on load |
| `TileEntityPowered_InitializePowerData_Patch` | Create PowerItemORGate |

All updated to check `IsLogicRelay` instead of `IsORGate`.

### Removed (moved to BlockLogicRelay)

| Patch | Replacement |
|-------|-------------|
| `Block_GetActivationText_Patch` | `BlockLogicRelay.GetActivationText()` |
| `Block_OnBlockActivated_Patch` | `BlockLogicRelay.OnBlockActivated()` |

## File Changes

| File | Action |
|------|--------|
| `Config/blocks.xml` | Rename block, add `Class="LogicRelay"`, update properties |
| `Config/Localization.txt` | Update display name and description |
| `Config/recipes.xml` | Update recipe name |
| `Sources/PowerItemORGate.cs` | Add GateMode enum, mode field, serialization, AND logic |
| `Sources/BlockLogicRelay.cs` | **New** — custom Block class with activation commands |
| `Sources/Harmony/DisconnectPatches.cs` | **Delete** — logic moves to BlockLogicRelay |
| `Sources/Harmony/TileEntityPatches.cs` | Remove GetActivationText patch, update property checks |
| `Sources/Harmony/PowerManagerPatches.cs` | Update property checks |
