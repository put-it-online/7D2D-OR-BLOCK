# Activation Menu Investigation Notes

## Status
The hover text (GetActivationText) works. The radial interaction menu does NOT open when pressing E.

## What works
- World loads without errors
- Block can be placed and functions as OR gate
- Hover text shows mode, input states, output state (GetActivationText patch fires)
- OR logic works correctly
- AND logic can't be tested because of the radial interaction menu problem
- Save/load works (duplicate key error fixed)

## What doesn't work
- Pressing E does not open the radial interaction menu
- The `electrictimerrelay` vanilla block DOES have a working interaction menu — use it as reference

## Key findings so far

### Patches are firing
The Harmony patches ARE being applied and DO fire (confirmed by hover text working). The issue is not with patch application — it's with the interaction menu logic specifically.

### The bool-return OnBlockActivated is the entry point
`Block.OnBlockActivated(WorldBase, int, Vector3i, BlockValue, EntityPlayerLocal)` is called when E is pressed. The original method body contains the code that checks `HasBlockActivationCommands` and opens the radial menu. Previous attempts that skipped the original (Prefix returning false) prevented the menu from opening. Current version lets the original run (Prefix returns true) but the menu still doesn't open.

### Method ownership (from decompilation)
- `Block` declares: `OnBlockActivated(WorldBase, int, Vector3i, BlockValue, EntityPlayerLocal)` (bool overload)
- `BlockPowered` overrides: `HasBlockActivationCommands`, `GetBlockActivationCommands`, `GetActivationText`, `OnBlockActivated(string, WorldBase, int, ...)`
- `BlockPowered` does NOT override the bool-return `OnBlockActivated`

### What to investigate next

1. **Decompile `Block.OnBlockActivated(WorldBase, int, Vector3i, BlockValue, EntityPlayerLocal)`** — read the EXACT code path that opens the radial menu. What conditions must be true? Does it check something beyond `HasBlockActivationCommands`?

2. **Decompile `BlockPowered.OnBlockActivated`** — does BlockPowered override the bool version after all? The previous decompilation may have been wrong. Check ALL overloads.

3. **Compare with `electrictimerrelay`** — this vanilla block has a working interaction menu. Find its block definition in `C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\Data\Config\blocks.xml`, note its Class value, then decompile that class. Trace the EXACT code path from E press to radial menu opening for that block.

4. **Check if `OnBlockActivated` is even being called** — look for the `[ORBlock] Block_OnBlockActivated_Bool_Patch fired` log line in the game output. If it never fires, the issue is upstream (the game never calls this method for our block).

5. **Check if `HasBlockActivationCommands` is being called** — look for `[ORBlock] HasBlockActivationCommands fired` in logs. If this fires and returns true but the menu doesn't open, the issue is downstream.

6. **Check if `GetBlockActivationCommands` is being called** — look for `[ORBlock] GetBlockActivationCommands fired` in logs. If HasBlockActivationCommands fires but this doesn't, something is wrong between those two steps.

7. **Check the player interaction code** — the code that handles E press is likely in `PlayerInteraction`, `GUIWindowManager`, or similar. Decompile that to understand the full flow from keypress to radial menu display.

8. **Check if `electricwirerelay` itself has an interaction menu** — if the base block we extend doesn't have one, there may be a property or flag we're missing. Place a vanilla `electricwirerelay` in creative mode and try pressing E on it.

9. **Check for required XML properties** — some blocks need specific properties like `ActivationType` or similar to enable the interaction menu. Compare the full property set of `electrictimerrelay` vs `electricwirerelay` vs our `LogicRelayBlock`.

10. **Check if the issue is land claim related** — the commands are only enabled when `IsMyLandProtectedBlock` returns true. Test in creative mode with god mode. If the player doesn't have a land claim, commands may be disabled (enabled=false), which could prevent the menu from showing if ALL commands are disabled.
