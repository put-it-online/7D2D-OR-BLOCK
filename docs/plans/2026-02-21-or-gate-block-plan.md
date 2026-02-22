# OR Gate Block Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Create a 7 Days to Die mod that adds an OR gate block accepting 2 electrical inputs, outputting power if either input is active.

**Architecture:** Harmony-patched C# mod with XML block definition. The vanilla power system is tree-based (one parent per PowerItem). Our OR gate stores a second parent reference internally and patches `PowerManager.SetParent()` to allow the second connection. Power state is: output = parent1.IsPowered OR parent2.IsPowered.

**Tech Stack:** C# (.NET 4.8), HarmonyLib, 7D2D ModAPI (IModApi), XPath XML modding

---

## Important Context

**Power system internals** (from decompiled OCB Electricity Overhaul):
- `PowerItem` has `Parent` (single PowerItem), `Children` (List<PowerItem>), `Position` (Vector3i), `IsPowered`, `BlockID`, `TileEntity`, `Depth`
- `PowerManager.SetParent(child, parent)` — if child already has parent, it calls `RemoveParent(child)` first, then sets new parent
- `PowerManager.RemoveParent(node)` — disconnects parent, adds node back to `Circuits` list, calls `HandleDisconnect()`
- `PowerManager.AddPowerNode(node, parent)` — registers node in `Circuits`, `PowerItemDictionary`, optionally parents
- `PowerItem.CreateItem(PowerItemTypes)` — factory method creating the right subclass
- Power update loop: every 0.16s calls `PowerSources[i].Update()` and `PowerTriggers[i].CachedUpdateCall()`
- Serialization: `read(BinaryReader, byte version)` / `write(BinaryWriter)` — writes `PowerItemType` byte then item data
- `PowerItemDictionary` maps `Vector3i` position to `PowerItem`

**Mod structure** (from wiki):
- `ModInfo.xml` at root — required, uses v2 format (Name, DisplayName, Version, etc.)
- `Config/` folder mirrors `Data/Config/` — XML files use XPath to modify vanilla data
- DLL files at root are auto-loaded
- Localization.txt is CSV with header: `Key,File,Type,UsedInMainMenu,NoTranslate,english,...`
- Recipes use `<recipe name="..." count="1"><ingredient name="..." count="N"/></recipe>`

**Critical constraint:** The game must be able to save/load the second parent connection. We must serialize the second parent's `Vector3i` position and restore it on load.

**Critical constraint:** We need to find the correct vanilla block to base our OR gate on. The user will need to check their game's `Data/Config/blocks.xml` for exact block names and properties of electrical relay/switch blocks. We'll write placeholder XML that follows the pattern and document what to verify.

---

### Task 1: Set up project scaffolding

**Files:**
- Create: `ModInfo.xml`
- Create: `ORBlock.csproj`
- Create: `.gitignore`

**Step 1: Create ModInfo.xml**

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<xml>
    <Name value="ORBlock" />
    <DisplayName value="OR Gate Block" />
    <Version value="1.0.0" />
    <Description value="Adds an OR gate electrical block with 2 inputs" />
    <Author value="wimput" />
</xml>
```

**Step 2: Create .gitignore**

```
bin/
obj/
*.dll
*.pdb
*.user
.vs/
```

**Step 3: Create ORBlock.csproj**

This project file references the game's managed DLLs. The user must set the `GAME_PATH` environment variable or edit the path.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <OutputType>Library</OutputType>
    <RootNamespace>ORBlock</RootNamespace>
    <AssemblyName>ORBlock</AssemblyName>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
    <OutputPath>bin/</OutputPath>
    <EnableDefaultCompileItems>true</EnableDefaultCompileItems>
  </PropertyGroup>

  <!--
    Set GAME_MANAGED to your 7D2D managed DLLs folder, e.g.:
    Windows: C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\7DaysToDie_Data\Managed
    Mac: ~/Library/Application Support/Steam/steamapps/common/7 Days To Die/7DaysToDie_Data/Managed
    Or set env var GAME_MANAGED before building.
  -->
  <PropertyGroup>
    <GameManaged Condition="'$(GAME_MANAGED)' == ''">$(HOME)/Library/Application Support/Steam/steamapps/common/7 Days To Die/7DaysToDie_Data/Managed</GameManaged>
    <GameManaged Condition="'$(GAME_MANAGED)' != ''">$(GAME_MANAGED)</GameManaged>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="Assembly-CSharp">
      <HintPath>$(GameManaged)/Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>$(GameManaged)/0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(GameManaged)/UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="LogLibrary">
      <HintPath>$(GameManaged)/LogLibrary.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

**Step 4: Commit**

```bash
git add ModInfo.xml ORBlock.csproj .gitignore
git commit -m "feat: add project scaffolding"
```

---

### Task 2: Create the IModApi entry point

**Files:**
- Create: `Sources/ORBlockMod.cs`

**Step 1: Write the mod entry point**

This class is loaded by the game and initializes Harmony patches.

```csharp
using HarmonyLib;
using System.Reflection;

