# Playnite GameSyncPlugin - Database Reset & Launcher Sync

A C# Generic Plugin for [Playnite](https://playnite.link/) that allows you to purge all games from your Playnite database and trigger a full re-synchronization across all enabled game launchers (Steam, Epic, GOG, EA, Ubisoft, Xbox, etc.) with a single click.

## Features

- 🔘 **Main Menu Integration**: Adds a **"Reset Database & Sync Game Launchers"** option directly in the main Playnite menu (accessible in both **Desktop** and **Fullscreen** modes).
- 🗑️ **Database Clean Purge**: Uses `BufferedUpdate()` to quickly and safely remove all existing game entries from the Playnite database.
- 🔄 **Launcher Re-Sync**: Queries all active `LibraryPlugin` extensions and re-imports games for each launcher.
- 📊 **Progress Overlay & Summary**: Displays a cancellable global progress dialog during sync and a final notification summary with stats on removed and re-imported titles.
- ⚠️ **Safety Prompt**: Confirms before execution to prevent accidental database resets.

## Installation

### Option 1: Install `.pext` Package
1. Double-click [GameSyncPlugin.pext](file:///c:/Users/andre/Documents/Projects/GameSyncPlugin/GameSyncPlugin.pext) or drag it into Playnite.
2. Confirm the installation prompt in Playnite.
3. Restart Playnite when prompted.

### Option 2: Manual Installation
1. Open Playnite's Extensions folder:
   - Default path: `%APPDATA%\Playnite\Extensions\`
2. Create a folder named `GameSyncPlugin_e8411d37-23b9-4a9b-9c71-08f237583a12`.
3. Copy the contents of `bin/Release/net6.0-windows/` (`GameSyncPlugin.dll`, `extension.yaml`, `icon.png`) into that folder.
4. Restart Playnite.

## How to Use

1. Open Playnite in either **Desktop** or **Fullscreen** mode.
2. Click the Main Menu button (top-left logo or menu button).
3. Select **"Reset Database & Sync Game Launchers"**.
4. Confirm the warning dialog.
5. The plugin will clear all games and re-import all titles from your configured launcher integrations.

## Building from Source

Requirements:
- .NET 6.0 SDK or newer

```bash
dotnet build -c Release
```
