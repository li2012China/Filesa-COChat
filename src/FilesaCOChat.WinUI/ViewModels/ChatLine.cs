using System.ComponentModel;
using System.Runtime.CompilerServices;
using FilesaCOChat.Core;

namespace FilesaCOChat.WinUI.ViewModels;

/// <summary>
/// UI-only presentation wrapper around the immutable <see cref="ChatEntry"/>.
/// The Core model stays a frozen protocol value; the two pieces of state that
/// must change while an item is already on screen (timestamp grouping and the
/// one-shot entrance animation) live here and raise property notifications, so
/// realised / recycled containers update in place instead of being rebound.
/// </summary>
public sealed class ChatLine : INotifyPropertyChanged
{
    public ChatLine(ChatEntry entry) => Entry = entry;

    public ChatEntry Entry { get; }

    // Same property names as ChatEntry: the three bubble templates keep binding
    // to Sender / Text / Kind / Timestamp without any change.
    public string Sender => Entry.Sender;
    public string Text => Entry.Text;
    public ChatEntryKind Kind => Entry.Kind;
    public DateTime Timestamp => Entry.Timestamp;

    /// <summary>True for every line rendered by the centred SystemLine template.</summary>
    public bool IsSystemLine => ChatTimeline.IsSystemLine(Entry);

    /// <summary>True for the local transcript of a user-issued command request.</summary>
    public bool IsCommandRequest => Entry.Kind == ChatEntryKind.CommandRequest;

    /// <summary>True for an incoming COMMAND frame (left-aligned terminal bubble).</summary>
    public bool IsCommandReceived => Entry.Kind == ChatEntryKind.CommandReceived;

    /// <summary>Target shown in the sent-command bubble header ("→ alice").</summary>
    public string CommandTarget =>
        Entry.Target is { Length: > 0 } target ? "→ " + target : string.Empty;

    /// <summary>Origin shown in the received-command bubble header ("← attacker").</summary>
    public string CommandSource =>
        Entry.Origin is { Length: > 0 } origin ? "← " + origin : string.Empty;

    /// <summary>Full refusal wording, rendered inside both command bubbles.</summary>
    public string CommandNotice => UiText.RemoteCommandDisabled;

    /// <summary>
    /// Command text in a monospace body. Falls back to the full transcript for
    /// entries that predate the structured Target/Command payload.
    /// </summary>
    public string CommandBody => Entry.Command ?? Entry.Text;

    private bool _showTimestamp = true;

    /// <summary>
    /// Only the last line of a consecutive system-message group shows the group
    /// time. Chat bubbles always keep their header time and are unaffected.
    /// </summary>
    public bool ShowTimestamp
    {
        get => _showTimestamp;
        set
        {
            if (_showTimestamp == value) return;
            _showTimestamp = value;
            Raise();
        }
    }

    private bool _playEntranceAnimation;

    /// <summary>Consumed exactly once by the container that realises a new bubble.</summary>
    public bool PlayEntranceAnimation
    {
        get => _playEntranceAnimation;
        set
        {
            if (_playEntranceAnimation == value) return;
            _playEntranceAnimation = value;
            Raise();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