public class ORBlockMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        var harmony = new Harmony("com.wimput.orblock");
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        Log.Out("[ORBlock] OR Gate Block mod loaded.");
    }
}
```

**Step 2: Verify it compiles**

Run: `dotnet build ORBlock.csproj`

Expected: Build succeeds (or fails if game DLLs not found — that's expected if game isn't installed on this machine). The important thing is the code is syntactically correct.

**Step 3: Commit**

```bash
git add Sources/ORBlockMod.cs
git commit -m "feat: add IModApi entry point with Harmony init"
```

---

### Task 3: Create the PowerItemORGate class

**Files:**
- Create: `Sources/PowerItemORGate.cs`

**Step 1: Write the custom power item class**

This is the core logic. It extends `PowerConsumer` (a vanilla class that receives power from a parent). We add a `SecondParent` reference and override power state to check both parents.

```csharp
using System.IO;
using UnityEngine;

public class PowerItemORGate : PowerConsumer
{
    // The second input connection (vanilla only supports one parent)
    public PowerItem SecondParent { get; set; }

    // Position of second parent, used for save/load restoration
    private Vector3i secondParentPosition = Vector3i.zero;
    private bool hasSecondParent = false;

    /// <summary>
    /// Check if either input is powered. This is the OR gate logic.
    /// </summary>
    public bool IsEitherInputPowered()
    {
        bool parent1Powered = (Parent != null && Parent.IsPowered);
        bool parent2Powered = (SecondParent != null && SecondParent.IsPowered);
        return parent1Powered || parent2Powered;
    }

    /// <summary>
    /// Connect the second parent. Called from our Harmony patch on SetParent.
    /// </summary>
    public void SetSecondParent(PowerItem parent)
    {
        if (SecondParent != null)
        {
            RemoveSecondParent();
        }
        SecondParent = parent;
        hasSecondParent = true;
        secondParentPosition = parent.Position;
        SendHasLocalChangesToRoot();
    }

    /// <summary>
    /// Disconnect the second parent.
    /// </summary>
    public void RemoveSecondParent()
    {
        if (SecondParent == null) return;

        // Remove ourselves from the second parent's children if we were added
        if (SecondParent.Children.Contains(this))
        {
            SecondParent.Children.Remove(this);
        }

        SecondParent = null;
        hasSecondParent = false;
        secondParentPosition = Vector3i.zero;
        SendHasLocalChangesToRoot();
    }

    /// <summary>
    /// Disconnect ALL inputs (both parents). Called from the disconnect UI button.
    /// </summary>
    public void DisconnectAllInputs()
    {
        // Remove second parent first
        RemoveSecondParent();

        // Remove primary parent via the standard power manager
        if (Parent != null)
        {
            PowerManager.Instance.RemoveParent(this);
        }
    }

    /// <summary>
    /// Serialize: write our extra data after the base class data.
    /// </summary>
    public override void write(BinaryWriter _bw)
    {
        base.write(_bw);
        _bw.Write(hasSecondParent);
        if (hasSecondParent)
        {
            _bw.Write(secondParentPosition.x);
            _bw.Write(secondParentPosition.y);
            _bw.Write(secondParentPosition.z);
        }
    }

    /// <summary>
    /// Deserialize: read our extra data after the base class data.
    /// </summary>
    public override void read(BinaryReader _br, byte _version)
    {
        base.read(_br, _version);
        hasSecondParent = _br.ReadBoolean();
        if (hasSecondParent)
        {
            int x = _br.ReadInt32();
            int y = _br.ReadInt32();
            int z = _br.ReadInt32();
            secondParentPosition = new Vector3i(x, y, z);
        }
    }

