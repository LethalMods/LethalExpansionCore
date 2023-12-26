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
                            if (newScrap == null || newScrap.prefab == null)
                            {
                                continue;
                            }

                            if (newScrap.RequiredBundles != null && !AssetBundlesManager.Instance.BundlesLoaded(newScrap.RequiredBundles))
                            {
                                continue;
                            }

                            if (newScrap.IncompatibleBundles != null && AssetBundlesManager.Instance.IncompatibleBundlesLoaded(newScrap.IncompatibleBundles))
                            {
                                continue;
                            }

                            Item scrapItem = ScriptableObject.CreateInstance<Item>();
                            scrapItem.name = newScrap.name;
                            scrapItem.itemName = newScrap.itemName;
                            scrapItem.canBeGrabbedBeforeGameStart = true;
                            scrapItem.isScrap = true;
                            scrapItem.minValue = newScrap.minValue;
                            scrapItem.maxValue = newScrap.maxValue;
                            scrapItem.weight = (float)newScrap.weight / 100 + 1;

                            CheckAndRemoveIllegalComponents(newScrap.prefab.transform, scrapPrefabComponentWhitelist);
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

                        /*foreach (var newMoon in bundle2.Item2.moons)
                        {
                            if (newMoon != null && newMoon.MainPrefab != null)
                            {
                                CheckAndRemoveIllegalComponents(newMoon.MainPrefab.transform, moonPrefabComponentWhitelist);
                            }
                        }*/

                        if (manifest.assetBank != null && manifest.assetBank.NetworkPrefabs() != null && manifest.assetBank.NetworkPrefabs().Length > 0)
                        {
                            foreach (var networkprefab in manifest.assetBank.NetworkPrefabs())
                            {
                                if (networkprefab.PrefabPath != null && networkprefab.PrefabPath.Length > 0)
                                {
                                    GameObject prefab = bundleKeyValue.Value.Item1.LoadAsset<GameObject>(networkprefab.PrefabPath);
                                    CheckAndRemoveIllegalComponents(bundleKeyValue.Value.Item1.LoadAsset<GameObject>(networkprefab.PrefabPath).transform, scrapPrefabComponentWhitelist);
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

        private static List<Type> scrapPrefabComponentWhitelist = new List<Type> {
            //Base
            typeof(Transform),
            //Mesh
            typeof(MeshFilter),
            typeof(MeshRenderer),
            typeof(SkinnedMeshRenderer),
            //Physics
            typeof(MeshCollider),
            typeof(BoxCollider),
            typeof(SphereCollider),
            typeof(CapsuleCollider),
            typeof(SphereCollider),
            typeof(TerrainCollider),
            typeof(WheelCollider),
            typeof(ArticulationBody),
            typeof(ConstantForce),
            typeof(ConfigurableJoint),
            typeof(FixedJoint),
            typeof(HingeJoint),
            typeof(Cloth),
            typeof(Rigidbody),
            //Netcode
            typeof(NetworkObject),
            typeof(NetworkRigidbody),
            typeof(NetworkTransform),
            typeof(NetworkAnimator),
            //Animation
            typeof(Animator),
            typeof(Animation),
            //Rendering
            typeof(DecalProjector),
            typeof(LODGroup),
            typeof(Light),
            typeof(HDAdditionalLightData),
            typeof(LightProbeGroup),
            typeof(LightProbeProxyVolume),
            typeof(LocalVolumetricFog),
            typeof(OcclusionArea),
            typeof(OcclusionPortal),
            typeof(ReflectionProbe),
            typeof(PlanarReflectionProbe),
            typeof(HDAdditionalReflectionData),
            typeof(SortingGroup),
            typeof(SpriteRenderer),
            //Audio
            typeof(AudioSource),
            typeof(AudioReverbZone),
            typeof(AudioReverbFilter),
            typeof(AudioChorusFilter),
            typeof(AudioDistortionFilter),
            typeof(AudioEchoFilter),
            typeof(AudioHighPassFilter),
            typeof(AudioLowPassFilter),
            typeof(AudioListener),
            //Effect
            typeof(LensFlare),
            typeof(TrailRenderer),
            typeof(LineRenderer),
            typeof(ParticleSystem),
            typeof(ParticleSystemRenderer),
            typeof(ParticleSystemForceField),
            //Video
            typeof(VideoPlayer)
        };

        private static List<Type> moonPrefabComponentWhitelist = new List<Type> {
            //Base
            typeof(Transform),
            //Mesh
            typeof(MeshFilter),
            typeof(MeshRenderer),
            typeof(SkinnedMeshRenderer),
            //Physics
            typeof(MeshCollider),
            typeof(BoxCollider),
            typeof(SphereCollider),
            typeof(CapsuleCollider),
            typeof(SphereCollider),
            typeof(TerrainCollider),
            typeof(WheelCollider),
            typeof(ArticulationBody),
            typeof(ConstantForce),
            typeof(ConfigurableJoint),
            typeof(FixedJoint),
            typeof(HingeJoint),
            typeof(Cloth),
            typeof(Rigidbody),
            //Netcode
            typeof(NetworkObject),
            typeof(NetworkRigidbody),
            typeof(NetworkTransform),
            typeof(NetworkAnimator),
            //Animation
            typeof(Animator),
            typeof(Animation),
            //Terrain
            typeof(Terrain),
            typeof(Tree),
            typeof(WindZone),
            //Rendering
            typeof(DecalProjector),
            typeof(LODGroup),
            typeof(Light),
            typeof(HDAdditionalLightData),
            typeof(LightProbeGroup),
            typeof(LightProbeProxyVolume),
            typeof(LocalVolumetricFog),
            typeof(OcclusionArea),
            typeof(OcclusionPortal),
            typeof(ReflectionProbe),
            typeof(PlanarReflectionProbe),
            typeof(HDAdditionalReflectionData),
            typeof(Skybox),
            typeof(SortingGroup),
            typeof(SpriteRenderer),
            typeof(Volume),
            //Audio
            typeof(AudioSource),
            typeof(AudioReverbZone),
            typeof(AudioReverbFilter),
            typeof(AudioChorusFilter),
            typeof(AudioDistortionFilter),
            typeof(AudioEchoFilter),
            typeof(AudioHighPassFilter),
            typeof(AudioLowPassFilter),
            typeof(AudioListener),
            //Effect
            typeof(LensFlare),
            typeof(TrailRenderer),
            typeof(LineRenderer),
            typeof(ParticleSystem),
            typeof(ParticleSystemRenderer),
            typeof(ParticleSystemForceField),
            typeof(Projector),
            //Video
            typeof(VideoPlayer),
            //Navigation
            typeof(NavMeshSurface),
            typeof(NavMeshModifier),
            typeof(NavMeshModifierVolume),
            typeof(NavMeshLink),
            typeof(NavMeshObstacle),
            typeof(OffMeshLink),
            //LethalSDK
            typeof(SI_AudioReverbPresets),
            typeof(SI_AudioReverbTrigger),
            typeof(SI_DungeonGenerator),
            typeof(SI_MatchLocalPlayerPosition),
            typeof(SI_AnimatedSun),
            typeof(SI_EntranceTeleport),
            typeof(SI_ScanNode),
            typeof(SI_DoorLock),
            typeof(SI_WaterSurface),
            typeof(SI_Ladder),
            typeof(SI_ItemDropship),
            typeof(SI_InteractTrigger),
            typeof(SI_DamagePlayer),
            typeof(SI_SoundYDistance),
            typeof(SI_AudioOutputInterface),
            typeof(PlayerShip)
        };

        static void CheckAndRemoveIllegalComponents(Transform prefab, List<Type> whitelist)
        {
            try
            {
                var components = prefab.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (!whitelist.Any(whitelistType => component.GetType() == whitelistType))
                    {
                        LethalExpansion.Log.LogWarning($"Removed illegal {component.GetType().Name} component.");
                        GameObject.Destroy(component);
                    }
                }

                foreach (Transform child in prefab)
                {
                    CheckAndRemoveIllegalComponents(child, whitelist);
                }
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError(ex.Message);
            }
        }
    }
}
