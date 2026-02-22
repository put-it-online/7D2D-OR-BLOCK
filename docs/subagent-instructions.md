# Subagent Instructions

Critical things that previous subagents got wrong or forgot. Read this before making any changes.

## Build process

**ALWAYS use `bash build.sh`** to build the mod. Do NOT run `dotnet build` directly.

The build outputs to `bin/ORBlock.dll` but the game loads from the mod root `ORBlock.dll`. The `build.sh` script handles the copy step. Previous subagents ran `dotnet build` directly, skipping the copy — the game loaded a stale DLL and none of the code changes took effect. Multiple debugging sessions were wasted on this.

## Patch registration

`ORBlockMod.cs` uses `harmony.PatchAll()` to discover all `[HarmonyPatch]` classes automatically. A previous subagent replaced this with an explicit list of patch classes but only listed the activation patches — completely omitting the TileEntity and PowerManager patches. This silently broke the entire mod. If you change the patch registration approach, make sure ALL patch classes from ALL files are included.

Patch classes exist in:
- `Sources/Harmony/ActivationPatches.cs`
- `Sources/Harmony/TileEntityPatches.cs`
- `Sources/Harmony/PowerManagerPatches.cs`

## Custom block classes don't work in mod DLLs

7D2D uses `Type.GetType()` to resolve block classes from `blocks.xml`. This only searches `Assembly-CSharp.dll`, not mod assemblies. Setting `Class="LogicRelay"` in blocks.xml crashes the game. Do NOT add a `Class` property to blocks.xml. All custom behavior must go through Harmony patches guarded by the `IsLogicRelay` property check.

## Harmony Prefix return values

- `return false` from a Prefix = **skip the original method** (only `__result` is used)
- `return true` from a Prefix = **run the original method normally**

A previous subagent set `__result = false; return false;` in the `OnBlockActivated` bool patch, which skipped the original method — the original method body is where the game opens the radial menu. This prevented the interaction menu from ever appearing.

## Verify changes in-game logs

All activation patches have `Log.Out("[ORBlock] ...")` debug lines. After making changes, tell the user to check the game log for these lines to confirm patches are firing. The log file is typically at `%APPDATA%/7DaysToDie/output_log.txt` or visible in the game's F1 console.

## The electrictimerrelay is the reference block

The user specifically identified this vanilla block as having a working interaction menu. Use it as the gold standard for how activation menus work. Its block definition is in `C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\Data\Config\blocks.xml` and its class can be decompiled from `Assembly-CSharp.dll` using `ilspycmd`.

## Game DLL location

`C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\7DaysToDie_Data\Managed\Assembly-CSharp.dll`

Use `ilspycmd` to decompile classes. Don't guess method signatures — verify them.
