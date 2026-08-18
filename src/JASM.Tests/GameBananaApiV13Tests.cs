using System.Text.Json;
using GIMI_ModManager.Core.Services.GameBanana.ApiModels;
using GIMI_ModManager.Core.Services.GameBanana.Serialization;

namespace JASM.Tests;

/// <summary>
/// Smoke tests for the GameBanana apiv13 response shapes, using captured
/// samples from the live API (Subfeed / Search / ProfilePage / Updates).
/// </summary>
public class GameBananaApiV13Tests
{
    private static string TestDataPath(string fileName) =>
        Path.GetFullPath($"..\\..\\..\\..\\JASM.Tests\\TestData\\GameBanana\\{fileName}");

    private static async Task<T> DeserializeAsync<T>(string fileName) where T : class
    {
        await using var stream = File.OpenRead(TestDataPath(fileName));
        var result = await JsonSerializer.DeserializeAsync(
            stream,
            GetTypeInfo<T>(),
            cancellationToken: default);
        Assert.NotNull(result);
        return result!;
    }

    private static System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> GetTypeInfo<T>()
    {
        // The source-generated context is exercised explicitly so the same
        // serialization path used by the app is what we verify.
        return typeof(T) switch
        {
            _ when typeof(T) == typeof(ApiPaginatedResponse<ApiModRecord>) =>
                (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)
                (object)GameBananaApiJsonContext.Default.ApiPaginatedResponseApiModRecord,
            _ when typeof(T) == typeof(ApiModProfile) =>
                (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)
                (object)GameBananaApiJsonContext.Default.ApiModProfile,
            _ when typeof(T) == typeof(ApiModUpdateList) =>
                (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)
                (object)GameBananaApiJsonContext.Default.ApiModUpdateList,
            _ => throw new NotSupportedException($"No type info for {typeof(T)}")
        };
    }

    [Fact]
    public async Task Subfeed_ParsesPreviewContentAndSfwThumbnail()
    {
        var response = await DeserializeAsync<ApiPaginatedResponse<ApiModRecord>>("Subfeed_apiv13.json");

        Assert.Equal(2, response.Records.Count);
        var first = response.Records[0];

        Assert.Equal(702798, first.ModId);
        Assert.True(first.HasContentRatings);
        Assert.NotNull(first.ThumbnailUrl);
        Assert.Contains("_530.webp", first.ThumbnailUrl);
        Assert.NotNull(first.SfwThumbnailUrl);
        Assert.Contains("_220_sfw.webp", first.SfwThumbnailUrl);
        Assert.Equal(530.0 / 298.0, first.ThumbnailAspectRatio, 2);

        Assert.False(response.Records[1].HasContentRatings);
    }

    [Fact]
    public async Task Search_ParsesRecords()
    {
        var response = await DeserializeAsync<ApiPaginatedResponse<ApiModRecord>>("Search_apiv13.json");

        Assert.Equal(2, response.Records.Count);
        Assert.Equal("Larger Liino", response.Records[0].Name);
        Assert.NotNull(response.Records[0].ThumbnailUrl);
        Assert.Equal(3, response.Records[0].Tags.Count);
    }

    [Fact]
    public async Task ProfilePage_ParsesScreenshotsAndFiles()
    {
        var profile = await DeserializeAsync<ApiModProfile>("ProfilePage_apiv13.json");

        Assert.Equal(691126, profile.ModId);
        Assert.Equal("1.2", profile.Version);
        Assert.Equal(5173, profile.DownloadCount);

        Assert.NotNull(profile.PreviewContent);
        var screenshots = profile.PreviewContent!.GetValueOrDefault("screenshots");
        Assert.NotNull(screenshots);
        Assert.Equal(3, screenshots!.Count);

        Assert.NotNull(profile.Files);
        Assert.Equal(3, profile.Files!.Count);
        var firstFile = profile.Files!.First();
        Assert.Equal(1744127, firstFile.FileId);
        Assert.True(firstFile.FileSize > 0);
        Assert.Equal("clean", firstFile.AvResult);
    }

    [Fact]
    public async Task Updates_ParsesChangeLogAndFiles()
    {
        var response = await DeserializeAsync<ApiModUpdateList>("Updates_apiv13.json");

        Assert.NotNull(response.Records);
        Assert.Equal(2, response.Records!.Count);

        var first = response.Records[0];
        Assert.Equal("1.2", first.Version);
        Assert.NotNull(first.ChangeLog);
        Assert.Single(first.ChangeLog!);
        Assert.False(string.IsNullOrEmpty(first.ChangeLog![0].Text));

        Assert.NotNull(first.Files);
        Assert.Single(first.Files!);
        Assert.Equal("gilberta_12.rar", first.Files![0].FileName);
    }
}
