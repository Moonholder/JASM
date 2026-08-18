using System.Text.Json.Serialization;
using System.Text.Json;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.Core.Services.GameBanana.Models;

namespace GIMI_ModManager.WinUI.Services.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ModNotificationsRoot))]
#pragma warning disable CS0618 // Deliberately deserializing the legacy notification format
[JsonSerializable(typeof(ModNotificationsRootLegacy))]
[JsonSerializable(typeof(ModNotification))]
[JsonSerializable(typeof(LegacyModNotification))]
[JsonSerializable(typeof(LegacyModNotification.ModsRetrievedResultLegacy))]
#pragma warning restore CS0618
[JsonSerializable(typeof(ModsRetrievedResult))]
[JsonSerializable(typeof(ModFileInfo))]
internal partial class NotificationsJsonContext : JsonSerializerContext
{
}