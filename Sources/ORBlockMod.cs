using HarmonyLib;

public class ORBlockMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        // PatchAll() discovers every [HarmonyPatch] class in this assembly
        // automatically, so no individual patch class needs to be listed here.
        // The previous approach of listing patches explicitly missed the
        // TileEntity and PowerManager patch classes entirely.
        var harmony = new Harmony("com.wimput.orblock");
        harmony.PatchAll();
        Log.Out("[ORBlock] Logic Relay Block mod loaded — all patches applied.");
    }
}
