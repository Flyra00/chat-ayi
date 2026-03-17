using System.Text.Json;
using ChatAyi.Models;

namespace ChatAyi.Services;

public sealed class PersonaProfileStore
{
    private const string PersonaKey = "ChatAyi.Persona";
    private const string ProfileKey = "ChatAyi.UserProfile";

    public AssistantPersona LoadPersona()
    {
        var raw = Preferences.Get(PersonaKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return AssistantPersona.Default.Normalize();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AssistantPersona>(raw);
            return (parsed ?? AssistantPersona.Default).Normalize();
        }
        catch
        {
            return AssistantPersona.Default.Normalize();
        }
    }

    public void SavePersona(AssistantPersona persona)
    {
        var normalized = (persona ?? AssistantPersona.Default).Normalize();
        Preferences.Set(PersonaKey, JsonSerializer.Serialize(normalized));
    }

    public UserProfile LoadProfile()
    {
        var raw = Preferences.Get(ProfileKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return UserProfile.Default.Normalize();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<UserProfile>(raw);
            return (parsed ?? UserProfile.Default).Normalize();
        }
        catch
        {
            return UserProfile.Default.Normalize();
        }
    }

    public void SaveProfile(UserProfile profile)
    {
        var normalized = (profile ?? UserProfile.Default).Normalize();
        Preferences.Set(ProfileKey, JsonSerializer.Serialize(normalized));
    }
}
