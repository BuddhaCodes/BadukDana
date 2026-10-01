using CommunityToolkit.Mvvm.ComponentModel;
using Hoshi.Core.Localization;

namespace Hoshi.App.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected ViewModelBase()
    {
        // Computed texts come from Tr: refresh every binding when the language changes.
        Tr.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);
    }
}
