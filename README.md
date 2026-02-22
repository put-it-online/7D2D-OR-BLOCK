# OR Gate Block - 7 Days to Die Mod

Adds an OR gate electrical block that accepts 2 inputs and outputs power
if either input is active.

## Requirements

- 7 Days to Die v2.5 (latest experimental)
- EAC (Easy Anti-Cheat) must be disabled
- Single-player only

## Installation

1. Build the mod: `./build.sh`
2. Copy `deploy/ORBlock/` to your game's `Mods/` directory
3. Launch the game with EAC disabled

## Usage

1. Craft the OR Gate at a workbench (3 electrical parts + 2 forged iron)
2. Place the OR gate block
3. Use the wire tool to connect Input 1 (any powered block → OR gate)
4. Use the wire tool to connect Input 2 (another powered block → OR gate)
5. Wire the OR gate's output to a consumer (light, door, etc.)

The consumer activates if either Input 1 OR Input 2 is powered.

## Disconnect Inputs

**Crouch + Activate** (sneak + E) on the OR gate to disconnect all inputs.

## Looking At Block

When looking at the OR gate, you'll see status text:
`OR Gate [Input 1: Connected] [Input 2: Empty] [Output: ON]`

## Known Limitations

- The wire tool shows 2 input wires but the game normally only expects 1
- Save/load should preserve connections but test thoroughly
- Some Harmony patch targets may need adjustment for game version 2.5
  (the current code is based on decompiled code from earlier versions)
- Requires `Assembly-CSharp.dll` to compile; not included in this repo

## Building from Source

1. Set `GAME_MANAGED` env var to your game's managed DLLs path
2. Run `dotnet build ORBlock.csproj`
3. Or use `./build.sh` to build and create deploy folder

## Troubleshooting

If the mod doesn't load:
- Check the game's log file for `[ORBlock]` messages
- Verify EAC is disabled
- Verify `Assembly-CSharp.dll` version matches your game version

If the block extends fail (error about "electricswitch"):
- Open your game's `Data/Config/blocks.xml`
- Search for electrical/switch/relay blocks
- Update `Config/blocks.xml` to extend the correct block name
