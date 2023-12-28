# LethalExpansionCore
This is a heavily de-bloated fork of [LethalExpansion](https://github.com/HolographicWings/LethalExpansion) down to the very core (adding scraps and moons with [LethalSDK](https://github.com/HolographicWings/LethalSDK-Unity-Project) modules). It acts as a simple library and does not add any features on its own\*. This is a drop in replacement for LethalExpansion and should be compatible with any mod relying on it. This is fully "compatible" with LethalExpansion, when both of the plugins are installed at the same time LethalExpansion will automatically be disabled unless the `UseOriginalLethalExpansion` setting is set to `true` in which case LethalExpansionCore will be disabled instead

\*There may still be some parts which are not required but the vast majority has been removed (I just haven't figured out what everything does yet)

The default modules from LethalExpansion are available in a separate package if you want or need them!  
[LethalExpansionCoreModules](https://thunderstore.io/c/lethal-company/p/jockie/LethalExpansionCoreModules)

## Features
- Support to load modules made with [LethalSDK](https://github.com/HolographicWings/LethalSDK-Unity-Project) to add new scraps and moons.

## Settings
* `IgnoreRequiredBundles`: Whether or not to allow a bundle to load without its required bundles, default `true`  
* `UseOriginalLethalExpansion`: Whether or not to use the original LethalExpansion instead of LethalExpansionCore when they are both loaded, default `false`  
* `LoadDefaultBundles`: Whether or not to load the default bundles from LethalExpansion when both LethalExpansion and LethalExpansionCore are present, default `false`  

## Changes
* 1.3.5.1
	* Added "compatibility" with LethalExpansion, when both of the plugins are installed at the same time LethalExpansion will automatically be disabled unless the `UseOriginalLethalExpansion` setting is set to `true` in which case LethalExpansionCore will be disabled instead. This means you can now have both as dependencies without any issues!

## Credits
All credit goes to the original mod and its creator!  
https://github.com/HolographicWings/LethalExpansion  
https://thunderstore.io/c/lethal-company/p/HolographicWings/LethalExpansion  