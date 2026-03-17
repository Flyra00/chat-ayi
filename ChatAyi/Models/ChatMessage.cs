using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChatAyi.Models;

public sealed class ChatMessage : INotifyPropertyChanged
{
    private string _content;
    private bool _isEphemeral;

    public ChatMessage(string role, string content, bool isEphemeral = false)
    {
        Role = role;
        _content = content;
        _isEphemeral = isEphemeral;
    }

    public string Role { get; }

    public string Content
    {
        get => _content;
        set
        {
            if (_content == value) return;
            _content = value;
            OnPropertyChanged();
        }
    }

    // Ephemeral messages are placeholders (streaming/queued) and should be excluded from LLM context.
    public bool IsEphemeral
    {
        get => _isEphemeral;
        set
        {
            if (_isEphemeral == value) return;
            _isEphemeral = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string name = "")
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
