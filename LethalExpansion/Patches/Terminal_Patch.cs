using HarmonyLib;
using LethalExpansionCore.Extenders;
using LethalExpansionCore.Utils;
using LethalSDK.Utils;
using LethalSDK.ScriptableObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using DunGen;
using DunGen.Graph;

namespace LethalExpansionCore.Patches;

[HarmonyPatch(typeof(Terminal))]
internal class Terminal_Patch
{
    private static TerminalKeyword[] defaultTerminalKeywords;
    public static bool scrapsPatched = false;
    public static bool moonsPatched = false;
    public static bool assetsGotten = false;
    public static bool flowFireExitSaved = false;

    public static List<int> fireExitAmounts = new List<int>();

    public static TerminalKeyword routeKeyword;

    public static List<string> newScrapsNames = new List<string>();
    public static List<string> newMoonsNames = new List<string>();
    public static Dictionary<int, Moon> newMoons = new Dictionary<int, Moon>();

    public static void MainPatch(Terminal __instance)
    {
        scrapsPatched = false;
        moonsPatched = false;
        routeKeyword = __instance.terminalNodes.allKeywords.First(k => k.word == "route");

        Hotfix_DoubleRoutes();
        GatherAssets();
        AddScraps(__instance);
        ResetTerminalKeywords(__instance);
        AddMoons(__instance);
        UpdateMoonsCatalogue(__instance);
        SaveFireExitAmounts();

        if (LethalExpansion.delayedLevelChange != -1)
        {
            StartOfRound.Instance.ChangeLevel(LethalExpansion.delayedLevelChange);
            StartOfRound.Instance.ChangePlanet();
        }

        // Because it uses a random based on the map seed the weather will always be synced as long as
        // the conditions are the same (e.g StartOfRound.levels and SelectableLevel.randomWeathers)
        StartOfRound.Instance.SetPlanetsWeather();

        LethalExpansion.Log.LogInfo("Terminal Main Patch.");
    }

    private static void GatherAssets()
    {
        if (assetsGotten)
        {
            return;
        }

        foreach (Item item in StartOfRound.Instance.allItemsList.itemsList)
        {
            AssetGather.Instance.AddAudioClip(item.grabSFX);
            AssetGather.Instance.AddAudioClip(item.dropSFX);
            AssetGather.Instance.AddAudioClip(item.pocketSFX);
            AssetGather.Instance.AddAudioClip(item.throwSFX);
        }

        AssetGather.Instance.AddSprites(GameObject.Find("Environment/HangarShip/StartGameLever").GetComponent<InteractTrigger>().hoverIcon);
        AssetGather.Instance.AddSprites(GameObject.Find("Environment/HangarShip/Terminal/TerminalTrigger/TerminalScript").GetComponent<InteractTrigger>().hoverIcon);
        AssetGather.Instance.AddSprites(GameObject.Find("Environment/HangarShip/OutsideShipRoom/Ladder/LadderTrigger").GetComponent<InteractTrigger>().hoverIcon);

        foreach (SelectableLevel level in StartOfRound.Instance.levels)
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

        foreach (KeyValuePair<String, (AssetBundle, ModManifest)> bundleKeyValue in AssetBundlesManager.Instance.assetBundles)
        {
            (AssetBundle bundle, ModManifest manifest) = AssetBundlesManager.Instance.Load(bundleKeyValue.Key);

            if (manifest.assetBank == null)
            {
                continue;
            }

            try
            {
                foreach (var a in manifest.assetBank.AudioClips())
                {
                    AssetGather.Instance.AddAudioClip(a.AudioClipName, bundleKeyValue.Value.Item1.LoadAsset<AudioClip>(a.AudioClipPath));
                }

                foreach (var p in manifest.assetBank.PlanetPrefabs())
                {
                    var prefab = bundleKeyValue.Value.Item1.LoadAsset<GameObject>(p.PlanetPrefabPath);
                    if (prefab == null)
                    {
                        continue;
                    }

                    Animator animator;
                    if (prefab.GetComponent<Animator>() == null)
                    {
                        animator = prefab.AddComponent<Animator>();
                    }
                    animator = AssetGather.Instance.planetPrefabs.First().Value.GetComponent<Animator>();

                    AssetGather.Instance.AddPlanetPrefabs(prefab);
                }
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError(ex.Message);
            }
        }

        // Interfacing LE and SDK Asset Gathers
        AssetGatherDialog.audioClips = AssetGather.Instance.audioClips;
        AssetGatherDialog.audioMixers = AssetGather.Instance.audioMixers;
        AssetGatherDialog.sprites = AssetGather.Instance.sprites;

        assetsGotten = true;
    }

