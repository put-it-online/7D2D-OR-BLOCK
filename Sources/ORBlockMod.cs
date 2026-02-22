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
