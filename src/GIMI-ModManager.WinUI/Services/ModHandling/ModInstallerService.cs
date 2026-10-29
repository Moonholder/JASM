using System.Diagnostics.CodeAnalysis;
using CommunityToolkitWrapper;
using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Entities.Mods.Contract;
using GIMI_ModManager.Core.Entities.Mods.SkinMod;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Interfaces;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Models.Settings;
using GIMI_ModManager.WinUI.Services.AppManagement;
using GIMI_ModManager.WinUI.Services;
using GIMI_ModManager.WinUI.ViewModels;
using GIMI_ModManager.WinUI.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Serilog;

namespace GIMI_ModManager.WinUI.Services.ModHandling;

public record SilentInstallResult(ISkinMod InstalledMod, IReadOnlyList<Guid> DisabledModIds);

public class ModInstallerService(
    IWindowManagerService windowManagerService,
    ILocalSettingsService localSettingsService,
    CharacterSkinService characterSkinService,
    UserPreferencesService userPreferencesService,
    ElevatorService elevatorService,
    IGameService gameService,
    ModSettingsService modSettingsService,
    ILogger logger)
{
    private readonly ILocalSettingsService _localSettingsService = localSettingsService;
    private readonly IWindowManagerService _windowManagerService = windowManagerService;
    private readonly CharacterSkinService _characterSkinService = characterSkinService;
    private readonly UserPreferencesService _userPreferencesService = userPreferencesService;
    private readonly ElevatorService _elevatorService = elevatorService;
    private readonly IGameService _gameService = gameService;
    private readonly ModSettingsService _modSettingsService = modSettingsService;
    private readonly ILogger _logger = logger.ForContext<ModInstallerService>();

    public async Task<InstallMonitor> StartModInstallationAsync(DirectoryInfo modFolder, ICharacterModList modList,
        ICharacterSkin? inGameSkin = null, Action<InstallOptions>? setup = null)
    {
        ArgumentNullException.ThrowIfNull(modFolder);
        ArgumentNullException.ThrowIfNull(modList);


        if (inGameSkin is not null && modList.Character is not ICharacter)
            throw new ArgumentException("The mod list must be a character mod list if inGameSkin is not null");

        var dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? App.MainWindow.DispatcherQueue;

        var modOptions = new InstallOptions();
        setup?.Invoke(modOptions);


        var monitor =
            await dispatcherQueue.EnqueueAsync(() => InternalStartAsync(modFolder, modList, inGameSkin, modOptions));

        return monitor;
    }

    private async Task<InstallMonitor> InternalStartAsync(DirectoryInfo modFolder, ICharacterModList modList,
        ICharacterSkin? inGameSkin = null, InstallOptions? options = null)
    {
        var modTitle = Guid.TryParse(modFolder.Name, out _)
            ? modFolder.EnumerateDirectories().FirstOrDefault()?.Name
            : modFolder.Name;

        modTitle ??= modFolder.Name;

        var modInstallerSettings =
            await _localSettingsService.ReadOrCreateSettingAsync<ModInstallerSettings>(ModInstallerSettings.Key);

        var modInstallPage = new ModInstallerPage(modList, modFolder, inGameSkin, options);
        var modInstallWindow = new WindowEx()
        {
            SystemBackdrop = new MicaBackdrop(),
            Title = $"模组安装器: {modTitle}",
            Content = modInstallPage,
            Width = 1200,
            Height = 750,
            MinHeight = 415,
            MinWidth = 1024,
            IsAlwaysOnTop = modInstallerSettings.ModInstallerWindowOnTop
        };
        _windowManagerService.CreateWindow(modInstallWindow, modList);

        return new InstallMonitor(modInstallPage, modInstallWindow);
    }

    public async Task<SilentInstallResult> InstallSilentlyAsync(
        DirectoryInfo modFolder,
        ICharacterModList modList,
        AddModOptions? options = null,
        bool autoEnable = false,
        ICharacterSkin? inGameSkin = null)
    {
        ArgumentNullException.ThrowIfNull(modFolder);
        ArgumentNullException.ThrowIfNull(modList);

        using var installation = ModInstallation.Start(modFolder, modList);
        installation.AutoSetModRootFolder();
        installation.AutoSetShaderFixesFolder();

        var dupeMod = installation.AnyDuplicateName();
        ISkinMod installedMod;

        if (dupeMod != null)
        {
            var baseName = !string.IsNullOrEmpty(options?.NewModFolderName)
                ? options.NewModFolderName
                : installation.ModFolder.Name;

            var uniqueFolderName = GenerateUniqueFolderName(modList, baseName);
            var renameOptions = options != null
                ? options with { NewModFolderName = uniqueFolderName }
                : new AddModOptions { NewModFolderName = uniqueFolderName };

            installedMod = await installation.RenameAndAddAsync(renameOptions, dupeMod, dupeMod.Name).ConfigureAwait(false);
        }
        else
        {
            installedMod = await installation.AddModAsync(options).ConfigureAwait(false);
        }

        var disabledModIds = new List<Guid>();

        if (autoEnable)
        {
            var modsToDisable = new List<ISkinMod>();
            bool isMultiMod = modList.Character.IsMultiMod;

            if (!isMultiMod)
            {
                if (modList.Character is ICharacter character && character.Skins.Count > 1)
                {
                    var detectedSkin = await _characterSkinService.GetFirstSkinForModAsync(installedMod, character).ConfigureAwait(false);
                    var targetSkin = inGameSkin ?? detectedSkin ?? character.Skins.FirstOrDefault(s => s.IsDefault);

                    if (targetSkin != null)
                    {
                        await foreach (var skinMod in _characterSkinService.GetModsForSkinAsync(targetSkin).ConfigureAwait(false))
                        {
                            if (skinMod.Id != installedMod.Id && modList.IsModEnabled(skinMod))
                            {
                                modsToDisable.Add(skinMod);
                            }
                        }
                    }
                    else
                    {
                        modsToDisable.AddRange(modList.Mods
                            .Where(m => modList.IsModEnabled(m.Mod) && m.Mod.Id != installedMod.Id)
                            .Select(m => m.Mod));
                    }

                    if (inGameSkin != null && detectedSkin != null && !detectedSkin.InternalNameEquals(inGameSkin.InternalName))
                    {
                        await _modSettingsService.SetCharacterSkinOverrideLegacy(installedMod.Id, inGameSkin.InternalName).ConfigureAwait(false);
                    }
                }
                else
                {
                    modsToDisable.AddRange(modList.Mods
                        .Where(m => modList.IsModEnabled(m.Mod) && m.Mod.Id != installedMod.Id)
                        .Select(m => m.Mod));
                }
            }

            foreach (var mod in modsToDisable)
            {
                if (modList.IsModEnabled(mod))
                {
                    modList.DisableMod(mod.Id);
                    disabledModIds.Add(mod.Id);
                }
            }

            if (!modList.IsModEnabled(installedMod))
            {
                modList.EnableMod(installedMod.Id);
            }
        }
        else
        {
            if (modList.IsModEnabled(installedMod))
            {
                modList.DisableMod(installedMod.Id);
            }
        }

        return new SilentInstallResult(installedMod, disabledModIds);
    }

    private static string GenerateUniqueFolderName(ICharacterModList modList, string baseName)
    {
        return ModFolderHelpers.GenerateUniqueFolderName(
            baseName,
            modList.Mods.Select(m => m.Mod.Name),
            candidate => Directory.Exists(Path.Combine(modList.AbsModsFolderPath, candidate)));
    }
}

