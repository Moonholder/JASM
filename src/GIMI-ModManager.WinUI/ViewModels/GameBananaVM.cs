using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Interfaces;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.Core.Services.GameBanana;
using GIMI_ModManager.Core.Services.GameBanana.ApiModels;
using GIMI_ModManager.Core.Services.GameBanana.Models;
using System.Text.Json;
using GIMI_ModManager.WinUI.Contracts.ViewModels;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Models.Settings;
using GIMI_ModManager.WinUI.Services;
using GIMI_ModManager.WinUI.Services.AppManagement;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.WinUI.Services.Notifications;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Serilog;

namespace GIMI_ModManager.WinUI.ViewModels;

public partial class GameBananaVM : ObservableRecipient, INavigationAware
{
    private readonly GameBananaCoreService _gbService = App.GetService<GameBananaCoreService>();
    private readonly IGameService _gameService = App.GetService<IGameService>();
    private readonly ISkinManagerService _skinManagerService = App.GetService<ISkinManagerService>();
    private readonly ModInstallerService _modInstallerService = App.GetService<ModInstallerService>();
    private readonly ArchiveService _archiveService = App.GetService<ArchiveService>();
    private readonly NotificationManager _notificationManager = App.GetService<NotificationManager>();
    private readonly IWindowManagerService _windowManagerService = App.GetService<IWindowManagerService>();
    private readonly ILocalSettingsService _localSettingsService = App.GetService<ILocalSettingsService>();
    private readonly ILogger _logger = App.GetService<ILogger>().ForContext<GameBananaVM>();
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();
    private readonly IGameBananaDownloadSessionService _downloadSessionService = App.GetService<IGameBananaDownloadSessionService>();

    private DispatcherQueue _dispatcherQueue = null!;

    // Static category cache — survives across Transient VM instances
    private static readonly Dictionary<string, List<GbCategoryDisplayItem>> _categoryCache = [];

    private string? _gameId;
    private int _currentPage = 1;
    private bool _hasMorePages = true;
    private CancellationTokenSource? _loadCts;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private const int MinItemsPerLoad = 10;
    private const int MaxBackfillIterations = 5;
    private bool _settingsLoaded;

    // ── Observable properties ──

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoadingMore;
    [ObservableProperty] private bool _isCategoriesLoading;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _selectedSort = "default";
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private GbNsfwDisplayPolicy _nsfwPolicy = GbNsfwDisplayPolicy.Blur;
    [ObservableProperty] private GbModelFilter _modelFilter = GbModelFilter.ModsOnly;
    [ObservableProperty] private bool _hasError;

    // Inverted loading for visibility binding
    [ObservableProperty] private Visibility _isNotLoading = Visibility.Visible;

    // Selected category (null = all / home)
    [ObservableProperty] private GbCategoryDisplayItem? _selectedCategory;

    // Mod detail pane
    [ObservableProperty] private bool _isDetailPaneOpen;
    [ObservableProperty] private GbModDisplayItem? _selectedMod;
    [ObservableProperty] private ModPageInfo? _selectedModDetail;
    [ObservableProperty] private bool _isDetailLoading;
    [ObservableProperty] private Visibility _hasModDetail = Visibility.Collapsed;

    // Detail pane display properties
    [ObservableProperty] private string _selectedModName;
    [ObservableProperty] private string _selectedModAuthor = string.Empty;
    [ObservableProperty] private string _selectedModLikes = string.Empty;
    [ObservableProperty] private string _selectedModViews = string.Empty;
    [ObservableProperty] private string _selectedModDate = string.Empty;
    [ObservableProperty] private string _selectedModDescription = string.Empty;
    [ObservableProperty] private string _selectedModUpdateLog = string.Empty;
    [ObservableProperty] private Visibility _hasDescription = Visibility.Collapsed;
    [ObservableProperty] private Visibility _hasUpdateLog = Visibility.Collapsed;

    [ObservableProperty] private Visibility _hasPreviewImages = Visibility.Collapsed;
    [ObservableProperty] private Visibility _hasFiles = Visibility.Collapsed;

    [ObservableProperty] private BitmapImage? _selectedModAuthorAvatar;

    public ObservableCollection<GbCategoryDisplayItem> Categories { get; } = new();
    public ObservableCollection<GbModDisplayItem> Mods { get; } = new();
    public ObservableCollection<GbPreviewImageItem> PreviewImages { get; } = new();
    public ObservableCollection<ModFileInfo> ModFiles { get; } = new();
    public ObservableCollection<GbDownloadTask> DownloadQueue => _downloadSessionService.DownloadQueue;
    public ObservableCollection<SortOption> SortOptions { get; }

