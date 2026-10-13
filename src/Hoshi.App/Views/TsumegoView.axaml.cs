using Avalonia.Controls;

namespace Hoshi.App.Views;

/// <summary>The problems view; on a phone the panel goes under the board.</summary>
public partial class TsumegoView : UserControl
{
    public TsumegoView()
    {
        InitializeComponent();
        if (Services.PlatformInfo.IsPhone)
        {
            TsumegoGrid.ColumnDefinitions = new ColumnDefinitions("*");
            TsumegoGrid.RowDefinitions = new RowDefinitions("Auto,*");
            TsumegoBoardArea.Height = 380;
            Grid.SetRow(TsumegoPanel, 1);
            Grid.SetColumn(TsumegoPanel, 0);
        }
    }
}
