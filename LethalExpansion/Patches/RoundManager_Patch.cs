using HarmonyLib;

namespace LethalExpansion.Patches
{
    [HarmonyPatch(typeof(RoundManager))]
    internal class RoundManager_Patch
    {
        [HarmonyPatch(nameof(RoundManager.SpawnMapObjects))]
        [HarmonyPrefix]
        public static bool SpawnMapObjects_Prefix(RoundManager __instance)
        {
            return __instance.currentLevel.spawnableMapObjects != null;
        }

        [HarmonyPatch(nameof(RoundManager.PlotOutEnemiesForNextHour))]
        [HarmonyPrefix]
        public static bool PlotOutEnemiesForNextHour_Prefix(RoundManager __instance)
        {
            return __instance.currentLevel.enemySpawnChanceThroughoutDay != null;
        }

        [HarmonyPatch(nameof(RoundManager.SpawnEnemiesOutside))]
        [HarmonyPrefix]
        public static bool SpawnEnemiesOutside_Prefix(RoundManager __instance)
        {
            return __instance.currentLevel.outsideEnemySpawnChanceThroughDay != null;
        }

        [HarmonyPatch(nameof(RoundManager.SpawnDaytimeEnemiesOutside))]
        [HarmonyPrefix]
        public static bool SpawnDaytimeEnemiesOutside_Prefix(RoundManager __instance)
        {
            return __instance.currentLevel.daytimeEnemySpawnChanceThroughDay != null;
        }
    }
}
