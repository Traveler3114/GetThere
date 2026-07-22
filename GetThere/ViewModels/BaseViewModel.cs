using CommunityToolkit.Mvvm.ComponentModel;

namespace GetThere.ViewModels;

public partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;
}
