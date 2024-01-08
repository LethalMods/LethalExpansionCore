using HarmonyLib;

namespace LethalExpansionCore.Patches;

[HarmonyPatch(typeof(MenuManager))]
internal class MenuManager_Patch
{
    [HarmonyPatch("Awake")]
    [HarmonyPostfix]
    public static void Awake_Postfix(MenuManager __instance)
    {
        if (__instance.versionNumberText != null)
        {
            __instance.versionNumberText.enableWordWrapping = false;
            __instance.versionNumberText.text += $"     LE(core)v{LethalExpansion.ModVersion}";
        }
    }
}
