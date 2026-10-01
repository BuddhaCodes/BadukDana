using System.ComponentModel;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Hoshi.Core.Localization;

namespace Hoshi.App.Localization;

/// <summary>Bindable view of <see cref="Tr"/>: <c>LocalizedStrings.Instance["Main.Pass"]</c>, refreshed when the language changes.</summary>
public sealed class LocalizedStrings : INotifyPropertyChanged
{
    private LocalizedStrings()
    {
        Tr.LanguageChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static LocalizedStrings Instance { get; } = new();

    public string this[string key] => Tr.T(key);
}

/// <summary>XAML: <c>Text="{l:T Main.Pass}"</c> — localized text that follows language changes live.</summary>
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = LocalizedStrings.Instance, Mode = BindingMode.OneWay };
}
