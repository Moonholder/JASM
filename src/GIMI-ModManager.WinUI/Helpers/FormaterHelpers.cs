using GIMI_ModManager.Core.Contracts.Services;

namespace GIMI_ModManager.WinUI.Helpers
{
    public static class FormaterHelpers
    {
        public static string FormatTimeSinceAdded(TimeSpan timeSinceAdded)
        {
            var localizer = App.GetService<ILanguageLocalizer>();

            if (timeSinceAdded.TotalDays >= 365)
            {
                return string.Format(
                    localizer.GetLocalizedStringOrDefault("TimeAgo_Years", "{0} years ago"),
                    (int)(timeSinceAdded.TotalDays / 365));
            }

            if (timeSinceAdded.TotalDays >= 30)
            {
                return string.Format(
                    localizer.GetLocalizedStringOrDefault("TimeAgo_Months", "{0} months ago"),
                    (int)(timeSinceAdded.TotalDays / 30));
            }

            return timeSinceAdded switch
            {
                { Days: > 0 } => string.Format(
                    localizer.GetLocalizedStringOrDefault("TimeAgo_Days", "{0} days ago"),
                    Math.Round(timeSinceAdded.TotalDays)),

                { Hours: > 0 } => string.Format(
                    localizer.GetLocalizedStringOrDefault("TimeAgo_Hours", "{0} hours ago"),
                    timeSinceAdded.Hours),

                { Minutes: > 0 } => string.Format(
                    localizer.GetLocalizedStringOrDefault("TimeAgo_Minutes", "{0} minutes ago"),
                    timeSinceAdded.Minutes),

                _ => string.Format(
                    localizer.GetLocalizedStringOrDefault("TimeAgo_Seconds", "{0} seconds ago"),
                    timeSinceAdded.Seconds)
            };
        }
    }
}