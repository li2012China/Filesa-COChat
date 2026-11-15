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
