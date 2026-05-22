using System.Diagnostics;
using System.Security.Cryptography;

namespace ChatAyi.Services;

public sealed record AuthResult(bool Success, string Message);

public sealed class AuthStore
{
    private const string HasAccountKey = "ChatAyi.Auth.HasAccount";
    private const string DisplayNameKey = "ChatAyi.Auth.DisplayName";
    private const string EmailKey = "ChatAyi.Auth.Email";
    private const string IsLoggedInKey = "ChatAyi.Auth.IsLoggedIn";
    private const string LastLoginUtcKey = "ChatAyi.Auth.LastLoginUtc";

    private const string PasswordHashStorageKey = "ChatAyi.Auth.PasswordHash";
    private const string PasswordSaltStorageKey = "ChatAyi.Auth.PasswordSalt";

    private const int SaltSizeBytes = 32;
    private const int HashSizeBytes = 32;
    private const int Pbkdf2Iterations = 100_000;

    // ── Query ──

    public Task<bool> HasAccountAsync()
    {
        var has = Preferences.Get(HasAccountKey, false);
        return Task.FromResult(has);
    }

    public bool IsLoggedIn()
        => Preferences.Get(IsLoggedInKey, false);

    public string GetDisplayName()
        => Preferences.Get(DisplayNameKey, string.Empty);

    public string GetEmail()
        => Preferences.Get(EmailKey, string.Empty);

    // ── Register ──

    public async Task<AuthResult> RegisterAsync(
        string displayName, string email, string password, CancellationToken ct)
    {
        displayName = (displayName ?? string.Empty).Trim();
        email = (email ?? string.Empty).Trim();
        password = (password ?? string.Empty);

        if (displayName.Length < 2)
            return new AuthResult(false, "Display name minimal 2 karakter.");

        if (email.Length < 3)
            return new AuthResult(false, "Email/username minimal 3 karakter.");

        if (password.Length < 6)
            return new AuthResult(false, "Password minimal 6 karakter.");

        if (Preferences.Get(HasAccountKey, false))
            return new AuthResult(false, "Akun sudah ada. Silakan login.");

        try
        {
            var saltBytes = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
                password,
                saltBytes,
                Pbkdf2Iterations,
                HashAlgorithmName.SHA256,
                HashSizeBytes);

            var saltB64 = Convert.ToBase64String(saltBytes);
            var hashB64 = Convert.ToBase64String(hashBytes);

            await SecureStorage.SetAsync(PasswordSaltStorageKey, saltB64);
            await SecureStorage.SetAsync(PasswordHashStorageKey, hashB64);

            Preferences.Set(DisplayNameKey, displayName);
            Preferences.Set(EmailKey, email);
            Preferences.Set(HasAccountKey, true);
            Preferences.Set(IsLoggedInKey, false);

            Debug.WriteLine($"[Auth] registered display={displayName} email={email}");
            return new AuthResult(true, "Akun berhasil dibuat.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Auth] register error: {ex}");
            return new AuthResult(false, $"Gagal membuat akun: {ex.Message}");
        }
    }

    // ── Login ──

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct)
    {
        email = (email ?? string.Empty).Trim();
        password = (password ?? string.Empty);

        if (!Preferences.Get(HasAccountKey, false))
            return new AuthResult(false, "Belum ada akun. Buat akun dulu.");

        var storedEmail = Preferences.Get(EmailKey, string.Empty);
        if (!string.Equals(email, storedEmail, StringComparison.OrdinalIgnoreCase))
            return new AuthResult(false, "Email/username tidak cocok.");

        try
        {
            var saltB64 = (await SecureStorage.GetAsync(PasswordSaltStorageKey))?.Trim() ?? string.Empty;
            var storedHashB64 = (await SecureStorage.GetAsync(PasswordHashStorageKey))?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(saltB64) || string.IsNullOrEmpty(storedHashB64))
                return new AuthResult(false, "Data akun rusak. Coba reset akun.");

            var saltBytes = Convert.FromBase64String(saltB64);
            var storedHashBytes = Convert.FromBase64String(storedHashB64);

            var computedHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                saltBytes,
                Pbkdf2Iterations,
                HashAlgorithmName.SHA256,
                HashSizeBytes);

            if (!CryptographicOperations.FixedTimeEquals(computedHash, storedHashBytes))
                return new AuthResult(false, "Password salah.");

            Preferences.Set(IsLoggedInKey, true);
            Preferences.Set(LastLoginUtcKey, DateTimeOffset.UtcNow.ToString("O"));

            Debug.WriteLine($"[Auth] login success email={email}");
            return new AuthResult(true, "Login berhasil.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Auth] login error: {ex}");
            return new AuthResult(false, $"Gagal login: {ex.Message}");
        }
    }

    // ── Logout ──

    public void Logout()
    {
        Preferences.Set(IsLoggedInKey, false);
        Debug.WriteLine("[Auth] logged out");
    }

    // ── Reset (development) ──

    public void ResetAccount()
    {
        Preferences.Remove(HasAccountKey);
        Preferences.Remove(DisplayNameKey);
        Preferences.Remove(EmailKey);
        Preferences.Remove(IsLoggedInKey);
        Preferences.Remove(LastLoginUtcKey);
        SecureStorage.Remove(PasswordHashStorageKey);
        SecureStorage.Remove(PasswordSaltStorageKey);
        Debug.WriteLine("[Auth] account reset");
    }
}
