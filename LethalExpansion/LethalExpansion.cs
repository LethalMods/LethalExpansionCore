using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LethalExpansion.Patches;
using LethalExpansion.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.Audio;
using DunGen;
using UnityEngine.UIElements;
using DunGen.Adapters;
using LethalSDK.Component;
using UnityEngine.AI;
using Unity.AI.Navigation;
using UnityEngine.Video;
using Unity.Netcode.Components;
using LethalSDK.Utils;
using BepInEx.Bootstrap;
using System.Collections;

namespace LethalExpansion
{
    [BepInPlugin(PluginGUID, PluginName, VersionString)]
    [BepInDependency("me.swipez.melonloader.morecompany", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("BrutalCompanyPlus", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("MoonOfTheDay", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("beeisyou.LandmineFix", BepInDependency.DependencyFlags.SoftDependency)]
    public class LethalExpansion : BaseUnityPlugin
    {
        private const string PluginGUID = "LethalExpansion";
        private const string PluginName = "LethalExpansion";
        private const string VersionString = "1.3.5";
        public static readonly Version ModVersion = new Version(VersionString);

        private readonly Version[] CompatibleModVersions = {
            new Version(1, 3, 5)
        };

        private readonly Dictionary<string, Compatibility> CompatibleMods = new Dictionary<string, Compatibility>
        {
            { "com.sinai.unityexplorer", Compatibility.Medium },
            { "HDLethalCompany", Compatibility.Good },
            { "LC_API", Compatibility.Good },
            { "me.swipez.melonloader.morecompany", Compatibility.Unknown },
            { "BrutalCompanyPlus", Compatibility.Unknown },
            { "MoonOfTheDay", Compatibility.Good },
            { "Television_Controller", Compatibility.Bad },
            { "beeisyou.LandmineFix", Compatibility.Perfect }
        };

        private enum Compatibility
        {
            Unknown = 0,
            Perfect = 1,
            Good = 2,
            Medium = 3,
            Bad = 4,
            Critical = 5,
            Incompatible = 6
        }

        List<BepInEx.PluginInfo> loadedPlugins = new List<BepInEx.PluginInfo>();

        public static readonly int[] CompatibleGameVersions = {45};

        public static bool sessionWaiting = true;
        public static bool hostDataWaiting = true;
        public static bool ishost = false;
        public static bool alreadypatched = false;
        public static bool weathersReadyToShare = false;
        public static bool isInGame = false;

        public static int delayedLevelChange = -1;

        public static string lastKickReason = string.Empty;

        private static readonly Harmony Harmony = new Harmony(PluginGUID);
        public static ManualLogSource Log = new ManualLogSource(PluginName);

        public static ConfigFile config;

        public static NetworkManager networkManager;

        public GameObject SpaceLight;
        public GameObject terrainfixer;
        public static Transform currentWaterSurface;

        private void Awake()
        {
            Log = Logger;
            Logger.LogInfo($"PluginName: {PluginName}, VersionString: {VersionString} is loading...");


            Logger.LogInfo("Getting other plugins list");
            loadedPlugins = GetLoadedPlugins();
            foreach (var plugin in loadedPlugins)
            {
                if (plugin.Metadata.GUID != PluginGUID)
                {
                    if (CompatibleMods.ContainsKey(plugin.Metadata.GUID))
                    {
                        Logger.LogInfo($"Plugin: {plugin.Metadata.Name} - Version: {plugin.Metadata.Version} - Compatibility: {CompatibleMods[plugin.Metadata.GUID]}");
                    }
                    else
                    {
                        Logger.LogInfo($"Plugin: {plugin.Metadata.Name} - Version: {plugin.Metadata.Version} - Compatibility: {Compatibility.Unknown}");
                    }
                }
            }

            config = Config;

            ConfigManager.Instance.AddItem(new ConfigItem("LoadModules", true, "Modules", "Load SDK Modules that add new content to the game. Disable it to play with Vanilla players. (RESTART REQUIRED)", sync:false, optional: false, requireRestart:true));

            ConfigManager.Instance.ReadConfig();

            Config.SettingChanged += ConfigSettingChanged;

            AssetBundlesManager.Instance.LoadAllAssetBundles();

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            Harmony.PatchAll(typeof(GameNetworkManager_Patch));
            Harmony.PatchAll(typeof(Terminal_Patch));
            Harmony.PatchAll(typeof(MenuManager_Patch));
            Harmony.PatchAll(typeof(RoundManager_Patch));
            Harmony.PatchAll(typeof(HUDManager_Patch));
            Harmony.PatchAll(typeof(StartOfRound_Patch));
            Harmony.PatchAll(typeof(EntranceTeleport_Patch));
            Harmony.PatchAll(typeof(AudioReverbTrigger_Patch));
            Harmony.PatchAll(typeof(InteractTrigger_Patch));
            Harmony.PatchAll(typeof(RuntimeDungeon));
            Harmony harmony = new Harmony("LethalExpansion");

            HDRenderPipelineAsset hdAsset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
            if (hdAsset != null)
            {
                var clonedSettings = hdAsset.currentPlatformRenderPipelineSettings;
                clonedSettings.supportWater = true;
                hdAsset.currentPlatformRenderPipelineSettings = clonedSettings;
                Logger.LogInfo("Water support applied to the HDRenderPipelineAsset.");
            }
            else
            {
                Logger.LogError("HDRenderPipelineAsset not found.");
            }

            Logger.LogInfo($"PluginName: {PluginName}, VersionString: {VersionString} is loaded.");
        }

        List<BepInEx.PluginInfo> GetLoadedPlugins()
        {
            return Chainloader.PluginInfos.Values.ToList();
        }

        private int width = 256;
        private int height = 256;
        private int depth = 20;
        private float scale = 20f;

        float[,] GenerateHeights()
        {
            float[,] heights = new float[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    heights[x, y] = CalculateHeight(x, y);
                }
            }
            return heights;
        }

        float CalculateHeight(int x, int y)
        {
            float xCoord = (float)x / width * scale;
            float yCoord = (float)y / height * scale;

            return Mathf.PerlinNoise(xCoord, yCoord);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo("Loading scene: " + scene.name);
            if (scene.name == "InitScene")
            {
                networkManager = GameObject.Find("NetworkManager").GetComponent<NetworkManager>();
            }
            else if (scene.name == "MainMenu")
            {
                OnMainMenuLoaded(scene);
            }
            else if (scene.name == "CompanyBuilding")
            {
                /*
                GameObject Labyrinth = Instantiate(AssetBundlesManager.Instance.mainAssetBundle.LoadAsset<GameObject>("Assets/Mods/LethalExpansion/Prefabs/labyrinth.prefab"));
                SceneManager.MoveGameObjectToScene(Labyrinth, scene);
                */

                SpaceLight.SetActive(false);
                terrainfixer.SetActive(false);
            }
            else if (scene.name == "SampleSceneRelay")
            {
                OnSampleSceneRelayLoaded(scene);
            }
            else if (scene.name.StartsWith("Level"))
            {
                SpaceLight.SetActive(false);
                terrainfixer.SetActive(false);
            }
            else if (scene.name == "InitSceneLaunchOptions" && isInGame)
            {
                OnInitSceneLaunchOptionsLoaded(scene);
            }
        }

        void OnMainMenuLoaded(Scene scene)
        {
            sessionWaiting = true;
            hostDataWaiting = true;
            ishost = false;
            alreadypatched = false;

            LethalExpansion.delayedLevelChange = -1;

            isInGame = false;

            AssetGather.Instance.AddAudioMixer(GameObject.Find("Canvas/MenuManager").GetComponent<AudioSource>().outputAudioMixerGroup.audioMixer);

            if (lastKickReason != null && lastKickReason.Length > 0)
            {
                Console.WriteLine($"You have been kicked\r\nReason: {lastKickReason}");
            }
        }

        void OnSampleSceneRelayLoaded(Scene scene)
        {
            SpaceLight = Instantiate(AssetBundlesManager.Instance.mainAssetBundle.LoadAsset<GameObject>("Assets/Mods/LethalExpansion/Prefabs/SpaceLight.prefab"));
            SceneManager.MoveGameObjectToScene(SpaceLight, scene);

            Mesh FixedMonitorWallMesh = AssetBundlesManager.Instance.mainAssetBundle.LoadAsset<GameObject>("Assets/Mods/LethalExpansion/Meshes/MonitorWall.fbx").GetComponent<MeshFilter>().mesh;
            GameObject MonitorWall = GameObject.Find("Environment/HangarShip/ShipModels2b/MonitorWall/Cube");
            MonitorWall.GetComponent<MeshFilter>().mesh = FixedMonitorWallMesh;

            MeshRenderer MonitorWallMeshRenderer = MonitorWall.GetComponent<MeshRenderer>();

            GameObject waterSurface = GameObject.Instantiate(GameObject.Find("Systems/GameSystems/TimeAndWeather/Flooding"));
            Destroy(waterSurface.GetComponent<FloodWeather>());
            waterSurface.name = "WaterSurface";
            waterSurface.transform.position = Vector3.zero;
            waterSurface.transform.Find("Water").GetComponent<MeshFilter>().sharedMesh = null;
            SpawnPrefab.Instance.waterSurface = waterSurface;

            /*Material BlueScreenMaterial = new Material(MonitorWallMeshRenderer.materials[1]);
            BlueScreenMaterial.SetColor("_BaseColor", new Color32(0,0,80, 255));*/

            Material[] materialArray = new Material[9];
            materialArray[0] = MonitorWallMeshRenderer.materials[0];
            materialArray[1] = MonitorWallMeshRenderer.materials[1];
            materialArray[2] = MonitorWallMeshRenderer.materials[1];
            //materialArray[2] = BlueScreenMaterial;
            materialArray[3] = MonitorWallMeshRenderer.materials[1];
            materialArray[4] = MonitorWallMeshRenderer.materials[1];
            //materialArray[4] = BlueScreenMaterial;
            materialArray[5] = MonitorWallMeshRenderer.materials[1];
            materialArray[6] = MonitorWallMeshRenderer.materials[1];
            materialArray[7] = MonitorWallMeshRenderer.materials[1];
            materialArray[8] = MonitorWallMeshRenderer.materials[2];

            MonitorWallMeshRenderer.materials = materialArray;

            StartOfRound.Instance.screenLevelDescription.gameObject.AddComponent<AutoScrollText>();

            /*MonitorWall.transform.Find("Canvas (1)/MainContainer/BG").gameObject.SetActive(false);
            MonitorWall.transform.Find("Canvas (1)/MainContainer/BG (1)").gameObject.SetActive(false);*/

            AssetGather.Instance.AddAudioMixer(GameObject.Find("Systems/Audios/DiageticBackground").GetComponent<AudioSource>().outputAudioMixerGroup.audioMixer);

            terrainfixer = new GameObject();
            terrainfixer.name = "terrainfixer";
            terrainfixer.transform.position = new Vector3(0, -500, 0);
            Terrain terrain = terrainfixer.AddComponent<Terrain>();
            TerrainData terrainData = new TerrainData();
            terrainData.heightmapResolution = width + 1;
            terrainData.size = new Vector3(width, depth, height);
            terrainData.SetHeights(0, 0, GenerateHeights());
            terrain.terrainData = terrainData;

            Terminal_Patch.ResetFireExitAmounts();

            UnityEngine.Object[] array = Resources.FindObjectsOfTypeAll(typeof(Volume));

            for (int i = 0; i < array.Length; i++)
            {
                if ((array[i] as Volume).sharedProfile == null)
                {
                    (array[i] as Volume).sharedProfile = AssetBundlesManager.Instance.mainAssetBundle.LoadAsset<VolumeProfile>("Assets/Mods/LethalExpansion/Sky and Fog Global Volume Profile.asset");
                }
            }

            waitForSession().GetAwaiter();

            isInGame = true;
        }

        void OnInitSceneLaunchOptionsLoaded(Scene scene)
        {
            SpaceLight.SetActive(false);
            terrainfixer.SetActive(false);
            foreach (GameObject obj in scene.GetRootGameObjects())
            {
                obj.SetActive(false);
            }

            //StartCoroutine(LoadCustomMoon(scene));
            LoadCustomMoon2(scene);

            String[] _tmp = { "MapPropsContainer", "OutsideAINode", "SpawnDenialPoint", "ItemShipLandingNode", "OutsideLevelNavMesh" };
            foreach (string s in _tmp)
            {
                if (GameObject.FindGameObjectWithTag(s) == null || GameObject.FindGameObjectsWithTag(s).Any(o => o.scene.name != "InitSceneLaunchOptions"))
                {
                    GameObject obj = new GameObject();
                    obj.name = s;
                    obj.tag = s;
                    obj.transform.position = new Vector3(0, -200, 0);
                    SceneManager.MoveGameObjectToScene(obj, scene);
                }
            }

            GameObject DropShip = GameObject.Find("ItemShipAnimContainer");
            if (DropShip != null)
            {
                var ItemShip = DropShip.transform.Find("ItemShip");
                if (ItemShip != null)
                {
                    ItemShip.GetComponent<AudioSource>().outputAudioMixerGroup = AssetGather.Instance.audioMixers.ContainsKey("Diagetic") ? AssetGather.Instance.audioMixers["Diagetic"].Item2.First(a => a.name == "Master") : null;
                }

                var ItemShipMusicClose = DropShip.transform.Find("ItemShip/Music");
                if (ItemShipMusicClose != null)
                {
                    ItemShipMusicClose.GetComponent<AudioSource>().outputAudioMixerGroup = AssetGather.Instance.audioMixers.ContainsKey("Diagetic") ? AssetGather.Instance.audioMixers["Diagetic"].Item2.First(a => a.name == "Master") : null;
                }

                var ItemShipMusicFar = DropShip.transform.Find("ItemShip/Music/Music (1)");
                if (ItemShipMusicFar != null)
                {
                    ItemShipMusicFar.GetComponent<AudioSource>().outputAudioMixerGroup = AssetGather.Instance.audioMixers.ContainsKey("Diagetic") ? AssetGather.Instance.audioMixers["Diagetic"].Item2.First(a => a.name == "Master") : null;
                }
            }

            RuntimeDungeon runtimeDungeon = GameObject.FindObjectOfType<RuntimeDungeon>(false);
            if (runtimeDungeon == null)
            {
                GameObject dungeonGenerator = new GameObject();
                dungeonGenerator.name = "DungeonGenerator";
                dungeonGenerator.tag = "DungeonGenerator";
                dungeonGenerator.transform.position = new Vector3(0, -200, 0);
                runtimeDungeon = dungeonGenerator.AddComponent<RuntimeDungeon>();
                runtimeDungeon.Generator.DungeonFlow = RoundManager.Instance.dungeonFlowTypes[0];
                runtimeDungeon.Generator.LengthMultiplier = 0.8f;
                runtimeDungeon.Generator.PauseBetweenRooms = 0.2f;
                runtimeDungeon.GenerateOnStart = false;
                runtimeDungeon.Root = dungeonGenerator;
                runtimeDungeon.Generator.DungeonFlow = RoundManager.Instance.dungeonFlowTypes[0];
                UnityNavMeshAdapter dungeonNavMesh = dungeonGenerator.AddComponent<UnityNavMeshAdapter>();
                dungeonNavMesh.BakeMode = UnityNavMeshAdapter.RuntimeNavMeshBakeMode.FullDungeonBake;
                dungeonNavMesh.LayerMask = 35072; //256 + 2048 + 32768 = 35072
                SceneManager.MoveGameObjectToScene(dungeonGenerator, scene);
            }
            else
            {
                if (runtimeDungeon.Generator.DungeonFlow == null)
                {
                    runtimeDungeon.Generator.DungeonFlow = RoundManager.Instance.dungeonFlowTypes[0];
                }
            }

            runtimeDungeon.Generator.DungeonFlow.GlobalProps.First(p => p.ID == 1231).Count = new IntRange(RoundManager.Instance.currentLevel.GetFireExitAmountOverwrite(), RoundManager.Instance.currentLevel.GetFireExitAmountOverwrite());

            GameObject OutOfBounds = GameObject.CreatePrimitive(PrimitiveType.Cube);
            OutOfBounds.name = "OutOfBounds";
            OutOfBounds.layer = 13;
            OutOfBounds.transform.position = new Vector3(0, -300, 0);
            OutOfBounds.transform.localScale = new Vector3(1000, 5, 1000);
            BoxCollider boxCollider = OutOfBounds.GetComponent<BoxCollider>();
            boxCollider.isTrigger = true;
            OutOfBounds.AddComponent<OutOfBoundsTrigger>();
            Rigidbody rigidbody = OutOfBounds.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
            SceneManager.MoveGameObjectToScene(OutOfBounds, scene);
        }

        void LoadCustomMoon2(Scene scene)
        {
            GameObject moonPrefab = Terminal_Patch.newMoons[StartOfRound.Instance.currentLevelID].MainPrefab;
            if (moonPrefab == null || moonPrefab.transform == null)
            {
                return;
            }

            Whitelist.CheckAndRemoveIllegalComponents(moonPrefab.transform, Whitelist.MoonPrefabComponentWhitelist);
            GameObject mainPrefab = GameObject.Instantiate(moonPrefab);
            if (mainPrefab == null)
            {
                return;
            }

            currentWaterSurface = mainPrefab.transform.Find("Environment/Water");
            SceneManager.MoveGameObjectToScene(mainPrefab, scene);

            var diageticBackground = mainPrefab.transform.Find("Systems/Audio/DiageticBackground");
            if (diageticBackground != null)
            {
                diageticBackground.GetComponent<AudioSource>().outputAudioMixerGroup = AssetGather.Instance.audioMixers.ContainsKey("Diagetic") ? AssetGather.Instance.audioMixers["Diagetic"].Item2.First(a => a.name == "Master") : null;
            }

            Terrain[] terrains = mainPrefab.GetComponentsInChildren<Terrain>();
            if (terrains != null && terrains.Length > 0)
            {
                foreach (Terrain terrain in terrains)
                {
                    terrain.drawInstanced = true;
                }
            }
        }

        IEnumerator LoadCustomMoon(Scene scene)
        {
            yield return null;

            LoadCustomMoon2(scene);

            String[] _tmp = { "MapPropsContainer", "OutsideAINode", "SpawnDenialPoint", "ItemShipLandingNode", "OutsideLevelNavMesh" };
            foreach (string s in _tmp)
            {
                if (GameObject.FindGameObjectWithTag(s) == null || GameObject.FindGameObjectsWithTag(s).Any(o => o.scene.name != "InitSceneLaunchOptions"))
                {
                    GameObject obj = new GameObject();
                    obj.name = s;
                    obj.tag = s;
                    obj.transform.position = new Vector3(0, -200, 0);
                    SceneManager.MoveGameObjectToScene(obj, scene);
                }
            }

            GameObject DropShip = GameObject.Find("ItemShipAnimContainer");
            if (DropShip != null)
            {
                var ItemShip = DropShip.transform.Find("ItemShip");
                if (ItemShip != null)
                {
                    ItemShip.GetComponent<AudioSource>().outputAudioMixerGroup = AssetGather.Instance.audioMixers.ContainsKey("Diagetic") ? AssetGather.Instance.audioMixers["Diagetic"].Item2.First(a => a.name == "Master") : null;
                }

                var ItemShipMusicClose = DropShip.transform.Find("ItemShip/Music");
                if (ItemShipMusicClose != null)
                {
                    ItemShipMusicClose.GetComponent<AudioSource>().outputAudioMixerGroup = AssetGather.Instance.audioMixers.ContainsKey("Diagetic") ? AssetGather.Instance.audioMixers["Diagetic"].Item2.First(a => a.name == "Master") : null;
                }

                var ItemShipMusicFar = DropShip.transform.Find("ItemShip/Music/Music (1)");
                if (ItemShipMusicFar != null)
                {
                    ItemShipMusicFar.GetComponent<AudioSource>().outputAudioMixerGroup = AssetGather.Instance.audioMixers.ContainsKey("Diagetic") ? AssetGather.Instance.audioMixers["Diagetic"].Item2.First(a => a.name == "Master") : null;
                }
            }

            RuntimeDungeon runtimeDungeon = GameObject.FindObjectOfType<RuntimeDungeon>(false);
            if (runtimeDungeon == null)
            {
                GameObject dungeonGenerator = new GameObject();
                dungeonGenerator.name = "DungeonGenerator";
                dungeonGenerator.tag = "DungeonGenerator";
                dungeonGenerator.transform.position = new Vector3(0, -200, 0);
                runtimeDungeon = dungeonGenerator.AddComponent<RuntimeDungeon>();
                runtimeDungeon.Generator.DungeonFlow = RoundManager.Instance.dungeonFlowTypes[0];
                runtimeDungeon.Generator.LengthMultiplier = 0.8f;
                runtimeDungeon.Generator.PauseBetweenRooms = 0.2f;
                runtimeDungeon.GenerateOnStart = false;
                runtimeDungeon.Root = dungeonGenerator;
                runtimeDungeon.Generator.DungeonFlow = RoundManager.Instance.dungeonFlowTypes[0];
                UnityNavMeshAdapter dungeonNavMesh = dungeonGenerator.AddComponent<UnityNavMeshAdapter>();
                dungeonNavMesh.BakeMode = UnityNavMeshAdapter.RuntimeNavMeshBakeMode.FullDungeonBake;
                dungeonNavMesh.LayerMask = 35072; //256 + 2048 + 32768 = 35072
                SceneManager.MoveGameObjectToScene(dungeonGenerator, scene);
            }
            else
            {
                if (runtimeDungeon.Generator.DungeonFlow == null)
                {
                    runtimeDungeon.Generator.DungeonFlow = RoundManager.Instance.dungeonFlowTypes[0];
                }
            }

            runtimeDungeon.Generator.DungeonFlow.GlobalProps.First(p => p.ID == 1231).Count = new IntRange(RoundManager.Instance.currentLevel.GetFireExitAmountOverwrite(), RoundManager.Instance.currentLevel.GetFireExitAmountOverwrite());

            GameObject OutOfBounds = GameObject.CreatePrimitive(PrimitiveType.Cube);
            OutOfBounds.name = "OutOfBounds";
            OutOfBounds.layer = 13;
            OutOfBounds.transform.position = new Vector3(0, -300, 0);
            OutOfBounds.transform.localScale = new Vector3(1000, 5, 1000);
            BoxCollider boxCollider = OutOfBounds.GetComponent<BoxCollider>();
            boxCollider.isTrigger = true;
            OutOfBounds.AddComponent<OutOfBoundsTrigger>();
            Rigidbody rigidbody = OutOfBounds.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
            SceneManager.MoveGameObjectToScene(OutOfBounds, scene);
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (scene.name.Length > 0)
            {
                Logger.LogInfo("Unloading scene: " + scene.name);
            }

            if (scene.name.StartsWith("Level") || scene.name == "CompanyBuilding" || (scene.name == "InitSceneLaunchOptions" && isInGame))
            {
                if (SpaceLight != null)
                {
                    SpaceLight.SetActive(true);
                }

                if (currentWaterSurface != null)
                {
                    currentWaterSurface = null;
                }

                Terminal_Patch.ResetFireExitAmounts();
            }
        }

        private async Task waitForSession()
        {
            while (sessionWaiting)
            {
                await Task.Delay(1000);
            }

            if (!ishost)
            {
                while (!sessionWaiting && hostDataWaiting)
                {
                    NetworkPacketManager.Instance.SendPacket(NetworkPacketManager.PacketType.Request, "hostconfig", string.Empty, 0);
                    await Task.Delay(3000);
                }
            }
            else
            {
                for (int i = 0; i < ConfigManager.Instance.GetAll().Count; i++)
                {
                    if (ConfigManager.Instance.MustBeSync(i))
                    {
                        ConfigManager.Instance.SetItemValue(i, ConfigManager.Instance.FindEntryValue(i));
                    }
                }
            }

            if (!alreadypatched)
            {
                Terminal_Patch.MainPatch(GameObject.Find("TerminalScript").GetComponent<Terminal>());
                alreadypatched = true;
            }
        }

        private void ConfigSettingChanged(object sender, EventArgs e)
        {
            SettingChangedEventArgs settingChangedEventArgs = e as SettingChangedEventArgs;

            if (settingChangedEventArgs == null)
            {
                return;
            }

            Log.LogInfo(string.Format("{0} Changed to {1}", settingChangedEventArgs.ChangedSetting.Definition.Key, settingChangedEventArgs.ChangedSetting.BoxedValue));
        }
    }
}
