namespace FilesaCOChat.Core.Security;

/// <summary>
/// DEFECT D1 (report §7, severity 严重): the 5.1 receive_messages COMMAND branch ran
/// subprocess.run(command, shell=True, timeout=5) with no authentication.
/// This class is the single decision point in the rewrite and it can never authorize
/// execution. There is deliberately no Process / shell API anywhere in this solution.
/// </summary>
public sealed record CommandEvaluation(bool Execute, string DisplayText, string Reason);

public static class CommandPolicy
{
    public const bool RemoteExecutionEnabled = false;
    public const string FixId = "D1";

    public static CommandEvaluation Evaluate(string targetUser, string command, string localUsername, bool fromNetwork)
    {
        var text = UiText.ReceivedCommand + command;
        var reason = fromNetwork
            ? "remote COMMAND frame rejected: unauthenticated shell execution was removed"
            : "local command entry is display-only in the WinUI3 build";
        // Even for the local user, only a transcript line is produced. No shell is spawned.
        return new CommandEvaluation(Execute: false, DisplayText: text, Reason: reason);
    }

    public static CommandEvaluation EvaluateRemote(string targetUser, string command, string localUsername) =>
        Evaluate(targetUser, command, localUsername, fromNetwork: true);
}