    /// <summary>
    /// Called after all power items are loaded to restore the second parent reference.
    /// Must be called from a Harmony postfix on the load process.
    /// </summary>
    public void RestoreSecondParent()
    {
        if (!hasSecondParent) return;
        PowerItem item = PowerManager.Instance.GetPowerItemByWorldPos(secondParentPosition);
        if (item != null)
        {
            SecondParent = item;
            // Add ourselves as a child of the second parent for wire drawing
            if (!item.Children.Contains(this))
            {
                item.Children.Add(this);
            }
        }
        else
        {
            Log.Warning("[ORBlock] Could not restore second parent at " + secondParentPosition);
            hasSecondParent = false;
            secondParentPosition = Vector3i.zero;
        }
    }
}
```

**Step 2: Commit**

```bash
git add Sources/PowerItemORGate.cs
git commit -m "feat: add PowerItemORGate with dual-parent logic"
```

---

### Task 4: Create Harmony patches for the power system

**Files:**
- Create: `Sources/Harmony/PowerManagerPatches.cs`

**Step 1: Write the Harmony patches**

These patches modify the power manager to:
1. Allow a second parent connection on our OR gate block
2. Override power state checking so our block uses OR logic
3. Restore second parent references after save/load

```csharp
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Patches PowerManager.SetParent to allow a second parent on OR gate blocks.
///
/// Vanilla behavior: SetParent always removes existing parent before setting new one.
/// Our behavior: If the child is a PowerItemORGate and already has a Parent,
/// store the new parent as SecondParent instead.
/// </summary>
[HarmonyPatch(typeof(PowerManager))]
[HarmonyPatch("SetParent")]
[HarmonyPatch(new[] { typeof(PowerItem), typeof(PowerItem) })]
public class PowerManager_SetParent_Patch
{
    static bool Prefix(PowerManager __instance, PowerItem child, PowerItem parent)
    {
        // Only intercept for our OR gate type
        if (child is PowerItemORGate orGate)
        {
            // If the OR gate has no parent yet, let vanilla handle it
            if (orGate.Parent == null)
                return true; // continue to original method

            // If trying to set same parent, skip
            if (orGate.Parent == parent)
                return false;

            // If already has a parent but no second parent, store as second
            if (orGate.SecondParent == null && parent != null)
            {
                orGate.SetSecondParent(parent);
                // Add ourselves as child of the second parent for wire rendering
                if (!parent.Children.Contains(orGate))
                {
                    parent.Children.Add(orGate);
                }
                return false; // skip original method
            }

            // If already has both parents, replace second parent
            if (orGate.SecondParent != null && parent != null)
            {
                orGate.RemoveSecondParent();
                orGate.SetSecondParent(parent);
                if (!parent.Children.Contains(orGate))
                {
                    parent.Children.Add(orGate);
                }
                return false;
            }
        }

        return true; // let vanilla handle non-OR-gate blocks
    }
}

/// <summary>
/// Patches PowerManager.RemoveParent to also handle our second parent.
/// When removing parent from an OR gate, if it's the second parent being
/// disconnected (by wire tool), handle that correctly.
/// </summary>
[HarmonyPatch(typeof(PowerManager))]
[HarmonyPatch("RemoveParent")]
[HarmonyPatch(new[] { typeof(PowerItem) })]
public class PowerManager_RemoveParent_Patch
{
    static void Postfix(PowerItem node)
    {
        // If a second parent's child was removed and that child is an OR gate,
        // we might need cleanup. But this is handled by DisconnectAllInputs.
        // This postfix is a safety net.
    }
}

/// <summary>
/// Patches PowerConsumer.HandlePowerReceived (or equivalent) to implement OR logic.
/// The exact method depends on what the game calls during power distribution.
///
/// Note: The exact patch target may need adjustment after decompiling the game.
/// Common targets: PowerConsumer.HandlePowerReceived, PowerItem.IsPowered getter,
/// or the power distribution loop in PowerSource.
///
/// This patch makes the OR gate pass power through to children if either input is active.
/// </summary>
[HarmonyPatch(typeof(PowerConsumer))]
[HarmonyPatch("get_IsPowered")]
public class PowerConsumer_IsPowered_Patch
{
    static bool Prefix(PowerConsumer __instance, ref bool __result)
    {
        if (__instance is PowerItemORGate orGate)
        {
            __result = orGate.IsEitherInputPowered();
            return false; // skip original
        }
        return true; // let vanilla handle
    }
}