public class InstallOptions
{
    public Uri? ModUrl { get; set; }
    public Guid? ExistingModIdToUpdate { get; set; }
}

public sealed class InstallMonitor : IDisposable
{
    private readonly TaskCompletionSource<CloseRequestedArgs> _taskCompletionSource = new();
    private readonly ModInstallerPage _modInstallerPage;
    private readonly WindowEx _modInstallerWindow;
    private CancellationTokenRegistration? _cancellationTokenRegistration;

    public Task Task => _taskCompletionSource.Task;

    public InstallMonitor(ModInstallerPage modInstallerPage, WindowEx modInstallerWindow)
    {
        _modInstallerPage = modInstallerPage;
        _modInstallerWindow = modInstallerWindow;

        _modInstallerPage.CloseRequested += (_, e) =>
        {
            _taskCompletionSource.SetResult(e);
            _modInstallerWindow.Close();
        };

        _modInstallerWindow.Closed += (_, _) =>
        {
            if (!_taskCompletionSource.Task.IsCompleted)
                _taskCompletionSource.TrySetResult(new CloseRequestedArgs(CloseRequestedArgs.CloseReasons.Canceled));
        };
    }

    public Task<CloseRequestedArgs> WaitForCloseAsync(CancellationToken? cancellationToken = null)
    {
        if (cancellationToken is not null)
            _cancellationTokenRegistration = cancellationToken.Value.Register(() =>
            {
                if (!_taskCompletionSource.Task.IsCompleted)
                {
                    _taskCompletionSource.TrySetCanceled();
                    _modInstallerWindow.Close();
                }
            });

        return _taskCompletionSource.Task;
    }

    public void Dispose()
    {
        _cancellationTokenRegistration?.Dispose();
        _cancellationTokenRegistration = null;
    }
}

