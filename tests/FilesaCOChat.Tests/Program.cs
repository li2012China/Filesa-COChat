using FilesaCOChat.Tests;

CodecTests.Register();
SecurityTests.Register();
NetworkTests.Register();
FileTransferTests.Register();
TimelineTests.Register();
SettingsTests.Register();
DirectConnectTests.Register();
RenderTests.Register();

static string FindSolutionRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "FilesaCOChat.sln")) ||
            Directory.Exists(Path.Combine(dir.FullName, "src", "FilesaCOChat.Core")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return AppContext.BaseDirectory;
}

var resultPath = Path.Combine(FindSolutionRoot(), "test-results.json");
Console.WriteLine("Filesa COChat WinUI3 - verification suite");
Console.WriteLine("result file: " + resultPath + "\n");
return await TestRunner.RunAllAsync(resultPath);