/// <summary>
/// Patch PowerManager.LoadPowerManager to restore second parent connections
/// after all power items have been loaded from the save file.
/// </summary>
[HarmonyPatch(typeof(PowerManager))]
[HarmonyPatch("LoadPowerManager")]
public class PowerManager_LoadPowerManager_Patch
{
    static void Postfix(PowerManager __instance)
    {
        // After all nodes are loaded, restore second parent references
        foreach (var kvp in __instance.PowerItemDictionary)
        {
            if (kvp.Value is PowerItemORGate orGate)
            {
                orGate.RestoreSecondParent();
            }
        }
        Log.Out("[ORBlock] Restored OR gate connections.");
    }
}

/// <summary>
/// Patch PowerItem.CreateItem to handle our custom power item type.
/// This factory method creates PowerItem subclasses based on PowerItemTypes enum.
/// Since we can't add to the enum, we use a convention: if the block at the
/// position has our custom property, we substitute our class.
///
/// Alternative approach: We register our block type and patch the factory.
/// The exact approach depends on how the game version handles this.
///
/// NOTE: This patch may need adjustment. If PowerItem.CreateItem uses an enum
/// that we can't extend, we may need to patch the TileEntity creation instead,
/// or patch Read() to swap the instance after creation.
/// </summary>
public class PowerItemFactory
{
    // This is called from our TileEntity to create the right PowerItem type
    public static PowerItemORGate CreateORGatePowerItem()
    {
        return new PowerItemORGate();
    }
}
```

**Step 2: Commit**

```bash
git add Sources/Harmony/PowerManagerPatches.cs
git commit -m "feat: add Harmony patches for dual-parent power connections"
```

---

### Task 5: Create TileEntity patch for block-to-PowerItem mapping

**Files:**
- Create: `Sources/Harmony/TileEntityPatches.cs`

**Step 1: Write the TileEntity patches**

When the game places a block, it creates a TileEntity and associates a PowerItem.
We need to ensure our block creates a `PowerItemORGate` instead of a regular `PowerConsumer`.

```csharp
using HarmonyLib;

/// <summary>
/// Patch the TileEntity creation to use our PowerItemORGate for our block type.
///
/// When a powered block is placed, TileEntityPowered.InitializePowerData() creates
/// the PowerItem. We patch this to create our custom type for our block.
///
/// NOTE: The exact method name may differ between game versions. Common candidates:
/// - TileEntityPowered.InitializePowerData()
/// - TileEntityPoweredBlock.CreatePowerItem()
/// - TileEntityPowered.SetValuesFromBlock()
///
/// After decompiling, verify the correct method and adjust this patch.
/// The key is: wherever the game does `new PowerConsumer()` for our block,
/// we replace it with `new PowerItemORGate()`.
/// </summary>
[HarmonyPatch(typeof(TileEntityPowered))]
[HarmonyPatch("InitializePowerData")]
public class TileEntityPowered_InitializePowerData_Patch
{
    static void Postfix(TileEntityPowered __instance)
    {
        // Check if this tile entity belongs to our OR gate block
        BlockValue blockValue = __instance.blockValue;
        Block block = blockValue.Block;

        if (block.Properties.Values.ContainsKey("IsORGate") &&
            block.Properties.Values["IsORGate"] == "true")
        {
            // Replace the PowerItem with our custom OR gate version
            // We need to swap out the PowerConsumer that was created
            if (__instance.PowerItem != null && !(__instance.PowerItem is PowerItemORGate))
            {
                PowerItemORGate orGate = new PowerItemORGate();
                // Copy over essential properties from the existing power item
                orGate.Position = __instance.PowerItem.Position;
                orGate.BlockID = __instance.PowerItem.BlockID;

                // Remove old power item from power manager
                PowerManager.Instance.RemovePowerNode(__instance.PowerItem);

                // Set our new power item
                __instance.PowerItem = orGate;
                orGate.TileEntity = __instance;

                // Register with power manager
                PowerManager.Instance.AddPowerNode(orGate);

                Log.Out("[ORBlock] Created PowerItemORGate at " + orGate.Position);
            }
        }
    }
}

