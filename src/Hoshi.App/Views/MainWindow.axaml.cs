using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Hoshi.App.ViewModels;

namespace Hoshi.App.Views;

public partial class MainWindow : Window
{
    /// <summary>Design-time constructor.</summary>
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        if (this.FindControl<Border>("AppHeader") is { } bar)
        {
            bar.SizeChanged += (_, e) => ApplyBarWidth(bar, e.NewSize.Width);

            // Window chrome: the header is the title bar while it shows (room for macOS's traffic lights).
            if (Controls.HoshiTitleBar.TrafficLightsRoom > 0)
            {
                bar.Padding = new Avalonia.Thickness(bar.Padding.Left + Controls.HoshiTitleBar.TrafficLightsRoom, 0, bar.Padding.Right, 0);
            }

            Controls.HoshiTitleBar.SetIsMerged(this, bar.IsVisible);
            bar.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty)
                {
                    Controls.HoshiTitleBar.SetIsMerged(this, bar.IsVisible);
                }
            };
        }

        // Layout only: the review's move column needs a wide window.
        SizeChanged += (_, e) =>
        {
            bool wide = e.NewSize.Width >= MoveColumnWidth;
            if (this.FindControl<Control>("MoveColumnHost") is { } column)
            {
                column.IsVisible = wide && DataContext is not MainWindowViewModel { IsZen: true };
            }

            if (DataContext is MainWindowViewModel vm)
            {
                vm.IsWideLayout = wide;
            }
        };
    }

    /// <summary>The review's move column shows from this window width (narrower: the board keeps the room).</summary>
    public const double MoveColumnWidth = 1180;

    /// <summary>Below this width the header drops the game's title; below <see cref="NarrowBarWidth"/> some tools move to "⋯".</summary>
    public const double CompactBarWidth = 1280;

    public const double NarrowBarWidth = 1000;

    // Layout only: the header fits the window (the title, then names and the less used tools, go first).
    private void ApplyBarWidth(Border bar, double width)
    {
        bool compact = width < CompactBarWidth, narrow = width < NarrowBarWidth;
        bar.Classes.Set("compact", compact);
        bar.Classes.Set("narrow", narrow);
        if (this.FindControl<Control>("OptionalBarButtons") is { } optional)
        {
            optional.IsVisible = !narrow;
        }

        if (this.FindControl<Control>("OnlineButtonHost") is { } online)
        {
            online.IsVisible = !narrow;
        }

        if (this.FindControl<Control>("MoreButton") is { } more)
        {
            more.IsVisible = narrow;
        }
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.FindControl<Control>("Board")?.Focus();
        if (DataContext is MainWindowViewModel { SidebarWidth: { } width } && RootGrid is { } root && root.ColumnDefinitions.Count > 2)
        {
            root.ColumnDefinitions[2].Width = new GridLength(Math.Clamp(width, 220, 420));
        }

        // The sidebar keeps the width it was dragged to.
        if (this.FindControl<GridSplitter>("SidebarSplitter") is { } splitter)
        {
            splitter.DragCompleted += (_, _) =>
            {
                if (DataContext is MainWindowViewModel vm && RootGrid is { } grid)
                {
                    vm.RememberSidebarWidth(grid.ColumnDefinitions[2].ActualWidth);
                }
            };
        }
    }

    private GridLength _sidebarWidth = new(280);

    /// <summary>The layout grid (inside the interface-size transform).</summary>
    private Grid? RootGrid => Content as Grid ?? (Content as LayoutTransformControl)?.Child as Grid;

    /// <summary>Zen mode hides the sidebar and its splitter (and gives the board their room); leaving restores them.</summary>
    private void ApplyZen(bool zen)
    {
        if (RootGrid is not { } root || root.ColumnDefinitions.Count < 3)
        {
            return;
        }

        if (zen)
        {
            _sidebarWidth = root.ColumnDefinitions[2].Width;
            root.ColumnDefinitions[2].Width = new GridLength(0);
        }
        else
        {
            root.ColumnDefinitions[2].Width = _sidebarWidth;
        }

        if (this.FindControl<Control>("Sidebar") is { } sidebar)
        {
            sidebar.IsVisible = !zen;
        }

        if (this.FindControl<Control>("SidebarHost") is { } host)
        {
            host.IsVisible = !zen;
        }

        if (this.FindControl<Control>("MoveColumnHost") is { } column)
        {
            column.IsVisible = !zen && Bounds.Width >= MoveColumnWidth;
        }

        if (this.FindControl<Control>("SidebarSplitter") is { } splitter)
        {
            splitter.IsVisible = !zen;
        }
    }

    // View-only plumbing: keep the newest chat line in view.
    private System.Collections.Specialized.INotifyCollectionChanged? _chat;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged += (_, a) =>
            {
                if (a.PropertyName == nameof(MainWindowViewModel.IsZen))
                {
                    ApplyZen(vm.IsZen);
                }

                if (a.PropertyName is nameof(MainWindowViewModel.Online) or "" or null)
                {
                    WatchChat(vm.Online?.ChatLines);
                }
            };
            WatchChat(vm.Online?.ChatLines);
        }
    }

    private void WatchChat(System.Collections.Specialized.INotifyCollectionChanged? lines)
    {
        if (ReferenceEquals(lines, _chat))
        {
            return;
        }

        if (_chat is not null)
        {
            _chat.CollectionChanged -= OnChatChanged;
        }

        _chat = lines;
        if (_chat is not null)
        {
            _chat.CollectionChanged += OnChatChanged;
        }
    }

    private void OnChatChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is { Count: > 0 } added && this.FindControl<ListBox>("ChatList") is { } list)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => list.ScrollIntoView(added[^1]!));
        }
    }

    // View-only plumbing: translate the dropped file into the view model's open operation.
    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm
            && e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath() is { } path)
        {
            // async void (an event handler): a bad file must end in the log, not take the app down.
            try
            {
                await vm.Game.OpenFileAsync(path);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Services.Diagnostics.CrashGuard.Report(ex, "Opening a dropped file");
            }
        }
    }

    /// <summary>The sound panel shows the current settings (Preferences may have changed them).</summary>
    private void OnAudioOpened(object? sender, EventArgs e) => (DataContext as MainWindowViewModel)?.Audio?.Refresh();
}
