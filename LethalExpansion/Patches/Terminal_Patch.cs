using HarmonyLib;
using LethalExpansionCore.Utils;
using LethalSDK.Utils;
using LethalSDK.ScriptableObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LethalExpansionCore.Patches;

[HarmonyPatch(typeof(Terminal))]
internal class Terminal_Patch
{
    private static TerminalKeyword[] defaultTerminalKeywords;

    public static bool scrapsPatched = false;
    public static bool moonsPatched = false;
    public static bool moonsTerminalPatched = false;
    public static bool assetsGotten = false;

    public static TerminalKeyword routeKeyword;
    public static TerminalKeyword infoKeyword;

    public static List<string> newScrapsNames = new List<string>();
    public static List<string> newMoonsNames = new List<string>();
    public static Dictionary<int, Moon> newMoons = new Dictionary<int, Moon>();

    [HarmonyPatch(typeof(StartOfRound), "Awake")]
    [HarmonyPostfix]
    public static void StartOfRound_Awake()
    {
        AddContent();
    }

    [HarmonyPatch(typeof(Terminal), "Start")]
    [HarmonyPrefix]
    public static void Terminal_Start(Terminal __instance)
    {
        UpdateTerminal(__instance);
    }

    public static void AddContent()
    {
        scrapsPatched = false;
        moonsPatched = false;

        // TODO: It might be a good idea to gather the assets again later on,
        // if there are other mods which add content you might want to use
        GatherAssets();
        AddScraps();
        AddMoons();
        // Apply scrap spawn chance again for the new moons
        UpdateAllScrapSpawnRate();

        LethalExpansion.Log.LogInfo("Finished adding moons and scrap");
    }

    public static void UpdateTerminal(Terminal terminal)
    {
        moonsTerminalPatched = false;

        routeKeyword = terminal.terminalNodes.allKeywords.First(k => k.word == "route");
        infoKeyword = terminal.terminalNodes.allKeywords.First(k => k.word == "info");

        Hotfix_DoubleRoutes();
        ResetTerminalKeywords(terminal);
        AddMoonTerminalEntries(terminal);
        UpdateMoonsCatalogue(terminal);

        // Because it uses a random based on the map seed the weather will always be synced as long as
        // the conditions are the same (e.g StartOfRound.levels and SelectableLevel.randomWeathers)
        StartOfRound.Instance.SetPlanetsWeather();

        LethalExpansion.Log.LogInfo("Finished updating terminal");
    }

    private static void GatherAssets()
    {
        if (assetsGotten)
        {
            return;
        }

        VanillaAssetGatherer.GatherAssets();

        foreach (KeyValuePair<String, (AssetBundle, ModManifest)> bundleKeyValue in AssetBundlesManager.Instance.contentAssetBundles)
        {
            (AssetBundle bundle, ModManifest manifest) = AssetBundlesManager.Instance.Load(bundleKeyValue.Key);

            if (manifest.assetBank == null)
            {
                continue;
            }

            try
            {
                foreach (AudioClipInfoPair audioClip in manifest.assetBank.AudioClips())
                {
                    AssetGather.Instance.AddAudioClip(audioClip.AudioClipName, bundle.LoadAsset<AudioClip>(audioClip.AudioClipPath));
                }

                foreach (PlanetPrefabInfoPair planetPrefab in manifest.assetBank.PlanetPrefabs())
                {
                    GameObject prefab = bundle.LoadAsset<GameObject>(planetPrefab.PlanetPrefabPath);
                    if (prefab == null)
                    {
                        continue;
                    }

                    Animator animator;
                    if (prefab.GetComponent<Animator>() == null)
                    {
                        animator = prefab.AddComponent<Animator>();
                    }
                    // heh?
                    animator = AssetGather.Instance.planetPrefabs.First().Value.GetComponent<Animator>();

                    AssetGather.Instance.AddPlanetPrefabs(prefab);
                }
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError($"Failed to collect prefabs from AssetBundle '{manifest.modName}'. {ex.Message}");
            }
        }

        // Interfacing LE and SDK Asset Gathers
        AssetGatherDialog.audioClips = AssetGather.Instance.audioClips;
        AssetGatherDialog.audioMixers = AssetGather.Instance.audioMixers;
        AssetGatherDialog.sprites = AssetGather.Instance.sprites;

        assetsGotten = true;
    }

