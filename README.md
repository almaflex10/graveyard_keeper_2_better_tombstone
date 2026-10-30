# Better Graves – Graveyard Keeper 2 Mod

Adds two new grave items to the game:

| Item | Group | Quality | Based on |
|---|---|---|---|
| **Ornate Marble Cross** | tombstone/cross | **10** | Marble Cross II (7) |
| **Ornate Marble Fence** | fence | **10** | Marble Fence II (7) |

Grave quality continues to use the game's original formula: **cross + fence − red skulls**, capped at the number of **white skulls** on the body.
A body with 20 white skulls can therefore have a grave quality of up to 20, while one with 14 can reach up to 14.

## How to get them

- Craft them at the same workbench as the Marble Cross II and Marble Fence II.
- They use the same materials, but require **1.5 times the quantity** (rounded up).
- They unlock at the same time as their vanilla Marble II counterparts (the Marble Grave Markers II technology).
- Place them in and remove them from graves just like any other cross or fence.

Note: They look the same as the Marble II versions in-game because they use the same images. Their names and the quality shown in their tooltips distinguish them.

## Installation

1. Install **BepInEx 5.4.23.5 x64**: <https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5>
   (`BepInEx_win_x64_5.4.23.5.zip`). Extract it next to `GraveyardKeeper2.exe`, launch the game once, then exit.
2. Extract the contents of `BetterGraves-1.0.0.zip` into the game folder. The resulting path should be:
   `Graveyard Keeper 2\BepInEx\plugins\BetterGraves\BetterGraves.dll`
3. Launch the game. The `BepInEx\LogOutput.log` file should contain lines like these:
   ```
   Better Graves 1.0.0 loaded (quality: 10, cost multiplier: 1.5).
   Added 'bettergraves_cross' (based on '...', quality 7 -> 10), N recipe(s) cloned.
   Added 'bettergraves_fence' (based on '...', quality 7 -> 10), N recipe(s) cloned.
   ```

## Configuration

After the first launch, the configuration file is created at `BepInEx\config\hu.burni.bettergraves.cfg`:

| Setting | Default | Description |
|---|---|---|
| `Quality` | `10` | Quality of each new cross and fence |
| `CostMultiplier` | `1.5` | Material cost multiplier compared with the Marble II versions |

Changes take effect the next time you launch the game.

## Before uninstalling

Without the mod, the game will not recognize these new items. **Remove them from all graves and chests before uninstalling the mod**, or your save will contain unknown items.

## Building from source (optional; the ZIP already includes the compiled DLL)

1. Install the .NET SDK (version 8 or later): <https://dotnet.microsoft.com/download>
2. From the project folder, run:
   ```
   dotnet build -c Release
   ```
   If the game is not installed in the default Steam folder:
   ```
   dotnet build -c Release -p:GamePath="D:\Games\Graveyard Keeper 2"
   ```
   The compiled DLL is automatically copied to the game's `BepInEx\plugins\BetterGraves` folder.

## How it works

Everything is in [src/Plugin.cs](src/Plugin.cs).

- When the game starts, before it builds its item and recipe caches (`GameBalance.InitCache`), the mod finds the best craftable item in the `gravetop` (cross) and `gravebot` (fence) groups and clones it with a new ID.
- It also clones every recipe that references those items, including crafting, placing items in graves, and removing them from graves.
- The new recipes unlock alongside their vanilla counterparts (`KnowledgeSystem.unlockedCrafts`).
- Item names and descriptions are provided through the game's localization function (`LLBase.L`) and are always in English.

Tested against the game's files (`Assembly-CSharp.dll`), October 2026.
