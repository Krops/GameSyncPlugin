using System;
using System.Collections.Generic;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace GameSyncPlugin
{
    public class GameSyncPluginSettings : ObservableObject
    {
        private bool syncAndImportActiveGames = false;

        public bool SyncAndImportActiveGames
        {
            get => syncAndImportActiveGames;
            set => SetValue(ref syncAndImportActiveGames, value);
        }
    }

    public class GameSyncPluginSettingsViewModel : ObservableObject, ISettings
    {
        private readonly GameSyncPlugin plugin;
        private GameSyncPluginSettings? editingClone;

        private GameSyncPluginSettings settings;
        public GameSyncPluginSettings Settings
        {
            get => settings;
            set => SetValue(ref settings, value);
        }

        public GameSyncPluginSettingsViewModel(GameSyncPlugin plugin)
        {
            this.plugin = plugin;
            var savedSettings = plugin.LoadPluginSettings<GameSyncPluginSettings>();
            if (savedSettings != null)
            {
                settings = savedSettings;
            }
            else
            {
                settings = new GameSyncPluginSettings();
            }
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            if (editingClone != null)
            {
                Settings = editingClone;
            }
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }
    }
}
