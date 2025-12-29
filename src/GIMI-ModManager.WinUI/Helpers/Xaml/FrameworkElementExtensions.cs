using System.Diagnostics;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
namespace GIMI_ModManager.WinUI.Helpers.Xaml;

public static class FrameworkElementExtensions
{
    public static async Task<bool> AwaitUiElementLoaded(this FrameworkElement element, CancellationToken ct)
    {
        if (element.IsLoaded) return true;
        var tcs = new TaskCompletionSource<bool>();
        using var reg = ct.Register(() => tcs.TrySetCanceled());

        void OnLoaded(object s, RoutedEventArgs e) => tcs.TrySetResult(true);

        try
        {
            element.Loaded += OnLoaded;
            await tcs.Task;
            return true;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        finally
        {
            element.Loaded -= OnLoaded;
        }
    }

    private const int PollingTime = 100;
    public static async Task AwaitItemsSourceLoaded(this DataGrid dataGrid, TimeSpan timeout)
    {
        if (dataGrid.ItemsSource is not null)
            return;

        var startTime = DateTime.Now;
        while (dataGrid.ItemsSource is null)
        {
            if (DateTime.Now > startTime.Add(timeout))
            {
                Debug.WriteLine("AwaitItemsSourceLoaded Timed out");
                return;
            }

            await Task.Delay(PollingTime);
        }
    }
    public static async Task AwaitItemsSourceLoaded(this DataGrid dataGrid, CancellationToken cancellationToken)
    {
        if (dataGrid.ItemsSource is not null)
            return;
        do
        {
            await Task.Delay(PollingTime, cancellationToken);
        } while (dataGrid.ItemsSource is null);
    }
}