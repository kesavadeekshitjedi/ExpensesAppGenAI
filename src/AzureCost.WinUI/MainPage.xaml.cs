using AzureCost_WinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace AzureCost_WinUI;

/// <summary>
/// The main content page. It resolves its view model from the DI container and kicks off
/// the initial load once the page is ready.
/// </summary>
public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }

    public MainPage()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (ViewModel.InitializeCommand.CanExecute(null))
            {
                await ViewModel.InitializeCommand.ExecuteAsync(null);
            }
        };
    }
}
