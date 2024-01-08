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

        int uniqueEntranceCount = entrances
            .Select(entrance => entrance.entranceId)
            .Distinct()
            .Count();

        int duplicateEntranceCount = entrances.Length - uniqueEntranceCount;
        if (duplicateEntranceCount > 0)
        {
            LethalExpansion.Log.LogWarning($"Found {duplicateEntranceCount} entrance(s) with the same id as another entrance, this means multiple entrances may take you to the same place");
        }

        int uniqueFireExits = entrances
            .Where(entrance => entrance.entranceId != 0)
            .Distinct()
            .Count();

        IntRange oldRange = SetFireExitAmount(__instance.DungeonFlow, new IntRange(uniqueFireExits, uniqueFireExits));
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

        IntRange oldRange = settings.Count;
        settings.Count = newRange;
        return oldRange;
    }
}
