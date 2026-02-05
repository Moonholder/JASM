using FluentValidation;
using GIMI_ModManager.WinUI.ViewModels.SubVms;

namespace GIMI_ModManager.WinUI.Validators;

public class ContainsAnyFileSystemEntryWithNames : AbstractValidator<PathPicker>
{
    public ContainsAnyFileSystemEntryWithNames(IEnumerable<string> filenames, string? customMessage = null, bool warning = false)
    {
        var fileNamesArray = filenames.ToArray();
        var filenamesLowerArray = fileNamesArray.Select(name => name.ToLower()).ToArray();

        customMessage ??=
            $"文件夹不包含任何具有指定名称的条目: {string.Join(" Or ", fileNamesArray)}， 这可能不是一个正确的加载器目录。";

        RuleFor(x => x.Path)
            .Must(path =>
                path is not null &&
                Directory.Exists(path) &&
                Directory.GetFileSystemEntries(path)
                    .Any(entry => filenamesLowerArray.Any(name => entry.ToLower().EndsWith(name)))
            )
            .WithMessage(customMessage)
            .WithSeverity(warning ? Severity.Warning : Severity.Error);
    }
}