/// <summary>
/// Patch to handle the activation/interaction text for the OR gate block.
/// Shows "OR Gate: Input 1 [status] | Input 2 [status]" when looking at the block.
/// </summary>
[HarmonyPatch(typeof(Block))]
[HarmonyPatch("GetActivationText")]
public class Block_GetActivationText_Patch
{
    static void Postfix(Block __instance, ref string __result,
        WorldBase _world, BlockValue _blockValue, int _clrIdx,
        Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        if (__instance.Properties.Values.ContainsKey("IsORGate") &&
            __instance.Properties.Values["IsORGate"] == "true")
        {
            PowerItem powerItem = PowerManager.Instance.GetPowerItemByWorldPos(_blockPos);
            if (powerItem is PowerItemORGate orGate)
            {
                string input1 = orGate.Parent != null ? "Connected" : "Empty";
                string input2 = orGate.SecondParent != null ? "Connected" : "Empty";
                string output = orGate.IsEitherInputPowered() ? "ON" : "OFF";
                __result = string.Format("OR Gate [Input 1: {0}] [Input 2: {1}] [Output: {2}]",
                    input1, input2, output);
            }
        }
    }
}
```

**Step 2: Commit**

```bash
git add Sources/Harmony/TileEntityPatches.cs
git commit -m "feat: add TileEntity patches for OR gate block creation"
```

---

### Task 6: Create disconnect command handler

**Files:**
- Create: `Sources/Harmony/DisconnectPatches.cs`

**Step 1: Write the disconnect interaction handler**

Instead of a full XUi panel (which requires complex UI XML), we use the block's activation (interact/E key) to cycle through options. Pressing E on the OR gate while holding the wire tool will disconnect all inputs.

This is simpler and more reliable than a custom UI panel. The activation text (from Task 5) already shows connection status.

```csharp
using HarmonyLib;
using UnityEngine;

/// <summary>
/// Patch block activation to handle the disconnect action.
/// When the player interacts with the OR gate while crouching (sneak + activate),
/// it disconnects all inputs. Normal interaction does the default behavior (wire tool).
/// </summary>
[HarmonyPatch(typeof(Block))]
[HarmonyPatch("OnBlockActivated")]
[HarmonyPatch(new[] { typeof(string), typeof(WorldBase), typeof(int),
    typeof(Vector3i), typeof(BlockValue), typeof(EntityPlayerLocal) })]
