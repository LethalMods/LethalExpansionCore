using System.Collections.Generic;
using System.Linq;
using LethalSDK.ScriptableObjects;
using UnityEngine;

namespace LethalExpansionCore.Utils;

public static class VanillaItemInstancier
{
    public static Item GetItem(Scrap scrap)
    {
        return scrap.prefab.GetComponent<GrabbableObject>().itemProperties;
    }

    public static void UpdateAudio(Scrap scrap)
    {
        UpdateItemAudio(scrap);

        switch (scrap.scrapType)
        {
            case ScrapType.Shovel:
                UpdateShovelAudio(scrap);
                break;
            case ScrapType.Flashlight:
                UpdateFlashlightAudio(scrap);
                break;
            case ScrapType.Noisemaker:
                UpdateNoisemakerAudio(scrap);
                break;
            case ScrapType.WhoopieCushion:
                UpdateWhoopieCushionAudio(scrap);
                break;
        }
    }

    public static void UpdateItemAudio(Scrap scrap)
    {
        Item item = GetItem(scrap);

        AudioSource audioSource = scrap.prefab.GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.outputAudioMixerGroup = AssetGather.Instance.GetDiageticMasterAudioMixer();
        }

        item.grabSFX = GetAudioClip(scrap.grabSFX, "ShovelPickUp");
        item.dropSFX = GetAudioClip(scrap.dropSFX, "DropCan");
    }

    public static void UpdateShovelAudio(Scrap scrap)
    {
        Shovel shovel = scrap.prefab.GetComponent<Shovel>();
        if (shovel == null)
        {
            return;
        }

        shovel.reelUp = GetAudioClip(scrap.reelUp, "ShovelReelUp");
        shovel.swing = GetAudioClip(scrap.swing, "ShovelSwing");
        shovel.hitSFX = GetAudioClips(scrap.hitSFX, "ShovelHitDefault", "ShovelHitDefault2");
    }

    public static void UpdateFlashlightAudio(Scrap scrap)
    {
        FlashlightItem flashlight = scrap.prefab.GetComponent<FlashlightItem>();
        if (flashlight == null)
        {
            return;
        }

        flashlight.outOfBatteriesClip = GetAudioClip(scrap.outOfBatteriesClip, "FlashlightOutOfBatteries");
        flashlight.flashlightFlicker = GetAudioClip(scrap.flashlightFlicker, "FlashlightFlicker");
        flashlight.flashlightClips = GetAudioClips(scrap.flashlightClips, "FlashlightClick");
    }

    public static void UpdateNoisemakerAudio(Scrap scrap)
    {
        NoisemakerProp noisemaker = scrap.prefab.GetComponent<NoisemakerProp>();
        if (noisemaker == null)
        {
            return;
        }

        noisemaker.noiseSFX = GetAudioClips(scrap.noiseSFX, "ClownHorn1");
        noisemaker.noiseSFXFar = GetAudioClips(scrap.noiseSFXFar, "ClownHornFar");
    }

    public static void UpdateWhoopieCushionAudio(Scrap scrap)
    {
        WhoopieCushionItem whoopieCushion = scrap.prefab.GetComponent<WhoopieCushionItem>();
        if (whoopieCushion == null)
        {
            return;
        }

        whoopieCushion.fartAudios = GetAudioClips(scrap.fartAudios, "Fart1", "Fart2", "Fart3", "Fart5");
    }

    public static AudioClip GetAudioClip(string audioClipName, string defaultAudioClipName)
    {
        AudioClip audioClip = null;
        if (!string.IsNullOrWhiteSpace(audioClipName))
        {
            AssetGather.Instance.audioClips.TryGetValue(audioClipName, out audioClip);
        }

        if (audioClip != null)
        {
            return audioClip;
        }
        
        return AssetGather.Instance.audioClips[defaultAudioClipName];
    }

    public static AudioClip[] GetAudioClips(string[] audioClipNames, params string[] defaultAudioClipNames)
    {
        List<AudioClip> audioClips = new List<AudioClip>();
        if (audioClipNames != null)
        {
            foreach (string audioClipName in audioClipNames)
            {
                if (AssetGather.Instance.audioClips.TryGetValue(audioClipName, out AudioClip audioClip))
                {
                    audioClips.Add(audioClip);
                }
            }
        }

        if (audioClips.Count > 0)
        {
            return audioClips.ToArray();
        }

        return defaultAudioClipNames
            .Select(audioClipName => AssetGather.Instance.audioClips[audioClipName])
            .ToArray();
    }

    public static void AddItemToScrap(Scrap scrap, Sprite scrapSprite)
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
        item.requiresBattery = scrap.requiresBattery;
        item.isConductiveMetal = scrap.isConductiveMetal;

        (bool twoHandedAnimation, string grabAnimation) = GetGrabAnimation(scrap);
        item.twoHandedAnimation = twoHandedAnimation;
        item.grabAnim = grabAnimation;

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

        GrabbableObject grabbableObject = AddGrabbableObject(scrap, item);
        grabbableObject.grabbable = true;
        grabbableObject.itemProperties = item;
        grabbableObject.mainObjectRenderer = scrap.prefab.GetComponent<MeshRenderer>();

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

    public static GrabbableObject AddGrabbableObject(Scrap scrap, Item item)
    {
        switch (scrap.scrapType)
        {
            case ScrapType.Normal:
                return scrap.prefab.AddComponent<PhysicsProp>();
            case ScrapType.Shovel:
                item.holdButtonUse = true;

                Shovel shovel = scrap.prefab.AddComponent<Shovel>();
                shovel.shovelHitForce = scrap.shovelHitForce;
                shovel.shovelAudio = scrap.shovelAudio ?? scrap.prefab.GetComponent<AudioSource>() ?? scrap.prefab.AddComponent<AudioSource>();
                if (scrap.prefab.GetComponent<OccludeAudio>() == null)
                {
                    scrap.prefab.AddComponent<OccludeAudio>();
                }
                return shovel;
            case ScrapType.Flashlight:
                FlashlightItem flashlight = scrap.prefab.AddComponent<FlashlightItem>();
                flashlight.usingPlayerHelmetLight = scrap.usingPlayerHelmetLight;
                flashlight.flashlightInterferenceLevel = scrap.flashlightInterferenceLevel;

                flashlight.flashlightBulb = scrap.flashlightBulb;
                if (flashlight.flashlightBulb == null)
                {
                    flashlight.flashlightBulb = new Light();
                    flashlight.flashlightBulb.intensity = 0;
                }

                flashlight.flashlightBulbGlow = scrap.flashlightBulbGlow;
                if (flashlight.flashlightBulbGlow == null)
                {
                    flashlight.flashlightBulbGlow = new Light();
                    flashlight.flashlightBulbGlow.intensity = 0;
                }

                flashlight.flashlightAudio = scrap.flashlightAudio ?? scrap.prefab.GetComponent<AudioSource>() ?? scrap.prefab.AddComponent<AudioSource>();
                if (scrap.prefab.GetComponent<OccludeAudio>() == null)
                {
                    scrap.prefab.AddComponent<OccludeAudio>();
                }

                flashlight.bulbLight = scrap.bulbLight ?? new Material(Shader.Find("HDRP/Lit"));
                flashlight.bulbDark = scrap.bulbDark ?? new Material(Shader.Find("HDRP/Lit"));
                flashlight.flashlightMesh = scrap.flashlightMesh ?? flashlight.mainObjectRenderer;
                flashlight.flashlightTypeID = scrap.flashlightTypeID;
                flashlight.changeMaterial = scrap.changeMaterial;
                return flashlight;
            case ScrapType.Noisemaker:
                NoisemakerProp noisemaker = scrap.prefab.AddComponent<NoisemakerProp>();
                noisemaker.noiseAudio = scrap.noiseAudio ?? scrap.prefab.GetComponent<AudioSource>() ?? scrap.prefab.AddComponent<AudioSource>();
                noisemaker.noiseAudioFar = scrap.noiseAudioFar;
                noisemaker.noiseRange = scrap.noiseRange;
                noisemaker.maxLoudness = scrap.maxLoudness;
                noisemaker.minLoudness = scrap.minLoudness;
                noisemaker.maxPitch = scrap.maxPitch;
                noisemaker.triggerAnimator = scrap.triggerAnimator;
                return noisemaker;
            case ScrapType.WhoopieCushion:
                WhoopieCushionItem whoopieCushion = scrap.prefab.AddComponent<WhoopieCushionItem>();
                whoopieCushion.whoopieCushionAudio = scrap.whoopieCushionAudio ?? scrap.prefab.GetComponent<AudioSource>() ?? scrap.prefab.AddComponent<AudioSource>();
                return whoopieCushion;
        }

        LethalExpansion.Log.LogWarning($"Unknown scrap type '{(int)scrap.scrapType}' for scrap '{scrap.itemName}'");
        return scrap.prefab.AddComponent<PhysicsProp>();
    }

    public static (bool, string) GetGrabAnimation(Scrap scrap)
    {
        switch (scrap.HandedAnimation)
        {
            case GrabAnim.OneHanded:
                return (false, string.Empty);
            case GrabAnim.TwoHanded:
                return (true, "HoldLung");
            case GrabAnim.Shotgun:
                return (true, "HoldShotgun");
            case GrabAnim.Jetpack:
                return (true, "HoldJetpack");
            case GrabAnim.Clipboard:
                return (false, "GrabClipboard");
            default:
                return (false, string.Empty);
        }
    }
}
