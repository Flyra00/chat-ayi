using System.Diagnostics;
using ChatAyi.Services;

namespace ChatAyi.Pages;

public partial class GetStartedPage : ContentPage
{
    private bool _hasAnimated;
    private readonly AuthStore _auth;

    public GetStartedPage()
    {
        InitializeComponent();

        var services = Application.Current?.Handler?.MauiContext?.Services;
        _auth = services?.GetService<AuthStore>() ?? new AuthStore();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_hasAnimated)
            return;

        _hasAnimated = true;
        Root.TranslationY = 12;

        await Task.WhenAll(
            Root.FadeTo(1, 350, Easing.CubicOut),
            Root.TranslateTo(0, 0, 350, Easing.CubicOut)
        );
    }

    private async void OnGetStartedClicked(object sender, EventArgs e)
    {
        StartButton.IsEnabled = false;
        try
        {
            var hasAccount = await _auth.HasAccountAsync();
            var route = hasAccount ? "login" : "register";
            Debug.WriteLine($"[GetStarted] hasAccount={hasAccount}, navigating to {route}");
            await Shell.Current.GoToAsync(route);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GetStarted] Auth navigation failed: {ex}");
            await DisplayAlert("Navigation Error", ex.Message, "OK");
        }
        finally
        {
            StartButton.IsEnabled = true;
        }
    }
}
