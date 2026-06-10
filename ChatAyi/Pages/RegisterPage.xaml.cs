using System.Diagnostics;
using ChatAyi.Services;

namespace ChatAyi.Pages;

public partial class RegisterPage : ContentPage
{
    private readonly AuthStore _auth;

    public RegisterPage()
    {
        InitializeComponent();

        var services = Application.Current?.Handler?.MauiContext?.Services;
        _auth = services?.GetService<AuthStore>() ?? new AuthStore();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        Root.TranslationY = 10;
        await Task.WhenAll(
            Root.FadeTo(1, 300, Easing.CubicOut),
            Root.TranslateTo(0, 0, 300, Easing.CubicOut));
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        RegisterButton.IsEnabled = false;
        RegisterButton.Text = "Creating...";

        try
        {
            var displayName = DisplayNameEntry.Text?.Trim() ?? string.Empty;
            var email = EmailEntry.Text?.Trim() ?? string.Empty;
            var password = PasswordEntry.Text ?? string.Empty;
            var confirmPassword = ConfirmPasswordEntry.Text ?? string.Empty;

            // Client-side validation
            if (string.IsNullOrWhiteSpace(displayName))
            {
                ShowError("Display name tidak boleh kosong.");
                return;
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                ShowError("Email/username tidak boleh kosong.");
                return;
            }

            if (password.Length < 6)
            {
                ShowError("Password minimal 6 karakter.");
                return;
            }

            if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
            {
                ShowError("Password dan konfirmasi tidak cocok.");
                return;
            }

            var result = await _auth.RegisterAsync(displayName, email, password, CancellationToken.None);

            if (result.Success)
            {
                Debug.WriteLine("[Register] success, navigating to login");
                await Shell.Current.GoToAsync("../login");
            }
            else
            {
                ShowError(result.Message);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Register] error: {ex}");
            ShowError($"Error: {ex.Message}");
        }
        finally
        {
            RegisterButton.IsEnabled = true;
            RegisterButton.Text = "Create account";
        }
    }

    private async void OnLoginLinkTapped(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("../login");
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }

    private void OnTogglePasswordVisibility(object sender, EventArgs e)
    {
        PasswordEntry.IsPassword = !PasswordEntry.IsPassword;
        PasswordToggle.Text = PasswordEntry.IsPassword ? "Show" : "Hide";
    }

    private void OnToggleConfirmPasswordVisibility(object sender, EventArgs e)
    {
        ConfirmPasswordEntry.IsPassword = !ConfirmPasswordEntry.IsPassword;
        ConfirmPasswordToggle.Text = ConfirmPasswordEntry.IsPassword ? "Show" : "Hide";
    }
}