    public static void AddScraps()
    {
        if (scrapsPatched)
        {
            return;
        }

        foreach (KeyValuePair<String, (AssetBundle, ModManifest)> bundleKeyValue in AssetBundlesManager.Instance.contentAssetBundles)
        {
            (AssetBundle bundle, ModManifest manifest) = AssetBundlesManager.Instance.Load(bundleKeyValue.Key);

            if (bundle == null || manifest == null || manifest.scraps == null)
            {
                continue;
            }

            foreach (Scrap scrap in manifest.scraps)
            {
                if (!AssetBundlesManager.Instance.IsScrapCompatible(scrap))
                {
                    continue;
                }

                if (newScrapsNames.Contains(scrap.itemName))
                {
                    LethalExpansion.Log.LogWarning($"Scrap '{scrap.itemName}' has already been added");
                    continue;
                }

                try
                {
                    VanillaItemInstancier.UpdateAudio(scrap);
                    AddScrap(scrap);

                    LethalExpansion.Log.LogInfo($"Added scrap '{scrap.itemName}'");
                }
                catch (Exception ex)
                {
                    LethalExpansion.Log.LogError($"Failed to add scrap '{scrap.itemName}'. {ex.Message}");
                }
            }
        }

        scrapsPatched = true;
    }

    public static void UpdateAllScrapSpawnRate()
    {
        foreach (KeyValuePair<String, (AssetBundle, ModManifest)> bundleKeyValue in AssetBundlesManager.Instance.contentAssetBundles)
        {
            (AssetBundle bundle, ModManifest manifest) = AssetBundlesManager.Instance.Load(bundleKeyValue.Key);

            if (bundle == null || manifest == null || manifest.scraps == null)
            {
                continue;
            }

            foreach (Scrap scrap in manifest.scraps)
            {
                UpdateScrapSpawnRate(scrap);
            }
        }
    }

    public static void UpdateScrapSpawnRate(Scrap scrap)
    {
        Item item = VanillaItemInstancier.GetItem(scrap);
        if (item == null)
        {
            return;
        }

        int? GetSpawnableItemRarity(SelectableLevel level)
        {
            if (scrap.useGlobalSpawnWeight)
            {
                return scrap.globalSpawnWeight;
            }

            ScrapSpawnChancePerScene[] perPlanetSpawnWeight = scrap.perPlanetSpawnWeight();

            bool containsPlanet = perPlanetSpawnWeight.Any(l => l.SceneName == level.PlanetName);
            if (!containsPlanet)
            {
                return null;
            }

            ScrapSpawnChancePerScene scrapSpawnChance = perPlanetSpawnWeight.First(l => l.SceneName == level.PlanetName);
            return scrapSpawnChance.SpawnWeight;
        }

        foreach (SelectableLevel level in StartOfRound.Instance.levels)
        {
            try
            {
                int? spawnableItemRarity = GetSpawnableItemRarity(level);
                if (spawnableItemRarity == null)
                {
                    continue;
                }

                UpdateOrAddItemSpawnRate(level, item, spawnableItemRarity.Value);
            }
            catch (Exception ex)
            {
                LethalExpansion.Log.LogError($"Failed to add scrap '{scrap.itemName}' spawn chance to moon '{level.PlanetName}'. {ex.Message}");
            }
        }
    }

    public static void UpdateOrAddItemSpawnRate(SelectableLevel level, Item item, int rarity)
    {
        int index = level.spawnableScrap.FindIndex(spawnableScrap => spawnableScrap.spawnableItem == item);
        if (index == -1)
        {
            LethalExpansion.DebugLog.LogInfo($"Added new spawn rate '{rarity}' for scrap '{item.itemName}' on moon '{level.PlanetName}'");
            level.spawnableScrap.Add(new SpawnableItemWithRarity() { spawnableItem = item, rarity = rarity });
        }
        else
        {
            SpawnableItemWithRarity currentSpawnableItem = level.spawnableScrap[index];
            if (currentSpawnableItem.rarity != rarity)
            {
                LethalExpansion.DebugLog.LogInfo($"Updated spawn rate from '{currentSpawnableItem.rarity}' to '{rarity}' for scrap '{item.itemName}' on moon '{level.PlanetName}'");
                level.spawnableScrap[index] = new SpawnableItemWithRarity() { spawnableItem = item, rarity = rarity };
            }
        }
    }

    public static void AddScrap(Scrap scrap)
    {
        Item item = VanillaItemInstancier.GetItem(scrap);

        StartOfRound.Instance.allItemsList.itemsList.Add(item);

        newScrapsNames.Add(item.itemName);
        AssetGather.Instance.AddScrap(item);
    }

