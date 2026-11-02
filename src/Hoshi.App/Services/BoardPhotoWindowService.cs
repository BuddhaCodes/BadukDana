using Hoshi.App.ViewModels;
using Hoshi.App.Views;

namespace Hoshi.App.Services;

/// <summary>Shows "Photo of the board": a window on the desktop, a page on the phone.</summary>
public interface IBoardPhotoWindowService
{
    void Show(BoardPhotoViewModel photo);
}

public sealed class BoardPhotoWindowService : IBoardPhotoWindowService
{
    public void Show(BoardPhotoViewModel photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        var window = new BoardPhotoWindow { DataContext = photo };
        photo.Opened += (_, _) => window.Close();
        if (MainWindowLocator.MainWindow is { } owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }
}

public sealed class PhoneBoardPhotoService(Phone.PhoneNavigation navigation) : IBoardPhotoWindowService
{
    public void Show(BoardPhotoViewModel photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        photo.Opened += (_, _) => navigation.Close(true);
        navigation.Show(new BoardPhotoView { DataContext = photo }, Hoshi.Core.Localization.Tr.T("Photo.Title"));
    }
}
