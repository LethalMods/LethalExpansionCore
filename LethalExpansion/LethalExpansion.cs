using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using LethalExpansionCore.Patches;
using LethalExpansionCore.Utils;
using System;
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
using DunGen.Adapters;
using LethalSDK.Utils;
using System.IO;

namespace LethalExpansionCore;

public static class PluginInformation
{
    public const string PLUGIN_GUID = "com.github.lethalmods.lethalexpansioncore";
    public const string PLUGIN_NAME = "LethalExpansion(core)";
    public const string PLUGIN_VERSION = "1.3.6";
}

[BepInPlugin(PluginInformation.PLUGIN_GUID, PluginInformation.PLUGIN_NAME, PluginInformation.PLUGIN_VERSION)]
// TODO: Figure out if these are still necessary
[BepInDependency("me.swipez.melonloader.morecompany", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("BrutalCompanyPlus", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("MoonOfTheDay", BepInDependency.DependencyFlags.SoftDependency)]
public class LethalExpansion : BaseUnityPlugin
{
    public static readonly Version ModVersion = new Version(PluginInformation.PLUGIN_VERSION);

    public static ConfigEntry<bool> IgnoreRequiredBundles;
    public static ConfigEntry<bool> UseOriginalLethalExpansion;
    public static ConfigEntry<bool> LoadDefaultBundles;

    public static string LethalExpansionPath = null;

    public static bool sessionWaiting = true;
    public static bool alreadyPatched = false;
    public static bool isInGame = false;

    public static int delayedLevelChange = -1;

    public static ManualLogSource Log;

    public static NetworkManager networkManager;

    // TODO: What are these two for?
    public GameObject terrainFixer;
    public static Transform currentWaterSurface;

    private static bool LethalExpansion_Awake()
    {
        LethalExpansion.Log.LogInfo("Prevented LethalExpansion from initializing");
        return false;
    }

    private static Assembly LoadLethalExpansion()
    {
        try
        {
            Assembly assembly = AppDomain.CurrentDomain.Load("LethalExpansion");
            LethalExpansion.Log.LogInfo($"Loaded LethalExpansion: {assembly}");
            return assembly;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static void PatchLethalExpansion(Harmony harmony, Assembly assembly)
    {
        if (assembly == null)
        {
            LethalExpansion.Log.LogInfo("LethalExpansion is not present, all is well!");
            return;
        }

        Type type = assembly.GetType("LethalExpansion.LethalExpansion");

        MethodInfo awakeMethod = type.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo prefixMethod = typeof(LethalExpansion).GetMethod(nameof(LethalExpansion_Awake), BindingFlags.NonPublic | BindingFlags.Static);

        harmony.Patch(awakeMethod, prefix: new HarmonyMethod(prefixMethod));
        LethalExpansion.Log.LogInfo("Patched LethalExpansion#Awake");
    }

    private void Awake()
    {
        Log = Logger;
        LethalExpansion.Log.LogInfo($"Plugin: {PluginInformation.PLUGIN_NAME} (version: {PluginInformation.PLUGIN_VERSION}) is loading...");

        // Both Orion and Aquatis "require" templatemod (seemingly they reference scrap added by it)
        // but it loads and play just fine without it
        IgnoreRequiredBundles = Config.Bind<bool>("Core", "IgnoreRequiredBundles", true, "Whether or not to allow a bundle to load without its required bundles");
        // If both LethalExpansion and LethalExpansion(core) are required dependecies for different mods
        // this will let you choose which one you want to use instead of forcing you to use this one.
        UseOriginalLethalExpansion = Config.Bind<bool>("Core", "UseOriginalLethalExpansion", false, "Whether or not to use the original LethalExpansion instead of LethalExpansion(core) when they are both loaded");
        LoadDefaultBundles = Config.Bind<bool>("Core", "LoadDefaultBundles", false, "Whether or not to load the default bundles from LethalExpansion when both LethalExpansion and LethalExpansion(core) are present");

        Assembly lethalExpansionAssembly = LoadLethalExpansion();
        if (lethalExpansionAssembly != null)
        {
            if (UseOriginalLethalExpansion.Value)
            {
                LethalExpansion.Log.LogInfo("Using original LethalExpansion instead");
                return;
            }

            LethalExpansionPath = Path.GetDirectoryName(lethalExpansionAssembly.Location);
        }

        Harmony harmony = new Harmony(PluginInformation.PLUGIN_GUID);
        // This is somewhat hacky and only works because this plugin is loaded before LethalExpansion
        // but as far as I understand the load order of BepInEx is consistent so as long as they don't
        // change the plugin GUID this should always load first.
        // 
        // I don't think this solution is perfect, I assume things which happen at the static level
        // could still run which might cause issues?
        PatchLethalExpansion(harmony, lethalExpansionAssembly);

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
        harmony.PatchAll(typeof(DungeonGenerator_Patch));
        // heh?
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

        LethalExpansion.Log.LogInfo($"Plugin: {PluginInformation.PLUGIN_NAME} (version: {PluginInformation.PLUGIN_VERSION}) is loaded.");
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
        LethalExpansion.Log.LogInfo($"Loading scene '{scene.name}'");

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

    // preload the terrain shader
    // otherwise it's 100% crash on 100% setups
    // theres not any vanilla map that use terrain
    // - HolographicWings on Discord
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

        string[] requiredObjectTags = { "MapPropsContainer", "OutsideAINode", "SpawnDenialPoint", "ItemShipLandingNode", "OutsideLevelNavMesh" };
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
            LethalExpansion.Log.LogInfo($"Added required object with tag '{requiredObjectTag}'");
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

        RuntimeDungeon runtimeDungeon = GameObject.FindObjectOfType<RuntimeDungeon>(false);
        if (runtimeDungeon == null)
        {
            LethalExpansion.Log.LogInfo("Adding 'DungeonGenerator'");

            GameObject dungeonGenerator = CreateDungeonGenerator();
            runtimeDungeon = dungeonGenerator.GetComponent<RuntimeDungeon>();

            SceneManager.MoveGameObjectToScene(dungeonGenerator, scene);
        }
        else if (runtimeDungeon.Generator.DungeonFlow == null)
        {
            runtimeDungeon.Generator.DungeonFlow = RoundManager.Instance.dungeonFlowTypes[0];

            LethalExpansion.Log.LogInfo("Setting missing DungeonFlow in DungeonGenerator");
        }

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

    private GameObject CreateDungeonGenerator()
    {
        GameObject dungeonGenerator = new GameObject();
        dungeonGenerator.name = "DungeonGenerator";
        dungeonGenerator.tag = "DungeonGenerator";
        dungeonGenerator.transform.position = new Vector3(0, -200, 0);

        RuntimeDungeon runtimeDungeon = dungeonGenerator.AddComponent<RuntimeDungeon>();
        runtimeDungeon.Generator.DungeonFlow = RoundManager.Instance.dungeonFlowTypes[0];
        runtimeDungeon.Generator.LengthMultiplier = 0.8f;
        runtimeDungeon.Generator.PauseBetweenRooms = 0.2f;
        runtimeDungeon.GenerateOnStart = false;
        runtimeDungeon.Root = dungeonGenerator;

        UnityNavMeshAdapter dungeonNavMesh = dungeonGenerator.AddComponent<UnityNavMeshAdapter>();
        dungeonNavMesh.BakeMode = UnityNavMeshAdapter.RuntimeNavMeshBakeMode.FullDungeonBake;
        dungeonNavMesh.LayerMask = 35072; // 256 + 2048 + 32768 = 35072 (What does each of these correspond to?)

        return dungeonGenerator;
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
