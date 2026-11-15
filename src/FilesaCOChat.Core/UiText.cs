namespace FilesaCOChat.Core;

/// <summary>
/// Chinese UI strings taken verbatim from main_strings_by_func.txt (402 constants).
/// Keeping them in the core layer makes user-visible behaviour assertable in tests.
/// </summary>
public static class UiText
{
    public const string System = "系统";
    public const string MustEnterUsername = "必须输入用户名";
    public const string Warning = "警告";
    public const string Searching = "正在搜索局域网聊天室...";
    public const string PreparingSearch = "准备开始搜索...";
    public const string BroadcastSearching = "正在使用广播方式发现聊天室...";
    public const string Scanning = "广播未发现聊天室，开始扫描...";
    public const string NoChatroomFound = "扫描完成，未找到现有聊天室";
    public const string CreatingChatroom = "未找到现有聊天室，创建新聊天室...";
    public const string Joined = " 加入了聊天室";
    public const string Left = " 离开了聊天室";
    public const string Welcome = " 使用 Filesa COChat";
    public const string WelcomePrefix = "欢迎 ";
    public const string CreatedRoomPrefix = "已在 ";
    public const string CreatedRoomSuffix = " 创建新的聊天室";
    public const string JoinedRoomPrefix = "已成功加入 ";
    public const string JoinedRoomSuffix = " 的聊天室";
    public const string UsernameExists = "用户名已存在，请重新启动并选择其他用户名";
    public const string UsernameConflict = "用户名冲突，无法加入聊天室";
    public const string UserCountPrefix = "用户: ";
    public const string OnlineUsers = "在线用户";
    public const string Send = "发送";
    public const string SendFile = "发送文件";
    public const string CommandLine = "命令行";
    public const string Settings = "设置";
    public const string CannotSendToSelfCommand = "不能向自己发送命令！";
    public const string CannotSendToSelfFile = "不能给自己发送文件！";
    public const string SelectFileToSend = "选择要发送的文件";
    public const string SendingFile = "正在发送文件: ";
    public const string FileAccepted = " 接受了文件传输，开始发送...";
    public const string FileRejected = " 拒绝了文件传输";
    public const string FileSendDone = "文件传输完成！";
    public const string FileReceiveDone = "✓ 文件接收成功: ";
    public const string FileHashMismatch = "✗ 警告: 文件接收失败，文件可能已损坏";
    public const string FileRequestTitle = "文件传输请求";
    public const string UpdateAvailable = "检测到更新，请在设置中更新。";
    public const string AlreadyLatest = "已是最新版本";
    public const string CheckUpdate = "检查更新";
    public const string ServerMigrated = "服务器已迁移，正在关闭...";
    public const string MigrationFailed = "服务器迁移失败，直接关闭";
    public const string NoOneToMigrate = "没有其他用户可迁移服务器";
    public const string MigratingTo = "正在迁移服务器到 ";
    public const string Promoted = "被指定为新的服务器，正在创建聊天室...";
    public const string MigrationOk = "迁移成功";
    public const string MigrationRetry = "迁移失败，尝试重新连接...";

    /// <summary>D1 fix: user-visible explanation replacing silent shell execution.</summary>
    public const string RemoteCommandDisabled =
        "[安全] 远程命令执行已在 WinUI3 版禁用，命令未执行。";
    public const string ReceivedCommand = "[收到命令] ";

    /// <summary>D3 fix: local input that starts with a reserved control token is refused.</summary>
    public const string ReservedPrefixBlocked =
        "[系统] 消息以协议保留前缀开头，已阻止发送（可改写后重发）。";

    /// <summary>D3 fix: legacy wire frame exceeded recv(1024) capacity.</summary>
    public const string MessageTooLong =
        "[系统] 消息超过 1024 字节的协议上限，未发送。";

    /// <summary>D8 fix: malformed update tag is ignored rather than surfaced.</summary>
    public const string UpdateCheckFailed = "检查失败";
}
