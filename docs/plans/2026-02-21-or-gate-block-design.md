# OR Gate Block Mod Design

## Overview

A 7 Days to Die mod that adds an OR gate block to the electricity system. The block accepts 2 electrical inputs and outputs power if either input is powered. Single-player only. Targets game version 2.5 (latest experimental).

## Approach

Harmony patching with XML block definition. This is the standard modding pattern used by popular electricity mods like OCB Electricity Overhaul.

## Mod Structure

```
ORBlock/
├── ModInfo.xml                    # Mod metadata
├── Config/
│   ├── blocks.xml                 # XPath: adds OR Gate block definition
│   ├── Localization.txt           # Display name & description
│   └── recipes.xml                # Crafting recipe
├── Sources/                       # C# source files
│   ├── ORBlockMod.cs              # IModApi entry point
│   ├── PowerItemORGate.cs         # Custom power item class
│   └── Harmony/
│       ├── PowerManagerPatches.cs # Patches for wire connections
│       └── TileEntityPatches.cs   # Patches for save/load
└── ORBlock.csproj                 # Build config
```

Deployment output: compiled DLL + Config/ + ModInfo.xml copied to game's `Mods/ORBlock/` folder.

## Architecture

### Power Logic

The vanilla power system allows one parent (input) per PowerItem. The OR gate works around this by:

1. A custom `PowerItemORGate` class that maintains a second input reference internally
2. Harmony patches on the power system to allow accepting a second wire connection
3. Override power state calculation: output = Input A OR Input B

```
Input A (powered block) ──wire──┐
                                ├──> [OR Gate] ──wire──> Output (consumer)
Input B (powered block) ──wire──┘
```

### Key Classes to Patch/Extend

- `PowerItem` — base class, understand parent/child relationship
- `PowerManager` — manages power grid, adding/removing connections
- `TileEntityPowered` — tile entity for powered blocks
- Wire connection handling — accept second input

**Prerequisite:** Decompile `Assembly-CSharp.dll` with dnSpy to confirm exact method signatures for the target game version (2.5).

### Block Definition

Based on an existing relay/switch model (re-skin). Custom C# class referenced via block properties in XML.

## Disconnect UI

When the player interacts with the OR gate block:

- A UI panel opens showing connection status (Input 1, Input 2, Output)
- A "Disconnect All Inputs" button resets both input wire connections
- After disconnecting, player re-wires using the standard wire tool
- Uses the existing XUi system for the panel

```
┌─────────────────────────┐
│  OR Gate                │
│                         │
│  Input 1: Connected     │
│  Input 2: Connected     │
│  Output:  Powered       │
│                         │
│  [Disconnect All Inputs]│
└─────────────────────────┘
```

## Development Prerequisites

1. Visual Studio with .NET Framework support
2. Reference to game's `Assembly-CSharp.dll`
3. dnSpy for decompiling game internals
4. EAC disabled in game launcher for testing

## Testing

Manual testing by copying the prepared mod folder to the game's `Mods/` directory:

- Place OR gate block in creative mode
- Wire 2 switches to the OR gate, wire a light to the output
- Toggle switch A on → light on
- Toggle switch B on → light stays on
- Toggle both off → light off
- Test disconnect button → wires removed
- Save/load game → connections persist

## Crafting Recipe

Simple recipe: electrical parts + iron (exact materials TBD based on game balance).

## Research Sources

- [OCB Electricity Overhaul](https://github.com/OCB7D2D/OcbElectricityOverhaul) — reference implementation for electricity modding
- [7D2D Harmony Docs](https://7d2dmods.github.io/HarmonyDocs/HarmonyMods1.html) — Harmony patching guide
- [Mod Structure Wiki](https://7daystodie.fandom.com/wiki/Mod_Structure) — mod folder structure
- [ModAPI Wiki](https://7daystodie.fandom.com/wiki/ModAPI) — IModApi interface
- [7days2mod/ModBase](https://github.com/7days2mod/ModBase) — project template
- [SDX Logic Gates](https://github.com/FrYakaTKoP/7dtd-SDX-LogicGates) — existing logic gates mod (older, non-working)
