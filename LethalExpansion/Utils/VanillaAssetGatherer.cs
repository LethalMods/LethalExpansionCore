using UnityEngine;
using System.Linq;

namespace LethalExpansionCore.Utils;

public static class VanillaAssetGatherer
{
    public static void GatherAssets()
    {
        GatherIconAssets();

        foreach (Item item in StartOfRound.Instance.allItemsList.itemsList)
        {
            GatherItemAssets(item);
        }

        foreach (SelectableLevel level in StartOfRound.Instance.levels)
        {
            GatherLevelAssets(level);
        }
    }

    public static void GatherIconAssets()
    {
        AssetGather.Instance.AddSprites(GameObject.Find("Environment/HangarShip/StartGameLever").GetComponent<InteractTrigger>().hoverIcon);
        AssetGather.Instance.AddSprites(GameObject.Find("Environment/HangarShip/Terminal/TerminalTrigger/TerminalScript").GetComponent<InteractTrigger>().hoverIcon);
        AssetGather.Instance.AddSprites(GameObject.Find("Environment/HangarShip/OutsideShipRoom/Ladder/LadderTrigger").GetComponent<InteractTrigger>().hoverIcon);
    }

    public static void GatherItemAssets(Item item)
    {
        AssetGather.Instance.AddAudioClip(item.grabSFX);
        AssetGather.Instance.AddAudioClip(item.dropSFX);
        AssetGather.Instance.AddAudioClip(item.pocketSFX);
        AssetGather.Instance.AddAudioClip(item.throwSFX);

        GameObject spawnPrefab = item.spawnPrefab;
        if (spawnPrefab != null)
        {
            GatherItemPrefabAssets(spawnPrefab);
        }
    }

    public static void GatherItemPrefabAssets(GameObject spawnPrefab)
    {
        foreach (Component component in spawnPrefab.GetComponents<Component>())
        {
            GatherItemPrefabComponentAssets(component);
        }
    }

    public static void GatherItemPrefabComponentAssets(Component component)
    {
        if (component is Shovel shovel)
        {
            AssetGather.Instance.AddAudioClip(shovel.reelUp);
            AssetGather.Instance.AddAudioClip(shovel.swing);
            AssetGather.Instance.AddAudioClip(shovel.hitSFX);
        }
        else if (component is FlashlightItem flashlightItem)
        {
            AssetGather.Instance.AddAudioClip(flashlightItem.flashlightClips);
            AssetGather.Instance.AddAudioClip(flashlightItem.outOfBatteriesClip);
            AssetGather.Instance.AddAudioClip(flashlightItem.flashlightFlicker);
        }
        else if (component is WalkieTalkie walkieTalkie)
        {
            AssetGather.Instance.AddAudioClip(walkieTalkie.stopTransmissionSFX);
            AssetGather.Instance.AddAudioClip(walkieTalkie.startTransmissionSFX);
            AssetGather.Instance.AddAudioClip(walkieTalkie.switchWalkieTalkiePowerOff);
            AssetGather.Instance.AddAudioClip(walkieTalkie.switchWalkieTalkiePowerOn);
            AssetGather.Instance.AddAudioClip(walkieTalkie.talkingOnWalkieTalkieNotHeldSFX);
            AssetGather.Instance.AddAudioClip(walkieTalkie.playerDieOnWalkieTalkieSFX);
        }
        else if (component is ExtensionLadderItem extensionLadderItem)
        {
            AssetGather.Instance.AddAudioClip(extensionLadderItem.hitRoof);
            AssetGather.Instance.AddAudioClip(extensionLadderItem.fullExtend);
            AssetGather.Instance.AddAudioClip(extensionLadderItem.hitWall);
            AssetGather.Instance.AddAudioClip(extensionLadderItem.ladderExtendSFX);
            AssetGather.Instance.AddAudioClip(extensionLadderItem.ladderFallSFX);
            AssetGather.Instance.AddAudioClip(extensionLadderItem.ladderShrinkSFX);
            AssetGather.Instance.AddAudioClip(extensionLadderItem.blinkWarningSFX);
            AssetGather.Instance.AddAudioClip(extensionLadderItem.lidOpenSFX);
        }
        else if (component is NoisemakerProp noisemakerProp)
        {
            AssetGather.Instance.AddAudioClip(noisemakerProp.noiseSFX);
            AssetGather.Instance.AddAudioClip(noisemakerProp.noiseSFXFar);
        }
        else if (component is PatcherTool patcherTool)
        {
            AssetGather.Instance.AddAudioClip(patcherTool.activateClips);
            AssetGather.Instance.AddAudioClip(patcherTool.beginShockClips);
            AssetGather.Instance.AddAudioClip(patcherTool.overheatClips);
            AssetGather.Instance.AddAudioClip(patcherTool.finishShockClips);
            AssetGather.Instance.AddAudioClip(patcherTool.outOfBatteriesClip);
            AssetGather.Instance.AddAudioClip(patcherTool.detectAnomaly);
            AssetGather.Instance.AddAudioClip(patcherTool.scanAnomaly);
        }
        else if (component is WhoopieCushionItem whoopieCushionItem)
        {
            AssetGather.Instance.AddAudioClip(whoopieCushionItem.fartAudios);
        }
        else if (component is ShotgunItem shotgunItem)
        {
            AssetGather.Instance.AddAudioClip(shotgunItem.gunShootSFX);
            AssetGather.Instance.AddAudioClip(shotgunItem.gunReloadSFX);
            AssetGather.Instance.AddAudioClip(shotgunItem.gunReloadFinishSFX);
            AssetGather.Instance.AddAudioClip(shotgunItem.noAmmoSFX);
            AssetGather.Instance.AddAudioClip(shotgunItem.gunSafetySFX);
            AssetGather.Instance.AddAudioClip(shotgunItem.switchSafetyOnSFX);
            AssetGather.Instance.AddAudioClip(shotgunItem.switchSafetyOffSFX);
        }
        else if (component is AudioSource audioSource)
        {
            AssetGather.Instance.AddAudioClip(audioSource.clip);
        }
    }

    public static void GatherLevelAssets(SelectableLevel level)
    {
        AssetGather.Instance.AddPlanetPrefabs(level.planetPrefab);
        AssetGather.Instance.AddLevelAmbiances(level.levelAmbienceClips);

        level.spawnableMapObjects.ToList().ForEach(e => AssetGather.Instance.AddMapObjects(e.prefabToSpawn));
        level.spawnableOutsideObjects.ToList().ForEach(e => AssetGather.Instance.AddOutsideObject(e.spawnableObject));
        level.spawnableScrap.ForEach(e => AssetGather.Instance.AddScrap(e.spawnableItem));

        level.Enemies.ForEach(e => AssetGather.Instance.AddEnemies(e.enemyType));
        level.OutsideEnemies.ForEach(e => AssetGather.Instance.AddEnemies(e.enemyType));
        level.DaytimeEnemies.ForEach(e => AssetGather.Instance.AddEnemies(e.enemyType));
    }
}
