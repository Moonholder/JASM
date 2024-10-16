namespace GIMI_ModManager.WinUI.Helpers
{
    public static class FormaterHelpers
    {
        public static string FormatTimeSinceAdded(TimeSpan timeSinceAdded)
        {
            return timeSinceAdded switch
            {
                { Days: > 0 } => $"{Math.Round(timeSinceAdded.TotalDays)} 天前",
                { Hours: > 0 } => $"{timeSinceAdded.Hours} 小时前",
                { Minutes: > 0 } => $"{timeSinceAdded.Minutes} 分钟前",
                _ => $"{timeSinceAdded.Seconds} 秒前"
            };
        }
    }
}