public sealed class ModInstallation : IDisposable
{
    private readonly ModCrawlerService _modCrawlerService = App.GetService<ModCrawlerService>();
    private readonly ISkinManagerService _skinManagerService = App.GetService<ISkinManagerService>();
    private readonly ICharacterModList _destinationModList;
    private readonly DirectoryInfo _originalModFolder;
    private readonly List<FileStream> _lockedFiles = new();

    private FileInfo? _jasmConfigFile;
    public DirectoryInfo ModFolder { get; private set; }
    // TODO: Enable later
    private DirectoryInfo? _shaderFixesFolder = null!;


    private ModInstallation(DirectoryInfo originalModFolder, ICharacterModList destinationModList)
    {
        _originalModFolder = originalModFolder;
        _destinationModList = destinationModList;
        SetRootModFolder(originalModFolder);
        LockFiles();
    }

    private void LockFiles()
    {
        foreach (var fileInfo in _originalModFolder.GetFiles("*", SearchOption.AllDirectories))
        {
            var fileStream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
            _lockedFiles.Add(fileStream);
        }
    }

    // Lock mod folder
    public static ModInstallation Start(DirectoryInfo modFolder, ICharacterModList destinationModList)
    {
        return new ModInstallation(modFolder, destinationModList);
    }

    [MemberNotNull(nameof(ModFolder))]
    public void SetRootModFolder(DirectoryInfo newRootFolder)
    {
        if (newRootFolder.FullName == _shaderFixesFolder?.FullName)
            throw new ArgumentException("The new root folder is the same as the current shader fixes folder");

        if (!newRootFolder.Exists)
            throw new DirectoryNotFoundException($"The folder {newRootFolder.FullName} does not exist");

        ModFolder = new DirectoryInfo(newRootFolder.FullName);
        _jasmConfigFile = _modCrawlerService.GetFirstJasmConfigFileAsync(ModFolder, false);
    }

    public void SetShaderFixesFolder(DirectoryInfo shaderFixesFolder)
    {
        // TODO: Enable later
    }


    public DirectoryInfo? AutoSetModRootFolder()
    {
        var jasmConfigFile = _modCrawlerService.GetFirstJasmConfigFileAsync(_originalModFolder);
        DirectoryInfo? modRootFolder = null;
        if (jasmConfigFile is not null)
        {
            modRootFolder = new DirectoryInfo(jasmConfigFile.DirectoryName!);
        }
        else
        {
            var mergedIniFile = _modCrawlerService.GetMergedIniFile(_originalModFolder);
            if (mergedIniFile is not null)
                modRootFolder = new DirectoryInfo(mergedIniFile.DirectoryName!);
        }

        if (modRootFolder is null)
        {
            var directories = _originalModFolder.EnumerateDirectories().ToList();
            var files = _originalModFolder.EnumerateFiles().ToList();

            if (directories.Count == 1 && files.Count == 0)
            {
                modRootFolder = directories[0];
            }
            else
            {
                // If there are multiple folders or any files in the root, the root itself is the mod root
                modRootFolder = _originalModFolder;
            }
        }

        if (modRootFolder is null)
            return null;

        SetRootModFolder(modRootFolder);
        return modRootFolder;
    }

    public DirectoryInfo? AutoSetShaderFixesFolder()
    {
        var shaderFixesFolder = _modCrawlerService.GetShaderFixesFolder(_originalModFolder);
        if (shaderFixesFolder is null)
            return null;

        SetShaderFixesFolder(shaderFixesFolder);
        return shaderFixesFolder;
    }

    public async Task<ModSettings?> TryReadModSettingsAsync()
    {
        if (_jasmConfigFile is null)
            return null;

        await RemoveJasmConfigFileLockAsync().ConfigureAwait(false);
        try
        {
            return await SkinModSettingsManager.ReadSettingsAsync(_jasmConfigFile.FullName);
        }
        catch (Exception)
        {
            // ignored
        }

        LockJasmConfigFile();
        return null;
    }

    public ISkinMod? AnyDuplicateName()
    {
        var skinEntries = _destinationModList.Mods;


        foreach (var skinEntry in skinEntries)
        {
            if (ModFolderHelpers.FolderNameEquals(skinEntry.Mod.Name, ModFolder.Name))
                return skinEntry.Mod;
        }

        return null;
    }

