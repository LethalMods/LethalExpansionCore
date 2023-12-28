using HarmonyLib;
using System;
using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using LethalExpansionCore.Utils;
using LethalSDK.ScriptableObjects;

namespace LethalExpansionCore.Patches;

[HarmonyPatch(typeof(GameNetworkManager))]
internal class GameNetworkManager_Patch
{
    [HarmonyPatch("Start")]
    [HarmonyPrefix]
    static void Start_Prefix(GameNetworkManager __instance)
    {
        AssetBank mainBank = AssetBundlesManager.Instance.mainAssetBundle.LoadAsset<ModManifest>("Assets/Mods/LethalExpansion/modmanifest.asset").assetBank;
        if (mainBank != null)
        {
            foreach (var networkprefab in mainBank.NetworkPrefabs())
            {
                if (networkprefab.PrefabPath != null && networkprefab.PrefabPath.Length > 0)
                {
                    GameObject prefab = AssetBundlesManager.Instance.mainAssetBundle.LoadAsset<GameObject>(networkprefab.PrefabPath);
                    __instance.GetComponent<NetworkManager>().PrefabHandler.AddNetworkPrefab(prefab);
                    LethalExpansion.Log.LogInfo($"{networkprefab.PrefabName} Prefab registered.");
                }
            }
        }

        Sprite scrapSprite = AssetBundlesManager.Instance.mainAssetBundle.LoadAsset<Sprite>("Assets/Mods/LethalExpansion/Sprites/ScrapItemIcon2.png");
        try
        {
            foreach (KeyValuePair<String, (AssetBundle, ModManifest)> bundleKeyValue in AssetBundlesManager.Instance.assetBundles)
            {
                (AssetBundle bundle, ModManifest manifest) = AssetBundlesManager.Instance.Load(bundleKeyValue.Key);

                if (bundle == null || manifest == null)
                {
                    continue;
                }

                if (manifest.scraps == null || manifest.scraps.Length == 0)
                {
                    continue;
                }

                foreach (var newScrap in manifest.scraps)
                {
                    if (!AssetBundlesManager.Instance.IsScrapCompatible(newScrap))
                    {
                        continue;
                    }

                    InitializeScrap(newScrap, scrapSprite);

                    try
                    {
                        __instance.GetComponent<NetworkManager>().PrefabHandler.AddNetworkPrefab(newScrap.prefab);
                        LethalExpansion.Log.LogInfo(newScrap.itemName + " Scrap registered.");
                    }
                    catch (Exception ex)
                    {
                        LethalExpansion.Log.LogError(ex.Message);
                    }
                }

                if (manifest.assetBank != null && manifest.assetBank.NetworkPrefabs() != null && manifest.assetBank.NetworkPrefabs().Length > 0)
                {
                    foreach (var networkprefab in manifest.assetBank.NetworkPrefabs())
                    {
                        if (networkprefab.PrefabPath != null && networkprefab.PrefabPath.Length > 0)
                        {
                            GameObject prefab = bundleKeyValue.Value.Item1.LoadAsset<GameObject>(networkprefab.PrefabPath);
                            ComponentWhitelist.CheckAndRemoveIllegalComponents(bundleKeyValue.Value.Item1.LoadAsset<GameObject>(networkprefab.PrefabPath).transform, ComponentWhitelist.ScrapPrefabComponentWhitelist);
                            __instance.GetComponent<NetworkManager>().PrefabHandler.AddNetworkPrefab(prefab);
                            LethalExpansion.Log.LogInfo($"{networkprefab.PrefabName} Prefab registered.");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LethalExpansion.Log.LogError(ex.Message);
        }
    }

    private static void InitializeScrap(Scrap newScrap, Sprite scrapSprite)
    {
        Item scrapItem = ScriptableObject.CreateInstance<Item>();
        scrapItem.name = newScrap.name;
        scrapItem.itemName = newScrap.itemName;
        scrapItem.canBeGrabbedBeforeGameStart = true;
        scrapItem.isScrap = true;
        scrapItem.minValue = newScrap.minValue;
        scrapItem.maxValue = newScrap.maxValue;
        scrapItem.weight = (float)newScrap.weight / 100 + 1;

        ComponentWhitelist.CheckAndRemoveIllegalComponents(newScrap.prefab.transform, ComponentWhitelist.ScrapPrefabComponentWhitelist);
        scrapItem.spawnPrefab = newScrap.prefab;

        scrapItem.twoHanded = newScrap.twoHanded;
        scrapItem.twoHandedAnimation = newScrap.twoHandedAnimation;
        scrapItem.requiresBattery = newScrap.requiresBattery;
        scrapItem.isConductiveMetal = newScrap.isConductiveMetal;

        scrapItem.itemIcon = scrapSprite;
        scrapItem.syncGrabFunction = false;
        scrapItem.syncUseFunction = false;
        scrapItem.syncDiscardFunction = false;
        scrapItem.syncInteractLRFunction = false;
        scrapItem.verticalOffset = newScrap.verticalOffset;
        scrapItem.restingRotation = newScrap.restingRotation;
        scrapItem.positionOffset = newScrap.positionOffset;
        scrapItem.rotationOffset = newScrap.rotationOffset;
        scrapItem.meshOffset = false;
        scrapItem.meshVariants = newScrap.meshVariants;
        scrapItem.materialVariants = newScrap.materialVariants;
        scrapItem.canBeInspected = false;

        PhysicsProp physicsProp = newScrap.prefab.AddComponent<PhysicsProp>();
        physicsProp.grabbable = true;
        physicsProp.itemProperties = scrapItem;
        physicsProp.mainObjectRenderer = newScrap.prefab.GetComponent<MeshRenderer>();

        AudioSource audioSource = newScrap.prefab.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;

        Transform scanNodeObject = newScrap.prefab.transform.Find("ScanNode");
        if (scanNodeObject != null)
        {
            ScanNodeProperties scanNode = scanNodeObject.gameObject.AddComponent<ScanNodeProperties>();
            scanNode.maxRange = 13;
            scanNode.minRange = 1;
            scanNode.headerText = newScrap.itemName;
            scanNode.subText = "Value: ";
            scanNode.nodeType = 2;
        }
    }
}