    public ObservableCollection<FilterOption<GbModelFilter>> ModelFilterOptions { get; }

    public ObservableCollection<FilterOption<GbNsfwDisplayPolicy>> NsfwPolicyOptions { get; }

    public GameBananaVM()
    {
        _selectedModName = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/ModDetailTitle", "Mod Detail");

        SortOptions = new ObservableCollection<SortOption>
        {
            new(_localizer.GetLocalizedStringOrDefault("/GameBananaPage/SortDefault", "Default"), "default"),
            new(_localizer.GetLocalizedStringOrDefault("/GameBananaPage/SortNew", "Newest"), "new"),
            new(_localizer.GetLocalizedStringOrDefault("/GameBananaPage/SortUpdated", "Updated"), "updated")
        };

        ModelFilterOptions = new ObservableCollection<FilterOption<GbModelFilter>>
        {
            new(_localizer.GetLocalizedStringOrDefault("/GameBananaPage/FilterAll", "All Categories"), GbModelFilter.All),
            new(_localizer.GetLocalizedStringOrDefault("/GameBananaPage/FilterModsOnly", "Mods Only"), GbModelFilter.ModsOnly)
        };

        NsfwPolicyOptions = new ObservableCollection<FilterOption<GbNsfwDisplayPolicy>>
        {
            new(_localizer.GetLocalizedStringOrDefault("/GameBananaPage/NsfwRemove", "Hide NSFW"), GbNsfwDisplayPolicy.Remove),
            new(_localizer.GetLocalizedStringOrDefault("/GameBananaPage/NsfwBlur", "Blur NSFW"), GbNsfwDisplayPolicy.Blur),
            new(_localizer.GetLocalizedStringOrDefault("/GameBananaPage/NsfwShow", "Show NSFW"), GbNsfwDisplayPolicy.Show)
        };
    }

    // ── Selected ComboBox items (for proper SelectedItem sync) ──

    [ObservableProperty] private SortOption? _selectedSortOption;
    [ObservableProperty] private FilterOption<GbModelFilter>? _selectedModelFilterOption;
    [ObservableProperty] private FilterOption<GbNsfwDisplayPolicy>? _selectedNsfwPolicyOption;

    partial void OnIsLoadingChanged(bool value)
    {
        _dispatcherQueue?.TryEnqueue(() => IsNotLoading = value ? Visibility.Collapsed : Visibility.Visible);
    }

    [ObservableProperty] private int _activeDownloadCount;
    [ObservableProperty] private Visibility _hasActiveDownloads = Visibility.Collapsed;

    private void UpdateActiveDownloadCount(object? sender = null, System.Collections.Specialized.NotifyCollectionChangedEventArgs? e = null)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            var count = DownloadQueue.Count(t => !t.IsCompleted && !t.IsError);
            ActiveDownloadCount = count;
            HasActiveDownloads = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void OnActiveTasksChanged() => UpdateActiveDownloadCount();

    // ── Navigation ──

    public async void OnNavigatedTo(object parameter)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        var gameBananaUrl = _gameService.GameBananaUrl;
        _gameId = GameBananaCoreService.ExtractGameId(gameBananaUrl);

        if (_gameId == null)
        {
            ShowError(_localizer.GetLocalizedStringOrDefault("/GameBananaPage/ErrorGameId", "Failed to get GameBanana game ID"));
            return;
        }

        await LoadSettingsAsync();

        _ = LoadCategoriesAsync();
        _ = _downloadSessionService.LoadDownloadHistoryAsync();
        _ = LoadModsAsync(reset: true);