    public static void RemoveMoonTerminalEntry(Terminal terminal, string moonName)
    {
        if (moonName == null)
        {
            return;
        }

        int countEntries()
        {
            return terminal.moonsCatalogueList.Length +
                routeKeyword.compatibleNouns.Length +
                infoKeyword.compatibleNouns.Length;
        }

        try
        {
            int count = countEntries();

            terminal.moonsCatalogueList = terminal.moonsCatalogueList
                .Where(moon => !moon.name.Contains(moonName))
                .ToArray();

            routeKeyword.compatibleNouns = routeKeyword.compatibleNouns
                .Where(noun => !noun.noun.name.Contains(moonName))
                .ToArray();

            infoKeyword.compatibleNouns = infoKeyword.compatibleNouns
                .Where(noun => !noun.noun.name.Contains(moonName))
                .ToArray();

            if (count - countEntries() != 0)
            {
                LethalExpansion.Log.LogInfo($"Removed terminal entry for moon '{moonName}'");
            }
            else
            {
                LethalExpansion.Log.LogInfo($"Terminal entry for moon '{moonName}' does not exist");
            }
        }
        catch (Exception ex)
        {
            LethalExpansion.Log.LogError($"Failed to remove terminal entry for moon '{moonName}'. {ex.Message}");
        }
    }

    public static void AddMoons()
    {
        if (moonsPatched)
        {
            return;
        }

        // TODO: This could be handled better,
        // right now we just assume all the moons will
        // be cleared, what if that changes in the future.
        newMoonsNames.Clear();
        newMoons.Clear();

        foreach (KeyValuePair<String, (AssetBundle, ModManifest)> bundleKeyValue in AssetBundlesManager.Instance.contentAssetBundles)
        {
            (AssetBundle bundle, ModManifest manifest) = AssetBundlesManager.Instance.Load(bundleKeyValue.Key);

            if (bundle == null || manifest == null || manifest.moons == null)
            {
                continue;
            }

            foreach (Moon moon in manifest.moons)
            {
                if (!AssetBundlesManager.Instance.IsMoonCompatible(moon))
                {
                    continue;
                }
                
                if (newMoonsNames.Contains(moon.MoonName))
                {
                    LethalExpansion.Log.LogWarning($"Moon '{moon.MoonName}' has already been added");
                    continue;
                }

                try
                {
                    SelectableLevel newLevel = CreateMoon(moon);

                    StartOfRound.Instance.levels = StartOfRound.Instance.levels.AddItem(newLevel).ToArray();

                    newMoons.Add(newLevel.levelID, moon);
                    newMoonsNames.Add(moon.MoonName);

                    LethalExpansion.Log.LogInfo($"Added moon '{moon.MoonName}'");
                }
                catch (Exception ex)
                {
                    LethalExpansion.Log.LogError($"Failed to add moon '{moon.MoonName}'. {ex.Message}");
                }
            }
        }

        moonsPatched = true;
    }

    public static void AddMoonTerminalEntries(Terminal __instance)
    {
        if (moonsTerminalPatched)
        {
            return;
        }

        foreach (KeyValuePair<int, Moon> moonEntry in newMoons)
        {
            SelectableLevel level = StartOfRound.Instance.levels.FirstOrDefault(level => level.levelID == moonEntry.Key);
            if (level == null)
            {
                LethalExpansion.Log.LogWarning($"Unable to add terminal entry for moon '{moonEntry.Value.MoonName}', it does not exist");
                continue;
            }

            AddMoonTerminalEntry(__instance, moonEntry.Value, level);
            LethalExpansion.Log.LogInfo($"Added terminal entry for moon '{moonEntry.Value.MoonName}'");
        }

        moonsTerminalPatched = true;
    }

