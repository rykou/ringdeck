using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using RingDeck.Core;

namespace RingDeck.App;

/// <summary>Edits one AI connection. Secrets typed here are encrypted before they reach the config; leaving a
/// secret box empty keeps what was saved.</summary>
public partial class ConnectionWindow : Window
{
    readonly AiConnection _target;
    readonly ISecretProtector _secrets = App.Instance.Secrets;
    readonly List<(TextBox Name, TextBox Value, PasswordBox SecretValue, CheckBox Secret, AiHeader? Original)> _rows = new();
    bool _forgetKey, _loading = true;
    CancellationTokenSource? _modelsLoad;

    public ConnectionWindow(AiConnection target)
    {
        InitializeComponent();
        _target = target;
        nameBox.Text = target.Name;
        formatBox.SelectedIndex = (int)target.Format;
        endpointBox.Text = target.Endpoint.Length > 0 ? target.Endpoint : AiRequests.DefaultEndpoint(target.Format);
        modelBox.Text = target.Model;
        temperatureBox.Text = target.Temperature?.ToString(CultureInfo.InvariantCulture) ?? "";
        maxTokensBox.Text = target.MaxTokens?.ToString(CultureInfo.InvariantCulture) ?? "";
        templateBox.Text = target.BodyTemplate.Length > 0 ? target.BodyTemplate : """{"model": "{{model}}", "messages": {{messages}}}""";
        pathBox.Text = target.ResponsePath.Length > 0 ? target.ResponsePath : "choices[0].message.content";
        foreach (var h in target.Headers) AddRow(h);
        UpdateKeyNote();
        UpdateFormat();
        _loading = false;
        // An existing connection with a key (or a local server that needs none) can list its models right away.
        Loaded += (_, _) => { if (target.ApiKey.Length > 0 || IsLocal()) _ = LoadModels(quiet: true); };
        Closed += (_, _) => _modelsLoad?.Cancel();
    }