        GbDownloadTask.ActiveTasksChanged += OnActiveTasksChanged;
        DownloadQueue.CollectionChanged += UpdateActiveDownloadCount;
        UpdateActiveDownloadCount();
    }

    public void OnNavigatedFrom()
    {
        CancelLoad();
        _ = SaveSettingsAsync();

        GbDownloadTask.ActiveTasksChanged -= OnActiveTasksChanged;
        DownloadQueue.CollectionChanged -= UpdateActiveDownloadCount;
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await _localSettingsService.ReadOrCreateSettingAsync<GameBananaSettings>(
                GameBananaSettings.Key);
            if (settings != null)
            {
                SelectedSort = settings.SelectedSort;
                NsfwPolicy = (GbNsfwDisplayPolicy)settings.NsfwPolicy;
                ModelFilter = (GbModelFilter)settings.ModelFilter;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load GameBanana settings");
        }

        // Sync ComboBox selected items
        SelectedSortOption = SortOptions.FirstOrDefault(o => o.Value == SelectedSort) ?? SortOptions[0];
        SelectedModelFilterOption = ModelFilterOptions.FirstOrDefault(o => o.Value == ModelFilter) ?? ModelFilterOptions[1];
        SelectedNsfwPolicyOption = NsfwPolicyOptions.FirstOrDefault(o => o.Value == NsfwPolicy) ?? NsfwPolicyOptions[1];

        _settingsLoaded = true;
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = new GameBananaSettings
            {
                SelectedSort = SelectedSort,
                NsfwPolicy = (int)NsfwPolicy,
                ModelFilter = (int)ModelFilter,
            };
            await _localSettingsService.SaveSettingAsync(GameBananaSettings.Key, settings);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to save GameBanana settings");
        }
    }

    // ── Sort / filter change ──

    partial void OnSelectedSortOptionChanged(SortOption? value)
    {
        if (value == null) return;
        SelectedSort = value.Value;
        if (_gameId != null && _settingsLoaded)
        {
            _ = LoadModsAsync(reset: true);
            _ = SaveSettingsAsync();
        }
    }

    partial void OnSelectedNsfwPolicyOptionChanged(FilterOption<GbNsfwDisplayPolicy>? value)
    {
        if (value == null) return;
        NsfwPolicy = value.Value;
        if (_gameId != null && !IsLoading && _settingsLoaded)
        {
            _ = LoadModsAsync(reset: true);
            _ = SaveSettingsAsync();
        }
    }

    partial void OnSelectedModelFilterOptionChanged(FilterOption<GbModelFilter>? value)
    {
        if (value == null) return;
        ModelFilter = value.Value;
        if (_gameId != null && !IsLoading && _settingsLoaded)
        {
            _ = LoadModsAsync(reset: true);
            _ = SaveSettingsAsync();
        }
    }

    // ── Search with debounce ──

    partial void OnSearchQueryChanged(string value)
    {
        // Cancel debounce search, only assign the string internally.
        // We now rely on OnSearchSubmitted to trigger load.
    }

    public void OnSearchSubmitted(Microsoft.UI.Xaml.Controls.AutoSuggestBox sender, Microsoft.UI.Xaml.Controls.AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        SearchQuery = args.QueryText;

        if (!string.IsNullOrWhiteSpace(SearchQuery) && SelectedCategory != null)
        {
            // Null out the selected category so we fallback to Game Subfeed search
            SelectedCategory = null;
        }

        if (_gameId != null)
        {
            _ = LoadModsAsync(reset: true);
        }
    }

    // ── Category selection ──

    [RelayCommand]
    private async Task SelectCategoryAsync(GbCategoryDisplayItem? category)
    {
        SelectedCategory = category;
        SearchQuery = string.Empty;
        await LoadModsAsync(reset: true);
    }

    // ── Load categories ──
    // Strategy: Get top-level categories for game, find the "Skins" category (highest item count),
    // get its sub-categories, and if any have _nCategoryCount > 0 (like "Characters"), go deeper.

    private async Task LoadCategoriesAsync()
    {
        if (_gameId == null) return;

        // Use cached categories if available
        if (_categoryCache.TryGetValue(_gameId, out var cached))
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                Categories.Clear();
                var allName = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/AllCategories", "All");
                Categories.Add(new GbCategoryDisplayItem { Name = allName, CategoryId = null, ItemCount = 0 });
                foreach (var item in cached)
                {
                    Categories.Add(item);
                    item.LoadIcon();
                }
            });
            return;
        }

        IsCategoriesLoading = true;
        const int maxRetries = 3;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    // Exponential backoff: 2s, 4s, 8s
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.Information("Retrying categories load (attempt {Attempt}/{Max}) after {Delay}s",
                        attempt + 1, maxRetries + 1, delay.TotalSeconds);
                    await Task.Delay(delay);
                }

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var apiClient = _gbService.CreateApiGameBananaClient();

                // 1. Get top-level categories for the game (Skins, UI, Objects, etc.)
                var topLevelCategories = await apiClient.GetCategoriesForGameAsync(_gameId, cts.Token);

                // 2. Find the primary category (highest item count, usually "Skins")
                var primaryCategory = topLevelCategories
                    .OrderByDescending(c => c.ItemCount)
                    .FirstOrDefault();

                // 3. Build the final category list
                var finalList = new List<GbCategoryDisplayItem>();

                // Add top-level sections first
                foreach (var top in topLevelCategories.OrderByDescending(c => c.ItemCount))
                {
                    finalList.Add(new GbCategoryDisplayItem
                    {
                        Name = top.Name,
                        CategoryId = top.CategoryId.ToString(),
                        IconUrl = top.IconUrl,
                        ItemCount = top.ItemCount,
                        IsSection = true
                    });
                }

                // 4. Get character-level sub-categories under the primary category
                if (primaryCategory != null)
                {
                    var subCategories = await apiClient.GetCategoriesAsync(
                        primaryCategory.CategoryId.ToString(), cts.Token);

                    var characterCategories = new List<ApiCategoryItem>();
                    var deepCategories = subCategories.Where(c => c.CategoryCount > 0).ToList();

                    if (deepCategories.Count > 0)
                    {
                        // Go one level deeper (e.g. Skins → Characters → individual characters)
                        foreach (var deepCat in deepCategories)
                        {
                            try
                            {
                                var chars = await apiClient.GetCategoriesAsync(
                                    deepCat.CategoryId.ToString(), cts.Token);
                                characterCategories.AddRange(chars);
                            }
                            catch (Exception ex)
                            {
                                _logger.Warning(ex, "Failed to load sub-categories for {cat}", deepCat.Name);
                            }
                        }
                    }
                    else
                    {
                        // Sub-categories are already character-level
                        characterCategories = subCategories;
                    }

                    // Add character categories sorted alphabetically
                    foreach (var cat in characterCategories.OrderBy(c => c.Name))
                    {
                        finalList.Add(new GbCategoryDisplayItem
                        {
                            Name = cat.Name,
                            CategoryId = cat.CategoryId.ToString(),
                            IconUrl = cat.IconUrl,
                            ItemCount = cat.ItemCount,
                            IsSection = false
                        });
                    }
                }

                _dispatcherQueue.TryEnqueue(() =>
                {
                    Categories.Clear();
                    var allName = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/AllCategories", "All");
                    Categories.Add(new GbCategoryDisplayItem { Name = allName, CategoryId = null, ItemCount = 0 });
                    foreach (var item in finalList)
                    {
                        Categories.Add(item);
                        // Icons are now lazy-loaded by XAML ContainerContentChanging
                    }
                });

                // Save to cache
                _categoryCache[_gameId] = finalList;

                // Success — exit retry loop
                break;
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                _logger.Warning(ex, "Categories load failed (attempt {Attempt}/{Max}), will retry",
                    attempt + 1, maxRetries + 1);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to load categories");
                break; // Non-retryable or final attempt
            }
        }

        IsCategoriesLoading = false;
    }

    // ── Load mods ──

    private async Task LoadModsAsync(bool reset = false)
    {
        if (_gameId == null) return;

        if (reset)
        {
            CancelLoad();
            await _loadLock.WaitAsync().ConfigureAwait(false);
        }
        else
        {
            if (!await _loadLock.WaitAsync(0).ConfigureAwait(false))
                return;
        }

        try
        {
            CancelLoad();
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;

            if (reset)
            {
                _currentPage = 1;
                _hasMorePages = true;
                _dispatcherQueue.TryEnqueue(() =>
                {
                    Mods.Clear();
                    IsLoading = true;
                });
            }
            else
            {
                if (!_hasMorePages) return;
                _dispatcherQueue.TryEnqueue(() => IsLoadingMore = true);
            }

            _dispatcherQueue.TryEnqueue(() => HasError = false);

            try
            {
                var collected = await FetchAndFilterAsync(ct);

                if (collected.Count > 0)
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        foreach (var item in collected)
                        {
                            Mods.Add(item);
                            item.LoadThumbnail();
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on navigation away or new search
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load mods from GameBanana");
                ShowError(_localizer.GetLocalizedStringOrDefault("/GameBananaPage/ErrorLoadMods", "Failed to load mods, please check your network connection"));
            }
            finally
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    IsLoading = false;
                    IsLoadingMore = false;
                });
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Fetches pages from the API and applies client-side filters.
    /// Automatically back-fills by fetching additional pages if NSFW/model filtering
    /// removed too many items, ensuring at least <see cref="MinItemsPerLoad"/> results
    /// (or until no more pages are available).
    /// </summary>
    private async Task<List<GbModDisplayItem>> FetchAndFilterAsync(CancellationToken ct)
    {
        var collected = new List<GbModDisplayItem>();
        var iterations = 0;

        while (collected.Count < MinItemsPerLoad && _hasMorePages && iterations < MaxBackfillIterations)
        {
            ct.ThrowIfCancellationRequested();
            iterations++;

            var rawItems = await FetchPageAsync(_currentPage, ct);

            if (rawItems.Count == 0)
            {
                _hasMorePages = false;
                break;
            }

            // Apply client-side filters
            var filtered = ApplyFilters(rawItems);
            collected.AddRange(filtered);

            _currentPage++;
        }

        return collected;
    }

    /// <summary>
    /// Fetches a single page of raw mod records from the appropriate API endpoint.
    /// </summary>
    private async Task<List<GbModDisplayItem>> FetchPageAsync(int page, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string? modelName = ModelFilter == GbModelFilter.ModsOnly ? "Mod" : null;
            var results = await _gbService.SearchModsAsync(_gameId!, SearchQuery, modelName, page, ct);
            return results.Select(ToDisplayItem).ToList();
        }

        if (SelectedCategory?.CategoryId != null)
        {
            var results = await _gbService.GetModsByCategoryAsync(
                SelectedCategory.CategoryId, page, 15, ct);
            return results.Select(ToDisplayItem).ToList();
        }

        var subfeed = await _gbService.GetGameSubfeedAsync(_gameId!, SelectedSort, page, ct);
        return subfeed.Select(ToDisplayItem).ToList();
    }

    /// <summary>
    /// Applies NSFW and model-type filters to a list of display items.
    /// </summary>
    private List<GbModDisplayItem> ApplyFilters(List<GbModDisplayItem> items)
    {
        IEnumerable<GbModDisplayItem> result = items;

        if (NsfwPolicy == GbNsfwDisplayPolicy.Remove)
            result = result.Where(i => !i.IsNsfw);

        if (ModelFilter == GbModelFilter.ModsOnly)
            result = result.Where(i => i.ModelName == "Mod");

        return result.ToList();
    }

    private GbModDisplayItem ToDisplayItem(ApiModRecord r) => new()
    {
        ModId = r.ModId,
        Name = r.Name,
        ModelName = r.ModelName,
        ProfileUrl = r.ProfileUrl,
        ThumbnailUrl = r.ThumbnailUrl ?? string.Empty,
        AuthorName = r.Submitter?.Name ?? "Unknown",
        AuthorAvatarUrl = r.Submitter?.AvatarUrl ?? string.Empty,
        LikeCount = r.LikeCount,
        DownloadCount = r.ViewCount, // API provides ViewCount, not downloads
        ViewCount = r.ViewCount,
        CommentCount = r.PostCount,
        DateAdded = r.DateAdded,
        DateUpdated = r.DateModified,
        IsNsfw = r.InitialVisibility == "hide",
        NsfwPolicy = NsfwPolicy,
    };

    // ── Load more (pagination) ──

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (!_hasMorePages || IsLoading || IsLoadingMore) return;
        await LoadModsAsync(reset: false);
    }

    /// <summary>
    /// Called by the View's ScrollViewer when scroll position changes.
    /// Triggers loading more items when approaching the bottom.
    /// </summary>
    public void NotifyScrollPosition(double verticalOffset, double scrollableHeight, double viewportHeight)
    {
        if (IsLoading || IsLoadingMore || !_hasMorePages) return;

        // Content doesn't fill the viewport (no scrollbar) — auto-load more to fill
        if (scrollableHeight <= 0)
        {
            _ = LoadMoreAsync();
            return;
        }

        // Trigger pre-fetch when within 2 viewport-heights of the bottom
        var distanceToBottom = scrollableHeight - verticalOffset;
        if (distanceToBottom < viewportHeight * 2)
        {
            _ = LoadMoreAsync();
        }
    }

    // ── Mod detail ──

    [RelayCommand]
    private async Task OpenModDetailAsync(GbModDisplayItem? mod)
    {
        if (mod == null) return;

        SelectedMod = mod;
        SelectedModName = mod.Name;
        SelectedModAuthor = mod.AuthorName;
        SelectedModLikes = mod.FormattedLikes;
        SelectedModViews = "";
        SelectedModDate = mod.FormattedDate;
        SelectedModDescription = string.Empty;
        SelectedModUpdateLog = string.Empty;
        SelectedModAuthorAvatar = null;
        HasDescription = Visibility.Collapsed;
        HasUpdateLog = Visibility.Collapsed;
        IsDetailPaneOpen = true;
        IsDetailLoading = true;
        HasModDetail = Visibility.Collapsed;
        PreviewImages.Clear();
        ModFiles.Clear();

        try
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var modId = new GbModId(mod.ModId.ToString());

            var detail = await _gbService.GetModProfileAsync(modId, mod.ModelName, cts.Token);
            SelectedModDetail = detail;

            if (detail != null)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    // Update view count from detail API
                    SelectedModViews = detail.ViewCount >= 1000
                        ? $"{detail.ViewCount / 1000.0:F1}k"
                        : detail.ViewCount.ToString();

                    // Load author avatar
                    if (!string.IsNullOrEmpty(detail.AuthorAvatarUrl))
                    {
                        Helpers.RemoteImageLoader.LoadInto(detail.AuthorAvatarUrl, 36,
                            img => SelectedModAuthorAvatar = img);
                    }

                    if (detail.PreviewImages != null)
                    {
                        foreach (var img in detail.PreviewImages)
                        {
                            var item = new GbPreviewImageItem { Url = img.ToString() };
                            PreviewImages.Add(item);
                            // Thumbnails are now lazy-loaded by XAML ContainerContentChanging
                        }
                    }

                    if (detail.Files != null)
                    {
                        foreach (var f in detail.Files)
                        {
                            ModFiles.Add(f);
                        }
                    }

                    HasPreviewImages = PreviewImages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                    HasFiles = ModFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                    // Description (keep raw HTML for WebView2 rendering)
                    if (!string.IsNullOrWhiteSpace(detail.Description))
                    {
                        SelectedModDescription = Helpers.GameBananaHtmlHelper.WrapHtml(detail.Description);
                        HasDescription = Visibility.Visible;
                    }

                    // Setup updates
                    if (detail.Updates != null && detail.Updates.Count > 0)
                    {
                        var log = string.Join("", detail.Updates.Select(u =>
                        {
                            var title = System.Net.WebUtility.HtmlEncode(u.Title ?? "Update");
                            var version = !string.IsNullOrEmpty(u.Version) ? $"<span class=\"version\">{System.Net.WebUtility.HtmlEncode(u.Version)}</span>" : "";

                            // Relative time approximation using FormaterHelpers
                            var ts = DateTimeOffset.Now - DateTimeOffset.FromUnixTimeSeconds(u.DateAdded);
                            string dateStr = Helpers.FormaterHelpers.FormatTimeSinceAdded(ts);

                            var text = u.Text ?? string.Empty;

                            var changeLogHtml = new System.Text.StringBuilder();
                            if (u.ChangeLog.ValueKind == JsonValueKind.Array)
                            {
                                var elements = u.ChangeLog.EnumerateArray().ToList();
                                if (elements.Count > 0)
                                {
                                    changeLogHtml.Append("<div class=\"changelog-items\">");
                                    foreach (var cl in elements)
                                    {
                                        var cat = cl.TryGetProperty("cat", out var catProp) && catProp.ValueKind == JsonValueKind.String ? catProp.GetString() : "";
                                        var txt = cl.TryGetProperty("text", out var txtProp) && txtProp.ValueKind == JsonValueKind.String ? txtProp.GetString() : "";

                                        var catLower = cat?.ToLowerInvariant() ?? "";
                                        string tagClass = "tag-default";
                                        if (catLower.Contains("adjustment") || catLower.Contains("tweak") || catLower.Contains("amendment")) tagClass = "tag-adjustment";
                                        else if (catLower.Contains("addition") || catLower.Contains("feature")) tagClass = "tag-addition";
                                        else if (catLower.Contains("bugfix")) tagClass = "tag-bugfix";
                                        else if (catLower.Contains("improvement") || catLower.Contains("suggestion")) tagClass = "tag-improvement";
                                        else if (catLower.Contains("overhaul") || catLower.Contains("refactor")) tagClass = "tag-overhaul";
                                        else if (catLower.Contains("optimization")) tagClass = "tag-optimization";
                                        else if (catLower.Contains("removal")) tagClass = "tag-removal";

                                        var tagHtml = string.IsNullOrWhiteSpace(cat)
                                            ? ""
                                            : $"<span class=\"tag {tagClass}\">{System.Net.WebUtility.HtmlEncode(cat)}</span> ";

                                        changeLogHtml.Append($"<div class=\"cl-item\">{tagHtml}<span class=\"cl-text\">{System.Net.WebUtility.HtmlEncode(txt)}</span></div>");
                                    }
                                    changeLogHtml.Append("</div>");
                                }
                            }

                            var filesHtml = new System.Text.StringBuilder();
                            if (u.Files.ValueKind == JsonValueKind.Array)
                            {
                                filesHtml.Append("<div class=\"update-files\"><div class=\"files-label\">Files</div><ul class=\"file-list\">");
                                foreach (var fileProp in u.Files.EnumerateArray())
                                {
                                    var fileObj = fileProp;
                                    if (fileObj.TryGetProperty("_sFile", out var sFile) && sFile.ValueKind == JsonValueKind.String)
                                    {
                                        filesHtml.Append($"<li>{System.Net.WebUtility.HtmlEncode(sFile.GetString())}</li>");
                                    }
                                }
                                filesHtml.Append("</ul></div>");
                            }

                            return $@"
<div class=""update-entry"">
    <div class=""update-header"">
        <span class=""icon"">⊞</span> <span class=""title"">{title}</span> {version}
        <span class=""date-right"">{dateStr}</span>
    </div>
    {changeLogHtml}
    <div class=""update-text"">{text}</div>
    {filesHtml}
</div>";
                        }));

                        SelectedModUpdateLog = Helpers.GameBananaHtmlHelper.WrapHtml(log);
                        HasUpdateLog = Visibility.Visible;
                    }

                    HasModDetail = Visibility.Visible;
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load mod detail for {modId}", mod.ModId);
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/GameBananaPage/ErrorLoadModDetail", "Failed to load mod detail"), ex.Message, TimeSpan.FromSeconds(5));
        }
        finally
        {
            IsDetailLoading = false;
        }
    }


    // ── Download and Install ──

    [RelayCommand]
    private void DownloadAndInstall(ModFileInfo? fileInfo)
    {
        if (fileInfo != null && SelectedMod != null && SelectedModDetail != null)
        {
            _downloadSessionService.DownloadAndInstall(fileInfo, SelectedMod, SelectedModDetail, _gameId);
        }
    }

    [RelayCommand]
    private void CancelDownloadTask(GbDownloadTask? task) => _downloadSessionService.CancelDownloadTask(task);

    [RelayCommand]
    private void RemoveDownloadTask(GbDownloadTask? task) => _downloadSessionService.RemoveDownloadTask(task);

    [RelayCommand]
    private void ClearAllCompletedTasks() => _downloadSessionService.ClearAllCompletedTasks();

    [RelayCommand]
    private void RetryDownloadTask(GbDownloadTask? task) => _downloadSessionService.RetryDownloadTask(task);

    [RelayCommand]
    private void CloseDetailPane()
    {
        IsDetailPaneOpen = false;
        SelectedMod = null;
        SelectedModDetail = null;
        SelectedModDescription = "<html></html>";
        SelectedModUpdateLog = "<html></html>";
        HasModDetail = Visibility.Collapsed;
        PreviewImages.Clear();
        ModFiles.Clear();
    }



    [RelayCommand]
    private void OpenInBrowser()
    {
        if (SelectedMod?.ProfileUrl == null) return;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = SelectedMod.ProfileUrl,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open mod page in browser");
        }
    }

    // ── Helpers ──

    private void CancelLoad()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }
}

