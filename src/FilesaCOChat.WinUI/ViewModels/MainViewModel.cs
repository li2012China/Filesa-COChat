using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FilesaCOChat.Core;
using Microsoft.UI.Dispatching;

namespace FilesaCOChat.WinUI.ViewModels;

/// <summary>
/// D9: every core event is raised on a worker thread. This is the single place
/// where they are marshalled onto the UI dispatcher before touching any
/// ObservableCollection or XAML-bound property.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly DispatcherQueue _dispatcher;
    private string _status = UiText.PreparingSearch;
    private bool _busy;
    private bool _loggedIn;
    private int _scanDone;
    private int _scanTotal = 254;

    public MainViewModel(ChatApplication app, DispatcherQueue dispatcher)
    {
        App = app;
        Settings = app.Settings;
        _dispatcher = dispatcher;
    }

    public ChatApplication App { get; }
    public AppSettings Settings { get; }
    public ObservableCollection<ChatLine> Messages { get; } = new();
    public ObservableCollection<string> Users { get; } = new();

    public event Action<ChatEntry>? NotificationRequested;
    /// <summary>A newer release was found (already filtered by the notification setting).</summary>
    public event Action<UpdateInfo>? UpdateNoticed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Status
    {
        get => _status;
        private set { _status = value; Raise(); }
    }
    public bool Busy
    {
        get => _busy;
        private set { _busy = value; Raise(); }
    }
    public bool LoggedIn
    {
        get => _loggedIn;
        private set { _loggedIn = value; Raise(); }
    }
    public int ScanDone { get => _scanDone; private set { _scanDone = value; Raise(); } }
    public int ScanTotal { get => _scanTotal; private set { _scanTotal = value; Raise(); } }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Dispatch(Action action)
    {
        if (_dispatcher.HasThreadAccess) action();
        else _dispatcher.TryEnqueue(() => action());
    }

    public void Attach()
    {
        App.MessageAdded += entry => Dispatch(() =>
        {
            AppendMessage(entry);
            if (Settings.NotificationsEnabled && entry.Kind == ChatEntryKind.Chat &&
                !string.Equals(entry.Sender, App.Username, StringComparison.Ordinal))
                NotificationRequested?.Invoke(entry);
        });
        App.UserListChanged += args => Dispatch(() =>
        {
            Users.Clear();
            foreach (var user in args.Users) Users.Add(user);
            Raise(nameof(Users));
        });
        App.StatusChanged += args => Dispatch(() =>
        {
            Status = string.IsNullOrEmpty(args.Detail) ? args.Status.ToString() : args.Detail;
            if (args.Status is AppStatus.Serving or AppStatus.Connected) { LoggedIn = true; Busy = false; }
            if (args.Status == AppStatus.Searching) Busy = true;
        });
        App.Progress += args => Dispatch(() =>
        {
            ScanDone = args.Done; ScanTotal = args.Total;
            Status = args.Detail + $" ({args.Done}/{args.Total})";
        });
        App.UpdateChecked += info => Dispatch(() =>
        {
            // The in-app update bar is not a system notification, so it is not
            // gated by the toast switch.
            if (!info.HasUpdate) return;
            Status = UiText.UpdateAvailable;
            UpdateNoticed?.Invoke(info);
        });
    }

    /// <summary>
    /// The single append path. Recomputes grouping on the UI thread (D9): the new
    /// line is the only animation target, and the previous system group's tail
    /// loses its timestamp through INotifyPropertyChanged. Everything else stays
    /// untouched and no line is reordered, inserted or removed.
    /// </summary>
    private void AppendMessage(ChatEntry entry)
    {
        var line = new ChatLine(entry);
        // Raise the one-shot flag BEFORE the item enters the collection: WinUI
        // may prepare or recycle a container (and fire DataContextChanged)
        // synchronously while the ObservableCollection is being changed.
        line.PlayEntranceAnimation = !line.IsSystemLine;
        Messages.Add(line);
        while (Messages.Count > 2000) Messages.RemoveAt(0);
        ChatTimeline.Refresh(Messages, ChatTimeline.EntranceTarget(line));
    }

    /// <summary>Preview-only: appends through the real path to exercise grouping + animation.</summary>
    public void AppendPreviewLine(ChatEntry entry) => AppendMessage(entry);

    /// <summary>
    /// Bulk / preview load: appends without an entrance animation. Call
    /// <see cref="RefreshTimeline"/> once afterwards to apply grouping.
    /// </summary>
    public void AddPreviewLine(ChatEntry entry) => Messages.Add(new ChatLine(entry));

    /// <summary>Recomputes timestamp groups without marking anything as "new".</summary>
    public void RefreshTimeline() => ChatTimeline.Refresh(Messages, null);

    public async Task<bool> LoginAsync(string username)
    {
        Busy = true;
        Status = UiText.Searching;
        try
        {
            var ok = await App.LoginAsync(username);
            if (!ok) { Busy = false; Status = UiText.MustEnterUsername; }
            return ok;
        }
        catch (Exception ex)
        {
            Dispatch(() => { Busy = false; Status = "错误: " + ex.Message; });
            return false;
        }
    }

    public Task<bool> SendAsync(string text) => App.SendMessageAsync(text);
    public Task<bool> SendFileAsync(string target, string path) => App.SendFileToUserAsync(target, path);
    public Task SendCommandAsync(string target, string command) => App.SendCommandAsync(target, command);
    public Task<UpdateInfo> CheckUpdateAsync() => App.CheckUpdateAsync();
    public Task RequestUserListAsync() => App.RequestUserListAsync();
}
