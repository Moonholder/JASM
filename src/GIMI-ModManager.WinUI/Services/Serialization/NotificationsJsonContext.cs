using System.Text.Json.Serialization;
using System.Text.Json;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.Core.Services.GameBanana.Models;

namespace GIMI_ModManager.WinUI.Services.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ModNotificationsRoot))]
[JsonSerializable(typeof(ModNotificationsRootLegacy))]
[JsonSerializable(typeof(ModNotification))]
[JsonSerializable(typeof(LegacyModNotification))]
[JsonSerializable(typeof(LegacyModNotification.ModsRetrievedResultLegacy))]
[JsonSerializable(typeof(ModsRetrievedResult))]
[JsonSerializable(typeof(ModFileInfo))]
internal partial class NotificationsJsonContext : JsonSerializerContext
{
}