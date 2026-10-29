using GIMI_ModManager.Core.Services;

namespace GIMI_ModManager.Tests;

public class DragAndDropScannerTests
{
    [Theory]
    [InlineData("7-Zip 24.08 (x64) : Copyright (c) 1999-2024 Igor Pavlov : 2024-08-11\n\nScanning the drive for archives:\n1 file, 12345 bytes\n\nExtracting archive: test.7z\n--\nPath = test.7z\nType = 7z\n\nERROR: Cannot open encrypted archive. Wrong password? : test.7z\n\nSub items Errors: 1")]
    [InlineData("ERROR: Wrong password : mod\\model.dds")]
    [InlineData("Extracting  mod/texture.dds     ERROR: Data Error in encrypted file. Wrong password? : mod/texture.dds")]
    [InlineData("Can't open encrypted file 'test.zip'")]
    [InlineData("Extracting archive: bundle.rar\nCRC failed in encrypted file character.ini (wrong password?)")]
    public void IsPasswordRequiredError_ReturnsTrue_ForPasswordErrors(string output)
    {
        var result = DragAndDropScanner.IsPasswordRequiredError(output);

        Assert.True(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("7-Zip 24.08 (x64) : Everything is Ok\nFiles: 10\nSize: 52428800")]
    [InlineData("7-Zip 24.08 (x64)\nScanning the drive for archives:\n1 file, 100 bytes\n\nERROR: Can't open as archive: corrupted.7z\n\nERRORS:\nUnexpected end of archive")]
    [InlineData("ERROR: The system cannot find the path specified.")]
    [InlineData("WARNING: Cannot find 1 file")]
    [InlineData("ERROR: There is not enough space on the disk.")]
    public void IsPasswordRequiredError_ReturnsFalse_ForNonPasswordOutputs(string? output)
    {
        var result = DragAndDropScanner.IsPasswordRequiredError(output!);

        Assert.False(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ScanAndGetContents_ThrowsArgumentException_WhenPathIsNullOrWhitespace(string? invalidPath)
    {
        var scanner = new DragAndDropScanner();

        Assert.Throws<ArgumentException>(() => scanner.ScanAndGetContents(invalidPath!));
    }

    [Fact]
    public void ScanAndGetContents_ThrowsFileNotFoundException_WhenFileDoesNotExist()
    {
        var scanner = new DragAndDropScanner();
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"non_existent_{Guid.NewGuid():N}.7z");

        Assert.Throws<FileNotFoundException>(() => scanner.ScanAndGetContents(nonExistentPath));
    }

    [Fact]
    public void ScanAndGetContents_ThrowsFileNotFoundException_WhenFileIsNotArchive()
    {
        var scanner = new DragAndDropScanner();
        var tempNonArchiveFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempNonArchiveFile, "plain text");

        try
        {
            Assert.Throws<FileNotFoundException>(() => scanner.ScanAndGetContents(tempNonArchiveFile));
        }
        finally
        {
            if (File.Exists(tempNonArchiveFile))
            {
                File.Delete(tempNonArchiveFile);
            }
        }
    }
}