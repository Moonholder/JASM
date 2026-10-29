using System.Collections.Generic;
using System.Text.Json.Serialization;
using GIMI_ModManager.Core.Services.Network.Models;

namespace GIMI_ModManager.Core.Services.Network.Serialization;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(DownloadSessionMetadata))]
[JsonSerializable(typeof(ChunkMetadata))]
[JsonSerializable(typeof(List<ChunkMetadata>))]
internal partial class NetworkJsonContext : JsonSerializerContext
{
}