using System.Text;
using ChatAyi.Models;

namespace ChatAyi.Services;

public sealed class PromptContextAssembler
{
    public sealed record BuildInput(
        string SafetyAndBoundaries,
        AssistantPersona Persona,
        UserProfile UserProfile,
        SessionContextSnapshot SessionContext,
        string UserMessage);

    public List<object> Build(BuildInput input)
    {
        var safetyBlock = BuildSafetyAndBoundariesBlock(input.SafetyAndBoundaries);
        var personaBlock = BuildPersonaBlock(input.Persona);
        var profileBlock = BuildProfileBlock(input.UserProfile);
        var sessionBlock = BuildSessionContextBlock(input.SessionContext);
        var userMessage = string.IsNullOrWhiteSpace(input.UserMessage)
            ? string.Empty
            : input.UserMessage.Trim();

        return new List<object>
        {
            new { role = "system", content = safetyBlock },
            new { role = "system", content = personaBlock },
            new { role = "system", content = profileBlock },
            new { role = "system", content = sessionBlock },
            new { role = "user", content = userMessage }
        };
    }

    private static string BuildSafetyAndBoundariesBlock(string safetyAndBoundaries)
    {
        if (string.IsNullOrWhiteSpace(safetyAndBoundaries))
            return "Follow safety and application boundaries.";

        return safetyAndBoundaries.Trim();
    }

    private static string BuildPersonaBlock(AssistantPersona persona)
    {
        var p = (persona ?? AssistantPersona.Default).Normalize();
        var sb = new StringBuilder();
        sb.AppendLine("Persona:");
        sb.AppendLine("- Role statement: " + p.RoleStatement);
        sb.AppendLine("- Tone: " + p.Tone);
        sb.AppendLine("- Response style directives: " + p.ResponseStyleDirectives);
        return sb.ToString().Trim();
    }

    private static string BuildProfileBlock(UserProfile profile)
    {
        var p = (profile ?? UserProfile.Default).Normalize();
        var sb = new StringBuilder();
        sb.AppendLine("User response preferences (format-only; do not change core reasoning):");
        sb.AppendLine("- Preferred language: " + MapLanguage(p.PreferredLanguage));
        sb.AppendLine("- Response length: " + MapLength(p.ResponseLengthPreference));
        sb.AppendLine("- Formality: " + MapFormality(p.FormalityPreference));
        return sb.ToString().Trim();
    }

    private static string BuildSessionContextBlock(SessionContextSnapshot snapshot)
    {
        var normalized = SessionContextSnapshot.Create(
            snapshot?.SessionId ?? string.Empty,
            snapshot?.RecentTurns?.Select(x => (x.Role, x.Content)) ?? Enumerable.Empty<(string Role, string Content)>(),
            snapshot?.SummaryBullets ?? Enumerable.Empty<string>());

        var sb = new StringBuilder();
        sb.AppendLine("Session context:");
        sb.AppendLine("- If summary conflicts with recent turns, prefer recent turns.");

        if (normalized.RecentTurns.Count > 0)
        {
            sb.AppendLine("- Recent turns (latest up to 6):");
            foreach (var turn in normalized.RecentTurns)
            {
                sb.AppendLine("  - " + turn.Role + ": " + turn.Content);
            }
        }
        else
        {
            sb.AppendLine("- Recent turns: none available.");
        }

        if (normalized.SummaryBullets.Count > 0)
        {
            sb.AppendLine("- Lightweight summary (up to 5 bullets):");
            foreach (var bullet in normalized.SummaryBullets)
            {
                sb.AppendLine("  - " + bullet);
            }
        }

        return sb.ToString().Trim();
    }

    private static string MapLanguage(string preferredLanguage)
    {
        var normalized = string.IsNullOrWhiteSpace(preferredLanguage)
            ? UserProfile.DefaultPreferredLanguage
            : preferredLanguage.Trim();

        if (normalized.StartsWith("id", StringComparison.OrdinalIgnoreCase))
            return "Use Indonesian (Bahasa Indonesia).";

        if (normalized.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return "Use English.";

        return "Use language code: " + normalized + ".";
    }

    private static string MapLength(string responseLengthPreference)
    {
        return UserProfile.NormalizeResponseLength(responseLengthPreference) switch
        {
            "brief" => "Keep answers concise.",
            "detailed" => "Provide thorough detail when useful.",
            _ => "Keep answers balanced in length."
        };
    }

    private static string MapFormality(string formalityPreference)
    {
        return UserProfile.NormalizeFormality(formalityPreference) switch
        {
            "casual" => "Use a casual, friendly tone.",
            "formal" => "Use a formal and professional tone.",
            _ => "Use a neutral tone."
        };
    }
}
