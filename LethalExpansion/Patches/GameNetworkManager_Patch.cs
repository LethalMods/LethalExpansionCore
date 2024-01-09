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
        AssetBundle mainAssetBundle = AssetBundlesManager.Instance.mainAssetBundle;
        ModManifest mainManifest = mainAssetBundle.LoadAsset<ModManifest>("Assets/Mods/LethalExpansion/modmanifest.asset");

        LoadNetworkPrefabs(__instance, mainAssetBundle, mainManifest, true);

        Sprite scrapSprite = mainAssetBundle.LoadAsset<Sprite>("Assets/Mods/LethalExpansion/Sprites/ScrapItemIcon2.png");
        foreach (KeyValuePair<String, (AssetBundle, ModManifest)> bundleKeyValue in AssetBundlesManager.Instance.assetBundles)
        {
            (AssetBundle assetBundle, ModManifest manifest) = AssetBundlesManager.Instance.Load(bundleKeyValue.Key);

            if (assetBundle == null || manifest == null)
            {
                continue;
            }
            
            try
            {
                LoadScrapPrefabs(__instance, assetBundle, manifest, scrapSprite);
                LoadNetworkPrefabs(__instance, assetBundle, manifest);

                LoadMoonPrefabs(__instance, assetBundle, manifest);
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError($"Failed to register AssetBundle prefabs for '{manifest.modName}', this may cause issues. {ex.Message}");
            }
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

            try
            {
                InitializeScrap(scrap, scrapSprite);

                prefabHandler.AddNetworkPrefab(scrap.prefab);
                LethalExpansion.Log.LogInfo($"Registered scrap '{scrap.itemName}' from '{manifest.modName}'");
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError($"Failed to register scrap '{scrap.itemName}' from '{manifest.modName}'. {ex.Message}");
            }
        }
    }

    private static void LoadNetworkPrefabs(GameNetworkManager networkManager, AssetBundle assetBundle, ModManifest manifest, bool allowIllegalComponents = false)
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

            if (!allowIllegalComponents)
            {
                ComponentWhitelist.CheckAndRemoveIllegalComponents(prefab.transform, ComponentWhitelist.ScrapPrefabComponentWhitelist);
            }
            
            prefabHandler.AddNetworkPrefab(prefab);

            LethalExpansion.Log.LogInfo($"Registered prefab '{networkPrefab.PrefabName}' from '{manifest.modName}'");
        }
    }

    private static void InitializeScrap(Scrap scrap, Sprite scrapSprite)
    {
        Item item = ScriptableObject.CreateInstance<Item>();
        item.name = scrap.name;
        item.itemName = scrap.itemName;
        item.canBeGrabbedBeforeGameStart = true;
        item.isScrap = true;
        item.minValue = scrap.minValue;
        item.maxValue = scrap.maxValue;
        item.weight = (float)scrap.weight / 100 + 1;

        ComponentWhitelist.CheckAndRemoveIllegalComponents(scrap.prefab.transform, ComponentWhitelist.ScrapPrefabComponentWhitelist);
        item.spawnPrefab = scrap.prefab;

        item.twoHanded = scrap.twoHanded;
        item.twoHandedAnimation = scrap.twoHandedAnimation;
        item.requiresBattery = scrap.requiresBattery;
        item.isConductiveMetal = scrap.isConductiveMetal;

        item.itemIcon = scrapSprite;
        item.syncGrabFunction = false;
        item.syncUseFunction = false;
        item.syncDiscardFunction = false;
        item.syncInteractLRFunction = false;
        item.verticalOffset = scrap.verticalOffset;
        item.restingRotation = scrap.restingRotation;
        item.positionOffset = scrap.positionOffset;
        item.rotationOffset = scrap.rotationOffset;
        item.meshOffset = false;
        item.meshVariants = scrap.meshVariants;
        item.materialVariants = scrap.materialVariants;
        item.canBeInspected = false;

        PhysicsProp physicsProp = scrap.prefab.AddComponent<PhysicsProp>();
        physicsProp.grabbable = true;
        physicsProp.itemProperties = item;
        physicsProp.mainObjectRenderer = scrap.prefab.GetComponent<MeshRenderer>();

        AudioSource audioSource = scrap.prefab.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;

        Transform scanNodeObject = scrap.prefab.transform.Find("ScanNode");
        if (scanNodeObject != null)
        {
            ScanNodeProperties scanNode = scanNodeObject.gameObject.AddComponent<ScanNodeProperties>();
            scanNode.maxRange = 13;
            scanNode.minRange = 1;
            scanNode.headerText = scrap.itemName;
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
                InitializeMoon(moon, prefabHandler);

                LethalExpansion.Log.LogInfo($"Registered moon '{moon.MoonName}' from '{manifest.modName}'");
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError($"Failed to register moon '{moon.MoonName}' from '{manifest.modName}'. {ex}");
            }
        }
    }

    private static void InitializeMoon(Moon moon, NetworkPrefabHandler prefabHandler)
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
    }
}
