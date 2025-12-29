using GIMI_ModManager.Core.Helpers;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace GIMI_ModManager.Core.GamesService.Requests;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
public class EditCustomCharacterRequest
{
    public NewValue<string> DisplayName { get; set; }

    public NewValue<bool> IsMultiMod { get; set; }

    public NewValue<Uri?> Image { get; set; }

    public NewValue<DateTime> ReleaseDate { get; set; }

    public NewValue<int> Rarity { get; set; }

    public NewValue<string> Element { get; set; }

    public NewValue<string[]> Keys { get; set; }


    public bool AnyValuesSet => GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.PropertyType.IsAssignableTo(typeof(ISettableProperty)))
        .Any(p => (p.GetValue(this) as ISettableProperty)?.IsSet == true);
}