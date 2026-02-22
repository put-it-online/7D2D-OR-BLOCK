# OR Gate Block — Build & Verification Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Context:** The mod code is complete but has never been compiled or tested. This plan is to be executed on the machine that has 7 Days to Die installed and the dotnet SDK available.

**Goal:** Build the mod, fix any compilation errors, verify XML config names match the game, and produce a deployable mod folder.

---

### Task 1: Verify prerequisites

**Step 1: Check dotnet SDK is available**

```bash
dotnet --version
```

Expected: 6.0+ (any version that supports `net48` target). If missing, install the .NET SDK.

**Step 2: Locate the game's managed DLLs folder**

Find the folder containing `Assembly-CSharp.dll`, `0Harmony.dll`, `UnityEngine.CoreModule.dll`, and `LogLibrary.dll`. Typical paths:

- **Windows:** `C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\7DaysToDie_Data\Managed`
- **Mac:** `~/Library/Application Support/Steam/steamapps/common/7 Days To Die/7DaysToDie_Data/Managed`
- **Linux:** `~/.steam/steam/steamapps/common/7 Days To Die/7DaysToDie_Data/Managed`

Verify all four DLLs exist:

```bash
ls "$GAME_MANAGED"/Assembly-CSharp.dll "$GAME_MANAGED"/0Harmony.dll "$GAME_MANAGED"/UnityEngine.CoreModule.dll "$GAME_MANAGED"/LogLibrary.dll
```

If `LogLibrary.dll` does not exist, remove the `<Reference Include="LogLibrary">` block from `ORBlock.csproj` and change `Log.Out(...)` / `Log.Warning(...)` calls in the C# files to use `UnityEngine.Debug.Log(...)` / `UnityEngine.Debug.LogWarning(...)` instead.

**Step 3: Set the environment variable**

```bash
export GAME_MANAGED="/path/to/7DaysToDie_Data/Managed"
```

Or edit `ORBlock.csproj` and replace the default `GameManaged` path with the correct one.

---

### Task 2: Verify XML block and item names against the game

This is critical — wrong names cause silent failures.

**Step 1: Verify the base block name for extends**

Open the game's `Data/Config/blocks.xml` and search for electrical switch/relay blocks:

```bash
grep -i "electric" "$GAME_PATH/Data/Config/blocks.xml" | grep -i "block name"
```

Look for the block our `Config/blocks.xml` extends: `electricswitch`. If that exact name does not exist, find the closest match (common alternatives: `switchElectric`, `electricRelay`, `electrictimerrelay`) and update `Config/blocks.xml`:

```xml
<property name="Extends" value="CORRECT_NAME_HERE" />
<property name="CustomIcon" value="CORRECT_NAME_HERE" />
```

**Step 2: Verify recipe ingredient names**

Search the game's `Data/Config/items.xml` for the ingredient names used in `Config/recipes.xml`:

```bash
grep -i "resourceElectricParts" "$GAME_PATH/Data/Config/items.xml"
grep -i "resourceForgedIron" "$GAME_PATH/Data/Config/items.xml"
```

If either name doesn't exist, find the correct name and update `Config/recipes.xml`.

**Step 3: Verify the craft_area name**

```bash
grep -i "workbench" "$GAME_PATH/Data/Config/recipes.xml" | head -5
```

Confirm `craft_area="workbench"` is valid. If the game uses a different name (e.g., `Workbench` with capital W), update `Config/recipes.xml`.

**Step 4: Commit any XML fixes**

```bash
git add Config/
git commit -m "fix: adjust XML names to match game version"
```

Only commit if changes were made.

---

### Task 3: Build the mod

**Step 1: Run the build**

```bash
dotnet build ORBlock.csproj -c Release
```

**Step 2: If build succeeds, skip to Task 4**

**Step 3: If build fails, fix errors**

Common issues and fixes:

