using System.Diagnostics;
using ChatAyi.Services;

namespace ChatAyi.Pages;

public partial class LoginPage : ContentPage, IQueryAttributable
{
    private readonly AuthStore _auth;
    private bool _fromGuard;

    public LoginPage()
    {
        InitializeComponent();

        var services = Application.Current?.Handler?.MauiContext?.Services;
        _auth = services?.GetService<AuthStore>() ?? new AuthStore();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query?.TryGetValue("guarded", out var v) == true)
            _fromGuard = v?.ToString() == "1";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Show welcome back if we know the user's name
        var displayName = _auth.GetDisplayName();
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            WelcomeLabel.Text = $"Halo, {displayName}";
            WelcomeLabel.IsVisible = true;

            // Pre-fill email
            var email = _auth.GetEmail();
            if (!string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(EmailEntry.Text))
                EmailEntry.Text = email;
        }

        Root.TranslationY = 10;
        await Task.WhenAll(
            Root.FadeTo(1, 300, Easing.CubicOut),
            Root.TranslateTo(0, 0, 300, Easing.CubicOut));
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        LoginButton.IsEnabled = false;
        LoginButton.Text = "Logging in...";

        try
        {
            var email = EmailEntry.Text?.Trim() ?? string.Empty;
            var password = PasswordEntry.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email))
            {
                ShowError("Email/username tidak boleh kosong.");
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                ShowError("Password tidak boleh kosong.");
                return;
            }

            var result = await _auth.LoginAsync(email, password, CancellationToken.None);

            if (result.Success)
            {
                Debug.WriteLine("[Login] success, navigating to chat");
                if (_fromGuard)
                {
                    // Pop back to ChatPage that triggered the auth guard
                    await Shell.Current.GoToAsync("..");
                }
                else
                {
                    // Normal GetStarted -> Login flow
                    await Shell.Current.GoToAsync("chat?fresh=1");
                }
            }
            else
            {
                ShowError(result.Message);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Login] error: {ex}");
            ShowError($"Error: {ex.Message}");
        }
        finally
        {
            LoginButton.IsEnabled = true;
            LoginButton.Text = "Login";
        }
    }

    private async void OnRegisterLinkTapped(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("../register");
    }

    private async void OnResetTapped(object sender, EventArgs e)
    {
        var confirm = await DisplayAlert(
            "Reset Akun",
            "Semua data akun lokal akan dihapus. Kamu harus register ulang. Lanjutkan?",
            "Ya, reset",
            "Batal");

        if (!confirm) return;

        _auth.ResetAccount();
        await DisplayAlert("Reset Berhasil", "Akun lokal dihapus. Silakan register ulang.", "OK");
        await Shell.Current.GoToAsync("../register");
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
