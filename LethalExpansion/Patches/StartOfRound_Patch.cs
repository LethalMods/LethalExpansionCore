using HarmonyLib;
using Unity.Netcode;
using UnityEngine.SceneManagement;

namespace LethalExpansionCore.Patches;

[HarmonyPatch(typeof(StartOfRound))]
internal class StartOfRound_Patch
{
    [HarmonyPatch(nameof(StartOfRound.StartGame))]
    [HarmonyPostfix]
    public static void StartGame_Postfix(StartOfRound __instance)
    {
        // TODO: What is the purpose of this?
        if (__instance.currentLevel.name.StartsWith("Assets/Mods/"))
        {
            SceneManager.LoadScene(__instance.currentLevel.name, LoadSceneMode.Additive);
        }

        LethalExpansion.Log.LogInfo("Game started.");
    }

    [HarmonyPatch(nameof(StartOfRound.SetMapScreenInfoToCurrentLevel))]
    [HarmonyPostfix]
    static void SetMapScreenInfoToCurrentLevel_Postfix(StartOfRound __instance)
    {
        AutoScrollText text = __instance.screenLevelDescription.GetComponent<AutoScrollText>();
        if (text != null)
        {
            text.ResetScrolling();
        }
    }

    [HarmonyPatch(nameof(StartOfRound.ChangeLevel))]
    [HarmonyPrefix]
    static void ChangeLevel_Prefix(StartOfRound __instance, ref int levelID)
    {
        if (levelID >= __instance.levels.Length)
        {
            levelID = 0;
            LethalExpansion.Log.LogError($"Error loading moon ID {levelID}.");
        }
    }
}