    public static void AddScraps(Terminal __instance)
    {
        if (scrapsPatched)
        {
            return;
        }

        AudioClip defaultDropSound = AssetGather.Instance.audioClips["DropCan"];
        AudioClip defaultGrabSound = AssetGather.Instance.audioClips["ShovelPickUp"];
        foreach (KeyValuePair<String, (AssetBundle, ModManifest)> bundleKeyValue in AssetBundlesManager.Instance.assetBundles)
        {
            (AssetBundle bundle, ModManifest manifest) = AssetBundlesManager.Instance.Load(bundleKeyValue.Key);

            if (bundle == null || manifest == null)
            {
                continue;
            }

            if (manifest.scraps == null)
            {
                continue;
            }

            foreach (var newScrap in manifest.scraps)
            {
                if (!AssetBundlesManager.Instance.IsScrapCompatible(newScrap))
                {
                    continue;
                }

                if (newScrapsNames.Contains(newScrap.itemName))
                {
                    LethalExpansion.Log.LogWarning($"{newScrap.itemName} Scrap already added.");
                    continue;
                }

                try
                {
                    Item item = newScrap.prefab.GetComponent<PhysicsProp>().itemProperties;

                    AudioSource audioSource = newScrap.prefab.GetComponent<AudioSource>();
                    audioSource.outputAudioMixerGroup = AssetGather.Instance.audioMixers.ContainsKey("Diagetic") ? AssetGather.Instance.audioMixers["Diagetic"].Item2.First(a => a.name == "Master") : null;

                    AudioClip grabSfx = null;
                    if (newScrap.grabSFX.Length > 0 && AssetGather.Instance.audioClips.ContainsKey(newScrap.grabSFX))
                    {
                        grabSfx = AssetGather.Instance.audioClips[newScrap.grabSFX];
                    }
                    item.grabSFX = grabSfx ?? defaultGrabSound;

                    AudioClip dropSfx = null;
                    if (newScrap.grabSFX.Length > 0 && AssetGather.Instance.audioClips.ContainsKey(newScrap.dropSFX))
                    {
                        dropSfx = AssetGather.Instance.audioClips[newScrap.dropSFX];
                    }
                    item.dropSFX = dropSfx ?? defaultDropSound;

                    StartOfRound.Instance.allItemsList.itemsList.Add(item);
                    if (newScrap.useGlobalSpawnWeight)
                    {
                        SpawnableItemWithRarity itemRarity = new SpawnableItemWithRarity();
                        itemRarity.spawnableItem = item;
                        itemRarity.rarity = newScrap.globalSpawnWeight;
                        foreach (SelectableLevel level in __instance.moonsCatalogueList)
                        {
                            level.spawnableScrap.Add(itemRarity);
                        }
                    }
                    else
                    {
                        ScrapSpawnChancePerScene[] perPlanetSpawnWeight = newScrap.perPlanetSpawnWeight();
                        foreach (SelectableLevel level in __instance.moonsCatalogueList)
                        {
                            bool containsPlanet = perPlanetSpawnWeight.Any(l => l.SceneName == level.PlanetName);
                            if (!containsPlanet)
                            {
                                continue;
                            }

                            try
                            {
                                ScrapSpawnChancePerScene scrapSpawnChance = perPlanetSpawnWeight.First(l => l.SceneName == level.PlanetName);

                                SpawnableItemWithRarity itemRarity = new SpawnableItemWithRarity();
                                itemRarity.spawnableItem = item;
                                itemRarity.rarity = scrapSpawnChance.SpawnWeight;

                                level.spawnableScrap.Add(itemRarity);
                            }
                            catch (Exception ex)
                            {
                                LethalExpansion.Log.LogError(ex.Message);
                            }
                        }
                    }

                    newScrapsNames.Add(item.itemName);
                    AssetGather.Instance.AddScrap(item);
                    LethalExpansion.Log.LogInfo($"{newScrap.itemName} Scrap added.");
                }
                catch (Exception ex)
                {
                    LethalExpansion.Log.LogError(ex.Message);
                }
            }
        }

        scrapsPatched = true;
    }

