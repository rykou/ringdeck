using System.IO;
using System.Windows;
using RingDeck.Core;
using RingDeck.Windows;

namespace RingDeck.App;

public partial class App : Application
{
    // RINGDECK_DATA points a developer copy at its own settings folder and gives it its own single-instance lock,
    // so it can run next to the installed RingDeck without touching the user's rings.
    static readonly string? DevData = Environment.GetEnvironmentVariable("RINGDECK_DATA") is { Length: > 0 } d ? d : null;
    static readonly string InstanceName = @"Local\RingDeck.Instance" + (DevData is null ? "" : ".dev");
    static readonly string OpenEditorName = @"Local\RingDeck.OpenEditor" + (DevData is null ? "" : ".dev");

    Mutex? _instance;
    EventWaitHandle? _openEditor;
    EditorWindow? _editor;

    public static App Instance => (App)Current;
    public ConfigStore Store { get; private set; } = null!;
    public AppConfig Config { get; set; } = null!;
    public IconStore Icons { get; private set; } = null!;
    public ISecretProtector Secrets { get; } = new DpapiProtector();
    public TrayIcon Tray { get; private set; } = null!;

    static string DataDir => DevData ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RingDeck");

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
        var probe = Array.IndexOf(e.Args, "--foreground");
        if (probe >= 0 && probe + 1 < e.Args.Length)
        {
            // Developer aid: report what the "in a game?" check sees for the focused window, then exit.
            var app = ForegroundProbe.Get();
            var settings = new ConfigStore(DataDir).Load().Config.Settings;
            File.WriteAllText(e.Args[probe + 1], $"exe={app.Exe ?? "(none)"} fullscreen={app.Fullscreen} ringsOff={Suppression.Applies(settings, app)}\n");
            Shutdown();
            return;
        }
        var demo = Array.IndexOf(e.Args, "--ai-demo");
        if (demo >= 0 && demo + 2 < e.Args.Length)
        {
            // Developer aid: chat with an OpenAI-compatible endpoint (e.g. a local test server), save a picture, exit.
            Store = new ConfigStore(DataDir);
            Config = Store.Load().Config;
            Theme.Apply(Config.Settings);
            var connection = new AiConnection { Name = "Local test", Model = "demo-model", Endpoint = e.Args[demo + 1], ApiKey = Secrets.Protect("sk-demo") };
            var panel = AiPanel.Open(connection, new AiChatAction { Instruction = "Translate to English:" }, "Bom dia! Como vai o projeto?", IntPtr.Zero, (_, _) => { });
            _ = panel.SaveWhenDoneAsync(e.Args[demo + 2], TimeSpan.FromSeconds(20)).ContinueWith(_ => Dispatcher.Invoke(Shutdown));
            return;
        }
        var render = Array.IndexOf(e.Args, "--render");
        if (render >= 0 && render + 1 < e.Args.Length)
        {
            // Developer aid, handled before the single-instance check so it works while RingDeck is running.
            Store = new ConfigStore(DataDir);
            Config = Store.Load().Config;
            Icons = new IconStore(Store.IconsDir);
            Theme.Apply(Config.Settings);
            RenderProbe.Render(Config, e.Args[render + 1], render + 2 < e.Args.Length && int.TryParse(e.Args[render + 2], out var slice) ? slice : -1);
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
        try { Autostart.MigrateFromRunKey(); }
        catch (Exception ex) { Log(ex); }
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
            timer.Tick += async (_, _) =>
            {
                timer.Stop();
                try
                {
                    _editor!.SaveShot(prefix + "-ring.png", false);
                    _editor.SaveShot(prefix + "-slice.png", true);
                    if (Config.AiConnections.FirstOrDefault() is { } c) await _editor.SaveConnectionShot(c, prefix + "-connection.png");
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
