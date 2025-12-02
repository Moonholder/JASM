using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.UI.Xaml.Controls;

namespace GIMI_ModManager.WinUI.Helpers;

public static class FrameExtensions
{
    public static object? GetPageViewModel(this Frame frame)
    {
        return GetPageViewModel_Internal(frame?.Content);
    }

    private static object? GetPageViewModel_Internal([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] object? page)
    {
        if (page == null) return null;
        var t = page.GetType();
        var prop = t.GetProperty("ViewModel", BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(page, null);
    }
}