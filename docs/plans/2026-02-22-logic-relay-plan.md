# Logic Relay Block Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Rename OR Gate to Logic Relay, fix the broken interaction system, and add selectable OR/AND gate mode via an activation command menu.

**Architecture:** Hybrid approach — custom `BlockLogicRelay` class for interaction/UI (follows vanilla patterns), Harmony patches kept only for power system internals. The `Class="LogicRelay"` XML property maps to `BlockLogicRelay` in C#.

**Tech Stack:** C# (.NET 4.8), Harmony 2.x, 7 Days to Die modding API (Assembly-CSharp.dll)

**Note:** No unit test framework available for 7D2D mods. Verification is build compilation + in-game testing.

---

### Task 1: Update XML Configs — Rename Block

**Files:**
- Modify: `Config/blocks.xml`
- Modify: `Config/Localization.txt`
- Modify: `Config/recipes.xml`
- Modify: `ModInfo.xml`

**Step 1: Update blocks.xml**

Replace the entire contents of `Config/blocks.xml` with:

```xml
<configs>
    <append xpath="/blocks">
        <block name="LogicRelayBlock">
            <property name="Extends" value="electricwirerelay" />
            <property name="Class" value="LogicRelay" />
            <property name="CreativeMode" value="Player" />
            <property name="CustomIcon" value="electricwirerelay" />
            <property name="DescriptionKey" value="LogicRelayBlockDesc" />
            <property name="IsLogicRelay" value="true" />
            <property name="RequiredPower" value="0" />
        </block>
    </append>
</configs>
```