    public async Task<ISkinMod> RenameAndAddAsync(AddModOptions options, ISkinMod dupeMod,
        string dupeModNewFolderName, string? dupeModNewCustomName = null)
    {
        if (dupeModNewFolderName.IsNullOrEmpty() && options.NewModFolderName.IsNullOrEmpty())
            throw new ArgumentException("The new mod folder name and old folder name cannot be null or empty");

        if (ModFolderHelpers.FolderNameEquals(dupeModNewFolderName, options.NewModFolderName!))
            throw new ArgumentException("The new mod folder name and old folder name cannot be the same");

        ReleaseLockedFiles();

        var skinMod = await CreateSkinModWithOptionsAsync(options);
        var newModRenamed = false;
        if (!options.NewModFolderName.IsNullOrEmpty() &&
            !ModFolderHelpers.FolderNameEquals(skinMod.Name, options.NewModFolderName))
        {
            var tmpFolder = App.GetUniqueTmpFolder();
            skinMod = await SkinMod.CreateModAsync(skinMod.CopyTo(tmpFolder.FullName).FullPath).ConfigureAwait(false);
            skinMod.Rename(options.NewModFolderName);
            newModRenamed = true;
        }

        if (!dupeModNewFolderName.IsNullOrEmpty() &&
            !ModFolderHelpers.FolderNameEquals(dupeMod.Name, dupeModNewFolderName))
        {
            _destinationModList.RenameMod(dupeMod, dupeModNewFolderName);
        }

        // Set new custom name for dupe mod
        if (!dupeModNewCustomName.IsNullOrEmpty())
        {
            var dupeModSettings = await dupeMod.Settings.ReadSettingsAsync().ConfigureAwait(false);

            await dupeMod.Settings
                .SaveSettingsAsync(
                    dupeModSettings.DeepCopyWithProperties(customName: NewValue<string?>.Set(dupeModNewCustomName)))
                .ConfigureAwait(false);
        }

        _skinManagerService.AddMod(skinMod, _destinationModList, newModRenamed);
        return skinMod;
    }


    public async Task<ISkinMod> AddAndReplaceAsync(ISkinMod dupeMod, AddModOptions? options = null)
    {
        ReleaseLockedFiles();
        var skinMod = await CreateSkinModWithOptionsAsync(options).ConfigureAwait(false);
        try
        {
            _destinationModList.DeleteModBySkinEntryId(dupeMod.Id);
        }
        catch (DirectoryNotFoundException)
        {
        }

        return _skinManagerService.AddMod(skinMod, _destinationModList);
    }

    public async Task<ISkinMod> AddModAsync(AddModOptions? options = null)
    {
        if (AnyDuplicateName() is not null)
            throw new InvalidOperationException("There is already a mod with the same name");

        ReleaseLockedFiles();
        var skinMod = await CreateSkinModWithOptionsAsync(options);

        return _skinManagerService.AddMod(skinMod, _destinationModList);
    }

    private async Task<ISkinMod> CreateSkinModWithOptionsAsync(AddModOptions? options = null)
    {
        await RemoveJasmConfigFileLockAsync().ConfigureAwait(false);

        var skinMod = await SkinMod.CreateModAsync(ModFolder, true).ConfigureAwait(false);

        if (options is null)
            return skinMod;

        var settings = new ModSettings(
            id: skinMod.Id,
            customName: options.ModName,
            imagePath: options.ModImage,
            author: options.Author,
            modUrl: Uri.TryCreate(options.ModUrl, UriKind.Absolute, out var modUrl) ? modUrl : null,
            description: options.Description,
            characterSkinOverride: options.CharacterSkinOverride.IsNullOrEmpty() ? null : options.CharacterSkinOverride,
            dateAdded: DateTime.Now
        );
        await skinMod.Settings.SaveSettingsAsync(settings, new SaveSettingsOptions { DeleteOldImage = false })
            .ConfigureAwait(false);
        return skinMod;
    }

    private async Task RemoveJasmConfigFileLockAsync()
    {
        if (_jasmConfigFile is not null)
        {
            _jasmConfigFile.Refresh();
            var jasmFs = _lockedFiles.FirstOrDefault(file =>
                file.Name.Equals(_jasmConfigFile.FullName, StringComparison.OrdinalIgnoreCase));

            if (jasmFs is not null)
            {
                await jasmFs.DisposeAsync().ConfigureAwait(false);
                _lockedFiles.Remove(jasmFs);
            }
        }
    }

    private void LockJasmConfigFile()
    {
        if (_jasmConfigFile is not null)
        {
            _jasmConfigFile.Refresh();
            if (!_jasmConfigFile.Exists) return;

            var jasmFs = _jasmConfigFile.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            _lockedFiles.Add(jasmFs);
        }
    }

    private void ReleaseLockedFiles()
    {
        foreach (var fileStream in _lockedFiles.ToArray())
        {
            fileStream.Dispose();
            _lockedFiles.Remove(fileStream);
        }

        Log.Debug("Released locked files, {time}", DateTime.Now);
    }

    public void Dispose()
    {
        ReleaseLockedFiles();
    }
}

public record AddModOptions
{
    public string? NewModFolderName { get; set; }
    public string? ModName { get; set; }
    public Uri? ModImage { get; set; }
    public string? ModUrl { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? CharacterSkinOverride { get; set; }
}