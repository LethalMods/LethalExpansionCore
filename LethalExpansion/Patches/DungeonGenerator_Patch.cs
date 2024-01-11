using DunGen;
using DunGen.Graph;
using HarmonyLib;
using System.Linq;

namespace LethalExpansionCore.Patches;

// See DungeonGenerator.ProcessGlobalProps for the only place where
// GlobalPropSettings is currently used in Lethal Company
[HarmonyPatch(typeof(DungeonGenerator))]
public class DungeonGenerator_Patch
{
    [HarmonyPatch(nameof(DungeonGenerator.Generate))]
    [HarmonyPrefix]
    public static void Generate_Prefix(DungeonGenerator __instance, out IntRange __state)
    {
        // Not our moon not our concern.
        bool isCustom = Terminal_Patch.newMoons.ContainsKey(RoundManager.Instance.currentLevel.levelID);
        if (!isCustom)
        {
            __state = null;
            return;
        }

        EntranceTeleport[] entrances = UnityEngine.GameObject.FindObjectsOfType<EntranceTeleport>();

        EntranceTeleport[] sortedEntrances = entrances
            .OrderBy(entrance => entrance.entranceId)
            .ToArray();

        for (int i = 0; i < sortedEntrances.Length; i++)
        {
            EntranceTeleport entrance = sortedEntrances[i];
            if (entrance.entranceId != i)
            {
                LethalExpansion.Log.LogWarning($"Entrance '{entrance.gameObject}' has an unexpected id, updating id from '{entrance.entranceId}' to '{i}'");
            }

            entrance.entranceId = i;
        }

        IntRange oldRange = SetFireExitAmount(__instance.DungeonFlow, new IntRange(entrances.Length - 1, entrances.Length - 1));
        __state = oldRange;
    }

    [HarmonyPatch(nameof(DungeonGenerator.Generate))]
    [HarmonyPostfix]
    public static void Generate_Postfix(DungeonGenerator __instance, IntRange __state)
    {
        // I don't think it's technically necessary to reset it,
        // this implementation seems to work fine either way but
        // just doing it for completeness sake.
        if (__state != null)
        {
            SetFireExitAmount(__instance.DungeonFlow, __state);
        }
    }

    public static IntRange SetFireExitAmount(DungeonFlow dungeonFlow, IntRange newRange)
    {
        // Lethal Company only handles the first one of any ID so we don't need to look further
        DungeonFlow.GlobalPropSettings settings = dungeonFlow.GlobalProps
            .Where(props => props.ID == 1231)
            .First();

        LethalExpansion.Log.LogInfo($"Updating fire exit count from ({settings.Count}) to ({newRange})");

        IntRange oldRange = settings.Count;
        settings.Count = newRange;
        return oldRange;
    }
}