public class Block_OnBlockActivated_Patch
{
    static bool Prefix(string _commandName, WorldBase _world, int _cIdx,
        Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player,
        ref bool __result)
    {
        Block block = _blockValue.Block;
        if (!block.Properties.Values.ContainsKey("IsORGate") ||
            block.Properties.Values["IsORGate"] != "true")
            return true; // not our block

        // Check if player is crouching (sneak) — this triggers disconnect
        if (_player.MovementInput.Down && _commandName == "activate")
        {
            PowerItem powerItem = PowerManager.Instance.GetPowerItemByWorldPos(_blockPos);
            if (powerItem is PowerItemORGate orGate)
            {
                orGate.DisconnectAllInputs();
                GameManager.ShowTooltip(_player, "OR Gate: All inputs disconnected");
                __result = true;
                return false; // skip original
            }
        }

        return true; // let vanilla handle
    }
}
```

**Step 2: Commit**

```bash
git add Sources/Harmony/DisconnectPatches.cs
git commit -m "feat: add crouch+activate disconnect for OR gate"
```

---

### Task 7: Create XML configuration files

**Files:**
- Create: `Config/blocks.xml`
- Create: `Config/Localization.txt`
- Create: `Config/recipes.xml`

**Step 1: Create blocks.xml**

This uses XPath to append a new block based on an existing electrical relay.

**IMPORTANT:** The exact `extends` block name must match what exists in the game's `Data/Config/blocks.xml`. Check your game installation for the correct relay/switch block name. Common names in recent versions: `electrictimerrelay`, `electricswitch`, `electricrelay`. The block below uses `electricswitch` as the base — verify this exists in your game version and adjust if needed.

```xml
<configs>
    <!-- Add the OR Gate block. Extends an existing electrical block for model/behavior base. -->
    <!-- NOTE: Verify that "electricswitch" exists in your game's blocks.xml. -->
    <!-- If not, check for similar names like "switchElectric" or "electricRelay" -->
    <append xpath="/blocks">
        <block name="ORGateBlock">
            <property name="Extends" value="electricswitch" />
            <property name="CreativeMode" value="Player" />
            <property name="CustomIcon" value="electricswitch" />
            <property name="DescriptionKey" value="ORGateBlockDesc" />
            <!-- Custom property that our C# code checks -->
            <property name="IsORGate" value="true" />
            <!-- Power consumption: 0W (it's a logic gate, not a consumer) -->
            <property name="RequiredPower" value="0" />
        </block>
    </append>
</configs>
```

**Step 2: Create Localization.txt**

```csv
Key,File,Type,UsedInMainMenu,NoTranslate,english
ORGateBlock,blocks,Block,false,false,OR Gate
ORGateBlockDesc,blocks,Block,false,false,"Electrical OR gate. Accepts 2 inputs, outputs power if either input is active. Crouch+Activate to disconnect all inputs."
```

**Step 3: Create recipes.xml**

```xml
<configs>
    <append xpath="/recipes">
        <recipe name="ORGateBlock" count="1" craft_area="workbench" craft_time="10">
            <ingredient name="resourceElectricParts" count="3" />
            <ingredient name="resourceForgedIron" count="2" />
        </recipe>
    </append>
</configs>
```

**Step 4: Commit**

```bash
git add Config/blocks.xml Config/Localization.txt Config/recipes.xml
git commit -m "feat: add XML block definition, localization, and recipe"
```

---

### Task 8: Create build and deploy script

**Files:**
- Create: `build.sh`

**Step 1: Write the build script**

This script compiles the mod and copies the output to a deploy folder ready to drop into the game.

```bash
#!/bin/bash
set -e

echo "Building ORBlock mod..."
dotnet build ORBlock.csproj -c Release

# Create deployment folder
DEPLOY_DIR="deploy/ORBlock"
rm -rf "$DEPLOY_DIR"
mkdir -p "$DEPLOY_DIR"

# Copy mod files
cp ModInfo.xml "$DEPLOY_DIR/"
cp -r Config "$DEPLOY_DIR/"
cp bin/ORBlock.dll "$DEPLOY_DIR/"

echo ""
echo "Build complete! Deploy folder: $DEPLOY_DIR"
echo "Copy the ORBlock folder to your game's Mods/ directory:"
echo "  cp -r $DEPLOY_DIR /path/to/7DaysToDie/Mods/"
```

**Step 2: Make executable and commit**

```bash
chmod +x build.sh
git add build.sh
git commit -m "feat: add build and deploy script"
```

---

### Task 9: Verify build compiles (if game DLLs available)

**Step 1: Attempt to build**

Run: `./build.sh`

If the game is installed and DLLs are accessible, this should produce `deploy/ORBlock/` with:
- `ModInfo.xml`
- `Config/blocks.xml`
- `Config/Localization.txt`
- `Config/recipes.xml`
- `ORBlock.dll`

If the game is not installed on this machine, the build will fail at the DLL reference step. That's expected — the user will build on their gaming machine.

**Step 2: Commit any fixes**

Fix any compilation errors and commit.

---

### Task 10: Document testing procedure and known limitations

**Files:**
- Create: `README.md`

**Step 1: Write README with install instructions and testing steps**

```markdown
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
```

**Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add README with install, usage, and troubleshooting"
```

---

## Post-Implementation Notes

**Things that MUST be verified with the actual game:**

1. **Block name to extend:** Check `Data/Config/blocks.xml` in your game for the correct electrical relay/switch block name
2. **PowerItem class hierarchy:** The patches target `PowerConsumer` and `PowerManager` — decompile with dnSpy to verify method names haven't changed in v2.5
3. **IsPowered getter:** The patch targets `get_IsPowered` on `PowerConsumer`. If this is a field instead of a property, or if the name changed, adjust the patch
4. **TileEntityPowered.InitializePowerData:** This method name is assumed. Decompile to verify
5. **Block.OnBlockActivated signature:** The parameter types in the patch must match exactly. Decompile to verify
6. **PowerItemDictionary accessibility:** The code assumes this is public (OCB mod made it public). If it's private/protected in vanilla, add a Harmony patch to access it
7. **Recipe ingredient names:** Verify `resourceElectricParts` and `resourceForgedIron` are correct item names in your game version
