using GIMI_ModManager.Core.Entities;
using GIMI_ModManager.Core.Entities.Mods.SkinMod;
using Xunit;

namespace GIMI_ModManager.Tests;

public class ModRenameTests
{
    [Fact]
    public async Task Rename_ShouldUpdateDirectoryInfoAndFullPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "JASM_Test_" + Guid.NewGuid().ToString("N"));
        var modDir = Path.Combine(tempDir, "DISABLED_TestMod");
        Directory.CreateDirectory(modDir);

        try
        {
            var skinMod = await SkinMod.CreateModAsync(modDir);
            Assert.Equal("DISABLED_TestMod", skinMod.Name);
            Assert.Equal(modDir, skinMod.FullPath);

            skinMod.Rename("TestMod");

            Assert.Equal("TestMod", skinMod.Name);
            var expectedPath = Path.Combine(tempDir, "TestMod");
            Assert.Equal(expectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), skinMod.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            Assert.True(Directory.Exists(skinMod.FullPath));
            Assert.False(Directory.Exists(modDir));

            var keySwaps = await skinMod.KeySwaps!.ReadAllKeySwapConfigurations();
            Assert.NotNull(keySwaps);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}