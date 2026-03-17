using System.Text.Json.Serialization;

namespace ChatAyi.Models;

public sealed record UserProfile
{
    public const string DefaultDisplayName = "User";
    public const string DefaultPreferredLanguage = "id-ID";
    public const string DefaultTimezone = "Asia/Jakarta";
    public const string DefaultResponseLengthPreference = "balanced";
    public const string DefaultFormalityPreference = "neutral";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = DefaultDisplayName;

    [JsonPropertyName("preferred_language")]
    public string PreferredLanguage { get; init; } = DefaultPreferredLanguage;

    [JsonPropertyName("timezone")]
    public string Timezone { get; init; } = DefaultTimezone;

    [JsonPropertyName("response_length_preference")]
    public string ResponseLengthPreference { get; init; } = DefaultResponseLengthPreference;

    [JsonPropertyName("formality_preference")]
    public string FormalityPreference { get; init; } = DefaultFormalityPreference;

    public static UserProfile Default => new();

    public UserProfile Normalize()
        => new()
        {
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? DefaultDisplayName : DisplayName.Trim(),
            PreferredLanguage = string.IsNullOrWhiteSpace(PreferredLanguage) ? DefaultPreferredLanguage : PreferredLanguage.Trim(),
            Timezone = string.IsNullOrWhiteSpace(Timezone) ? DefaultTimezone : Timezone.Trim(),
            ResponseLengthPreference = NormalizeResponseLength(ResponseLengthPreference),
            FormalityPreference = NormalizeFormality(FormalityPreference)
        };

    public static string NormalizeResponseLength(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "brief" or "balanced" or "detailed"
            ? normalized
            : DefaultResponseLengthPreference;
    }

    public static string NormalizeFormality(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "casual" or "neutral" or "formal"
            ? normalized
            : DefaultFormalityPreference;
    }
}
