using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.Core.Services.ModPresetService.JsonModels;
using GIMI_ModManager.Core.Services.ModPresetService.Models;
using GIMI_ModManager.Core.Services.ModPresetService.Serialization;
using Serilog;

namespace GIMI_ModManager.Core.Services.ModPresetService;

public sealed class ModVariantService(
    ILogger logger,
    ISkinManagerService skinManagerService,
    UserPreferencesService userPreferencesService) : IDisposable
{
    private readonly ILogger _logger = logger.ForContext<ModVariantService>();
    private readonly ISkinManagerService _skinManagerService = skinManagerService;
    private readonly UserPreferencesService _userPreferencesService = userPreferencesService;
    private readonly AsyncLock _asyncLock = new();

    public async Task<IReadOnlyList<ModVariantPreset>> GetVariantsAsync(Guid modId, CancellationToken cancellationToken = default)
    {
        using var _ = await _asyncLock.LockAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        var mod = _skinManagerService.GetModById(modId);
        if (mod == null) return Array.Empty<ModVariantPreset>();

        var filePath = Path.Combine(mod.FullPath, Constants.ModVariantsFileName);
        if (!File.Exists(filePath)) return Array.Empty<ModVariantPreset>();

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var collection = JsonSerializer.Deserialize<JsonModVariantCollection>(content, ModPresetJsonContext.Default.JsonModVariantCollection);
            if (collection?.Variants == null) return Array.Empty<ModVariantPreset>();

            return collection.Variants.Select(v => new ModVariantPreset
            {
                Name = v.Name,
                Preferences = v.Preferences ?? new(),
                Created = DateTime.TryParse(v.Created, out var date) ? date : DateTime.Now
            }).ToList();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to load variants from {FilePath}", filePath);
            return Array.Empty<ModVariantPreset>();
        }
    }

    public async Task<string?> GetActiveVariantNameAsync(Guid modId, CancellationToken cancellationToken = default)
    {
        using var _ = await _asyncLock.LockAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        var mod = _skinManagerService.GetModById(modId);
        if (mod == null) return null;

        var filePath = Path.Combine(mod.FullPath, Constants.ModVariantsFileName);
        if (!File.Exists(filePath)) return null;

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var collection = JsonSerializer.Deserialize<JsonModVariantCollection>(content, ModPresetJsonContext.Default.JsonModVariantCollection);
            return collection?.ActiveVariantName;
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to load active variant name from {FilePath}", filePath);
            return null;
        }
    }

    public async Task SaveCurrentAsVariantAsync(Guid modId, string name, CancellationToken cancellationToken = default)
    {
        using var _ = await _asyncLock.LockAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        var mod = _skinManagerService.GetModById(modId);
        if (mod == null) throw new ArgumentException($"Mod with Id {modId} not found", nameof(modId));

        name = name.Trim();
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("变体名称不能为空", nameof(name));

        // 1. Sync from d3dx_user.ini to ModSettings first
        var syncSuccess = await _userPreferencesService.SaveModPreferencesAsync(modId).ConfigureAwait(false);
        if (!syncSuccess)
        {
            _logger.Warning("Failed to save mod preferences from d3dx_user.ini for mod {ModId}", modId);
        }

        // 2. Read preferences from ModSettings
        var modSettings = await mod.Settings.TryReadSettingsAsync(useCache: false, cancellationToken).ConfigureAwait(false);
        if (modSettings == null)
        {
            throw new InvalidOperationException("无法读取模组设置");
        }

        var preferences = modSettings.Preferences.ToDictionary(kv => kv.Key, kv => kv.Value);

        // 3. Load or create collection
        var filePath = Path.Combine(mod.FullPath, Constants.ModVariantsFileName);
        JsonModVariantCollection collection;
        if (File.Exists(filePath))
        {
            try
            {
                var content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                collection = JsonSerializer.Deserialize<JsonModVariantCollection>(content, ModPresetJsonContext.Default.JsonModVariantCollection) ?? new();
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to parse existing variants, creating new collection");
                collection = new();
            }
        }
        else
        {
            collection = new();
        }

        // 4. Update or add variant
        var existing = collection.Variants.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Preferences = preferences;
            existing.Created = DateTime.Now.ToString("o");
        }
        else
        {
            collection.Variants.Add(new JsonModVariantPreset
            {
                Name = name,
                Preferences = preferences,
                Created = DateTime.Now.ToString("o")
            });
        }

        collection.ActiveVariantName = name;

        // 5. Save collection
        var json = JsonSerializer.Serialize(collection, ModPresetJsonContext.Default.JsonModVariantCollection);
        await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyVariantAsync(Guid modId, string name, CancellationToken cancellationToken = default)
    {
        using var _ = await _asyncLock.LockAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        var mod = _skinManagerService.GetModById(modId);
        if (mod == null) throw new ArgumentException($"Mod with Id {modId} not found", nameof(modId));

        name = name.Trim();
        var filePath = Path.Combine(mod.FullPath, Constants.ModVariantsFileName);
        if (!File.Exists(filePath)) throw new FileNotFoundException("未找到变体配置文件", filePath);

        // 1. Read variants
        JsonModVariantCollection collection;
        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            collection = JsonSerializer.Deserialize<JsonModVariantCollection>(content, ModPresetJsonContext.Default.JsonModVariantCollection) ?? throw new InvalidOperationException();
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("无法读取变体配置文件", e);
        }

        var variant = collection.Variants.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (variant == null)
        {
            throw new ArgumentException($"未找到名为 {name} 的变体", nameof(name));
        }

        // 2. Load ModSettings
        var modSettings = await mod.Settings.TryReadSettingsAsync(useCache: false, cancellationToken).ConfigureAwait(false);
        if (modSettings == null)
        {
            throw new InvalidOperationException("无法读取模组设置");
        }

        // 3. Set preferences
        modSettings.SetPreferences(new Dictionary<string, string>(variant.Preferences));
        await mod.Settings.SaveSettingsAsync(modSettings).ConfigureAwait(false);

        // 4. Sync preferences to d3dx_user.ini
        var setSuccess = await _userPreferencesService.SetModPreferencesAsync(modId, cancellationToken).ConfigureAwait(false);
        if (!setSuccess)
        {
            throw new InvalidOperationException("应用变体到 d3dx_user.ini 失败");
        }

        // 4b. Sync preferences to local .ini files (e.g. mod.ini)
        await _userPreferencesService.SyncPreferencesToModLocalFilesAsync(modId).ConfigureAwait(false);

        // 5. Update active variant and save variants JSON
        collection.ActiveVariantName = name;
        var json = JsonSerializer.Serialize(collection, ModPresetJsonContext.Default.JsonModVariantCollection);
        await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteVariantAsync(Guid modId, string name, CancellationToken cancellationToken = default)
    {
        using var _ = await _asyncLock.LockAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        var mod = _skinManagerService.GetModById(modId);
        if (mod == null) return;

        name = name.Trim();
        var filePath = Path.Combine(mod.FullPath, Constants.ModVariantsFileName);
        if (!File.Exists(filePath)) return;

        JsonModVariantCollection collection;
        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            collection = JsonSerializer.Deserialize<JsonModVariantCollection>(content, ModPresetJsonContext.Default.JsonModVariantCollection) ?? new();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to read variants file for deletion");
            return;
        }

        var variant = collection.Variants.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (variant != null)
        {
            collection.Variants.Remove(variant);
        }

        bool wasActive = string.Equals(collection.ActiveVariantName, name, StringComparison.OrdinalIgnoreCase);

        if (collection.Variants.Count == 0)
        {
            if (wasActive)
            {
                collection.ActiveVariantName = null;
            }

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        else
        {
            if (wasActive)
            {
                var nextVariant = collection.Variants[0];
                collection.ActiveVariantName = nextVariant.Name;

                // Load ModSettings and set preferences to the next variant
                var modSettings = await mod.Settings.TryReadSettingsAsync(useCache: false, cancellationToken).ConfigureAwait(false);
                if (modSettings != null)
                {
                    modSettings.SetPreferences(new Dictionary<string, string>(nextVariant.Preferences));
                    await mod.Settings.SaveSettingsAsync(modSettings).ConfigureAwait(false);

                    // Sync preferences to d3dx_user.ini
                    await _userPreferencesService.SetModPreferencesAsync(modId, cancellationToken).ConfigureAwait(false);
                    // Sync preferences to local .ini files (e.g. mod.ini)
                    await _userPreferencesService.SyncPreferencesToModLocalFilesAsync(modId).ConfigureAwait(false);
                }
            }

            var json = JsonSerializer.Serialize(collection, ModPresetJsonContext.Default.JsonModVariantCollection);
            await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RenameVariantAsync(Guid modId, string oldName, string newName, CancellationToken cancellationToken = default)
    {
        using var _ = await _asyncLock.LockAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        var mod = _skinManagerService.GetModById(modId);
        if (mod == null) throw new ArgumentException($"Mod with Id {modId} not found", nameof(modId));

        oldName = oldName.Trim();
        newName = newName.Trim();

        if (string.IsNullOrEmpty(newName)) throw new ArgumentException("新变体名称不能为空", nameof(newName));

        var filePath = Path.Combine(mod.FullPath, Constants.ModVariantsFileName);
        if (!File.Exists(filePath)) throw new FileNotFoundException("未找到变体配置文件", filePath);

        JsonModVariantCollection collection;
        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            collection = JsonSerializer.Deserialize<JsonModVariantCollection>(content, ModPresetJsonContext.Default.JsonModVariantCollection) ?? throw new InvalidOperationException();
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("无法读取变体配置文件", e);
        }

        var oldVariant = collection.Variants.FirstOrDefault(v => v.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (oldVariant == null)
        {
            throw new ArgumentException($"未找到名为 {oldName} 的变体", nameof(oldName));
        }

        var newVariantExists = collection.Variants.Any(v => v != oldVariant && v.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));
        if (newVariantExists)
        {
            throw new InvalidOperationException($"已存在名为 {newName} 的变体");
        }

        oldVariant.Name = newName;

        if (string.Equals(collection.ActiveVariantName, oldName, StringComparison.OrdinalIgnoreCase))
        {
            collection.ActiveVariantName = newName;
        }

        var json = JsonSerializer.Serialize(collection, ModPresetJsonContext.Default.JsonModVariantCollection);
        await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _asyncLock.Dispose();
    }
}