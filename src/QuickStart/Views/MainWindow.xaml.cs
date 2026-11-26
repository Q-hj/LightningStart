using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using QuickStart.Interop;
using QuickStart.Models;
using QuickStart.ViewModels;

namespace QuickStart.Views;

public partial class MainWindow : Window
{
    private Point _dragStartPoint;
    private AppEntry? _dragCandidate;
    private DispatcherTimer? _dragPreviewTimer;
    private ListBoxItem? _dragSourceItem;

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

    private void EntryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = null;
        _dragStartPoint = e.GetPosition(EntryList);

        if (e.OriginalSource is not DependencyObject source)
            return;

        // Clicks on the inline hide/delete buttons must not start a reorder drag.
        if (FindAncestor<ButtonBase>(source) is not null)
            return;

        if (ItemsControl.ContainerFromElement(EntryList, source) is ListBoxItem { DataContext: AppEntry entry })
            _dragCandidate = entry;
    }

    private void EntryList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var position = e.GetPosition(EntryList);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var entry = _dragCandidate;
        _dragCandidate = null;

        _dragSourceItem = EntryList.ItemContainerGenerator.ContainerFromItem(entry) as ListBoxItem;
        if (_dragSourceItem is not null)
            _dragSourceItem.Opacity = 0.45;

        DragGhost.DataContext = entry;
        DragGhost.Visibility = Visibility.Visible;
        UpdateDragPreviewPosition();

        _dragPreviewTimer ??= CreateDragPreviewTimer();
        _dragPreviewTimer.Start();

        try
        {
            DragDrop.DoDragDrop(EntryList, new DataObject(typeof(AppEntry), entry), DragDropEffects.Move);
        }
        finally
        {
            EndDragPreview();
            DropIndicator.Visibility = Visibility.Collapsed;
        }
    }

    private DispatcherTimer CreateDragPreviewTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        timer.Tick += (_, _) => UpdateDragPreviewPosition();
        return timer;
    }

    private void UpdateDragPreviewPosition()
    {
        if (DragGhost.Visibility != Visibility.Visible)
            return;

        if (!NativeMethods.GetCursorPos(out var cursor))
            return;

        var position = DragPreviewCanvas.PointFromScreen(new Point(cursor.X, cursor.Y));
        Canvas.SetLeft(DragGhost, position.X + 14);
        Canvas.SetTop(DragGhost, position.Y + 14);
    }

    private void EndDragPreview()
    {
        _dragPreviewTimer?.Stop();
        DragGhost.Visibility = Visibility.Collapsed;
        DragGhost.DataContext = null;

        if (_dragSourceItem is not null)
            _dragSourceItem.Opacity = 1;

        _dragSourceItem = null;
    }

    private void EntryList_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(AppEntry)))
            return;

        e.Effects = DragDropEffects.Move;
        UpdateDragPreviewPosition();

        if (GetDropInsertIndex(e, out var y) < 0)
        {
            DropIndicator.Visibility = Visibility.Collapsed;
        }
        else
        {
            DropIndicator.Margin = new Thickness(4, Math.Max(0, y), 4, 0);
            DropIndicator.Visibility = Visibility.Visible;
        }

        e.Handled = true;
    }

    private void EntryList_DragLeave(object sender, DragEventArgs e)
    {
        DropIndicator.Visibility = Visibility.Collapsed;
    }

    private void EntryList_Drop(object sender, DragEventArgs e)
    {
        DropIndicator.Visibility = Visibility.Collapsed;

        if (e.Data.GetData(typeof(AppEntry)) is not AppEntry source || ViewModel is not { } viewModel)
            return;

        var insertIndex = GetDropInsertIndex(e, out _);
        if (insertIndex >= 0)
            viewModel.MoveEntryTo(source, insertIndex);

        e.Handled = true;
    }

    private int GetDropInsertIndex(DragEventArgs e, out double y)
    {
        y = 0;

        if (ViewModel is not { } viewModel || EntryList.Items.Count == 0)
            return -1;

        var position = e.GetPosition(EntryList);
        var source = e.OriginalSource as DependencyObject;
        var container = source is null
            ? null
            : ItemsControl.ContainerFromElement(EntryList, source) as ListBoxItem;

        if (container?.DataContext is AppEntry target)
        {
            var top = container.TranslatePoint(new Point(0, 0), EntryList).Y;
            var insertAfter = position.Y > top + container.ActualHeight / 2;
            y = insertAfter ? top + container.ActualHeight : top;
            return viewModel.Entries.IndexOf(target) + (insertAfter ? 1 : 0);
        }

        var lastContainer = EntryList.ItemContainerGenerator
            .ContainerFromIndex(EntryList.Items.Count - 1) as ListBoxItem;

        y = lastContainer is not null
            ? lastContainer.TranslatePoint(new Point(0, lastContainer.ActualHeight), EntryList).Y
            : EntryList.ActualHeight;

        return viewModel.Entries.Count;
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T typed)
                return typed;

            node = GetParent(node);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject node)
    {
        if (node is Visual)
            return VisualTreeHelper.GetParent(node);
        if (node is FrameworkContentElement contentElement)
            return contentElement.Parent;
        return LogicalTreeHelper.GetParent(node);
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
        if (e.Data.GetDataPresent(typeof(AppEntry)))
        {
            // Internal list reorder: let the ListBox handle this drag.
            e.Effects = DragDropEffects.Move;
            DropOverlay.Visibility = Visibility.Collapsed;
            return;
        }

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
        if (e.Data.GetDataPresent(typeof(AppEntry)))
            return;

        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;

        if (ViewModel is not { } viewModel)
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            viewModel.AddFiles(paths);
    }
}
