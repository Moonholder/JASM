using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Interfaces;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.WinUI.Services;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.WinUI.ViewModels.CharacterManagerViewModels.Validation;
using System.Xml.Linq;

namespace GIMI_ModManager.WinUI.ViewModels.CharacterManagerViewModels;

public sealed partial class EditCharacterForm : Form
{
    public EditCharacterForm()
    {
    }

    public void Initialize(ICharacter character, ICollection<IModdableObject> allModdableObjects, ICollection<IGameElement> elements, ILanguageLocalizer localizer)
    {
        allModdableObjects = allModdableObjects.Contains(character)
            ? allModdableObjects.Where(mo => !mo.Equals(character)).ToArray()
            : allModdableObjects;

        InternalName.ValidationRules.AddInternalNameValidators(allModdableObjects, localizer);
        InternalName.ReInitializeInput(character.InternalName);

        DisplayName.ValidationRules.AddDisplayNameValidators(allModdableObjects, localizer);
        DisplayName.ValidationRules.Add(context =>
            context.Value.Trim().IsNullOrEmpty()
            ? new ValidationResult { Message = localizer.GetLocalizedStringOrDefault("/CharacterManager/Validation_DisplayNameEmpty", "Display Name cannot be empty") }
            : null);
        DisplayName.ReInitializeInput(character.DisplayName);

        Image.ValidationRules.AddImageValidators(localizer);
        Image.ReInitializeInput(character.ImageUri ?? ImageHandlerService.StaticPlaceholderImageUri);

        Rarity.ReInitializeInput(character.Rarity);

        Element.ReInitializeInput(character.Element.InternalName);

        if (character.ReleaseDate != null)
        {
            var safeDate = GetSafeDateTime(character.ReleaseDate);
            ReleaseDate.ReInitializeInput(safeDate);
        }

        Keys.ValidationRules.AddKeysValidators([.. allModdableObjects.OfType<ICharacter>()], localizer);
        Keys.ReInitializeInput(character.Keys);

        IsMultiMod.ReInitializeInput(character.IsMultiMod);
        IsInitialized = true;
    }

    private DateTime GetSafeDateTime(DateTime? inputDate)
    {
        if (inputDate == null)
            return DateTime.Today;

        var date = inputDate.Value;

        if (date == DateTime.MinValue || date == DateTime.MaxValue)
            return DateTime.Today;

        int year = Math.Clamp(date.Year, 1, 9999);

        int month = Math.Clamp(date.Month, 1, 12);
        int day = Math.Clamp(date.Day, 1, DateTime.DaysInMonth(year, month));

        return new DateTime(year, month, day, date.Hour, date.Minute, date.Second, date.Millisecond);
    }

    public InputField<Uri> Image { get; } = new(ImageHandlerService.StaticPlaceholderImageUri);
    public StringInputField InternalName { get; } = new(string.Empty);
    public StringInputField DisplayName { get; } = new(string.Empty);

    public InputField<DateTimeOffset> ReleaseDate { get; } = new(DateTime.Now);

    public InputField<int> Rarity { get; } = new(5);

    public InputField<string> Element { get; } = new("none");
    public ListInputField<string> Keys { get; } = new();

    public InputField<bool> IsMultiMod { get; } = new(false);
}