// ── Display Models ──

public partial class GbCategoryDisplayItem : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string? CategoryId { get; init; }
    public string? IconUrl { get; init; }
    public int ItemCount { get; init; }
    public bool IsSection { get; init; }
    public string ItemCountText => ItemCount > 0 ? ItemCount.ToString() : string.Empty;
    public Visibility HasItemCountText => ItemCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string IconGlyph => CategoryId == null ? "\uE8FD" : IsSection ? "\uE8B7" : "\uE71D";
    public Windows.UI.Text.FontWeight SectionFontWeight => IsSection
        ? Microsoft.UI.Text.FontWeights.SemiBold
        : Microsoft.UI.Text.FontWeights.Normal;

    [ObservableProperty] private BitmapImage? _iconImage;

    public void LoadIcon()
    {
        Helpers.RemoteImageLoader.LoadInto(IconUrl, 24, img => IconImage = img);
    }
}

public partial class GbModDisplayItem : ObservableObject
{
    public int ModId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ModelName { get; init; } = "Mod";
    public string? ProfileUrl { get; init; }
    public string ThumbnailUrl { get; init; } = string.Empty;
    public string AuthorName { get; init; } = "Unknown";
    public string AuthorAvatarUrl { get; init; } = string.Empty;
    public int LikeCount { get; init; }
    public int DownloadCount { get; init; }
    public int ViewCount { get; init; }
    public int CommentCount { get; init; }
    public long DateAdded { get; init; }
    public long DateUpdated { get; init; }
    [ObservableProperty] private bool _isNsfw;
    [ObservableProperty] private GbNsfwDisplayPolicy _nsfwPolicy;

