using HarmonyLib;
using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using LethalExpansion.Utils;
using LethalSDK.ScriptableObjects;
using BepInEx.Bootstrap;
using LethalSDK.Component;
using Unity.AI.Navigation;
using Unity.Netcode.Components;
using UnityEngine.AI;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace LethalExpansion.Patches
{
    [HarmonyPatch(typeof(GameNetworkManager))]
    internal class GameNetworkManager_Patch
    {
        [HarmonyPatch("Start")]
        [HarmonyPrefix]
        static void Start_Prefix(GameNetworkManager __instance)
        {
            if (!LethalExpansion.CompatibleGameVersions.Contains(__instance.gameVersionNum))
            {
                bool showWarning = true;
                if (Chainloader.PluginInfos.ContainsKey("me.swipez.melonloader.morecompany") && __instance.gameVersionNum == 9999)
                {
                    showWarning = false;
                }

                if (showWarning)
                {
                    LethalExpansion.Log.LogWarning("Warning, this mod is not made for this Game Version, this could cause unexpected behaviors.");
                    LethalExpansion.Log.LogWarning(string.Format("Game version: {0}", __instance.gameVersionNum));
                    LethalExpansion.Log.LogWarning(string.Format("Compatible mod versions: {0}", string.Join(",", LethalExpansion.CompatibleGameVersions)));
                }
            }

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

            if (ConfigManager.Instance.FindItemValue<bool>("LoadModules"))
            {
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
                            if (!IsScrapCompatible(newScrap))
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

                        /*foreach (var newMoon in manifest.moons)
                        {
                            if (newMoon != null && newMoon.MainPrefab != null)
                            {
                                Whitelist.CheckAndRemoveIllegalComponents(newMoon.MainPrefab.transform, Whitelist.MoonPrefabComponentWhitelistUnused);
                            }
                        }*/

                        if (manifest.assetBank != null && manifest.assetBank.NetworkPrefabs() != null && manifest.assetBank.NetworkPrefabs().Length > 0)
                        {
                            foreach (var networkprefab in manifest.assetBank.NetworkPrefabs())
                            {
                                if (networkprefab.PrefabPath != null && networkprefab.PrefabPath.Length > 0)
                                {
                                    GameObject prefab = bundleKeyValue.Value.Item1.LoadAsset<GameObject>(networkprefab.PrefabPath);
                                    Whitelist.CheckAndRemoveIllegalComponents(bundleKeyValue.Value.Item1.LoadAsset<GameObject>(networkprefab.PrefabPath).transform, Whitelist.ScrapPrefabComponentWhitelist);
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

            /*LethalExpansion.Log.LogInfo("1");
            var objtest = AssetBundlesManager.Instance.mainAssetBundle.LoadAsset<GameObject>("Assets/Mods/LethalExpansion/Prefabs/itemshipanimcontainer.prefab");
            GameObject.DontDestroyOnLoad(objtest);
            __instance.GetComponent<NetworkManager>().PrefabHandler.AddNetworkPrefab(objtest);
            LethalExpansion.Log.LogInfo("2");*/
        }

        private static bool IsScrapCompatible(Scrap newScrap)
        {
            if (newScrap == null || newScrap.prefab == null)
            {
                return false;
            }

            if (newScrap.RequiredBundles != null && !AssetBundlesManager.Instance.BundlesLoaded(newScrap.RequiredBundles))
            {
                return false;
            }

            if (newScrap.IncompatibleBundles != null && AssetBundlesManager.Instance.IncompatibleBundlesLoaded(newScrap.IncompatibleBundles))
            {
                return false;
            }

            return true;
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

            Whitelist.CheckAndRemoveIllegalComponents(newScrap.prefab.transform, Whitelist.ScrapPrefabComponentWhitelist);
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
}
