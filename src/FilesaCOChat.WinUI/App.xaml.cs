using Microsoft.UI.Xaml;

namespace FilesaCOChat.WinUI;

public partial class App : Application
{
    public static MainWindow? Main { get; private set; }

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Register the Windows notification platform before the first toast.
        // Unpackaged apps need this to obtain a usable AUMID + COM activator.
        SystemNotifier.Initialize();

        Main = new MainWindow();
        Main.Activate();
    }
}
