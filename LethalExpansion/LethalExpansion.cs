using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using LethalExpansion.Patches;
using LethalExpansion.Utils;
using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.Audio;
using DunGen;
using DunGen.Adapters;
using LethalSDK.Utils;

namespace LethalExpansion
{
    [BepInPlugin(PluginGUID, PluginName, VersionString)]
    // TODO: Figure out if these are still necessary
    [BepInDependency("me.swipez.melonloader.morecompany", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("BrutalCompanyPlus", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("MoonOfTheDay", BepInDependency.DependencyFlags.SoftDependency)]
    public class LethalExpansion : BaseUnityPlugin
    {
        private const string PluginGUID = "LethalExpansionCore";
        private const string PluginName = "LethalExpansionCore";
        private const string VersionString = "1.3.5";
        public static readonly Version ModVersion = new Version(VersionString);

        // Both Orion and Aquatis "require" templatemod (seemingly they reference scrap added by it)
        // but it loads and play just fine without it
        public static ConfigEntry<bool> IgnoreRequiredBundles;

        public static bool sessionWaiting = true;
        public static bool alreadyPatched = false;
        public static bool isInGame = false;

        public static int delayedLevelChange = -1;

        private static readonly Harmony harmony = new Harmony(PluginGUID);
        public static ManualLogSource Log = new ManualLogSource(PluginName);

        public static NetworkManager networkManager;

        // TODO: What are these two for?
        public GameObject terrainFixer;
        public static Transform currentWaterSurface;

        private void Awake()
        {
            Log = Logger;
            Logger.LogInfo($"PluginName: {PluginName}, VersionString: {VersionString} is loading...");

            IgnoreRequiredBundles = Config.Bind<bool>("Core", "IgnoreRequiredBundles", true, "Whether or not to allow a bundle to load without its required bundles");

            AssetBundlesManager.Instance.LoadAllAssetBundles();

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            harmony.PatchAll(typeof(GameNetworkManager_Patch));
            harmony.PatchAll(typeof(Terminal_Patch));
            harmony.PatchAll(typeof(MenuManager_Patch));
            harmony.PatchAll(typeof(RoundManager_Patch));
            harmony.PatchAll(typeof(StartOfRound_Patch));
            harmony.PatchAll(typeof(EntranceTeleport_Patch));
            harmony.PatchAll(typeof(AudioReverbTrigger_Patch));
            harmony.PatchAll(typeof(InteractTrigger_Patch));
            harmony.PatchAll(typeof(RuntimeDungeon));

            // TODO: What is this for?
            HDRenderPipelineAsset hdAsset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
            if (hdAsset != null)
            {
                var clonedSettings = hdAsset.currentPlatformRenderPipelineSettings;
                clonedSettings.supportWater = true;
                hdAsset.currentPlatformRenderPipelineSettings = clonedSettings;
                LethalExpansion.Log.LogInfo("Water support applied to the HDRenderPipelineAsset.");
            }
            else
            {
                LethalExpansion.Log.LogError("HDRenderPipelineAsset not found.");
            }

            LethalExpansion.Log.LogInfo($"PluginName: {PluginName}, VersionString: {VersionString} is loaded.");
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
            LethalExpansion.Log.LogInfo($"Loading scene: {scene.name}");

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
                terrainFixer.SetActive(false);
            }
            else if (scene.name == "SampleSceneRelay")
            {
                OnSampleSceneRelayLoaded(scene);
            }
            else if (scene.name.StartsWith("Level"))
            {
                terrainFixer.SetActive(false);
            }
            else if (scene.name == "InitSceneLaunchOptions" && isInGame)
            {
                OnInitSceneLaunchOptionsLoaded(scene);
            }
        }

        void OnMainMenuLoaded(Scene scene)
        {
            sessionWaiting = true;
            alreadyPatched = false;

            LethalExpansion.delayedLevelChange = -1;

            isInGame = false;

            AssetGather.Instance.AddAudioMixer(GameObject.Find("Canvas/MenuManager").GetComponent<AudioSource>().outputAudioMixerGroup.audioMixer);
        }

        void OnSampleSceneRelayLoaded(Scene scene)
        {
            // TODO: What is this for?
            GameObject waterSurface = GameObject.Instantiate(GameObject.Find("Systems/GameSystems/TimeAndWeather/Flooding"));
            Destroy(waterSurface.GetComponent<FloodWeather>());
            waterSurface.name = "WaterSurface";
            waterSurface.transform.position = Vector3.zero;
            waterSurface.transform.Find("Water").GetComponent<MeshFilter>().sharedMesh = null;
            SpawnPrefab.Instance.waterSurface = waterSurface;

            // Ship monitor auto scrolling for long texts
            StartOfRound.Instance.screenLevelDescription.gameObject.AddComponent<AutoScrollText>();

            // TODO: What is this for?
            AssetGather.Instance.AddAudioMixer(GameObject.Find("Systems/Audios/DiageticBackground").GetComponent<AudioSource>().outputAudioMixerGroup.audioMixer);

            // TODO: What is this for?
            SetupTerrainFixer();

            Terminal_Patch.ResetFireExitAmounts();

            // TODO: What is this for?
            UnityEngine.Object[] volumes = Resources.FindObjectsOfTypeAll(typeof(Volume));
            for (int i = 0; i < volumes.Length; i++)
            {
                Volume volume = volumes[i] as Volume;
                if (volume.sharedProfile != null)
                {
                    continue;
                }
                
                volume.sharedProfile = AssetBundlesManager.Instance.mainAssetBundle.LoadAsset<VolumeProfile>("Assets/Mods/LethalExpansion/Sky and Fog Global Volume Profile.asset");
            }

            WaitForSession().GetAwaiter();

            isInGame = true;
        }

        void SetupTerrainFixer()
        {
            terrainFixer = new GameObject();
            terrainFixer.name = "terrainfixer";
            terrainFixer.transform.position = new Vector3(0, -500, 0);

            TerrainData terrainData = new TerrainData();
            terrainData.heightmapResolution = width + 1;
            terrainData.size = new Vector3(width, depth, height);
            terrainData.SetHeights(0, 0, GenerateHeights());

            Terrain terrain = terrainFixer.AddComponent<Terrain>();
            terrain.terrainData = terrainData;
        }

        void OnInitSceneLaunchOptionsLoaded(Scene scene)
        {
            terrainFixer.SetActive(false);
            foreach (GameObject obj in scene.GetRootGameObjects())
            {
                obj.SetActive(false);
            }

            // StartCoroutine(LoadCustomMoon(scene));
            LoadCustomMoon(scene);

            String[] requiredObjectTags = { "MapPropsContainer", "OutsideAINode", "SpawnDenialPoint", "ItemShipLandingNode", "OutsideLevelNavMesh" };
            foreach (string requiredObjectTag in requiredObjectTags)
            {
                bool missingGameObject = GameObject.FindGameObjectWithTag(requiredObjectTag) == null || GameObject.FindGameObjectsWithTag(requiredObjectTag).Any(o => o.scene.name != "InitSceneLaunchOptions");
                if (!missingGameObject)
                {
                    continue;
                }

                GameObject requiredObject = new GameObject();
                requiredObject.name = requiredObjectTag;
                requiredObject.tag = requiredObjectTag;
                requiredObject.transform.position = new Vector3(0, -200, 0);

                SceneManager.MoveGameObjectToScene(requiredObject, scene);
                LethalExpansion.Log.LogInfo($"Added required object with tag: {requiredObjectTag}");
            }

            GameObject dropShip = GameObject.Find("ItemShipAnimContainer");
            if (dropShip != null)
            {
                Transform itemShip = dropShip.transform.Find("ItemShip");
                if (itemShip != null)
                {
                    itemShip.GetComponent<AudioSource>().outputAudioMixerGroup = GetDiageticMasterAudioMixer();
                }

                Transform itemShipMusicClose = dropShip.transform.Find("ItemShip/Music");
                if (itemShipMusicClose != null)
                {
                    itemShipMusicClose.GetComponent<AudioSource>().outputAudioMixerGroup = GetDiageticMasterAudioMixer();
                }

                Transform itemShipMusicFar = dropShip.transform.Find("ItemShip/Music/Music (1)");
                if (itemShipMusicFar != null)
                {
                    itemShipMusicFar.GetComponent<AudioSource>().outputAudioMixerGroup = GetDiageticMasterAudioMixer();
                }
            }

            // TODO: What is this for?
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
                dungeonNavMesh.LayerMask = 35072; // 256 + 2048 + 32768 = 35072 (What does each of these correspond to?)

                SceneManager.MoveGameObjectToScene(dungeonGenerator, scene);
            }
            else
            {
                if (runtimeDungeon.Generator.DungeonFlow == null)
                {
                    runtimeDungeon.Generator.DungeonFlow = RoundManager.Instance.dungeonFlowTypes[0];
                }
            }

            // TODO: What is thie purpose of this?
            // Is it to force an exact amount of fire exits? but why?
            int fireExists = RoundManager.Instance.currentLevel.GetFireExitAmountOverwrite();
            runtimeDungeon.Generator.DungeonFlow.GlobalProps.First(p => p.ID == 1231).Count = new IntRange(fireExists, fireExists);

            GameObject outOfBounds = GameObject.CreatePrimitive(PrimitiveType.Cube);
            outOfBounds.name = "OutOfBounds";
            outOfBounds.layer = 13;
            outOfBounds.transform.position = new Vector3(0, -300, 0);
            outOfBounds.transform.localScale = new Vector3(1000, 5, 1000);

            BoxCollider boxCollider = outOfBounds.GetComponent<BoxCollider>();
            boxCollider.isTrigger = true;

            outOfBounds.AddComponent<OutOfBoundsTrigger>();

            Rigidbody rigidbody = outOfBounds.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;

            SceneManager.MoveGameObjectToScene(outOfBounds, scene);
        }

        private AudioMixerGroup GetDiageticMasterAudioMixer()
        {
            if (!AssetGather.Instance.audioMixers.TryGetValue("Diagetic", out var tuple))
            {
                return null;
            }

            return tuple.Item2.First(a => a.name == "Master");
        }

        private void LoadCustomMoon(Scene scene)
        {
            GameObject moonPrefab = Terminal_Patch.newMoons[StartOfRound.Instance.currentLevelID].MainPrefab;
            if (moonPrefab == null || moonPrefab.transform == null)
            {
                return;
            }

            ComponentWhitelist.CheckAndRemoveIllegalComponents(moonPrefab.transform, ComponentWhitelist.MoonPrefabComponentWhitelist);
            GameObject mainPrefab = GameObject.Instantiate(moonPrefab);
            if (mainPrefab == null)
            {
                return;
            }

            currentWaterSurface = mainPrefab.transform.Find("Environment/Water");
            SceneManager.MoveGameObjectToScene(mainPrefab, scene);

            Transform diageticBackground = mainPrefab.transform.Find("Systems/Audio/DiageticBackground");
            if (diageticBackground != null)
            {
                diageticBackground.GetComponent<AudioSource>().outputAudioMixerGroup = GetDiageticMasterAudioMixer();
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

        private void OnSceneUnloaded(Scene scene)
        {
            if (scene.name.Length > 0)
            {
                LethalExpansion.Log.LogInfo($"Unloading scene: {scene.name}");
            }

            if (scene.name.StartsWith("Level") || scene.name == "CompanyBuilding" || (scene.name == "InitSceneLaunchOptions" && isInGame))
            {
                currentWaterSurface = null;
                Terminal_Patch.ResetFireExitAmounts();
            }
        }

        private async Task WaitForSession()
        {
            while (sessionWaiting)
            {
                await Task.Delay(1000);
            }

            if (!alreadyPatched)
            {
                Terminal_Patch.MainPatch(GameObject.Find("TerminalScript").GetComponent<Terminal>());
                alreadyPatched = true;
            }
        }
    }
}
