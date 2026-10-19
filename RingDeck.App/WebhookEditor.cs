using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RingDeck.Core;
using RingDeck.Windows;

namespace RingDeck.App;

public partial class EditorWindow
{
    static readonly (WebService Service, string Name)[] ServiceChoices =
    {
        (WebService.Discord, "Discord"),
        (WebService.Slack, "Slack (also Mattermost and Rocket.Chat)"),
        (WebService.Teams, "Microsoft Teams"),
        (WebService.GoogleChat, "Google Chat"),
        (WebService.Telegram, "Telegram"),
        (WebService.Ntfy, "ntfy (notifications on your phone)"),
        (WebService.Pushover, "Pushover (notifications on your phone)"),
        (WebService.HomeAssistant, "Home Assistant"),
        (WebService.Automation, "n8n, Make, Zapier or IFTTT"),
        (WebService.Custom, "Custom web request"),
    };

    static string HowTo(WebService service) => service switch
    {
        WebService.Discord => "In Discord: the channel's settings → Integrations → Webhooks → New Webhook → Copy Webhook URL.",
        WebService.Slack => "In Slack: create an app with Incoming Webhooks turned on, add a webhook to a channel and copy its URL.",
        WebService.Teams => "In Teams: the channel's … menu → Workflows → \"Post to a channel when a webhook request is received\", then copy the URL it gives.",
        WebService.GoogleChat => "In Google Chat: the space's name → Apps & integrations → Webhooks → Add webhook, then copy its URL.",
        WebService.Telegram => "Create a bot with @BotFather to get its token. Send the bot a message, then open api.telegram.org/bot<token>/getUpdates to find your chat ID.",
        WebService.Ntfy => "Install the ntfy app, subscribe to a topic with a hard-to-guess name, and use https://ntfy.sh/<that name> here. No account needed.",
        WebService.Pushover => "On pushover.net: your user key is on the dashboard; create an application to get its API token.",
        WebService.HomeAssistant => "In Home Assistant: make an automation with a Webhook trigger. Its URL is http://<your server>:8123/api/webhook/<webhook id>. The message arrives as trigger.json.message.",
        WebService.Automation => "Paste the webhook URL from an n8n Webhook node, a Make custom webhook, a Zapier \"Catch Hook\" or IFTTT Webhooks. The message is sent as \"message\" (and \"value1\" for IFTTT).",
        _ => "Any web address. The body can use {message} and the other placeholders; inside JSON they are escaped for you.",
    };

    static TextBlock WebHint(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = (Brush)Application.Current.Resources["SubtleBrush"],
    };

    /// <summary>A field for a stored-encrypted value (webhook address, token): shows it in plain text here, saves it encrypted.</summary>
    static TextBox SecretField(Func<string> get, Action<string> set, string tip)
    {
        var secrets = App.Instance.Secrets;
        var box = new TextBox { Text = secrets.Reveal(get()), ToolTip = tip };
        box.TextChanged += (_, _) => set(box.Text.Trim().Length == 0 ? "" : secrets.Protect(box.Text.Trim()));
        return box;
    }

