using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Interfaces;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.WinUI.Services;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.WinUI.ViewModels.CharacterManagerViewModels.Validation;

namespace GIMI_ModManager.WinUI.ViewModels.CharacterManagerViewModels;

public partial class CreateCharacterForm : Form
{
    public StringInputField InternalName { get; } = new(string.Empty);
    public StringInputField DisplayName { get; } = new(string.Empty);

    public StringInputField ModFilesName { get; } = new(string.Empty);

    public InputField<DateTimeOffset> ReleaseDate { get; } = new(DateTime.Now);

    public InputField<Uri> Image { get; } = new(ImageHandlerService.StaticPlaceholderImageUri);

    public InputField<int> Rarity { get; } = new(5);

    public ListInputField<string> Keys { get; } = new();

    public InputField<string> Element { get; } = new("none");

    public InputField<bool> IsMultiMod { get; } = new(false);


    public void Initialize(ICollection<IModdableObject> allModdableObjects, ICollection<IGameElement> elements, ILanguageLocalizer localizer)
    {
        InternalName.ValidationRules.AddInternalNameValidators(allModdableObjects, localizer);

        ModFilesName.ValidationRules.AddModFilesNameValidators(allModdableObjects, localizer);

        DisplayName.ValidationRules.AddDisplayNameValidators(allModdableObjects, localizer);

        DisplayName.ValidationRules.Add(context => context.Value != string.Empty && context.Value.Trim().IsNullOrEmpty()
            ? new ValidationResult { Message = localizer.GetLocalizedStringOrDefault("/CharacterManager/Validation_DisplayNameEmpty", "Display Name cannot be empty") }
            : null);

        Image.ValidationRules.AddImageValidators(localizer);

        Rarity.ValidationRules.AddRarityValidators(localizer);

        Element.ValidationRules.AddElementValidators(elements, localizer);

        Keys.ValidationRules.AddKeysValidators(allModdableObjects.OfType<ICharacter>().ToList(), localizer);

        IsInitialized = true;
    }

    //public CreateCharacterForm()
    //{
    //    var fields = GetType()
    //        .GetProperties().Where(p => p.PropertyType.IsAssignableTo(typeof(BaseInputField)))
    //        .Select(p => new
    //        {
    //            Property = (BaseInputField)p.GetValue(this)!,
    //            PropertyName = p.Name
    //        });

    //    foreach (var field in fields)
    //    {
    //        field.Property.FieldName = field.PropertyName;
    //        field.Property.PropertyChanged += (sender, _) => OnValueChanged((BaseInputField)sender!);
    //        Fields.Add(field.Property);
    //    }
    //}

    public override void OnValueChanged(BaseInputField field)
    {
        if (!IsInitialized) return;
        var oldValidValue = IsValid;

        AnyFieldDirty = Fields.Any(f => f.IsDirty);
        field.Validate(this);


        if (field.FieldName == nameof(InternalName) && DisplayName.Value.IsNullOrEmpty())
        {
            var internalName = InternalName.Value.Trim();
            if (internalName.Length > 1)
                DisplayName.PlaceHolderText = internalName[0].ToString().ToUpper() + internalName.Substring(1);
            else
                DisplayName.PlaceHolderText = string.Empty;
        }

        if (oldValidValue != IsValid)
            OnPropertyChanged(nameof(IsValid));
    }
}