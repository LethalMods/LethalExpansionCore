using HarmonyLib;
using System;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using System.Collections.Generic;
using LethalExpansionCore.Utils;
using LethalSDK.ScriptableObjects;
using LethalSDK.Utils;
using LethalSDK.Component;
using LethalExpansionCore.MonoBehaviours;
using LethalExpansionCore.Netcode;

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
            foreach (PrefabInfoPair networkPrefab in mainBank.NetworkPrefabs())
            {
                string prefabPath = networkPrefab.PrefabPath;
                if (string.IsNullOrEmpty(prefabPath))
                {
                    continue;
                }

                GameObject prefab = AssetBundlesManager.Instance.mainAssetBundle.LoadAsset<GameObject>(networkPrefab.PrefabPath);
                __instance.GetComponent<NetworkManager>().PrefabHandler.AddNetworkPrefab(prefab);
                LethalExpansion.Log.LogInfo($"Registered prefab '{networkPrefab.PrefabName}'");
            }
        }

        Sprite scrapSprite = AssetBundlesManager.Instance.mainAssetBundle.LoadAsset<Sprite>("Assets/Mods/LethalExpansion/Sprites/ScrapItemIcon2.png");
        try
        {
            foreach (KeyValuePair<String, (AssetBundle, ModManifest)> bundleKeyValue in AssetBundlesManager.Instance.assetBundles)
            {
                (AssetBundle assetBundle, ModManifest manifest) = AssetBundlesManager.Instance.Load(bundleKeyValue.Key);

                if (assetBundle == null || manifest == null)
                {
                    continue;
                }

                LoadScrapPrefabs(__instance, assetBundle, manifest, scrapSprite);
                LoadNetworkPrefabs(__instance, assetBundle, manifest);

                LoadMoonPrefabs(__instance, assetBundle, manifest);
            }
        }
        catch (Exception ex)
        {
            LethalExpansion.Log.LogError($"Failed to register AssetBundle prefabs. {ex.Message}");
        }
    }

    private static void LoadScrapPrefabs(GameNetworkManager networkManager, AssetBundle assetBundle, ModManifest manifest, Sprite scrapSprite)
    {
        Scrap[] scraps = manifest.scraps;
        if (scraps == null || scraps.Length == 0)
        {
            return;
        }

        NetworkPrefabHandler prefabHandler = networkManager.GetComponent<NetworkManager>().PrefabHandler;

        foreach (Scrap scrap in manifest.scraps)
        {
            if (!AssetBundlesManager.Instance.IsScrapCompatible(scrap))
            {
                continue;
            }

            InitializeScrap(scrap, scrapSprite);

            try
            {
                prefabHandler.AddNetworkPrefab(scrap.prefab);
                LethalExpansion.Log.LogInfo($"Registered scrap '{scrap.itemName}'");
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError($"Failed to register scrap '{scrap.itemName}'. {ex.Message}");
            }
        }
    }

    private static void LoadNetworkPrefabs(GameNetworkManager networkManager, AssetBundle assetBundle, ModManifest manifest)
    {
        PrefabInfoPair[] networkPrefabs = manifest.assetBank?.NetworkPrefabs();
        if (networkPrefabs == null || networkPrefabs.Length == 0)
        {
            return;
        }

        NetworkPrefabHandler prefabHandler = networkManager.GetComponent<NetworkManager>().PrefabHandler;

        foreach (PrefabInfoPair networkPrefab in networkPrefabs)
        {
            string prefabPath = networkPrefab.PrefabPath;
            if (string.IsNullOrEmpty(prefabPath))
            {
                continue;
            }

            GameObject prefab = assetBundle.LoadAsset<GameObject>(prefabPath);

            // Is it necessary for it to be loaded again? I am going to guess no and hope it doesn't break anything
            // ComponentWhitelist.CheckAndRemoveIllegalComponents(bundleKeyValue.Value.Item1.LoadAsset<GameObject>(prefabPath).transform, ComponentWhitelist.ScrapPrefabComponentWhitelist);

            ComponentWhitelist.CheckAndRemoveIllegalComponents(prefab.transform, ComponentWhitelist.ScrapPrefabComponentWhitelist);
            prefabHandler.AddNetworkPrefab(prefab);

            LethalExpansion.Log.LogInfo($"Registered prefab '{networkPrefab.PrefabName}'");
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

    private static void LoadMoonPrefabs(GameNetworkManager networkManager, AssetBundle assetBundle, ModManifest manifest)
    {
        Moon[] moons = manifest.moons;
        if (moons == null || moons.Length == 0)
        {
            return;
        }

        NetworkPrefabHandler prefabHandler = networkManager.GetComponent<NetworkManager>().PrefabHandler;

        foreach (Moon moon in manifest.moons)
        {
            if (!AssetBundlesManager.Instance.IsMoonCompatible(moon))
            {
                continue;
            }

            if (moon.MainPrefab == null)
            {
                LethalExpansion.Log.LogWarning($"Moon '{moon.PlanetName}' does not have a MainPrefab");
                continue;
            }

            try
            {
                foreach (NetworkObject networkObject in moon.MainPrefab.GetComponentsInChildren<NetworkObject>())
                {
                    SI_EntranceTeleport teleport = networkObject.GetComponent<SI_EntranceTeleport>();
                    if (!teleport)
                    {
                        continue;
                    }

                    GameObject gameObject = networkObject.gameObject;
                    // Sync BoxCollider bounds. For some reason it does not
                    // have the proper bounds when it's instantiated on the
                    // client which can sometimes cause the entrance to be
                    // inaccessible (on Aquatis for instance).
                    //
                    // Syncing it with a NetworkTransform might be a bit hacky
                    // but it will do until I can figure out the root cause of
                    // the desync, though this entire solution feels a bit hacky
                    // to be honest.
                    gameObject.AddComponent<NetworkTransform>();
                    gameObject.SetActive(false);

                    GameObject parent = gameObject.transform.parent.gameObject;

                    GameObject instancier = new GameObject("NetworkPrefabInstancier");
                    instancier.transform.parent = parent.transform;
                    instancier.transform.position = gameObject.transform.position;
                    instancier.transform.rotation = gameObject.transform.rotation;

                    instancier.AddComponent<LECore_InactiveNetworkPrefabInstancier>().prefab = gameObject;

                    prefabHandler.AddHandler(gameObject, new InactiveNetworkPrefabInstanceHandler(gameObject));
                }

                LethalExpansion.Log.LogInfo($"Registered moon '{moon.MoonName}'");
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError($"Failed to register moon '{moon.MoonName}'. {ex}");
            }
        }
    }
}
