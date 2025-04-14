This Mod is still in BETA
============

EntranceTeleportOptimizations
============
[![GitHub Release](https://img.shields.io/github/v/release/mattymatty97/LTC_EntranceTeleportOptimizations?display_name=release&logo=github&logoColor=white)](https://github.com/mattymatty97/LTC_EntranceTeleportOptimizations/releases/latest)
[![GitHub Pre-Release](https://img.shields.io/github/v/release/mattymatty97/LTC_EntranceTeleportOptimizations?include_prereleases&display_name=release&logo=github&logoColor=white&label=preview)](https://github.com/mattymatty97/LTC_EntranceTeleportOptimizations/releases)  
[![Thunderstore Downloads](https://img.shields.io/thunderstore/dt/mattymatty/EntranceTeleportOptimizations?style=flat&logo=thunderstore&logoColor=white&label=thunderstore)](https://thunderstore.io/c/lethal-company/p/mattymatty/EntranceTeleportOptimizations/)

**EntranceTeleportOptimizations** is a focused on the performance of the `EntranceTeleport` system (FireExits & Main).  
It improves performance during teleport usage and dungeon generation when using mods like **Loadstone**.

---

## ✱ Optimizations

### • Cached Teleports
All `EntranceTeleport` instances are cached in an internal list upon spawn and removed on destroy.  
This replaces expensive `GetObjectsByType<EntranceTeleport>()` calls with fast, direct list access.

### • Smarter ExitPoint Lookup
- Normally, `EntranceTeleport` searches for its `exitPoint` on every use, which is costly.  
  This mod stops the search early if the teleport already knows its exit.  
  A reference to the exit script is cached and is used for sanity checks to avoid issues caused by runtime modifications from other mods.
- **Exterior** teleports previously ran this search every frame while the moon was loading—this mod removes that overhead by assigning the exit after dungeon generation (After `RoundManager.SetExitIDs`).

### • ID Assignment Fix
Some custom interiors use incorrect prefabs for FireExits with already-assigned IDs ( should be ID `1` except main that should have ID `0` ).  
This can cause issues like:
- The infamous *"The entrance appears to be blocked."*
- Warping to the wrong fire exit

This mod forces all **interior** teleports to use ID `1`, so the vanilla game can assign IDs correctly.

### • NRE Fix
The vanilla game sometimes creates an NRE spam when an enemy is destroyed without being removed from the internal `SpawnedEnemyList`

This mod adds a `null` check to prevent this exception

---

## ✦ Extra Features

### 🔸 Enemy Detection: `[Near activity detected!]`
- **Exterior Detection**:  
  Vanilla only checks enemies *inside* the facility—this mod optionally allows detection of *outside* enemies.
- **Configurable Range**:  
  Detection ranges for both interior and exterior can be customized.

---

## ⚙️ Developer Features

### 🔸 Matching Teleport Names
If enabled, the mod renames interior `EntranceTeleports` to match their exterior counterpart, appending ` (Interior)` for clarity.

---

## 📦 Installation

This mod is intended to be installed using a mod manager such as [Gale](https://thunderstore.io/package/Kesomannen/GaleModManager/).  
Manual installation is not officially supported.
