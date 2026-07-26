using CommunityToolkit.Mvvm.ComponentModel;

namespace GetThere.ViewModels;

public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isAuthenticated;
}
