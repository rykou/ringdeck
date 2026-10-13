using System.IO;
using System.Windows;
using RingDeck.Core;
using RingDeck.Windows;

namespace RingDeck.App;

public partial class App : Application
{
    const string InstanceName = @"Local\RingDeck.Instance", OpenEditorName = @"Local\RingDeck.OpenEditor";

    Mutex? _instance;
    EventWaitHandle? _openEditor;
    EditorWindow? _editor;

    public static App Instance => (App)Current;
    public ConfigStore Store { get; private set; } = null!;
    public AppConfig Config { get; set; } = null!;
    public IconStore Icons { get; private set; } = null!;
    public TrayIcon Tray { get; private set; } = null!;

    static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RingDeck");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var autostart = Array.IndexOf(e.Args, "--set-autostart");
        if (autostart >= 0)
        {
            Autostart.Set(autostart + 1 < e.Args.Length && e.Args[autostart + 1] == "on");
            Shutdown();
            return;
        }
        _instance = new Mutex(true, InstanceName, out var first);
        if (!first)
        {
            try { using var signal = EventWaitHandle.OpenExisting(OpenEditorName); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) => { Log(args.Exception); args.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => { if (args.ExceptionObject is Exception ex) Log(ex); };

        Store = new ConfigStore(DataDir);
        var loaded = Store.Load();
        Config = loaded.Config;
        Icons = new IconStore(Store.IconsDir);
        Theme.Apply(Config.Settings);
        Tray = new TrayIcon(ShowEditor, SetPaused, ExitApp);
        if (loaded.Recovered)
            Tray.Notify("Your settings file was damaged",
                $"RingDeck loaded the last good copy. The damaged file was kept as {Path.GetFileName(loaded.BrokenPath)}.");
        StartController();

        _openEditor = new EventWaitHandle(false, EventResetMode.AutoReset, OpenEditorName);
        new Thread(() => { while (_openEditor.WaitOne()) Dispatcher.BeginInvoke(ShowEditor); }) { IsBackground = true }.Start();
        if (loaded.FirstRun || !e.Args.Contains("--tray")) ShowEditor();
    }

    // Task 10 replaces these three bodies.
    void StartController() { }
    void SetPaused(bool paused) { }
    void StopController() { }

    public void ShowEditor()
    {
        if (_editor is null)
        {
            _editor = new EditorWindow();
            _editor.Closed += (_, _) => _editor = null;
        }
        _editor.Show();
        if (_editor.WindowState == WindowState.Minimized) _editor.WindowState = WindowState.Normal;
        _editor.Activate();
    }

    /// <summary>Called by the editor after any change to the config.</summary>
    public void ConfigChanged() => Theme.Apply(Config.Settings);

    void ExitApp()
    {
        _editor?.Close();
        StopController();
        Tray.Dispose();
        Shutdown();
    }

    public static void Log(Exception ex)
    {
        try
        {
            var dir = Path.Combine(DataDir, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, $"ringdeck-{DateTime.Now:yyyyMMdd}.log"), $"{DateTime.Now:O} {ex}\n\n");
        }
        catch (IOException) { }
    }
}