    /// <summary>The settings of one web request, for a slice or a workflow step. <paramref name="rebuild"/> redraws the
    /// form when the service changes (each service asks for different things).</summary>
    internal static void FillWebhookForm(Panel panel, WebRequest r, Action rebuild)
    {
        var add = panel.Children;
        add.Add(Caption("Send to"));
        var service = new ComboBox { ItemsSource = ServiceChoices.Select(c => c.Name).ToList(), SelectedIndex = Array.FindIndex(ServiceChoices, c => c.Service == r.Service) };
        service.SelectionChanged += (_, e) =>
        {
            e.Handled = true;
            if (service.SelectedIndex < 0 || ServiceChoices[service.SelectedIndex].Service == r.Service) return;
            r.Service = ServiceChoices[service.SelectedIndex].Service;
            if (r.Service == WebService.Custom && r.Headers.Count == 0 && r.Body.Length == 0) r.Body = new WebRequest().Body;
            rebuild();
        };
        add.Add(service);
        add.Add(WebHint(HowTo(r.Service)));

        switch (r.Service)
        {
            case WebService.Telegram:
                add.Add(Caption("Bot token"));
                add.Add(SecretField(() => r.Token, v => r.Token = v, "From @BotFather, like 123456:ABC-DEF…"));
                add.Add(Caption("Chat ID"));
                add.Add(SecretField(() => r.Target, v => r.Target = v, "A number; groups and channels start with -100"));
                break;
            case WebService.Pushover:
                add.Add(Caption("Application API token"));
                add.Add(SecretField(() => r.Token, v => r.Token = v, "From the application you created on pushover.net"));
                add.Add(Caption("Your user key"));
                add.Add(SecretField(() => r.Target, v => r.Target = v, "From your pushover.net dashboard"));
                break;
            case WebService.Ntfy:
                add.Add(Caption("Topic address"));
                add.Add(SecretField(() => r.Url, v => r.Url = v, "For example https://ntfy.sh/your-secret-topic"));
                add.Add(Caption("Access token (optional, for protected topics)"));
                add.Add(SecretField(() => r.Token, v => r.Token = v, "tk_… from your ntfy account or server"));
                break;
            case WebService.Custom:
                CustomFields(panel, r);
                break;
            default:
                add.Add(Caption(r.Service == WebService.Teams ? "Workflow URL" : "Webhook URL"));
                add.Add(SecretField(() => r.Url, v => r.Url = v, "Anyone with this address can post, so RingDeck stores it encrypted and never exports it"));
                break;
        }
        if (Uri.TryCreate(App.Instance.Secrets.Reveal(r.Url), UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttp
            && !url.IsLoopback && r.Service != WebService.HomeAssistant)
            add.Add(new TextBlock { Text = "This address isn't encrypted (http://). Use https:// if the service offers it.", Foreground = Brushes.Orange,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });

        if (r.Service is not (WebService.Telegram or WebService.Custom))
        {
            add.Add(Caption(r.Service == WebService.Discord ? "Post as (optional, the name shown in Discord)" : "Title (optional)"));
            add.Add(Bound(new TextBox(), TextBox.TextProperty, r, nameof(WebRequest.Title)));
        }
        add.Add(Caption("Message"));
        add.Add(Bound(new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 }, TextBox.TextProperty, r, nameof(WebRequest.Message)));
        add.Add(WebHint("Placeholders: {selection} (the text selected where you used the ring)  {clipboard}  {date}  {time}." +
            (r.Service == WebService.Custom ? " In the address and body, {message} is this message." : "")));
        if (Webhooks.SupportsImages(r.Service))
            add.Add(Bound(new CheckBox { Content = new TextBlock { Text = "Also send the picture on the clipboard (a screenshot or a copied image), when there is one", TextWrapping = TextWrapping.Wrap },
                Margin = new Thickness(0, 12, 0, 0) },
                ToggleButton.IsCheckedProperty, r, nameof(WebRequest.SendImage)));
        if (r.Service == WebService.Teams)
            add.Add(WebHint("Teams only accepts small messages (about 28 KB), so the picture is made smaller to fit: usually 500 to 800 pixels wide, less for busy screenshots. Fine for a dialog or an error, too small for fine print."));
        else if (!Webhooks.SupportsImages(r.Service))
            add.Add(WebHint($"{Webhooks.Name(r.Service)} webhooks can't receive pictures, so only the text is sent."));

        var testRow = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var test = new Button { Content = "Send a test" };
        var result = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
        test.Click += async (_, _) =>
        {
            test.IsEnabled = false;
            result.Foreground = (Brush)Application.Current.Resources["SubtleBrush"];
            result.Text = "Sending…";
            try
            {
                await SendTest(r);
                result.Foreground = Brushes.LimeGreen;
                result.Text = "Sent. Check " + Webhooks.Name(r.Service) + ".";
            }
            catch (Exception ex) when (ex is WebhookException or OperationCanceledException)
            {
                result.Foreground = Brushes.Orange;
                result.Text = ex is WebhookException ? ex.Message : "No answer.";
            }
            finally { test.IsEnabled = true; }
        };
        DockPanel.SetDock(test, Dock.Left);
        testRow.Children.Add(test);
        testRow.Children.Add(result);
        add.Add(testRow);
    }

    /// <summary>Sends the message as configured, with "Test from RingDeck" standing in for the selection.</summary>
    static async Task SendTest(WebRequest r)
    {
        var clipboard = new ClipboardService();
        var image = r.SendImage && Webhooks.SupportsImages(r.Service) ? clipboard.GetImage() : null;
        var now = DateTime.Now;
        var extra = new Dictionary<string, Func<string?>> { ["selection"] = () => "Test from RingDeck" };
        var message = Placeholders.Expand(r.Message, now, clipboard.GetText, extra);
        if (message.Trim().Length == 0 && image is null) message = "Test from RingDeck";
        extra["message"] = () => message;
        if (image is not null && r.Service == WebService.Teams) image = ActionRunner.FitForTeams(image, message, r.Title.Trim(), ImageShrinker.Fit);
        var reveal = App.Instance.Secrets.Reveal;
        using var request = Webhooks.Build(r, message, image, reveal, (text, escape) => Placeholders.Expand(text, now, clipboard.GetText, extra, escape));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await TestClient.SendAsync(request, Webhooks.Secrets(r, reveal), cts.Token);
    }

    static readonly WebhookClient TestClient = new();

