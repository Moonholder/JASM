using GIMI_ModManager.Core.Services;

namespace GIMI_ModManager.Tests;

public class ArchiveExtractionGuardTests
{
    [Theory]
    [InlineData("mod/file.png")]
    [InlineData("folder/sub/file.txt")]
    [InlineData("folder with spaces/file.txt")]
    public void AcceptsSafePaths(string entryKey)
    {
        ArchiveExtractionGuard.ValidateEntryPath(entryKey, @"C:\out");
    }

    [Theory]
    [InlineData("../evil.exe")]
    [InlineData("a/../../evil.exe")]
    [InlineData("..\\evil.exe")]
    [InlineData("/absolute/evil.exe")]
    [InlineData("C:\\evil.exe")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsUnsafePaths(string? entryKey)
    {
        Assert.Throws<InvalidDataException>(() =>
            ArchiveExtractionGuard.ValidateEntryPath(entryKey, @"C:\out"));
    }
}