    public static SelectableLevel CreateMoon(Moon moon)
    {
        SelectableLevel level = ScriptableObject.CreateInstance<SelectableLevel>();
        level.name = moon.PlanetName;
        level.PlanetName = moon.PlanetName;
        level.sceneName = "InitSceneLaunchOptions";
        level.levelID = StartOfRound.Instance.levels.Length;

        if (!string.IsNullOrEmpty(moon.OrbitPrefabName) && AssetGather.Instance.planetPrefabs.ContainsKey(moon.OrbitPrefabName))
        {
            level.planetPrefab = AssetGather.Instance.planetPrefabs[moon.OrbitPrefabName];
        }
        else
        {
            level.planetPrefab = AssetGather.Instance.planetPrefabs.First().Value;
        }

        level.lockedForDemo = true;
        level.spawnEnemiesAndScrap = moon.SpawnEnemiesAndScrap;

        if (!string.IsNullOrWhiteSpace(moon.PlanetDescription))
        {
            level.LevelDescription = moon.PlanetDescription;
        }
        else
        {
            level.LevelDescription = string.Empty;
        }

        level.videoReel = moon.PlanetVideo;

        if (!string.IsNullOrEmpty(moon.RiskLevel))
        {
            level.riskLevel = moon.RiskLevel ?? string.Empty;
        }
        else
        {
            level.riskLevel = string.Empty;
        }

        level.timeToArrive = moon.TimeToArrive;
        level.DaySpeedMultiplier = moon.DaySpeedMultiplier;
        level.planetHasTime = moon.PlanetHasTime;
        level.factorySizeMultiplier = moon.FactorySizeMultiplier;

        level.overrideWeather = moon.OverwriteWeather;
        level.overrideWeatherType = (LevelWeatherType)(int)moon.OverwriteWeatherType;
        level.currentWeather = LevelWeatherType.None;

        level.randomWeathers = moon.RandomWeatherTypes()
            .Select(weather => new RandomWeatherWithVariables() { weatherType = (LevelWeatherType)(int)weather.Weather, weatherVariable = weather.WeatherVariable1, weatherVariable2 = weather.WeatherVariable2 })
            .ToArray();

        level.dungeonFlowTypes = moon.DungeonFlowTypes()
            .Select(dungeonFlow => new IntWithRarity() { id = dungeonFlow.ID, rarity = dungeonFlow.Rarity })
            .ToArray();

        List<SpawnableItemWithRarity> spawnableScrap = new List<SpawnableItemWithRarity>();
        foreach (SpawnableScrapPair scrap in moon.SpawnableScrap())
        {
            if (!AssetGather.Instance.scraps.TryGetValue(scrap.ObjectName, out Item item))
            {
                LethalExpansion.Log.LogWarning($"Scrap '{scrap.ObjectName}' on moon '{moon.MoonName}' could not be found, it has not been registered");
                continue;
            }

            UpdateOrAddItemSpawnRate(level, item, scrap.SpawnWeight);
        }
        level.spawnableScrap = spawnableScrap;

        level.minScrap = moon.MinScrap;
        level.maxScrap = moon.MaxScrap;

        if (!string.IsNullOrEmpty(moon.LevelAmbienceClips) && AssetGather.Instance.levelAmbiances.ContainsKey(moon.LevelAmbienceClips))
        {
            level.levelAmbienceClips = AssetGather.Instance.levelAmbiances[moon.LevelAmbienceClips];
        }
        else
        {
            level.levelAmbienceClips = AssetGather.Instance.levelAmbiances.First().Value;
        }

        level.maxEnemyPowerCount = moon.MaxEnemyPowerCount;

        level.Enemies = moon.Enemies()
            .Select(enemy => new SpawnableEnemyWithRarity() { enemyType = AssetGather.Instance.enemies[enemy.EnemyName], rarity = enemy.SpawnWeight })
            .ToList();

        level.enemySpawnChanceThroughoutDay = moon.EnemySpawnChanceThroughoutDay;
        level.spawnProbabilityRange = moon.SpawnProbabilityRange;

        level.spawnableMapObjects = moon.SpawnableMapObjects()
            .Select(item => new SpawnableMapObject() { prefabToSpawn = AssetGather.Instance.mapObjects[item.ObjectName], spawnFacingAwayFromWall = item.SpawnFacingAwayFromWall, numberToSpawn = item.SpawnRate })
            .ToArray();

        level.spawnableOutsideObjects = moon.SpawnableOutsideObjects()
            .Select(item => new SpawnableOutsideObjectWithRarity() { spawnableObject = AssetGather.Instance.outsideObjects[item.ObjectName], randomAmount = item.SpawnRate })
            .ToArray();

        level.maxOutsideEnemyPowerCount = moon.MaxOutsideEnemyPowerCount;
        level.maxDaytimeEnemyPowerCount = moon.MaxDaytimeEnemyPowerCount;

        level.OutsideEnemies = moon.OutsideEnemies()
            .Select(enemy => new SpawnableEnemyWithRarity() { enemyType = AssetGather.Instance.enemies[enemy.EnemyName], rarity = enemy.SpawnWeight })
            .ToList();

        level.DaytimeEnemies = moon.DaytimeEnemies()
            .Select(enemy => new SpawnableEnemyWithRarity() { enemyType = AssetGather.Instance.enemies[enemy.EnemyName], rarity = enemy.SpawnWeight })
            .ToList();

        level.outsideEnemySpawnChanceThroughDay = moon.OutsideEnemySpawnChanceThroughDay;
        level.daytimeEnemySpawnChanceThroughDay = moon.DaytimeEnemySpawnChanceThroughDay;
        level.daytimeEnemiesProbabilityRange = moon.DaytimeEnemiesProbabilityRange;
        level.levelIncludesSnowFootprints = moon.LevelIncludesSnowFootprints;
        return level;
    }

