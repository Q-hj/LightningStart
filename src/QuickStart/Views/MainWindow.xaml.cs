using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickStart.ViewModels;

namespace QuickStart.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SyncBehaviorRadios();
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (AppListPage is null || SettingsPage is null)
            return;

        var appsSelected = ReferenceEquals(sender, NavApps);
        AppListPage.Visibility = appsSelected ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = appsSelected ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (App.IsShuttingDown)
            return;

        e.Cancel = true;
        Hide();
    }

    private void SyncBehaviorRadios()
    {
        if (ViewModel is not { } viewModel)
            return;

        var behavior = viewModel.Behavior;

        HoverRadio.IsChecked = behavior.Trigger != "Click";
        ClickRadio.IsChecked = behavior.Trigger == "Click";

        Cols3.IsChecked = behavior.Columns == 3;
        Cols4.IsChecked = behavior.Columns == 4;
        Cols5.IsChecked = behavior.Columns == 5;
        Cols6.IsChecked = behavior.Columns == 6;

        IconSize24.IsChecked = behavior.IconSize == 24;
        IconSize32.IsChecked = behavior.IconSize == 32;
        IconSize40.IsChecked = behavior.IconSize == 40;
        IconSize48.IsChecked = behavior.IconSize == 48;
        IconSize64.IsChecked = behavior.IconSize == 64;
    }

    private void TriggerRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is FrameworkElement { Tag: string tag })
            viewModel.Behavior.Trigger = tag;
    }

    private void ColumnsRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel &&
            sender is FrameworkElement { Tag: string tag } &&
            int.TryParse(tag, out var columns))
        {
            viewModel.Behavior.Columns = columns;
        }
    }

    private void IconSizeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel &&
            sender is FrameworkElement { Tag: string tag } &&
            int.TryParse(tag, out var iconSize))
        {
            viewModel.Behavior.IconSize = iconSize;
        }
    }

    private void EntryList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && ViewModel is { } viewModel)
        {
            viewModel.RemoveCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        (Application.Current as App)?.ExitApplication();
    }

    private void Window_PreviewDragEnter(object sender, DragEventArgs e)
    {
        UpdateDragState(e);
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        UpdateDragState(e);
    }

    private void UpdateDragState(DragEventArgs e)
    {
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = hasFiles ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Window_PreviewDragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;

        if (ViewModel is not { } viewModel)
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            viewModel.AddFiles(paths);
    }
}