    bool IsLocal() => Uri.TryCreate(endpointBox.Text.Trim(), UriKind.Absolute, out var u) && (u.IsLoopback || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));

    void OnLoadModels(object sender, RoutedEventArgs e) => _ = LoadModels(quiet: false);

    void OnKeyEntered(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (keyBox.Password.Length > 0) _ = LoadModels(quiet: true);
    }

    /// <summary>Fills the Model drop-down from the service. Quiet loads (on open, after typing a key) only speak up
    /// when they found something; the button always reports what happened.</summary>
    async Task LoadModels(bool quiet)
    {
        if (formatBox.SelectedIndex == (int)AiFormat.Custom)
        {
            if (!quiet) Note("The custom format has no standard way to list models; type the model name.");
            return;
        }
        _modelsLoad?.Cancel();
        var cts = _modelsLoad = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        loadModelsButton.IsEnabled = false;
        if (!quiet) Note("Asking the service for its models…");
        try
        {
            var models = await new AiClient().ListModelsAsync(Read(), _secrets.Reveal, cts.Token);
            if (cts.IsCancellationRequested) return;
            var typed = modelBox.Text;
            modelBox.ItemsSource = models;
            modelBox.Text = typed;
            if (models.Count > 0) Note($"{models.Count} models available. Pick one or type a name.");
            else if (!quiet) Note("The service returned no models.");
            if (models.Count > 0 && typed.Trim().Length == 0) modelBox.IsDropDownOpen = true;
        }
        catch (Exception ex) when (ex is AiException or OperationCanceledException)
        {
            if (!quiet && !cts.IsCancellationRequested) Note("Couldn't list models: " + ex.Message);
            else if (!quiet) Note("The service didn't answer in time.");
        }
        finally
        {
            if (ReferenceEquals(_modelsLoad, cts)) { _modelsLoad = null; loadModelsButton.IsEnabled = true; }
            cts.Dispose();
        }
    }

    void Note(string text)
    {
        modelsNote.Text = text;
        modelsNote.Visibility = Visibility.Visible;
    }

    void UpdateKeyNote()
    {
        var saved = _target.ApiKey.Length > 0 && !_forgetKey;
        keyNote.Text = saved ? "A key is saved (encrypted for your Windows account). Type a new one to replace it." : "No key saved. Local services such as Ollama don't need one.";
        forgetKeyButton.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateFormat() => customPanel.Visibility = formatBox.SelectedIndex == (int)AiFormat.Custom ? Visibility.Visible : Visibility.Collapsed;

    void OnFormatChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateFormat();
        if (_loading) return;
        // Swap in the new format's usual address unless the user typed their own.
        var current = endpointBox.Text.Trim();
        if (current.Length == 0 || Enum.GetValues<AiFormat>().Any(f => AiRequests.DefaultEndpoint(f) == current))
            endpointBox.Text = AiRequests.DefaultEndpoint((AiFormat)formatBox.SelectedIndex);
        modelBox.ItemsSource = null;                     // the old list belongs to the old service
        modelsNote.Visibility = Visibility.Collapsed;
        loadModelsButton.IsEnabled = formatBox.SelectedIndex != (int)AiFormat.Custom;
    }

    void OnEndpointChanged(object sender, TextChangedEventArgs e) =>
        insecureText.Visibility = AiRequests.IsInsecure(endpointBox.Text) ? Visibility.Visible : Visibility.Collapsed;

    void OnForgetKey(object sender, RoutedEventArgs e) { _forgetKey = true; keyBox.Clear(); UpdateKeyNote(); }

    void OnAddHeader(object sender, RoutedEventArgs e) => AddRow(null);

    void AddRow(AiHeader? original)
    {
        var name = new TextBox { Text = original?.Name ?? "", Width = 150 };
        var value = new TextBox { Text = original is { Secret: false } ? original.Value : "" };
        var secretValue = new PasswordBox();
        var secret = new CheckBox { Content = "Secret", IsChecked = original?.Secret ?? false, Margin = new Thickness(8, 0, 0, 0), MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        if (original is { Secret: true, Value.Length: > 0 }) secretValue.ToolTip = "Saved (encrypted). Leave empty to keep it.";
        var remove = new Button { Content = "✕", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        void Sync()
        {
            value.Visibility = secret.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
            secretValue.Visibility = secret.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
        secret.Checked += (_, _) => Sync();
        secret.Unchecked += (_, _) => Sync();
        Sync();
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(name, Dock.Left);
        DockPanel.SetDock(remove, Dock.Right);
        DockPanel.SetDock(secret, Dock.Right);
        row.Children.Add(name);
        row.Children.Add(remove);
        row.Children.Add(secret);
        var values = new Grid { Margin = new Thickness(6, 0, 0, 0) };
        values.Children.Add(value);
        values.Children.Add(secretValue);
        row.Children.Add(values);
        var entry = (name, value, secretValue, secret, original);
        _rows.Add(entry);
        remove.Click += (_, _) => { headerRows.Children.Remove(row); _rows.Remove(entry); };
        headerRows.Children.Add(row);
    }

    /// <summary>The connection as the form describes it, with secrets in stored (encrypted) form.</summary>
    AiConnection Read()
    {
        var key = keyBox.Password.Length > 0 ? _secrets.Protect(keyBox.Password) : _forgetKey ? "" : _target.ApiKey;
        var headers = new List<AiHeader>();
        foreach (var (name, value, secretValue, secret, original) in _rows)
        {
            if (string.IsNullOrWhiteSpace(name.Text)) continue;
            var isSecret = secret.IsChecked == true;
            var stored = !isSecret ? value.Text
                : secretValue.Password.Length > 0 ? _secrets.Protect(secretValue.Password)
                : original is { Secret: true } ? original.Value : "";
            headers.Add(new AiHeader { Name = name.Text.Trim(), Value = stored, Secret = isSecret });
        }
        return new AiConnection
        {
            Id = _target.Id, Name = nameBox.Text.Trim().Length > 0 ? nameBox.Text.Trim() : "AI", Format = (AiFormat)Math.Max(0, formatBox.SelectedIndex),
            Endpoint = endpointBox.Text.Trim(), Model = modelBox.Text.Trim(), ApiKey = key, Headers = headers,
            Temperature = double.TryParse(temperatureBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : null,
            MaxTokens = int.TryParse(maxTokensBox.Text, out var n) && n > 0 ? n : null,
            BodyTemplate = templateBox.Text, ResponsePath = pathBox.Text.Trim(),
        };
    }

    async void OnTest(object sender, RoutedEventArgs e)
    {
        testResult.Text = "Asking…";
        testResult.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["SubtleBrush"];
        try
        {
            var reply = "";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await foreach (var piece in new AiClient().SendAsync(Read(), null, new[] { new ChatMessage("user", "Reply with the single word OK.") }, _secrets.Reveal, cts.Token))
                reply += piece;
            testResult.Text = "Works. Reply: " + reply.Trim();
            testResult.Foreground = Theme.Frozen(System.Windows.Media.Color.FromRgb(0x3F, 0xB9, 0x50));
        }
        catch (Exception ex) when (ex is AiException or OperationCanceledException)
        {
            testResult.Text = ex is AiException ? ex.Message : "No answer within 60 seconds.";
            testResult.ToolTip = testResult.Text;
            testResult.Foreground = Theme.Frozen(System.Windows.Media.Color.FromRgb(0xF2, 0x99, 0x4A));
        }
    }

    void OnSave(object sender, RoutedEventArgs e)
    {
        var c = Read();
        (_target.Name, _target.Format, _target.Endpoint, _target.Model, _target.ApiKey, _target.Headers) = (c.Name, c.Format, c.Endpoint, c.Model, c.ApiKey, c.Headers);
        (_target.Temperature, _target.MaxTokens, _target.BodyTemplate, _target.ResponsePath) = (c.Temperature, c.MaxTokens, c.BodyTemplate, c.ResponsePath);
        DialogResult = true;
    }
}