Changes from original:
- Block name: `ORGateBlock` → `LogicRelayBlock`
- Added: `<property name="Class" value="LogicRelay" />` (maps to `BlockLogicRelay` C# class)
- Property: `IsORGate` → `IsLogicRelay`
- Description key: `ORGateBlockDesc` → `LogicRelayBlockDesc`

**Step 2: Update Localization.txt**

Replace the entire contents of `Config/Localization.txt` with:

```
Key,File,Type,UsedInMainMenu,NoTranslate,english
LogicRelayBlock,blocks,Block,false,false,Logic Relay
LogicRelayBlockDesc,blocks,Block,false,false,"Logic relay with 2 inputs. Supports OR mode (either input) and AND mode (both inputs required). Use the activation menu to toggle mode or disconnect."
```

**Step 3: Update recipes.xml**

Replace the entire contents of `Config/recipes.xml` with:

```xml
<configs>
    <append xpath="/recipes">
        <recipe name="LogicRelayBlock" count="1" craft_area="workbench" craft_time="10">
            <ingredient name="resourceElectricParts" count="3" />
            <ingredient name="resourceForgedIron" count="2" />
        </recipe>
    </append>
</configs>
```

**Step 4: Update ModInfo.xml**

Replace the entire contents of `ModInfo.xml` with:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<xml>
    <Name value="ORBlock" />
    <DisplayName value="Logic Relay Block" />
    <Version value="2.0.0" />
    <Description value="Adds a Logic Relay electrical block with 2 inputs and selectable OR/AND mode" />
    <Author value="wimput" />
</xml>
```

**Step 5: Commit**

```bash
git add Config/blocks.xml Config/Localization.txt Config/recipes.xml ModInfo.xml
git commit -m "rename: OR Gate → Logic Relay in XML configs"
```

---

### Task 2: Add GateMode to PowerItemORGate

**Files:**
- Modify: `Sources/PowerItemORGate.cs`

**Step 1: Add GateMode enum and update the class**

Replace the entire contents of `Sources/PowerItemORGate.cs` with:

```csharp
using System.IO;
using UnityEngine;

public enum GateMode : byte
{
    OR = 0,
    AND = 1
}

public class PowerItemORGate : PowerConsumer
{
    // The second input connection (vanilla only supports one parent)
    public PowerItem SecondParent { get; set; }

    // Position of second parent, used for save/load restoration
    private Vector3i secondParentPosition = Vector3i.zero;
    private bool hasSecondParent = false;

    // Gate mode: OR (default) or AND
    public GateMode Mode { get; set; } = GateMode.OR;

    /// <summary>
    /// Check if the gate output should be powered, based on current mode.
    /// </summary>
    public bool IsOutputPowered()
    {
        bool parent1Powered = (Parent != null && Parent.IsPowered);
        bool parent2Powered = (SecondParent != null && SecondParent.IsPowered);

        if (Mode == GateMode.AND)
        {
            // AND: both inputs must be connected AND powered
            return (Parent != null && Parent.IsPowered)
                && (SecondParent != null && SecondParent.IsPowered);
        }

        // OR: either input powered
        return parent1Powered || parent2Powered;
    }

    /// <summary>
    /// Toggle between OR and AND mode.
    /// </summary>
    public void ToggleMode()
    {
        Mode = (Mode == GateMode.OR) ? GateMode.AND : GateMode.OR;
        SendHasLocalChangesToRoot();
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
    /// Disconnect ALL inputs (both parents). Called from the block activation menu.
    /// </summary>
    public void DisconnectAllInputs()
    {
        RemoveSecondParent();

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
        _bw.Write((byte)Mode);
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
        // Read gate mode (defaults to OR if not present for backwards compat)
        if (_br.BaseStream.Position < _br.BaseStream.Length)
        {
            Mode = (GateMode)_br.ReadByte();
        }
        else
        {
            Mode = GateMode.OR;
        }
    }

    /// <summary>
    /// Called after all power items are loaded to restore the second parent reference.
    /// </summary>
    public void RestoreSecondParent()
    {
        if (!hasSecondParent) return;
        PowerItem item = PowerManager.Instance.GetPowerItemByWorldPos(secondParentPosition);
        if (item != null)
        {
            SecondParent = item;
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

Key changes from original:
- Added `GateMode` enum (OR=0, AND=1)
- Added `Mode` property (default OR)
- Renamed `IsEitherInputPowered()` → `IsOutputPowered()` (handles both OR and AND)
- Added `ToggleMode()` method
- Serialize/deserialize `Mode` as 1 byte at end of stream
- Backwards-compatible read: if no mode byte exists, defaults to OR

**Step 2: Commit**

```bash
git add Sources/PowerItemORGate.cs
git commit -m "feat: add OR/AND gate mode to PowerItemORGate"
```

---

### Task 3: Create BlockLogicRelay Class

**Files:**
- Create: `Sources/BlockLogicRelay.cs`

**Step 1: Create the custom block class**

Create `Sources/BlockLogicRelay.cs` with:

```csharp
using UnityEngine;

/// <summary>
/// Custom Block class for the Logic Relay.
/// Provides activation commands (Toggle Mode, Disconnect) and activation text.
/// The game maps Class="LogicRelay" in blocks.xml to this class (prefixes "Block").
/// </summary>
public class BlockLogicRelay : BlockPowered
{
    private readonly BlockActivationCommand[] cmds = new BlockActivationCommand[]
    {
        new BlockActivationCommand("toggle_mode", "electric_switch", false, false),
        new BlockActivationCommand("disconnect", "electric_disconnect", false, false)
    };

    public override BlockActivationCommand[] GetBlockActivationCommands(
        WorldBase _world,
        BlockValue _blockValue,
        int _clrIdx,
        Vector3i _blockPos,
        EntityAlive _entityFocusing)
    {
        bool isUsable = _world.IsMyLandProtectedBlock(_blockPos,
            _world.GetGameManager().GetPersistentLocalPlayer());

        cmds[0].enabled = isUsable; // toggle_mode
        cmds[1].enabled = isUsable; // disconnect

        return cmds;
    }

    public override string GetActivationText(
        WorldBase _world,
        BlockValue _blockValue,
        int _clrIdx,
        Vector3i _blockPos,
        EntityAlive _entityFocusing)
    {
        PowerItem powerItem = PowerManager.Instance.GetPowerItemByWorldPos(_blockPos);
        if (powerItem is PowerItemORGate orGate)
        {
            string mode = orGate.Mode == GateMode.OR ? "OR" : "AND";
            string input1 = orGate.Parent != null ? "Connected" : "Empty";
            string input2 = orGate.SecondParent != null ? "Connected" : "Empty";
            string output = orGate.IsOutputPowered() ? "ON" : "OFF";
            return string.Format("Logic Relay [Mode: {0}] [Input 1: {1}] [Input 2: {2}] [Output: {3}]",
                mode, input1, input2, output);
        }
        return "Logic Relay";
    }

    public override bool OnBlockActivated(
        WorldBase _world,
        int _cIdx,
        Vector3i _blockPos,
        BlockValue _blockValue,
        EntityPlayerLocal _player)
    {
        // Default activation: toggle mode
        return OnBlockActivated("toggle_mode", _world, _cIdx, _blockPos, _blockValue, _player);
    }

    public override bool OnBlockActivated(
        string _commandName,
        WorldBase _world,
        int _cIdx,
        Vector3i _blockPos,
        BlockValue _blockValue,
        EntityPlayerLocal _player)
    {
        PowerItem powerItem = PowerManager.Instance.GetPowerItemByWorldPos(_blockPos);
        if (!(powerItem is PowerItemORGate orGate))
            return false;

        switch (_commandName)
        {
            case "toggle_mode":
                orGate.ToggleMode();
                string modeName = orGate.Mode == GateMode.OR ? "OR" : "AND";
                GameManager.ShowTooltip(_player, "Logic Relay: Mode set to " + modeName);
                return true;

            case "disconnect":
                orGate.DisconnectAllInputs();
                GameManager.ShowTooltip(_player, "Logic Relay: All inputs disconnected");
                return true;

            default:
                return false;
        }
    }
}
```

**Step 2: Commit**

```bash
git add Sources/BlockLogicRelay.cs
git commit -m "feat: add BlockLogicRelay class with activation commands"
```

---

### Task 4: Update Harmony Patches

**Files:**
- Delete: `Sources/Harmony/DisconnectPatches.cs`
- Modify: `Sources/Harmony/TileEntityPatches.cs`
- Modify: `Sources/Harmony/PowerManagerPatches.cs`

**Step 1: Delete DisconnectPatches.cs**

Delete the file `Sources/Harmony/DisconnectPatches.cs`. The disconnect logic now lives in `BlockLogicRelay.OnBlockActivated()`.

**Step 2: Update TileEntityPatches.cs**

Replace the entire contents of `Sources/Harmony/TileEntityPatches.cs` with:

```csharp
using HarmonyLib;

/// <summary>
/// Patch TileEntityPowered.InitializePowerData() to create PowerItemORGate
/// for our Logic Relay block instead of the default PowerConsumer.
/// </summary>
[HarmonyPatch(typeof(TileEntityPowered))]
[HarmonyPatch("InitializePowerData")]
public class TileEntityPowered_InitializePowerData_Patch
{
    static void Postfix(TileEntityPowered __instance)
    {
        BlockValue blockValue = __instance.blockValue;
        Block block = blockValue.Block;

        if (block.Properties.Values.ContainsKey("IsLogicRelay") &&
            block.Properties.Values["IsLogicRelay"] == "true")
        {
            if (__instance.PowerItem != null && !(__instance.PowerItem is PowerItemORGate))
            {
                PowerItemORGate orGate = new PowerItemORGate();
                orGate.Position = __instance.PowerItem.Position;
                orGate.BlockID = __instance.PowerItem.BlockID;

                PowerManager.Instance.RemovePowerNode(__instance.PowerItem);

                __instance.PowerItem = orGate;
                orGate.TileEntity = __instance;

                PowerManager.Instance.AddPowerNode(orGate);

                Log.Out("[ORBlock] Created PowerItemORGate at " + orGate.Position);
            }
        }
    }
}
```

Changes: Removed `Block_GetActivationText_Patch` (now in `BlockLogicRelay`), updated property check from `IsORGate` to `IsLogicRelay`.

**Step 3: Update PowerManagerPatches.cs**

Replace the entire contents of `Sources/Harmony/PowerManagerPatches.cs` with:

```csharp
using HarmonyLib;
using UnityEngine;

/// <summary>
/// Patches PowerManager.SetParent to allow a second parent on Logic Relay blocks.
/// </summary>
[HarmonyPatch(typeof(PowerManager))]
[HarmonyPatch("SetParent")]
[HarmonyPatch(new[] { typeof(PowerItem), typeof(PowerItem) })]
public class PowerManager_SetParent_Patch
{
    static bool Prefix(PowerManager __instance, PowerItem child, PowerItem parent)
    {
        if (child is PowerItemORGate orGate)
        {
            if (orGate.Parent == null)
                return true;

            if (orGate.Parent == parent)
                return false;

            if (orGate.SecondParent == null && parent != null)
            {
                orGate.SetSecondParent(parent);
                if (!parent.Children.Contains(orGate))
                {
                    parent.Children.Add(orGate);
                }
                return false;
            }

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

        return true;
    }
}

/// <summary>
/// Safety net for RemoveParent on Logic Relay blocks.
/// </summary>
[HarmonyPatch(typeof(PowerManager))]
[HarmonyPatch("RemoveParent")]
[HarmonyPatch(new[] { typeof(PowerItem) })]
public class PowerManager_RemoveParent_Patch
{
    static void Postfix(PowerItem node)
    {
        // Safety net — main cleanup handled by DisconnectAllInputs
    }
}

/// <summary>
/// Patches PowerItem.IsPowered getter to implement OR/AND logic.
/// </summary>
[HarmonyPatch(typeof(PowerItem))]
[HarmonyPatch("get_IsPowered")]
public class PowerItem_IsPowered_Patch
{
    static bool Prefix(PowerItem __instance, ref bool __result)
    {
        if (__instance is PowerItemORGate orGate)
        {
            __result = orGate.IsOutputPowered();
            return false;
        }
        return true;
    }
}

/// <summary>
/// Patch PowerManager.LoadPowerManager to restore second parent connections after load.
/// </summary>
[HarmonyPatch(typeof(PowerManager))]
[HarmonyPatch("LoadPowerManager")]
public class PowerManager_LoadPowerManager_Patch
{
    static void Postfix(PowerManager __instance)
    {
        foreach (var kvp in __instance.PowerItemDictionary)
        {
            if (kvp.Value is PowerItemORGate orGate)
            {
                orGate.RestoreSecondParent();
            }
        }
        Log.Out("[ORBlock] Restored Logic Relay connections.");
    }
}

/// <summary>
/// Factory helper for creating PowerItemORGate instances.
/// </summary>
public class PowerItemFactory
{
    public static PowerItemORGate CreateORGatePowerItem()
    {
        return new PowerItemORGate();
    }
}
```

Changes: Updated `IsEitherInputPowered()` → `IsOutputPowered()`, updated log message.

**Step 4: Update ORBlockMod.cs log message**

In `Sources/ORBlockMod.cs`, change the log message from:

```csharp
Log.Out("[ORBlock] OR Gate Block mod loaded.");
```

to:

```csharp
Log.Out("[ORBlock] Logic Relay Block mod loaded.");
```

**Step 5: Commit**

```bash
git rm Sources/Harmony/DisconnectPatches.cs
git add Sources/Harmony/TileEntityPatches.cs Sources/Harmony/PowerManagerPatches.cs Sources/ORBlockMod.cs
git commit -m "refactor: move interaction to BlockLogicRelay, update patches for IsLogicRelay"
```

---

### Task 5: Build and Verify

**Files:**
- Uses: `build.sh` and `ORBlock.csproj`

**Step 1: Build the mod**

```bash
cd "C:/Program Files (x86)/Steam/steamapps/common/7 Days To Die/Mods/7D2D-OR-BLOCK"
bash build.sh
```

Expected: Build succeeds, `ORBlock.dll` is produced in project root.

**Step 2: Verify the DLL was updated**

```bash
ls -la ORBlock.dll
```

Expected: File timestamp matches current time.

**Step 3: If build fails, fix compilation errors**

Common issues to check:
- Missing `using` directives
- Method signature mismatches with game API
- The `BlockActivationCommand` constructor may have different parameter names — adjust if compiler complains

**Step 4: Commit build artifact**

```bash
git add ORBlock.dll
git commit -m "build: compile Logic Relay v2.0.0"
```

---

### Task 6: In-Game Testing Checklist

Launch 7 Days to Die with EAC disabled. Test each item:

**Basic Block:**
- [ ] Block appears in creative menu as "Logic Relay"
- [ ] Block can be crafted at workbench with correct recipe
- [ ] Block places and shows the relay model

**Interaction System (the main bug fix):**
- [ ] Looking at block shows activation text: `Logic Relay [Mode: OR] [Input 1: Empty] [Input 2: Empty] [Output: OFF]`
- [ ] Pressing E shows activation command menu with "toggle_mode" and "disconnect" options

**Toggle Mode:**
- [ ] Selecting "toggle_mode" changes mode to AND, tooltip shows "Logic Relay: Mode set to AND"
- [ ] Selecting "toggle_mode" again changes back to OR
- [ ] Activation text updates to reflect current mode

**OR Mode Logic:**
- [ ] Connect 1 powered input → output ON
- [ ] Connect 2 powered inputs → output ON
- [ ] Disconnect both → output OFF

**AND Mode Logic:**
- [ ] Connect 1 powered input only → output OFF (AND requires both)
- [ ] Connect 2 powered inputs → output ON
- [ ] Disconnect one → output OFF

**Disconnect:**
- [ ] Selecting "disconnect" removes all wires, tooltip shows "Logic Relay: All inputs disconnected"
- [ ] Activation text shows both inputs as "Empty" after disconnect

**Save/Load:**
- [ ] Place block, set to AND mode, connect 2 inputs, save and exit
- [ ] Reload — mode is still AND, both inputs still connected, output correct

---

### Summary of All File Changes

| File | Action | Task |
|------|--------|------|
| `Config/blocks.xml` | Modify | 1 |
| `Config/Localization.txt` | Modify | 1 |
| `Config/recipes.xml` | Modify | 1 |
| `ModInfo.xml` | Modify | 1 |
| `Sources/PowerItemORGate.cs` | Modify | 2 |
| `Sources/BlockLogicRelay.cs` | Create | 3 |
| `Sources/Harmony/DisconnectPatches.cs` | Delete | 4 |
| `Sources/Harmony/TileEntityPatches.cs` | Modify | 4 |
| `Sources/Harmony/PowerManagerPatches.cs` | Modify | 4 |
| `Sources/ORBlockMod.cs` | Modify | 4 |
| `ORBlock.dll` | Rebuild | 5 |