    public static void AddMoonTerminalEntry(Terminal terminal, Moon newMoon, SelectableLevel newLevel)
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
        moonRouteConfirm.buyRerouteToMoon = newLevel.levelID;
        moonRouteConfirm.lockedInDemo = true;
        moonRouteConfirm.itemCost = newMoon.RoutePrice;

        TerminalNode moonRoute = ScriptableObject.CreateInstance<TerminalNode>();
        moonRoute.name = newMoon.MoonName.ToLower() + "Route";
        moonRoute.displayText = $"The cost to route to {newMoon.PlanetName} is [totalCost]. It is \r\ncurrently [currentPlanetTime] on this moon.\r\n\r\nPlease CONFIRM or DENY.\r\n\r\n\r\n";
        moonRoute.clearPreviousText = true;
        moonRoute.buyRerouteToMoon = -2;
        moonRoute.displayPlanetInfo = newLevel.levelID;
        moonRoute.lockedInDemo = true;
        moonRoute.overrideOptions = true;
        moonRoute.itemCost = newMoon.RoutePrice;
        moonRoute.terminalOptions = new CompatibleNoun[]
        {
            new CompatibleNoun(){ noun = denyKeyword, result = cancelRouteNode != null ? cancelRouteNode : new TerminalNode() },
            new CompatibleNoun(){ noun = confirmKeyword, result = moonRouteConfirm },
        };

        CompatibleNoun moonRouteNoun = new CompatibleNoun() { noun = moonKeyword, result = moonRoute };
        routeKeyword.compatibleNouns = routeKeyword.compatibleNouns.AddItem(moonRouteNoun).ToArray();

        TerminalNode moonInfo = ScriptableObject.CreateInstance<TerminalNode>();
        moonInfo.name = newMoon.MoonName.ToLower() + "Info";
        moonInfo.displayText = $"{newMoon.PlanetName}\r\n----------------------\r\n\r\n";
        if (!string.IsNullOrWhiteSpace(newMoon.PlanetDescription))
        {
            moonInfo.displayText += $"{newMoon.PlanetDescription}\r\n";
        }
        else
        {
            moonInfo.displayText += "No info about this moon can be found.\r\n";
        }
        moonInfo.clearPreviousText = true;
        moonInfo.maxCharactersToType = 35;

        CompatibleNoun moonInfoNoun = new CompatibleNoun { noun = moonKeyword, result = moonInfo };
        infoKeyword.compatibleNouns = infoKeyword.compatibleNouns.AddItem(moonInfoNoun).ToArray();
    }

    // TODO: is this still necessary?
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
            LethalExpansion.Log.LogError($"Failed to remove duplicated route(s). {ex.Message}");
        }
    }

    public static void ResetTerminalKeywords(Terminal terminal)
    {
        try
        {
            if (defaultTerminalKeywords == null || defaultTerminalKeywords.Length == 0)
            {
                defaultTerminalKeywords = terminal.terminalNodes.allKeywords;
            }
            else
            {
                terminal.terminalNodes.allKeywords = defaultTerminalKeywords;
            }

            LethalExpansion.Log.LogInfo("Terminal reset.");
        }
        catch (Exception ex)
        {
            LethalExpansion.Log.LogError($"Failed to reset terminal keywords. {ex.Message}");
        }
    }

    public static void UpdateMoonsCatalogue(Terminal terminal)
    {
        try
        {
            string text = "Welcome to the exomoons catalogue.\r\nTo route the autopilot to a moon, use the word ROUTE.\r\nTo learn about any moon, use the word INFO.\r\n____________________________\r\n\r\n* The Company building   //   Buying at [companyBuyingPercent].\r\n\r\n";

            foreach (SelectableLevel moon in terminal.moonsCatalogueList)
            {
                bool isHidden = newMoons.ContainsKey(moon.levelID) && newMoons[moon.levelID].IsHidden;
                if (isHidden)
                {
                    text += "[hidden]";
                }

                text += $"* {moon.PlanetName} [planetTime]\r\n";
            }
            text += "\r\n";

            terminal.terminalNodes.allKeywords.First(node => node.name == "Moons").specialKeywordResult.displayText = text;
        }
        catch (Exception ex)
        {
            LethalExpansion.Log.LogError($"Failed to update moon catalogue {ex.Message}");
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
            LethalExpansion.Log.LogError($"Failed to hide terminal text. {ex.Message}");
        }

        return inputText;
    }
}