| Error | Likely cause | Fix |
|-------|-------------|-----|
| `CS0246: type 'PowerConsumer' not found` | Assembly-CSharp.dll not found or wrong path | Check `GAME_MANAGED` path, verify Assembly-CSharp.dll exists there |
| `CS0246: type 'IModApi' not found` | Same as above | Same as above |
| `CS0117: 'PowerManager' does not contain 'RemovePowerNode'` | Method name changed in this game version | Decompile Assembly-CSharp.dll with dnSpy/ILSpy and find the correct method name. Search for methods on PowerManager that remove/unregister a PowerItem |
| `CS0117: 'PowerItem' does not contain 'Position'` | Property name changed | Decompile and find the correct property name (might be `WorldPos`, `BlockPos`, etc.) |
| `CS0115: no suitable method to override` for `read`/`write` | Method signature changed | Decompile `PowerConsumer` and match the exact override signature |
| `CS1061: 'Block' does not contain 'Properties'` | API changed | Decompile `Block` class and find how block properties are accessed |
| `CS0234: 'HarmonyLib' does not exist` | 0Harmony.dll not found | Check DLL path. Some game versions bundle Harmony differently |
| Any `Vector3i` error | Might be in a different namespace | Add `using UnityEngine;` or check the actual namespace |

For each error, decompile the relevant class from `Assembly-CSharp.dll` using dnSpy or ILSpy to find the correct API. The plan's design doc (`docs/plans/2026-02-21-or-gate-block-design.md`) has context on the expected API shape.

**Step 4: Commit fixes**

```bash
git add Sources/
git commit -m "fix: adjust API calls to match game version"
```

**Step 5: Rebuild and confirm success**

```bash
dotnet build ORBlock.csproj -c Release
```

Repeat Steps 3-5 until the build succeeds.

---

### Task 4: Verify Harmony patch targets with decompiler

Even if the build succeeds, the patches may target wrong method names and silently fail at runtime. This task verifies each patch target exists.

**Step 1: Open Assembly-CSharp.dll in dnSpy or ILSpy**

**Step 2: Verify each patch target exists**

Check these exact method signatures. For each one, search the decompiled code and confirm the method exists with the expected signature:

| Patch class | Target | What to look for |
|------------|--------|-----------------|
| `PowerManager_SetParent_Patch` | `PowerManager.SetParent(PowerItem, PowerItem)` | Method with two PowerItem params |
| `PowerManager_RemoveParent_Patch` | `PowerManager.RemoveParent(PowerItem)` | Method with one PowerItem param |
| `PowerConsumer_IsPowered_Patch` | `PowerConsumer.get_IsPowered` | IsPowered as a property (not a field). If it's a field, we need a different approach |
| `PowerManager_LoadPowerManager_Patch` | `PowerManager.LoadPowerManager()` | Parameterless method, or note actual params |
| `TileEntityPowered_InitializePowerData_Patch` | `TileEntityPowered.InitializePowerData()` | If this method doesn't exist, look for where `new PowerConsumer()` is called |
| `Block_GetActivationText_Patch` | `Block.GetActivationText(WorldBase, BlockValue, int, Vector3i, EntityAlive)` | Check exact param types and order |
| `Block_OnBlockActivated_Patch` | `Block.OnBlockActivated(string, WorldBase, int, Vector3i, BlockValue, EntityPlayerLocal)` | Check exact param types and order |

**Step 3: For each mismatch, update the corresponding patch file**

- If a method name is different, update the `[HarmonyPatch("MethodName")]` attribute
- If parameter types differ, update the `[HarmonyPatch(new[] { typeof(...) })]` attribute
- If `IsPowered` is a field instead of a property, change the patch approach: instead of patching `get_IsPowered`, patch the power update loop to set the field value

**Step 4: Also verify these accessed members**

- `PowerManager.Instance` — is it a singleton accessor? Could be `PowerManager.GetInstance()`
- `PowerManager.PowerItemDictionary` — is it public? If not, use Harmony's `AccessTools.Field()` to read it
- `PowerManager.GetPowerItemByWorldPos(Vector3i)` — does this method exist? Could be named differently
- `PowerManager.AddPowerNode(PowerItem)` — verify name and params
- `PowerManager.RemovePowerNode(PowerItem)` — verify name and params
- `PowerItem.Parent` — is it a property or field?
- `PowerItem.Children` — is it `List<PowerItem>`?
- `PowerItem.IsPowered` — property or field?
- `PowerItem.Position` — or is it `WorldPos`?
- `PowerItem.BlockID` — verify exists
- `PowerItem.SendHasLocalChangesToRoot()` — verify exists, or remove calls if not
- `TileEntityPowered.PowerItem` — verify this is how you access it, could be a method
- `TileEntityPowered.blockValue` — verify field name (might be `BlockValue` with capital B)
- `Block.Properties.Values` — verify this is a Dictionary<string,string> accessor
- `EntityPlayerLocal.MovementInput.Down` — verify this is how crouch/sneak is detected
- `GameManager.ShowTooltip(EntityPlayerLocal, string)` — verify signature