    static void CustomFields(Panel panel, WebRequest r)
    {
        var add = panel.Children;
        add.Add(Caption("Method and address"));
        var row = new DockPanel();
        var method = new ComboBox { ItemsSource = new[] { "POST", "GET", "PUT", "PATCH", "DELETE" }, Width = 96, Margin = new Thickness(0, 0, 6, 0) };
        method.SelectedItem = (r.Method ?? "POST").ToUpperInvariant();
        if (method.SelectedItem is null) method.SelectedIndex = 0;
        method.SelectionChanged += (_, _) => { if (method.SelectedItem is string m) r.Method = m; };
        DockPanel.SetDock(method, Dock.Left);
        row.Children.Add(method);
        row.Children.Add(SecretField(() => r.Url, v => r.Url = v, "https://… Placeholders work here too, escaped for a web address"));
        add.Add(row);
        add.Add(Caption("Bearer token (optional, sent as Authorization: Bearer …)"));
        add.Add(SecretField(() => r.Token, v => r.Token = v, "Leave empty if the service doesn't need one, or set an Authorization header below"));

        add.Add(Caption("Headers"));
        var list = new StackPanel();
        void Rows()
        {
            list.Children.Clear();
            foreach (var h in r.Headers.ToList())
            {
                var line = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var remove = new Button { Content = "✕", Width = 30, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), ToolTip = "Remove" };
                remove.Click += (_, _) => { r.Headers.Remove(h); Rows(); };
                var secret = new CheckBox { Content = "Secret", IsChecked = h.Secret, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Stored encrypted and left out of exports" };
                var name = Bound(new TextBox { Width = 140, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Name" }, TextBox.TextProperty, h, nameof(AiHeader.Name));
                var value = new TextBox { ToolTip = "Value", Text = h.Secret ? App.Instance.Secrets.Reveal(h.Value) : h.Value };
                value.TextChanged += (_, _) => h.Value = h.Secret && value.Text.Length > 0 ? App.Instance.Secrets.Protect(value.Text) : value.Text;
                secret.Click += (_, _) =>
                {
                    h.Secret = secret.IsChecked == true;
                    h.Value = h.Secret && value.Text.Length > 0 ? App.Instance.Secrets.Protect(value.Text) : value.Text;
                };
                DockPanel.SetDock(remove, Dock.Right);
                DockPanel.SetDock(secret, Dock.Right);
                DockPanel.SetDock(name, Dock.Left);
                line.Children.Add(remove);
                line.Children.Add(secret);
                line.Children.Add(name);
                line.Children.Add(value);
                list.Children.Add(line);
            }
        }
        Rows();
        add.Add(list);
        var addHeader = new Button { Content = "Add header", HorizontalAlignment = HorizontalAlignment.Left };
        addHeader.Click += (_, _) => { r.Headers.Add(new AiHeader()); Rows(); };
        add.Add(addHeader);

        add.Add(Caption("Content type"));
        add.Add(Bound(new TextBox { ToolTip = "application/json, text/plain, application/x-www-form-urlencoded…" }, TextBox.TextProperty, r, nameof(WebRequest.ContentType)));
        add.Add(Caption("Body"));
        add.Add(Bound(new TextBox { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, FontFamily = new FontFamily("Cascadia Mono, Consolas") },
            TextBox.TextProperty, r, nameof(WebRequest.Body)));
    }

    void WebhookEditor(WebhookAction hook) => FillWebhookForm(slicePanel, hook.Request, () => ShowSlice(_slice));

    /// <summary>A workflow step's row: what it sends, and a button that opens the full settings.</summary>
    UIElement WebhookStepEditor(WebhookStep step)
    {
        var row = new DockPanel();
        var edit = new Button { Content = "Set up…", Margin = new Thickness(4, 0, 0, 0) };
        edit.Click += (_, _) =>
        {
            new WebhookWindow(step.Request) { Owner = TopWindow }.ShowDialog();
            RefreshMacro();
            Touch();
        };
        DockPanel.SetDock(edit, Dock.Right);
        row.Children.Add(edit);
        var message = step.Request.Message.ReplaceLineEndings(" ");
        row.Children.Add(new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Text = Webhooks.Name(step.Request.Service) + (message.Length > 0 ? ": " + message : "") + (step.Request.SendImage ? " + picture" : ""),
        });
        return row;
    }
}

/// <summary>The settings of a workflow's web request step, in their own window.</summary>
public sealed class WebhookWindow : Window
{
    readonly WebRequest _request;
    readonly StackPanel _panel = new() { Margin = new Thickness(20) };

    public WebhookWindow(WebRequest request)
    {
        _request = request;
        Title = "Web request step";
        Width = 560;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var close = new Button { Content = "Done", IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 0, 20, 16), MinWidth = 90 };
        close.Click += (_, _) => Close();
        var root = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = _panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        Fill();
    }

    void Fill()
    {
        _panel.Children.Clear();
        EditorWindow.FillWebhookForm(_panel, _request, Fill);
    }
}