    public Visibility NsfwVisibility => IsNsfw && NsfwPolicy == GbNsfwDisplayPolicy.Show ? Visibility.Visible : (IsNsfw && NsfwPolicy == GbNsfwDisplayPolicy.Blur ? Visibility.Visible : Visibility.Collapsed);
    public double NsfwBlurRadius => IsNsfw && NsfwPolicy == GbNsfwDisplayPolicy.Blur ? 15.0 : 0.0;
    public Visibility NsfwLabelVisibility => IsNsfw && NsfwPolicy == GbNsfwDisplayPolicy.Blur ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsNsfwVisible => IsNsfw ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Type label to show on the card (empty for regular Mods).
    /// </summary>
    public string TypeLabel => ModelName switch
    {
        "Mod" => string.Empty,
        "Wip" => "WIP",
        _ => ModelName
    };

    public Visibility TypeLabelVisibility => string.IsNullOrEmpty(TypeLabel)
        ? Visibility.Collapsed
        : Visibility.Visible;

    [ObservableProperty] private BitmapImage? _thumbnailImage;
    [ObservableProperty] private BitmapImage? _blurredThumbnailImage;
    [ObservableProperty] private BitmapImage? _authorAvatarImage;

    public void LoadThumbnail()
    {
        Helpers.RemoteImageLoader.LoadInto(ThumbnailUrl, 220, img => ThumbnailImage = img);

        if (!string.IsNullOrEmpty(AuthorAvatarUrl))
        {
            Helpers.RemoteImageLoader.LoadInto(AuthorAvatarUrl, 24, img => AuthorAvatarImage = img);
        }

        // For NSFW items, also load a tiny version that naturally pixelates when stretched
        if (IsNsfw)
        {
            Helpers.RemoteImageLoader.LoadInto(ThumbnailUrl, 12, img => BlurredThumbnailImage = img, cacheKeySuffix: "_blur12");
        }
    }

