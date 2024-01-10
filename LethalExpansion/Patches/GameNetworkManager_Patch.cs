using HarmonyLib;
using System;
using System.Linq;
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
using System.Security.Cryptography;
using System.Text;

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

        // If we don't ensure that each prefab is only used for one scrap
        // we will have issues with components, which were added by us, being
        // removed because they are not whitelisted.
        //
        // See usage of ComponentWhitelist.CheckAndRemoveIllegalComponents
        List<GameObject> usedScrapPrefabs = new List<GameObject>();

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
                LoadScrapPrefabs(__instance, assetBundle, manifest, scrapSprite, usedScrapPrefabs);
                LoadNetworkPrefabs(__instance, assetBundle, manifest);

                LoadMoonPrefabs(__instance, assetBundle, manifest);
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError($"Failed to register AssetBundle prefabs for '{manifest.modName}', this may cause issues. {ex.Message}");
            }
        }
    }

    private static void LoadScrapPrefabs(GameNetworkManager networkManager, AssetBundle assetBundle, ModManifest manifest, Sprite scrapSprite, List<GameObject> usedPrefabs)
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

            if (usedPrefabs.Any(prefab => ReferenceEquals(prefab, scrap.prefab)))
            {
                LethalExpansion.Log.LogWarning($"Prefab used by scrap '{scrap.itemName}' from '{manifest.modName}' is already in use by another scrap");
                continue;
            }

            usedPrefabs.Add(scrap.prefab);

            try
            {
                VanillaItemInstancier.AddItemToScrap(scrap, scrapSprite);
                prefabHandler.AddNetworkPrefab(scrap.prefab);
                LethalExpansion.Log.LogInfo($"Registered scrap '{scrap.itemName}' from '{manifest.modName}'");
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError($"Failed to register scrap '{scrap.itemName}' from '{manifest.modName}'. {ex}");
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

    // Thanks Xilophor
    private static NetworkObject AddNetworkObject(GameObject gameObject)
    {
        List<NetworkPrefab> networkPrefabs = Traverse.Create(NetworkManager.Singleton.NetworkConfig.Prefabs)
            .Field("m_Prefabs")
            .GetValue<List<NetworkPrefab>>();

        uint unusedId = networkPrefabs
            .First(i => networkPrefabs.Any(x => x.SourcePrefabGlobalObjectIdHash != i.SourcePrefabGlobalObjectIdHash + 1))
            .SourcePrefabGlobalObjectIdHash + 1;

        return AddNetworkObject(gameObject, unusedId);
    }

    private static NetworkObject AddNetworkObject(GameObject gameObject, string uniqueIdentifier)
    {
        uint id = BitConverter.ToUInt32(MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(uniqueIdentifier)), 0);
        return AddNetworkObject(gameObject, id);
    }

    private static NetworkObject AddNetworkObject(GameObject gameObject, uint id)
    {
        NetworkObject networkObject = gameObject.AddComponent<NetworkObject>();

        Traverse.Create(networkObject)
            .Field("GlobalObjectIdHash")
            .SetValue(id);

        LethalExpansion.Log.LogInfo($"Added NetworkObject with id '{id}' to '{gameObject}'");
        return networkObject;
    }

    private static void InitializeMoon(Moon moon, NetworkPrefabHandler prefabHandler)
    {
        // TODO: We can solve this in a better way
        // now that I know how to create NetworkObjects
        // at runtime.

        foreach (SI_EntranceTeleport teleport in moon.MainPrefab.GetComponentsInChildren<SI_EntranceTeleport>())
        {
            NetworkObject networkObject = teleport.GetComponent<NetworkObject>();
            if (!networkObject)
            {
                if (teleport.GetComponentInParent<NetworkObject>())
                {
                    continue;
                }

                LethalExpansion.Log.LogInfo($"Adding missing NetworkObject to EntranceTeleport '{teleport.gameObject}' to fix desync issue");
                networkObject = AddNetworkObject(teleport.gameObject, $"{moon.MoonName}_{teleport.gameObject.name}_{teleport.EntranceID}_{teleport.EntrancePoint.position}");
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

            prefabHandler.AddNetworkPrefab(gameObject);
            prefabHandler.AddHandler(gameObject, new InactiveNetworkPrefabInstanceHandler(gameObject));
        }
    }
}
