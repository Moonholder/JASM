using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Interfaces;
using GIMI_ModManager.Core.Helpers;

namespace GIMI_ModManager.WinUI.ViewModels.CharacterManagerViewModels.Validation;

public static class Validators
{
    public static void AddInternalNameValidators(this FieldValidators<string> validators, ICollection<IModdableObject> allModdableObjects)
    {
        validators.AddRange([
            context => string.IsNullOrWhiteSpace(context.Value.Trim())
                ? new ValidationResult { Message = "内部名称不能为空" }
                : null,
            context => allModdableObjects.FirstOrDefault(m => m.InternalNameEquals(context.Value)) is { } existingModdableObject
                ? new ValidationResult { Message = $"内部的 {context.Value} 名称已被 {existingModdableObject.DisplayName} 占用" }
                : null,
            context =>
            {
                var invalidFileSystemChars = Path.GetInvalidFileNameChars();


                foreach (var invalidChar in invalidFileSystemChars)
                {
                    if (context.Value.Contains(invalidChar))
                    {
                        return new ValidationResult
                        {
                            Message = $"内部名称包含无效的文件系统字符 '{invalidChar}'"
                        };
                    }
                }

                return null;
            }
        ]);
    }

    public static void AddModFilesNameValidators(this FieldValidators<string> validators, ICollection<IModdableObject> allModdableObjects)
    {
        validators.Add(context => !context.Value.Trim().IsNullOrEmpty() &&
                                  allModdableObjects.FirstOrDefault(m => m.ModFilesName.Equals(context.Value.Trim(), StringComparison.OrdinalIgnoreCase)) is
                                  { } existingModdableObject
            ? new ValidationResult { Message = $"模组文件名已被 {existingModdableObject.DisplayName} 占用" }
            : null);
    }

    public static void AddDisplayNameValidators(this FieldValidators<string> validators, ICollection<IModdableObject> allModdableObjects)
    {
        validators.Add(context => allModdableObjects.FirstOrDefault(m => m.DisplayName.Equals(context.Value.Trim(), StringComparison.OrdinalIgnoreCase)) is
        { } existingModdableObject
            ? new ValidationResult
            {
                Message = $"另一个模组对象 ({existingModdableObject.InternalName}) 已经使用了这个显示名称，这可能会导致搜索出现异常情况",
                Type = ValidationType.Warning
            }
            : null);
    }

    public static void AddImageValidators(this FieldValidators<Uri> validators)
    {
        validators.AddRange([
            context => context.Value == null! ? new ValidationResult { Message = "图像不能为空" } : null, context =>
                !context.Value!.IsFile || !File.Exists(context.Value.LocalPath)
                    ? new ValidationResult() { Message = "图像必须是有效的现有图像" }
                    : null,
            context =>
            {
                if (!context.Value!.IsFile) return null;
                var fileExtension = Path.GetExtension((string?)context.Value.LocalPath);

                var isSupportedExtension = Constants.SupportedImageExtensions.Contains(fileExtension, StringComparer.OrdinalIgnoreCase);

                return isSupportedExtension
                    ? null
                    : new ValidationResult
                    {
                        Message =
                            $"图像必须属于以下类型之一: {string.Join(", ", Constants.SupportedImageExtensions)}. 扩展名 {fileExtension} 不受支持."
                    };
            }
        ]);
    }

    public static void AddRarityValidators(this List<ValidationCallback<int>> validators)
    {
        validators.AddRange([
            context => context.Value < 0 ? new ValidationResult { Message = "稀有度必须大于 -1" } : null,
            context => context.Value > 10 ? new ValidationResult { Message = "稀有度必须小于 11" } : null
        ]);
    }

    public static void AddElementValidators(this List<ValidationCallback<string>> validators, ICollection<IGameElement> elements)
    {
        validators.Add(context => elements.Any(e => e.InternalNameEquals(context.Value))
            ? null
            : new ValidationResult
            {
                Message = $"属性 {context.Value} 不存在。有效值 {string.Join(',', elements.Select(e => e.InternalName))}"
            });
    }

    public static void AddKeysValidators(this FieldValidators<IReadOnlyCollection<string>> validators, ICollection<ICharacter> allCharacters)
    {
        validators.Add(context =>
                {
                    var newKeys = context.Value;
                    if (newKeys.Count == 0) return null;


                    ValueTuple<ICharacter, string>? duplicateKey = null;
                    foreach (var character in allCharacters)
                    {
                        var duplicate = newKeys.FirstOrDefault(k => character.Keys.Contains(k, StringComparer.OrdinalIgnoreCase));
                        if (duplicate is not null)
                        {
                            duplicateKey = (character, duplicate);
                            break;
                        }
                    }

                    if (duplicateKey is null) return null;

                    return new ValidationResult
                    {
                        Message = $"键 {duplicateKey.Value.Item2} 已被 {duplicateKey.Value.Item1.DisplayName} 占用"
                    };
                });
    }
}