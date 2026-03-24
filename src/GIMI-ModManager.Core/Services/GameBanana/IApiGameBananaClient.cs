using GIMI_ModManager.Core.Services.GameBanana.ApiModels;
using GIMI_ModManager.Core.Services.GameBanana.Models;

namespace GIMI_ModManager.Core.Services.GameBanana;

public interface IApiGameBananaClient
{
    /// <summary>
    /// Checks if the GameBanana API is reachable.
    /// </summary>
    public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the mod profile from the GameBanana API.
    /// </summary>
    /// <param name="modId">The Game banana's mod Id</param>
    /// <param name="cancellationToken"></param>
    /// <returns>ApiModProfile if mod exists or null</returns>
    public Task<ApiModProfile?> GetModProfileAsync(GbModId modId, string modelName = "Mod", CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the mod files info from the GameBanana API.
    /// </summary>
    /// <param name="modId">The Game banana's mod Id</param>
    /// <param name="cancellationToken"></param>
    /// <returns>ApiModFilesInfo if mod exists or null</returns>
    public Task<ApiModFilesInfo?> GetModFilesInfoAsync(GbModId modId, string modelName = "Mod", CancellationToken cancellationToken = default);


    /// <summary>
    /// Gets the mod file info from the GameBanana API.
    /// </summary>
    /// <param name="modId">The Game banana's mod Id</param>
    /// <param name="modFileId">The Game banana's mod files Id</param>
    /// <param name="cancellationToken"></param>
    /// <returns>ApiModFileInfo if file exists or null</returns>
    [Obsolete("Use GetModFilesInfoAsync instead")]
    public Task<ApiModFileInfo?> GetModFileInfoAsync(GbModId modId, GbModFileId modFileId, string modelName = "Mod", CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the mod file exists on GameBanana.
    /// </summary>
    public Task<bool> ModFileExists(GbModFileId modFileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Download mod file from GameBanana.
    /// </summary>
    /// <param name="modFileId">The Game banana's mod files Id</param>
    /// <param name="destinationFile">File  stream to write the contents to</param>
    /// <param name="progress">Reports to as a percentage from 0 to 100</param>
    /// <param name="cancellationToken">Cancels the download but does not delete the destinationFile</param>
    /// <exception cref="InvalidOperationException">When mod is not found</exception>
    /// <exception cref="HttpRequestException"></exception>
    public Task DownloadModAsync(GbModFileId modFileId, FileStream destinationFile, IProgress<int>? progress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Download mod file from GameBanana using the direct download URL from _sDownloadUrl.
    /// </summary>
    public Task DownloadModByUrlAsync(string downloadUrl, FileStream destinationFile, IProgress<int>? progress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the sub-categories (e.g. character names) under a parent category for a game.
    /// </summary>
    public Task<List<ApiCategoryItem>> GetCategoriesAsync(string parentCategoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets top-level mod categories for a game (e.g. Skins, UI, Objects).
    /// </summary>
    public Task<List<ApiCategoryItem>> GetCategoriesForGameAsync(string gameId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the game's mod subfeed (home page mods).
    /// </summary>
    public Task<List<ApiModRecord>> GetGameSubfeedAsync(string gameId, string sort = "default", int page = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets mods filtered by a specific category.
    /// </summary>
    public Task<List<ApiModRecord>> GetModsByCategoryAsync(string categoryId, int page = 1, int perPage = 15, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for mods on GameBanana.
    /// </summary>
    public Task<List<ApiModRecord>> SearchModsAsync(string gameId, string query, string? modelName = null, int page = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the recent updates for a mod.
    /// </summary>
    public Task<List<ApiModUpdate>> GetModUpdatesAsync(string modId, string modelName = "Mod", CancellationToken cancellationToken = default);
}