**Step 5: Rebuild after fixes**

```bash
dotnet build ORBlock.csproj -c Release
```

**Step 6: Commit**

```bash
git add Sources/
git commit -m "fix: align Harmony patch targets with decompiled game API"
```

Only commit if changes were made.

---

### Task 5: Deploy and test in-game

**Step 1: Run the build script**

```bash
./build.sh
```

Verify the `deploy/ORBlock/` folder contains:
- `ModInfo.xml`
- `Config/blocks.xml`
- `Config/Localization.txt`
- `Config/recipes.xml`
- `ORBlock.dll`

**Step 2: Install the mod**

```bash
cp -r deploy/ORBlock /path/to/7DaysToDie/Mods/
```

**Step 3: Launch the game with EAC disabled**

**Step 4: Check the game log for mod loading**

Look for these messages in the game console or log file:
- `[ORBlock] OR Gate Block mod loaded.` — confirms Harmony patches applied
- No red errors mentioning "ORBlock" or "Harmony"

**Step 5: Test block placement**

1. Open creative menu or use `cm` command
2. Search for "OR Gate"
3. Place the block — should appear as a switch-like block
4. Look at the block — should show `OR Gate [Input 1: Empty] [Input 2: Empty] [Output: OFF]`

**Step 6: Test single input**

1. Place a generator/battery/solar bank nearby and turn it on
2. Use wire tool to connect the power source to the OR gate (Input 1)
3. Place a light or other consumer
4. Wire the OR gate to the consumer
5. Verify: consumer should turn ON (OR gate passes power through)
6. Look at OR gate: `[Input 1: Connected] [Input 2: Empty] [Output: ON]`

**Step 7: Test dual input (the OR logic)**

1. Place a second power source nearby
2. Wire the second source to the OR gate (this should become Input 2)
3. Verify: consumer stays ON
4. Turn off power source 1 — consumer should stay ON (powered by source 2)
5. Turn off power source 2 — consumer should turn OFF (both inputs off)
6. Turn on either source — consumer should turn ON again

**Step 8: Test disconnect**

1. Crouch (sneak) and press E on the OR gate
2. Should see tooltip: "OR Gate: All inputs disconnected"
3. Both wires should be gone
4. Consumer should turn OFF

**Step 9: Test save/load**

1. Wire up the OR gate with 2 inputs again
2. Save and exit the game
3. Reload the save
4. Check game log for `[ORBlock] Restored OR gate connections.`
5. Verify both input wires are still connected
6. Verify the OR gate still functions correctly

**Step 10: Commit any final fixes**

```bash
git add -A
git commit -m "fix: adjustments from in-game testing"
```

---

## Quick reference: files you may need to edit

| Issue | File to edit |
|-------|-------------|
| Wrong base block name | `Config/blocks.xml` — change `Extends` and `CustomIcon` values |
| Wrong ingredient names | `Config/recipes.xml` — change `ingredient name` values |
| Wrong craft area name | `Config/recipes.xml` — change `craft_area` value |
| Method name mismatches | `Sources/Harmony/*.cs` — update `[HarmonyPatch]` attributes |
| Property/field name mismatches | `Sources/PowerItemORGate.cs` and `Sources/Harmony/*.cs` |
| Missing LogLibrary | `ORBlock.csproj` (remove reference), all `.cs` files (replace `Log.Out`/`Log.Warning` with `UnityEngine.Debug.Log`/`LogWarning`) |
| PowerItemDictionary is private | `Sources/Harmony/PowerManagerPatches.cs` — use `AccessTools.Field(typeof(PowerManager), "PowerItemDictionary")` |
