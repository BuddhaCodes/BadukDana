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
        if (DataContext is MainWindowViewModel { SidebarWidth: { } width } && Content is Grid root && root.ColumnDefinitions.Count > 2)
        {
            root.ColumnDefinitions[2].Width = new GridLength(Math.Clamp(width, 220, 420));
        }

        // The sidebar keeps the width it was dragged to.
        if (this.FindControl<GridSplitter>("SidebarSplitter") is { } splitter)
        {
            splitter.DragCompleted += (_, _) =>
            {
                if (DataContext is MainWindowViewModel vm && Content is Grid grid)
                {
                    vm.RememberSidebarWidth(grid.ColumnDefinitions[2].ActualWidth);
                }
            };
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
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm
            && e.Data.GetFiles()?.FirstOrDefault()?.TryGetLocalPath() is { } path)
        {
            await vm.Game.OpenFileAsync(path);
        }
    }

    /// <summary>The sound panel shows the current settings (Preferences may have changed them).</summary>
    private void OnAudioOpened(object? sender, EventArgs e) => (DataContext as MainWindowViewModel)?.Audio?.Refresh();
}
