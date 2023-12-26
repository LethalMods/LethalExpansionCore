using HarmonyLib;
using LethalExpansion.Utils;
using UnityEngine;

namespace LethalExpansion.Patches
{
    [HarmonyPatch(typeof(HUDManager))]
    internal class HUDManager_Patch
    {
        [HarmonyPatch("AddChatMessage")]
        [HarmonyPrefix]
        static bool ChatInterpreter(HUDManager __instance, string chatMessage)
        {
            return !ChatMessageProcessor.ProcessMessage(chatMessage);
        }
    }
}
