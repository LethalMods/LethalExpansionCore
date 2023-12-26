using HarmonyLib;
using LethalExpansion.Utils;
using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using static UnityEngine.Rendering.HighDefinition.ScalableSettingLevelParameter;

namespace LethalExpansion.Patches
{
    [HarmonyPatch(typeof(StartOfRound))]
    internal class StartOfRound_Patch
    {
        [HarmonyPatch(nameof(StartOfRound.StartGame))]
        [HarmonyPostfix]
        public static void StartGame_Postfix(StartOfRound __instance)
        {
            if (__instance.currentLevel.name.StartsWith("Assets/Mods/"))
            {
                SceneManager.LoadScene(__instance.currentLevel.name, LoadSceneMode.Additive);
            }

            LethalExpansion.Log.LogInfo("Game started.");
        }

        [HarmonyPatch("OnPlayerConnectedClientRpc")]
        [HarmonyPostfix]
        static void OnPlayerConnectedClientRpc_Postfix(StartOfRound __instance, ulong clientId, int connectedPlayers, ulong[] connectedPlayerIdsOrdered, int assignedPlayerObjectId, int serverMoneyAmount, int levelID, int profitQuota, int timeUntilDeadline, int quotaFulfilled, int randomSeed)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                return;
            }

            LethalExpansion.sessionWaiting = false;
            LethalExpansion.Log.LogInfo($"LethalExpansion Client Started. {__instance.NetworkManager.LocalClientId}");
        }

        [HarmonyPatch(nameof(StartOfRound.SetMapScreenInfoToCurrentLevel))]
        [HarmonyPostfix]
        static void SetMapScreenInfoToCurrentLevel_Postfix(StartOfRound __instance)
        {
            AutoScrollText obj = __instance.screenLevelDescription.GetComponent<AutoScrollText>();
            if (obj != null)
            {
                obj.ResetScrolling();
            }
        }

        [HarmonyPatch(nameof(StartOfRound.ChangeLevel))]
        [HarmonyPrefix]
        static void ChangeLevel_Prefix(StartOfRound __instance, ref int levelID)
        {
            if (levelID < __instance.levels.Length)
            {
                return;
            }

            if (LethalExpansion.delayedLevelChange == -1)
            {
                LethalExpansion.delayedLevelChange = levelID;
                levelID = 0;
            }
            else
            {
                LethalExpansion.Log.LogError($"Error loading moon ID {levelID}.");
                levelID = 0;
            }
        }
    }
}
