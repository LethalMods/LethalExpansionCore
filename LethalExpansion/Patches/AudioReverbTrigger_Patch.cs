using HarmonyLib;

namespace LethalExpansion.Patches
{
    [HarmonyPatch(typeof(AudioReverbTrigger))]
    internal class AudioReverbTrigger_Patch
    {
        [HarmonyPatch(nameof(AudioReverbTrigger.ChangeAudioReverbForPlayer))]
        [HarmonyPostfix]
        public static void SChangeAudioReverbForPlayer_Postfix(AudioReverbTrigger __instance)
        {
            if (LethalExpansion.currentWaterSurface != null)
            {
                LethalExpansion.currentWaterSurface.gameObject.SetActive(!__instance.disableAllWeather);
            }
        }
    }
}
