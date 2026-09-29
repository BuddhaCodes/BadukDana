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
}