    public string FormattedDownloads => DownloadCount >= 1000
        ? $"{DownloadCount / 1000.0:F1}k"
        : DownloadCount.ToString();

    public string FormattedLikes => LikeCount >= 1000
        ? $"{LikeCount / 1000.0:F1}k"
        : LikeCount.ToString();

    public string FormattedComments => CommentCount >= 1000
        ? $"{CommentCount / 1000.0:F1}k"
        : CommentCount.ToString();

    public string FormattedDate
    {
        get
        {
            // Use DateAdded if DateUpdated is 0 or less than DateAdded
            long ts = DateUpdated > DateAdded ? DateUpdated : DateAdded;
            if (ts == 0) return string.Empty;

            var dt = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime;
            var diff = DateTime.Now - dt;
            return Helpers.FormaterHelpers.FormatTimeSinceAdded(diff);
        }
    }
}

public partial class GbPreviewImageItem : ObservableObject
{
    public string Url { get; init; } = string.Empty;
    [ObservableProperty] private BitmapImage? _imageSource;

    public void Load()
    {
        Helpers.RemoteImageLoader.LoadInto(Url, 380, img => ImageSource = img, "_380");
    }
}

public record SortOption(string DisplayName, string Value);

public enum GbNsfwDisplayPolicy
{
    Remove,
    Blur,
    Show
}

