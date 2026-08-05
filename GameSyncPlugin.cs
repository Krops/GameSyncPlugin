using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace GameSyncPlugin
{
    public class GameSyncPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public override Guid Id { get; } = Guid.Parse("e8411d37-23b9-4a9b-9c71-08f237583a12");

        public GameSyncPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties
            {
                HasSettings = false
            };
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            return new List<MainMenuItem>
            {
                new MainMenuItem
                {
                    Description = "Reset Database & Sync Game Launchers",
                    MenuSection = "@",
                    Action = (mainMenuItemArgs) =>
                    {
                        ResetDatabaseAndSync();
                    }
                }
            };
        }

        private void ResetDatabaseAndSync()
        {
            var result = PlayniteApi.Dialogs.ShowMessage(
                "Are you sure you want to remove ALL games from the database and resync all game launchers?\n\nThis action cannot be undone.",
                "Reset Database & Resync",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            int removedCount = 0;
            int importedCount = 0;
            int duplicatesRemoved = 0;

            try
            {
                // 1. Purge all games from database
                using (PlayniteApi.Database.BufferedUpdate())
                {
                    var gamesToRemove = PlayniteApi.Database.Games.ToList();
                    removedCount = gamesToRemove.Count;
                    foreach (var game in gamesToRemove)
                    {
                        PlayniteApi.Database.Games.Remove(game);
                    }
                }

                logger.Info($"GameSyncPlugin: Removed {removedCount} games from database.");

                // 2. Fetch active LibraryPlugins and MetadataPlugins
                var libraryPlugins = PlayniteApi.Addons.Plugins
                    .OfType<LibraryPlugin>()
                    .ToList();

                var metadataPlugins = PlayniteApi.Addons.Plugins
                    .OfType<MetadataPlugin>()
                    .ToList();

                var progressOptions = new GlobalProgressOptions("Syncing game launchers & downloading metadata...", true)
                {
                    IsIndeterminate = false
                };

                PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                {
                    for (int i = 0; i < libraryPlugins.Count; i++)
                    {
                        if (progressArgs.CancelToken.IsCancellationRequested)
                        {
                            break;
                        }

                        var plugin = libraryPlugins[i];
                        progressArgs.Text = $"Syncing {plugin.Name} ({i + 1}/{libraryPlugins.Count})...";

                        try
                        {
                            var getGamesArgs = new LibraryGetGamesArgs();
                            var rawGameMetadataList = plugin.GetGames(getGamesArgs)?.ToList();

                            if (rawGameMetadataList != null && rawGameMetadataList.Count > 0)
                            {
                                // De-duplicate games by Name within the same library plugin
                                var uniqueMetadataList = DeduplicateGameMetadata(rawGameMetadataList);

                                using (var downloader = plugin.GetMetadataDownloader())
                                {
                                    using (PlayniteApi.Database.BufferedUpdate())
                                    {
                                        foreach (var metadata in uniqueMetadataList)
                                        {
                                            if (progressArgs.CancelToken.IsCancellationRequested)
                                            {
                                                break;
                                            }

                                            if (metadata != null)
                                            {
                                                // Enrich metadata with cover images, icons, and backgrounds from library or IGDB (automatic background mode)
                                                EnrichMetadata(metadata, plugin, downloader, metadataPlugins);

                                                // Import EXACTLY ONCE per unique game title
                                                PlayniteApi.Database.ImportGame(metadata, plugin);
                                                importedCount++;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, $"Failed to sync launcher/library plugin: {plugin.Name}");
                        }
                    }

                    // 3. Final global deduplication pass across all imported launchers
                    progressArgs.Text = "Performing global deduplication cleanup...";
                    duplicatesRemoved = DeduplicateDatabaseGames();

                }, progressOptions);

                PlayniteApi.Dialogs.ShowMessage(
                    $"Successfully reset database and resynced library!\n\n" +
                    $"• Removed games: {removedCount}\n" +
                    $"• Re-imported games: {importedCount}\n" +
                    $"• Duplicates merged: {duplicatesRemoved}",
                    "Database Reset & Resync Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error during database reset and sync.");
                PlayniteApi.Dialogs.ShowMessage(
                    $"An error occurred during database reset and sync:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private int DeduplicateDatabaseGames()
        {
            int duplicatesRemoved = 0;

            using (PlayniteApi.Database.BufferedUpdate())
            {
                var allGames = PlayniteApi.Database.Games.ToList();

                var groupedGames = allGames
                    .Where(g => g != null && !string.IsNullOrWhiteSpace(g.Name))
                    .GroupBy(g => CleanGameName(g.Name).ToLowerInvariant())
                    .Where(g => g.Count() > 1)
                    .ToList();

                foreach (var group in groupedGames)
                {
                    // Select best primary game to keep: Prefer Installed > Has Cover > Has Icon > First
                    var primaryGame = group
                        .OrderByDescending(g => g.IsInstalled)
                        .ThenByDescending(g => !string.IsNullOrEmpty(g.CoverImage))
                        .ThenByDescending(g => !string.IsNullOrEmpty(g.Icon))
                        .ThenByDescending(g => !string.IsNullOrEmpty(g.BackgroundImage))
                        .First();

                    var duplicates = group.Where(g => g.Id != primaryGame.Id).ToList();

                    foreach (var duplicate in duplicates)
                    {
                        if (duplicate.IsInstalled && !primaryGame.IsInstalled)
                        {
                            primaryGame.IsInstalled = true;
                            if (!string.IsNullOrEmpty(duplicate.InstallDirectory))
                            {
                                primaryGame.InstallDirectory = duplicate.InstallDirectory;
                            }
                        }

                        if (string.IsNullOrEmpty(primaryGame.CoverImage) && !string.IsNullOrEmpty(duplicate.CoverImage))
                        {
                            primaryGame.CoverImage = duplicate.CoverImage;
                        }

                        if (string.IsNullOrEmpty(primaryGame.Icon) && !string.IsNullOrEmpty(duplicate.Icon))
                        {
                            primaryGame.Icon = duplicate.Icon;
                        }

                        if (string.IsNullOrEmpty(primaryGame.BackgroundImage) && !string.IsNullOrEmpty(duplicate.BackgroundImage))
                        {
                            primaryGame.BackgroundImage = duplicate.BackgroundImage;
                        }

                        if (string.IsNullOrEmpty(primaryGame.Description) && !string.IsNullOrEmpty(duplicate.Description))
                        {
                            primaryGame.Description = duplicate.Description;
                        }

                        PlayniteApi.Database.Games.Remove(duplicate);
                        duplicatesRemoved++;
                    }

                    PlayniteApi.Database.Games.Update(primaryGame);
                }
            }

            return duplicatesRemoved;
        }

        private List<GameMetadata> DeduplicateGameMetadata(List<GameMetadata> rawList)
        {
            var uniqueList = new List<GameMetadata>();

            foreach (var raw in rawList)
            {
                if (raw == null || string.IsNullOrWhiteSpace(raw.Name))
                {
                    continue;
                }

                var normalizedName = CleanGameName(raw.Name);
                var existing = uniqueList.FirstOrDefault(m =>
                    string.Equals(CleanGameName(m.Name), normalizedName, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    uniqueList.Add(raw);
                }
                else
                {
                    if (raw.IsInstalled)
                    {
                        existing.IsInstalled = true;
                        if (!string.IsNullOrEmpty(raw.InstallDirectory))
                        {
                            existing.InstallDirectory = raw.InstallDirectory;
                        }
                    }

                    if (existing.CoverImage == null && raw.CoverImage != null)
                    {
                        existing.CoverImage = raw.CoverImage;
                    }

                    if (existing.Icon == null && raw.Icon != null)
                    {
                        existing.Icon = raw.Icon;
                    }

                    if (existing.BackgroundImage == null && raw.BackgroundImage != null)
                    {
                        existing.BackgroundImage = raw.BackgroundImage;
                    }

                    if (!string.IsNullOrEmpty(raw.GameId) && existing.GameId != null && existing.GameId.Contains("_") && !raw.GameId.Contains("_"))
                    {
                        existing.GameId = raw.GameId;
                    }
                }
            }

            return uniqueList;
        }

        private void EnrichMetadata(GameMetadata metadata, LibraryPlugin libraryPlugin, LibraryMetadataProvider libraryDownloader, List<MetadataPlugin> metadataPlugins)
        {
            var tempGame = new Game
            {
                Name = CleanGameName(metadata.Name),
                GameId = metadata.GameId,
                PluginId = libraryPlugin.Id,
                InstallDirectory = metadata.InstallDirectory
            };

            // 1. Try library metadata downloader first
            if (libraryDownloader != null)
            {
                try
                {
                    var fullMetadata = libraryDownloader.GetMetadata(tempGame);
                    if (fullMetadata != null)
                    {
                        if (fullMetadata.CoverImage != null) metadata.CoverImage = fullMetadata.CoverImage;
                        if (fullMetadata.Icon != null) metadata.Icon = fullMetadata.Icon;
                        if (fullMetadata.BackgroundImage != null) metadata.BackgroundImage = fullMetadata.BackgroundImage;
                        if (!string.IsNullOrEmpty(fullMetadata.Description)) metadata.Description = fullMetadata.Description;
                        if (fullMetadata.Genres != null && fullMetadata.Genres.Count > 0) metadata.Genres = fullMetadata.Genres;
                        if (fullMetadata.Developers != null && fullMetadata.Developers.Count > 0) metadata.Developers = fullMetadata.Developers;
                        if (fullMetadata.Publishers != null && fullMetadata.Publishers.Count > 0) metadata.Publishers = fullMetadata.Publishers;
                        if (fullMetadata.ReleaseDate != null) metadata.ReleaseDate = fullMetadata.ReleaseDate;
                        if (fullMetadata.Links != null && fullMetadata.Links.Count > 0) metadata.Links = fullMetadata.Links;
                        if (fullMetadata.Features != null && fullMetadata.Features.Count > 0) metadata.Features = fullMetadata.Features;
                        if (fullMetadata.Tags != null && fullMetadata.Tags.Count > 0) metadata.Tags = fullMetadata.Tags;
                        if (fullMetadata.AgeRatings != null && fullMetadata.AgeRatings.Count > 0) metadata.AgeRatings = fullMetadata.AgeRatings;
                        if (fullMetadata.Series != null && fullMetadata.Series.Count > 0) metadata.Series = fullMetadata.Series;
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, $"Library downloader failed for game: {metadata.Name}");
                }
            }

            // 2. If cover image or icon is missing, fallback to MetadataPlugins (e.g. IGDB) in fully automatic background mode (isBackgroundDownload = true)
            if ((metadata.CoverImage == null || metadata.Icon == null) && metadataPlugins != null && metadataPlugins.Count > 0)
            {
                foreach (var metaPlugin in metadataPlugins)
                {
                    try
                    {
                        // CRITICAL: isBackgroundDownload = true forces automatic top-match selection without manual picker UI!
                        var requestOptions = new MetadataRequestOptions(tempGame, true);
                        using (var metaProvider = metaPlugin.GetMetadataProvider(requestOptions))
                        {
                            if (metaProvider != null)
                            {
                                var args = new GetMetadataFieldArgs();

                                if (metadata.CoverImage == null)
                                {
                                    var cover = metaProvider.GetCoverImage(args);
                                    if (cover != null)
                                    {
                                        metadata.CoverImage = cover;
                                    }
                                }

                                if (metadata.Icon == null)
                                {
                                    var icon = metaProvider.GetIcon(args);
                                    if (icon != null)
                                    {
                                        metadata.Icon = icon;
                                    }
                                }

                                if (metadata.BackgroundImage == null)
                                {
                                    var bg = metaProvider.GetBackgroundImage(args);
                                    if (bg != null)
                                    {
                                        metadata.BackgroundImage = bg;
                                    }
                                }

                                if (string.IsNullOrEmpty(metadata.Description))
                                {
                                    var desc = metaProvider.GetDescription(args);
                                    if (!string.IsNullOrEmpty(desc))
                                    {
                                        metadata.Description = desc;
                                    }
                                }

                                if (metadata.CoverImage != null)
                                {
                                    break; // Automatically obtained cover image
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, $"MetadataPlugin {metaPlugin.Name} automatic search failed for game: {metadata.Name}");
                    }
                }
            }
        }

        private string CleanGameName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return name.Replace("™", "")
                       .Replace("®", "")
                       .Replace("©", "")
                       .Trim();
        }
    }
}
