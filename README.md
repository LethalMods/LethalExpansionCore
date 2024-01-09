# LethalExpansionCore
This is a heavily de-bloated fork of [LethalExpansion](https://github.com/HolographicWings/LethalExpansion) down to the very core (adding scraps and moons with [LethalSDK](https://github.com/HolographicWings/LethalSDK-Unity-Project) modules). It acts as a simple library and does not add any features on its own\*. This is a drop in replacement for LethalExpansion and should be compatible with any mod relying on it. This is fully "compatible" with LethalExpansion, when both of the plugins are installed at the same time LethalExpansion will automatically be disabled unless the `UseOriginalLethalExpansion` setting is set to `true` in which case LethalExpansionCore will be disabled instead

\*There may still be some parts which are not required but the vast majority has been removed (I just haven't figured out what everything does yet)

## Thunderstore
[LethalExpansionCore](https://thunderstore.io/c/lethal-company/p/jockie/LethalExpansionCore/)

The default modules from LethalExpansion are also available in a separate package if you want or need them!  
[LethalExpansionCoreModules](https://thunderstore.io/c/lethal-company/p/jockie/LethalExpansionCoreModules/)

## Features
- Support to load modules made with [LethalSDK](https://github.com/HolographicWings/LethalSDK-Unity-Project) to add new scraps and moons.

## Settings
* `IgnoreRequiredBundles`: Whether or not to allow a bundle to load without its required bundles, default `true`  
* `UseOriginalLethalExpansion`: Whether or not to use the original LethalExpansion instead of LethalExpansionCore when they are both loaded, default `false`  
* `LoadDefaultBundles`: Whether or not to load the default bundles from LethalExpansion when both LethalExpansion and LethalExpansionCore are present, default `false`  

## Changes
* 1.3.9
	* LethalLevelLoader compatibility hotfix
* 1.3.8
	* Fixed dungeon desync in online mode which caused players to fall into the void or have a different dungeon layout than the host
	* Fixed fire exits not working properly when the map is misconfigured and has duplicate entrance ids, it will no longer generate fire exits for entrances which has an id of another entrance
	* Improved compatibility with other mods by adding moons and items earlier in the process
* 1.3.7
	* Fixed fire exits not working properly
	* Fixed entrance desync which caused
		* Mobs to ignore clients in the dungeon
		* Lightning strikes inside the dungeon
		* Probably a whole lot of other issues
* 1.3.6
	* Added "compatibility" with LethalExpansion, when both of the plugins are installed at the same time LethalExpansion will automatically be disabled unless the `UseOriginalLethalExpansion` setting is set to `true` in which case LethalExpansionCore will be disabled instead. This means you can now have both as dependencies without any issues!

## Credits
All credit goes to the original mod and its creator!  
https://github.com/HolographicWings/LethalExpansion  
https://thunderstore.io/c/lethal-company/p/HolographicWings/LethalExpansion  