public enum GbModelFilter
{
    All,
    ModsOnly
}

public record FilterOption<T>(string DisplayName, T Value);

public partial class GbDownloadTask : ObservableObject
{
    public static event Action? ActiveTasksChanged;

    public GbModDisplayItem Mod { get; init; } = null!;
    public ModFileInfo FileInfo { get; init; } = null!;
    public string? CategoryName { get; init; }
    public string? ModUrl { get; set; }
    public CancellationTokenSource? Cts { get; set; }
    public string? ArchivePath { get; set; }

    [ObservableProperty] private double _progressPercentage;
    [ObservableProperty] private string _statusMessage = "等待中...";
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private bool _isError;

    public Visibility CancelVisibility => !IsCompleted && !IsError ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RemoveVisibility => IsCompleted || IsError ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RetryVisibility => IsCompleted || IsError ? Visibility.Visible : Visibility.Collapsed;

    partial void OnIsCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(CancelVisibility));
        OnPropertyChanged(nameof(RemoveVisibility));
        OnPropertyChanged(nameof(RetryVisibility));
        ActiveTasksChanged?.Invoke();
    }

    partial void OnIsErrorChanged(bool value)
    {
        OnPropertyChanged(nameof(CancelVisibility));
        OnPropertyChanged(nameof(RemoveVisibility));
        OnPropertyChanged(nameof(RetryVisibility));
        ActiveTasksChanged?.Invoke();
    }
}