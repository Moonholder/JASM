using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Interfaces;
using GIMI_ModManager.Core.Helpers;

namespace GIMI_ModManager.WinUI.ViewModels.CharacterManagerViewModels.Validation;

public static class Validators
{
    public static void AddInternalNameValidators(this FieldValidators<string> validators, ICollection<IModdableObject> allModdableObjects, ILanguageLocalizer localizer)
    {
        validators.AddRange([
            context => string.IsNullOrWhiteSpace(context.Value.Trim())
                ? new ValidationResult { Message = localizer.GetLocalizedStringOrDefault("/CharacterManager/Validation_InternalNameEmpty", "Internal Name cannot be empty") }
                : null,
            context => allModdableObjects.FirstOrDefault(m => m.InternalNameEquals(context.Value)) is { } existingModdableObject
                ? new ValidationResult { Message = string.Format(localizer.GetLocalizedStringOrDefault("/CharacterManager/Validation_InternalNameTaken", "Internal Name '{0}' is already taken by {1}"), context.Value, existingModdableObject.DisplayName) }
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
                            Message = string.Format(localizer.GetLocalizedStringOrDefault("/CharacterManager/Validation_InternalNameInvalidChar", "Internal Name contains invalid file system character '{0}'"), invalidChar)
                        };
                    }
                }

                return null;
            }
        ]);
    }

    public static void AddModFilesNameValidators(this FieldValidators<string> validators, ICollection<IModdableObject> allModdableObjects, ILanguageLocalizer localizer)
    {
        validators.Add(context => !context.Value.Trim().IsNullOrEmpty() &&
                                  allModdableObjects.FirstOrDefault(m => m.ModFilesName.Equals(context.Value.Trim(), StringComparison.OrdinalIgnoreCase)) is
                                  { } existingModdableObject
            ? new ValidationResult { Message = string.Format(localizer.GetLocalizedStringOrDefault("/CharacterManager/Validation_ModFilesNameTaken", "Mod Files Name is already taken by {0}"), existingModdableObject.DisplayName) }
            : null);
    }

    public static void AddDisplayNameValidators(this FieldValidators<string> validators, ICollection<IModdableObject> allModdableObjects, ILanguageLocalizer localizer)
    {
        validators.Add(context => allModdableObjects.FirstOrDefault(m => m.DisplayName.Equals(context.Value.Trim(), StringComparison.OrdinalIgnoreCase)) is
        { } existingModdableObject
            ? new ValidationResult
            {
                Message = string.Format(localizer.GetLocalizedStringOrDefault("/CharacterManager/Validation_DisplayNameTakenWarning", "Another moddable object ({0}) already uses this Display Name. This might cause issues with search."), existingModdableObject.InternalName),
                Type = ValidationType.Warning
            }
            : null);
    }

    public static void AddImageValidators(this FieldValidators<Uri> validators, ILanguageLocalizer localizer)
    {
        validators.AddRange([
            context => context.Value == null! ? new ValidationResult { Message = localizer.GetLocalizedStringOrDefault("/CharacterManager/Validation_ImageEmpty", "Image cannot be empty") } : null,
            context =>
                !context.Value!.IsFile || !File.Exists(context.Value.LocalPath)
                    ? new ValidationResult() { Message = localizer.GetLocalizedStringOrDefault("/CharacterManager/Validation_ImageInvalid", "Image must be a valid existing file") }
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
                        Message = string.Format(localizer.GetLocalizedStringOrDefault("/CharacterManager/Validation_ImageExtensionNotSupported", "Image must be one of the following types: {0}. Extension {1} is not supported."), string.Join(", ", Constants.SupportedImageExtensions), fileExtension)
                    };
            }
        ]);
    }

    public static void AddRarityValidators(this List<ValidationCallback<int>> validators, ILanguageLocalizer localizer)
    {
        validators.AddRange([
            context => context.Value < 0 ? new ValidationResult { Message = localizer.GetLocalizedStringOrDefault("/CharacterManager/Validation_RarityTooLow", "Rarity must be greater than -1") } : null,
            context => context.Value > 10 ? new ValidationResult { Message = localizer.GetLocalizedStringOrDefault("/CharacterManager/Validation_RarityTooHigh", "Rarity must be less than 11") } : null
        ]);
    }

    public static void AddElementValidators(this List<ValidationCallback<string>> validators, ICollection<IGameElement> elements, ILanguageLocalizer localizer)
    {
        validators.Add(context => elements.Any(e => e.InternalNameEquals(context.Value))
            ? null
            : new ValidationResult
            {
                Message = string.Format(localizer.GetLocalizedStringOrDefault("/CharacterManager/Validation_ElementInvalid", "Element {0} does not exist. Valid values: {1}"), context.Value, string.Join(',', elements.Select(e => e.InternalName)))
            });
    }

    public static void AddKeysValidators(this FieldValidators<IReadOnlyCollection<string>> validators, ICollection<ICharacter> allCharacters, ILanguageLocalizer localizer)
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
                        Message = string.Format(localizer.GetLocalizedStringOrDefault("/CharacterManager/Validation_KeyTaken", "Key '{0}' is already taken by {1}"), duplicateKey.Value.Item2, duplicateKey.Value.Item1.DisplayName)
                    };
                });
    }
}