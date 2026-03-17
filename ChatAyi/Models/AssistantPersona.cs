using System.Text.Json.Serialization;

namespace ChatAyi.Models;

public sealed record AssistantPersona
{
    public const string DefaultRoleStatement = "You are ChatAyi, a private personal AI assistant for one user.";
    public const string DefaultTone = "calm";
    public const string DefaultResponseStyleDirectives = "Direct, practical, and context-aware.";

    [JsonPropertyName("role_statement")]
    public string RoleStatement { get; init; } = DefaultRoleStatement;

    [JsonPropertyName("tone")]
    public string Tone { get; init; } = DefaultTone;

    [JsonPropertyName("response_style_directives")]
    public string ResponseStyleDirectives { get; init; } = DefaultResponseStyleDirectives;

    public static AssistantPersona Default => new();

    public AssistantPersona Normalize()
        => new()
        {
            RoleStatement = string.IsNullOrWhiteSpace(RoleStatement)
                ? DefaultRoleStatement
                : RoleStatement.Trim(),
            Tone = NormalizeTone(Tone),
            ResponseStyleDirectives = string.IsNullOrWhiteSpace(ResponseStyleDirectives)
                ? DefaultResponseStyleDirectives
                : ResponseStyleDirectives.Trim()
        };

    public static string NormalizeTone(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "calm" or "toxic" or "professional"
            ? normalized
            : DefaultTone;
    }
}
