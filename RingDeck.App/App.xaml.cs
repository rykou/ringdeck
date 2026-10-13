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

        try { Start(e); }
        catch (Exception ex)
        {
            // Without this the process would stay alive with no tray icon, holding the single-instance lock.
            Log(ex);
            MessageBox.Show("RingDeck could not start.\n\n" + ex.Message, "RingDeck", MessageBoxButton.OK, MessageBoxImage.Error);
            try { Tray?.Dispose(); } catch (Exception) { }
            Shutdown();
        }
    }

    void Start(StartupEventArgs e)
    {
        Store = new ConfigStore(DataDir);
        var loaded = Store.Load();
        Config = loaded.Config;
        Icons = new IconStore(Store.IconsDir);
        Theme.Apply(Config.Settings);
        var render = Array.IndexOf(e.Args, "--render");
        if (render >= 0 && render + 1 < e.Args.Length)
        {
            RenderProbe.Render(Config, e.Args[render + 1], render + 2 < e.Args.Length && int.TryParse(e.Args[render + 2], out var slice) ? slice : -1);
            Shutdown();
            return;
        }
        Tray = new TrayIcon(ShowEditor, SetPaused, ExitApp);
        if (loaded.Recovered)
            Tray.Notify("Your settings file was damaged",
                (Config.Rings.Count > 0 ? "RingDeck loaded the last good copy." : "There was no good copy to fall back on, so RingDeck started with no rings.")
                + $" The damaged file was kept as {Path.GetFileName(loaded.BrokenPath)}.");
        StartController();

        _openEditor = new EventWaitHandle(false, EventResetMode.AutoReset, OpenEditorName);
        new Thread(() => { while (_openEditor.WaitOne()) Dispatcher.BeginInvoke(ShowEditor); }) { IsBackground = true }.Start();
        if (loaded.FirstRun || !e.Args.Contains("--tray")) ShowEditor();

        var shot = Array.IndexOf(e.Args, "--shot-editor");
        if (shot >= 0 && shot + 1 < e.Args.Length)
        {
            // Developer aid: save two pictures of the editor (ring panel, slice panel) and exit.
            ShowEditor();
            var prefix = e.Args[shot + 1];
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    _editor!.SaveShot(prefix + "-ring.png", false);
                    _editor.SaveShot(prefix + "-slice.png", true);
                }
                catch (Exception ex) { Log(ex); }
                _editor!.ProbeDropDown(prefix + "-probe.txt", ExitApp);
            };
            timer.Start();
        }
    }

    public RingController Controller { get; private set; } = null!;

    void StartController()
    {
        try { Controller = new RingController(() => Config, Tray); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log(ex);
            MessageBox.Show(ex.Message, "RingDeck", MessageBoxButton.OK, MessageBoxImage.Error);
            Tray.Dispose();
            Shutdown();
        }
    }

    void SetPaused(bool paused) => Controller.Paused = paused;

    void StopController() => Controller?.Dispose();

    protected override void OnExit(ExitEventArgs e)
    {
        StopController();
        base.OnExit(e);
    }

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
    public void ConfigChanged()
    {
        Theme.Apply(Config.Settings);
        Controller.Reload();
    }

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