    public static T[] RemoveElementFromArray<T>(T[] originalArray, int indexToRemove)
    {
        if (indexToRemove < 0 || indexToRemove >= originalArray.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(indexToRemove));
        }

        T[] newArray = new T[originalArray.Length - 1];
        for (int i = 0, j = 0; i < originalArray.Length; i++)
        {
            if (i != indexToRemove)
            {
                newArray[j] = originalArray[i];
                j++;
            }
        }

        return newArray;
    }

    public static void RemoveMoon(Terminal __instance, string moonName)
    {
        try
        {
            if (moonName == null)
            {
                return;
            }

            CompatibleNoun[] nouns = routeKeyword.compatibleNouns;
            if (!__instance.moonsCatalogueList.Any(level => level.name.Contains(moonName)) && !nouns.Any(level => level.noun.name.Contains(moonName)))
            {
                LethalExpansion.Log.LogInfo($"{moonName} moon not exist.");
                return;
            }

            for (int i = 0; i < __instance.moonsCatalogueList.Length; i++)
            {
                if (__instance.moonsCatalogueList[i].name.Contains(moonName))
                {
                    __instance.moonsCatalogueList = RemoveElementFromArray(__instance.moonsCatalogueList, i);
                }
            }

            for (int i = 0; i < nouns.Length; i++)
            {
                if (nouns[i].noun.name.Contains(moonName))
                {
                    routeKeyword.compatibleNouns = RemoveElementFromArray(nouns, i);
                }
            }

            if (!__instance.moonsCatalogueList.Any(level => level.name.Contains(moonName)) &&
                !routeKeyword.compatibleNouns.Any(level => level.noun.name.Contains(moonName)))
            {
                LethalExpansion.Log.LogInfo($"{moonName} moon removed.");
            }
            else
            {
                LethalExpansion.Log.LogInfo($"{moonName} moon failed to remove.");
            }
        }
        catch (Exception ex)
        {
            LethalExpansion.Log.LogError(ex.Message);
        }
    }

    public static void AddMoons(Terminal __instance)
    {
        newMoons = new Dictionary<int, Moon>();

        if (moonsPatched)
        {
            return;
        }

        foreach (KeyValuePair<String, (AssetBundle, ModManifest)> bundleKeyValue in AssetBundlesManager.Instance.assetBundles)
        {
            (AssetBundle bundle, ModManifest manifest) = AssetBundlesManager.Instance.Load(bundleKeyValue.Key);

            if (bundle == null || manifest == null)
            {
                continue;
            }

            if (manifest.moons == null)
            {
                continue;
            }

            foreach (Moon newMoon in manifest.moons)
            {
                if (!AssetBundlesManager.Instance.IsMoonCompatible(newMoon))
                {
                    continue;
                }

                if (newMoonsNames.Contains(newMoon.MoonName))
                {
                    LethalExpansion.Log.LogWarning($"Moon '{newMoon.MoonName}' has already been added.");
                    continue;
                }

                try
                {
                    SelectableLevel newLevel = CreateMoon(newMoon);
                    AddMoonTerminalEntry(__instance, newMoon, newLevel);

                    StartOfRound.Instance.levels = StartOfRound.Instance.levels.AddItem(newLevel).ToArray();

                    newMoons.Add(newLevel.levelID, newMoon);

                    LethalExpansion.Log.LogInfo(newMoon.MoonName + " Moon added.");
                }
                catch (Exception ex)
                {
                    LethalExpansion.Log.LogError(ex.Message);
                }
            }
        }

        moonsPatched = true;
    }

    private static SelectableLevel CreateMoon(Moon newMoon)
    {
        SelectableLevel newLevel = ScriptableObject.CreateInstance<SelectableLevel>();

        newLevel.name = newMoon.PlanetName;
        newLevel.PlanetName = newMoon.PlanetName;
        newLevel.sceneName = "InitSceneLaunchOptions";
        newLevel.levelID = StartOfRound.Instance.levels.Length;

        if (newMoon.OrbitPrefabName != null && newMoon.OrbitPrefabName.Length > 0 && AssetGather.Instance.planetPrefabs.ContainsKey(newMoon.OrbitPrefabName))
        {
            newLevel.planetPrefab = AssetGather.Instance.planetPrefabs[newMoon.OrbitPrefabName];
        }
        else
        {
            newLevel.planetPrefab = AssetGather.Instance.planetPrefabs.First().Value;
        }

        newLevel.lockedForDemo = true;
        newLevel.spawnEnemiesAndScrap = newMoon.SpawnEnemiesAndScrap;

        if (newMoon.PlanetDescription != null && newMoon.PlanetDescription.Length > 0)
        {
            newLevel.LevelDescription = newMoon.PlanetDescription;
        }
        else
        {
            newLevel.LevelDescription = string.Empty;
        }

        newLevel.videoReel = newMoon.PlanetVideo;

        if (newMoon.RiskLevel != null && newMoon.RiskLevel.Length > 0)
        {
            newLevel.riskLevel = newMoon.RiskLevel;
        }
        else
        {
            newLevel.riskLevel = string.Empty;
        }

        newLevel.timeToArrive = newMoon.TimeToArrive;
        newLevel.DaySpeedMultiplier = newMoon.DaySpeedMultiplier;
        newLevel.planetHasTime = newMoon.PlanetHasTime;
        newLevel.factorySizeMultiplier = newMoon.FactorySizeMultiplier;

        newLevel.overrideWeather = newMoon.OverwriteWeather;
        newLevel.overrideWeatherType = (LevelWeatherType)(int)newMoon.OverwriteWeatherType;
        newLevel.currentWeather = LevelWeatherType.None;

        List<RandomWeatherWithVariables> randomWeatherTypes = new List<RandomWeatherWithVariables>();
        foreach (RandomWeatherPair item in newMoon.RandomWeatherTypes())
        {
            randomWeatherTypes.Add(new RandomWeatherWithVariables() { weatherType = (LevelWeatherType)(int)item.Weather, weatherVariable = item.WeatherVariable1, weatherVariable2 = item.WeatherVariable2 });
        }
        newLevel.randomWeathers = randomWeatherTypes.ToArray();

        List<IntWithRarity> dungeonFlowTypes = new List<IntWithRarity>();
        foreach (var item in newMoon.DungeonFlowTypes())
        {
            dungeonFlowTypes.Add(new IntWithRarity() { id = item.ID, rarity = item.Rarity });
        }
        newLevel.dungeonFlowTypes = dungeonFlowTypes.ToArray();

        List<SpawnableItemWithRarity> spawnableScrap = new List<SpawnableItemWithRarity>();
        foreach (SpawnableScrapPair item in newMoon.SpawnableScrap())
        {
            if (!AssetGather.Instance.scraps.TryGetValue(item.ObjectName, out Item scrap))
            {
                LethalExpansion.Log.LogWarning($"Scrap '{item.ObjectName}' on moon '{newMoon.MoonName}' could not be found, it has not been registered");
                continue;
            }

            spawnableScrap.Add(new SpawnableItemWithRarity() { spawnableItem = scrap, rarity = item.SpawnWeight });
        }
        newLevel.spawnableScrap = spawnableScrap;

        newLevel.minScrap = newMoon.MinScrap;
        newLevel.maxScrap = newMoon.MaxScrap;

        if (newMoon.LevelAmbienceClips != null && newMoon.LevelAmbienceClips.Length > 0 && AssetGather.Instance.levelAmbiances.ContainsKey(newMoon.LevelAmbienceClips))
        {
            newLevel.levelAmbienceClips = AssetGather.Instance.levelAmbiances[newMoon.LevelAmbienceClips];
        }
        else
        {
            newLevel.levelAmbienceClips = AssetGather.Instance.levelAmbiances.First().Value;
        }

        newLevel.maxEnemyPowerCount = newMoon.MaxEnemyPowerCount;

        List<SpawnableEnemyWithRarity> enemies = new List<SpawnableEnemyWithRarity>();
        foreach (SpawnableEnemiesPair item in newMoon.Enemies())
        {
            enemies.Add(new SpawnableEnemyWithRarity() { enemyType = AssetGather.Instance.enemies[item.EnemyName], rarity = item.SpawnWeight });
        }
        newLevel.Enemies = enemies;

        newLevel.enemySpawnChanceThroughoutDay = newMoon.EnemySpawnChanceThroughoutDay;
        newLevel.spawnProbabilityRange = newMoon.SpawnProbabilityRange;

        List<SpawnableMapObject> spawnableMapObjects = new List<SpawnableMapObject>();
        foreach (SpawnableMapObjectPair item in newMoon.SpawnableMapObjects())
        {
            spawnableMapObjects.Add(new SpawnableMapObject() { prefabToSpawn = AssetGather.Instance.mapObjects[item.ObjectName], spawnFacingAwayFromWall = item.SpawnFacingAwayFromWall, numberToSpawn = item.SpawnRate });
        }
        newLevel.spawnableMapObjects = spawnableMapObjects.ToArray();

        List<SpawnableOutsideObjectWithRarity> spawnableOutsideObjects = new List<SpawnableOutsideObjectWithRarity>();
        foreach (SpawnableOutsideObjectPair item in newMoon.SpawnableOutsideObjects())
        {
            spawnableOutsideObjects.Add(new SpawnableOutsideObjectWithRarity() { spawnableObject = AssetGather.Instance.outsideObjects[item.ObjectName], randomAmount = item.SpawnRate });
        }
        newLevel.spawnableOutsideObjects = spawnableOutsideObjects.ToArray();

        newLevel.maxOutsideEnemyPowerCount = newMoon.MaxOutsideEnemyPowerCount;
        newLevel.maxDaytimeEnemyPowerCount = newMoon.MaxDaytimeEnemyPowerCount;

        List<SpawnableEnemyWithRarity> outsideEnemies = new List<SpawnableEnemyWithRarity>();
        foreach (var item in newMoon.OutsideEnemies())
        {
            outsideEnemies.Add(new SpawnableEnemyWithRarity() { enemyType = AssetGather.Instance.enemies[item.EnemyName], rarity = item.SpawnWeight });
        }
        newLevel.OutsideEnemies = outsideEnemies;

        List<SpawnableEnemyWithRarity> daytimeEnemies = new List<SpawnableEnemyWithRarity>();
        foreach (var item in newMoon.DaytimeEnemies())
        {
            daytimeEnemies.Add(new SpawnableEnemyWithRarity() { enemyType = AssetGather.Instance.enemies[item.EnemyName], rarity = item.SpawnWeight });
        }
        newLevel.DaytimeEnemies = daytimeEnemies;

        newLevel.outsideEnemySpawnChanceThroughDay = newMoon.OutsideEnemySpawnChanceThroughDay;
        newLevel.daytimeEnemySpawnChanceThroughDay = newMoon.DaytimeEnemySpawnChanceThroughDay;
        newLevel.daytimeEnemiesProbabilityRange = newMoon.DaytimeEnemiesProbabilityRange;
        newLevel.levelIncludesSnowFootprints = newMoon.LevelIncludesSnowFootprints;

        newLevel.SetFireExitAmountOverwrite(newMoon.FireExitsAmountOverwrite);
        return newLevel;
    }

    private static void AddMoonTerminalEntry(Terminal terminal, Moon newMoon, SelectableLevel newLevel)
    {
        TerminalKeyword confirmKeyword = terminal.terminalNodes.allKeywords.First(k => k.word == "confirm");
        TerminalKeyword denyKeyword = terminal.terminalNodes.allKeywords.First(k => k.word == "deny");
        TerminalNode cancelRouteNode = null;
        foreach (CompatibleNoun option in routeKeyword.compatibleNouns[0].result.terminalOptions)
        {
            if (option.result.name == "CancelRoute")
            {
                cancelRouteNode = option.result;
                break;
            }
        }

        terminal.moonsCatalogueList = terminal.moonsCatalogueList.AddItem(newLevel).ToArray();

        TerminalKeyword moonKeyword = ScriptableObject.CreateInstance<TerminalKeyword>();
        moonKeyword.word = newMoon.RouteWord != null || newMoon.RouteWord.Length >= 3 ? newMoon.RouteWord.ToLower() : Regex.Replace(newMoon.MoonName, @"\s", "").ToLower();
        moonKeyword.name = newMoon.MoonName;
        moonKeyword.defaultVerb = routeKeyword;
        terminal.terminalNodes.allKeywords = terminal.terminalNodes.allKeywords.AddItem(moonKeyword).ToArray();

        TerminalNode moonRouteConfirm = ScriptableObject.CreateInstance<TerminalNode>();
        moonRouteConfirm.name = newMoon.MoonName.ToLower() + "RouteConfirm";
        moonRouteConfirm.displayText = $"Routing autopilot to {newMoon.PlanetName}.\r\nYour new balance is [playerCredits].\r\n\r\n{newMoon.BoughtComment}\r\n\r\n";
        moonRouteConfirm.clearPreviousText = true;
        moonRouteConfirm.buyRerouteToMoon = StartOfRound.Instance.levels.Length;
        moonRouteConfirm.lockedInDemo = true;
        moonRouteConfirm.itemCost = newMoon.RoutePrice;

        TerminalNode moonRoute = ScriptableObject.CreateInstance<TerminalNode>();
        moonRoute.name = newMoon.MoonName.ToLower() + "Route";
        moonRoute.displayText = $"The cost to route to {newMoon.PlanetName} is [totalCost]. It is \r\ncurrently [currentPlanetTime] on this moon.\r\n\r\nPlease CONFIRM or DENY.\r\n\r\n\r\n";
        moonRoute.clearPreviousText = true;
        moonRoute.buyRerouteToMoon = -2;
        moonRoute.displayPlanetInfo = StartOfRound.Instance.levels.Length;
        moonRoute.lockedInDemo = true;
        moonRoute.overrideOptions = true;
        moonRoute.itemCost = newMoon.RoutePrice;
        moonRoute.terminalOptions = new CompatibleNoun[]
        {
            new CompatibleNoun(){ noun = denyKeyword, result = cancelRouteNode != null ? cancelRouteNode : new TerminalNode() },
            new CompatibleNoun(){ noun = confirmKeyword, result = moonRouteConfirm },
        };

        CompatibleNoun moonNoun = new CompatibleNoun() { noun = moonKeyword, result = moonRoute };
        routeKeyword.compatibleNouns = routeKeyword.compatibleNouns.AddItem(moonNoun).ToArray();
    }

    private static void Hotfix_DoubleRoutes()
    {
        try
        {
            LethalExpansion.Log.LogDebug("Hotfix: Removing duplicated routes");
            HashSet<string> uniqueNames = new HashSet<string>();
            List<CompatibleNoun> uniqueNouns = new List<CompatibleNoun>();

            int duplicateCount = 0;

            foreach (CompatibleNoun noun in routeKeyword.compatibleNouns)
            {
                if (!uniqueNames.Contains(noun.result.name) || noun.result.name == "Daily Moon" /* MoonOfTheDay 1.0.4 compatibility workaround*/)
                {
                    uniqueNames.Add(noun.result.name);
                    uniqueNouns.Add(noun);
                }
                else
                {
                    LethalExpansion.Log.LogDebug($"{noun.result.name} duplicated route removed.");
                    duplicateCount++;
                }
            }

            routeKeyword.compatibleNouns = uniqueNouns.ToArray();

            LethalExpansion.Log.LogDebug($"Hotfix: {duplicateCount} duplicated route(s) removed");
        }
        catch (Exception ex)
        {
            LethalExpansion.Log.LogError(ex.Message);
        }
    }

    private static void ResetTerminalKeywords(Terminal __instance)
    {
        try
        {
            if (defaultTerminalKeywords == null || defaultTerminalKeywords.Length == 0)
            {
                defaultTerminalKeywords = __instance.terminalNodes.allKeywords;
            }
            else
            {
                __instance.terminalNodes.allKeywords = defaultTerminalKeywords;
            }

            LethalExpansion.Log.LogInfo("Terminal reset.");
        }
        catch (Exception ex)
        {
            LethalExpansion.Log.LogError(ex.Message);
        }
    }

    private static void UpdateMoonsCatalogue(Terminal __instance)
    {
        try
        {
            string text = "Welcome to the exomoons catalogue.\r\nTo route the autopilot to a moon, use the word ROUTE.\r\nTo learn about any moon, use the word INFO.\r\n____________________________\r\n\r\n* The Company building   //   Buying at [companyBuyingPercent].\r\n\r\n";

            foreach (SelectableLevel moon in __instance.moonsCatalogueList)
            {
                bool isHidden = newMoons.ContainsKey(moon.levelID) && newMoons[moon.levelID].IsHidden;
                if (isHidden)
                {
                    text += "[hidden]";
                }

                text += $"* {moon.PlanetName} [planetTime]\r\n";
            }
            text += "\r\n";

            __instance.terminalNodes.allKeywords.First(node => node.name == "Moons").specialKeywordResult.displayText = text;
        }
        catch (Exception ex)
        {
            LethalExpansion.Log.LogError(ex.Message);
        }
    }

    [HarmonyPatch("TextPostProcess")]
    [HarmonyPostfix]
    private static void TextPostProcess_Postfix(Terminal __instance, ref string __result)
    {
        __result = TextHidder(__result);
    }

    private static string TextHidder(string inputText)
    {
        try
        {
            string pattern = @"^\[hidden\].*\r\n";
            string result = Regex.Replace(inputText, pattern, "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            return result;
        }
        catch (Exception ex)
        {
            LethalExpansion.Log.LogError(ex.Message);
        }

        return inputText;
    }

    private static void SaveFireExitAmounts()
    {
        if (flowFireExitSaved)
        {
            return;
        }

        try
        {
            foreach (var flow in RoundManager.Instance.dungeonFlowTypes)
            {
                flow.SetDefaultFireExitAmount(flow.GlobalProps.First(p => p.ID == 1231).Count.Min);
            }

            flowFireExitSaved = true;
        }
        catch (Exception ex)
        {
            LethalExpansion.Log.LogError(ex.Message);
        }
    }

    public static void ResetFireExitAmounts()
    {
        if (!flowFireExitSaved)
        {
            return;
        }

        try
        {
            foreach (DungeonFlow flow in RoundManager.Instance.dungeonFlowTypes)
            {
                flow.GlobalProps.First(p => p.ID == 1231).Count = new IntRange(flow.GetDefaultFireExitAmount(), flow.GetDefaultFireExitAmount());
            }
        }
        catch (Exception ex)
        {
            LethalExpansion.Log.LogError(ex.Message);
        }
    }
}
