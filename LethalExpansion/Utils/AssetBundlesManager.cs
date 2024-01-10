using System.Collections.Generic;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using LethalSDK.ScriptableObjects;
using System.Linq;
using System.Diagnostics;

namespace LethalExpansionCore.Utils;

public class AssetBundlesManager
{
    private static AssetBundlesManager _instance;
    public static AssetBundlesManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new AssetBundlesManager();
            }
            return _instance;
        }
    }

    public AssetBundle mainAssetBundle = AssetBundle.LoadFromFile(Assembly.GetExecutingAssembly().Location.Replace("LethalExpansionCore.dll", "lethalexpansion.lem"));
    public Dictionary<String, (AssetBundle, ModManifest)> assetBundles = new Dictionary<String, (AssetBundle, ModManifest)>();
    public Dictionary<String, (AssetBundle, ModManifest)> contentAssetBundles = new Dictionary<String, (AssetBundle, ModManifest)>();

    public (AssetBundle, ModManifest) Load(string name)
    {
        return assetBundles[name.ToLower()];
    }

    public DirectoryInfo modPath = new DirectoryInfo(Assembly.GetExecutingAssembly().Location);
    public DirectoryInfo modDirectory;
    public DirectoryInfo pluginsDirectory;

    public void LoadAllAssetBundles()
    {
        modDirectory = modPath.Parent;
        pluginsDirectory = modDirectory;

        while (pluginsDirectory != null && pluginsDirectory.Name != "plugins")
        {
            pluginsDirectory = pluginsDirectory.Parent;
        }

        if (pluginsDirectory == null)
        {
            LethalExpansion.Log.LogWarning("Mod is not in a plugins folder.");
            return;
        }

        LethalExpansion.Log.LogInfo($"Plugins folder found: {pluginsDirectory.FullName}");
        LethalExpansion.Log.LogInfo($"Mod path is: {modDirectory.FullName}");

        if (modDirectory.FullName == pluginsDirectory.FullName)
        {
            LethalExpansion.Log.LogWarning($"LethalExpansion is Rooting the Plugins folder, this is not recommended. {modDirectory.FullName}");
        }

        foreach (string file in Directory.GetFiles(pluginsDirectory.FullName, "*.lem", SearchOption.AllDirectories))
        {
            LoadBundle(file);
        }
    }

    public void LoadBundle(string file)
    {
        string bundleFileName = Path.GetFileName(file);
        if (bundleFileName == "lethalexpansion.lem")
        {
            return;
        }

        string bundleName = Path.GetFileNameWithoutExtension(file).ToLower();
        if (assetBundles.ContainsKey(bundleName))
        {
            LethalExpansion.Log.LogWarning($"An AssetBundle with the same name '{bundleFileName}' has already been loaded");
            return;
        }

        Stopwatch stopwatch = new Stopwatch();
        AssetBundle loadedBundle = null;
        try
        {
            stopwatch.Start();
            loadedBundle = AssetBundle.LoadFromFile(file);
            stopwatch.Stop();
        }
        catch (Exception e)
        {
            LethalExpansion.Log.LogError(e);
        }

        if (loadedBundle == null)
        {
            LethalExpansion.Log.LogWarning($"File '{bundleFileName}' is not an AssetBundle");
            return;
        }

        string manifestPath = $"Assets/Mods/{bundleName}/ModManifest.asset";

        ModManifest modManifest = loadedBundle.LoadAsset<ModManifest>(manifestPath);
        if (modManifest == null)
        {
            LethalExpansion.Log.LogWarning($"AssetBundle '{bundleFileName}' does not have a ModManifest");
            loadedBundle.Unload(true);
            LethalExpansion.Log.LogInfo($"AssetBundle '{bundleFileName}' unloaded");

            return;
        }

        if (assetBundles.Any(b => b.Value.Item2.modName == modManifest.modName))
        {
            LethalExpansion.Log.LogWarning($"Another mod with same name '{modManifest.modName}' is already loaded");
            loadedBundle.Unload(true);
            LethalExpansion.Log.LogInfo($"AssetBundle '{bundleFileName}' unloaded");

            return;
        }

        LethalExpansion.Log.LogInfo($"Module '{modManifest.modName}' v{modManifest?.GetVersion()?.ToString() ?? "0.0.0.0"} was loaded in {stopwatch.ElapsedMilliseconds}ms");
        if (modManifest.GetVersion() == null || modManifest.GetVersion().ToString() == "0.0.0.0")
        {
            LethalExpansion.Log.LogWarning($"Module '{modManifest.modName}' does not have a version number, this is unsafe!");
        }

        assetBundles.Add(bundleName, (loadedBundle, modManifest));

        if (!LethalExpansion.Settings.LoadDefaultBundles.Value)
        {
            if (LethalExpansion.LethalExpansionPath != null && Path.GetDirectoryName(file).StartsWith(LethalExpansion.LethalExpansionPath))
            {
                LethalExpansion.Log.LogWarning($"Ignorning content from default AssetBundle '{bundleFileName}'");
                return;
            }
        }

        contentAssetBundles.Add(bundleName, (loadedBundle, modManifest));
    }

    public bool BundleLoaded(string bundleName)
    {
        return assetBundles.ContainsKey(bundleName.ToLower());
    }

    public IEnumerable<string> GetMissingBundles(string[] bundleNames)
    {
        foreach (string bundleName in bundleNames)
        {
            if (!assetBundles.ContainsKey(bundleName.ToLower()))
            {
                yield return bundleName;
            }
        }
    }

    public IEnumerable<string> GetLoadedBundles(string[] bundleNames)
    {
        foreach (string bundleName in bundleNames)
        {
            if (assetBundles.ContainsKey(bundleName.ToLower()))
            {
                yield return bundleName;
            }
        }
    }

    public bool BundlesLoaded(string[] bundleNames)
    {
        foreach (string bundleName in bundleNames)
        {
            if (!assetBundles.ContainsKey(bundleName.ToLower()))
            {
                return false;
            }
        }

        return true;
    }

    public bool IncompatibleBundlesLoaded(string[] bundleNames)
    {
        foreach (string bundleName in bundleNames)
        {
            if (assetBundles.ContainsKey(bundleName.ToLower()))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsScrapCompatible(Scrap scrap)
    {
        if (scrap == null || scrap.prefab == null)
        {
            return false;
        }

        if (scrap.RequiredBundles != null)
        {
            List<string> missingBundles = AssetBundlesManager.Instance.GetMissingBundles(scrap.RequiredBundles).ToList();
            if (missingBundles.Count > 0)
            {
                if (!LethalExpansion.Settings.IgnoreRequiredBundles.Value)
                {
                    LethalExpansion.Log.LogWarning($"Scrap '{scrap.itemName}' can't be added, missing required bundles: {string.Join(", ", missingBundles)}");
                    return false;
                }
                else
                {
                    LethalExpansion.Log.LogWarning($"Scrap '{scrap.itemName}' may not work as intended, missing required bundles: {string.Join(", ", missingBundles)}");
                }
            }
        }

        if (scrap.IncompatibleBundles != null)
        {
            List<string> incompatibleBundles = AssetBundlesManager.Instance.GetLoadedBundles(scrap.IncompatibleBundles).ToList();
            if (incompatibleBundles.Count > 0)
            {
                LethalExpansion.Log.LogWarning($"Scrap '{scrap.itemName}' can't be added, incompatible bundles: {string.Join(", ", incompatibleBundles)}");
                return false;
            }
        }

        return true;
    }

    public bool IsMoonCompatible(Moon moon)
    {
        if (moon == null || !moon.IsEnabled)
        {
            return false;
        }


        if (moon.RequiredBundles != null)
        {
            List<string> missingBundles = AssetBundlesManager.Instance.GetMissingBundles(moon.RequiredBundles).ToList();
            if (missingBundles.Count > 0)
            {
                if (!LethalExpansion.Settings.IgnoreRequiredBundles.Value)
                {
                    LethalExpansion.Log.LogWarning($"Moon '{moon.MoonName}' can't be added, missing required bundles: {string.Join(", ", missingBundles)}");
                    return false;
                }
                else
                {
                    LethalExpansion.Log.LogWarning($"Moon '{moon.MoonName}' may not work as intended, missing required bundles: {string.Join(", ", missingBundles)}");
                }
            }
        }

        if (moon.IncompatibleBundles != null)
        {
            List<string> incompatibleBundles = AssetBundlesManager.Instance.GetLoadedBundles(moon.IncompatibleBundles).ToList();
            if (incompatibleBundles.Count > 0)
            {
                LethalExpansion.Log.LogWarning($"Moon '{moon.MoonName}' can't be added, incompatible bundles: {string.Join(", ", incompatibleBundles)}");
                return false;
            }
        }

        return true;
    }
}
