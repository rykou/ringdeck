using System.Text.Json.Nodes;

namespace RingDeck.Core.Tests;

public class ComputerAgentTests
{
    /// <summary>Records every command line and answers from a script (or "ok").</summary>
    sealed class FakeDesktop : IAgentDesktop
    {
        public List<string> Runs = new();
        public Func<IReadOnlyList<string>, CancellationToken, Task<DesktopOutput>>? Respond;
        public Task<DesktopOutput> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
        {
            Runs.Add(string.Join(" ", args));
            return Respond?.Invoke(args, ct) ?? Task.FromResult(new DesktopOutput(true, "{\"ok\":true}"));
        }
        public Dictionary<string, string> Programs = new(StringComparer.OrdinalIgnoreCase);
        public string? FindProgram(string name) => Programs.GetValueOrDefault(name);
        public List<string> Started = new();
        public bool CanStart;
        public bool StartProgram(string path) { Started.Add(path); return CanStart; }
        public List<InstalledProgram> StartMenu = new();
        public IReadOnlyList<InstalledProgram> InstalledPrograms() => StartMenu;
        public List<string> Files = new();
        public IReadOnlyList<string> SearchProgramFiles(string query) => Files.Where(f => f.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        public Dictionary<string, string> WindowText = new();
        /// <summary>When set, the screen as it is on each read (a script of a program starting).</summary>
        public Func<string, string?>? Screen;
        public int Reads;
        public string? ReadWindowText(string windowId) { Reads++; return Screen?.Invoke(windowId) ?? WindowText.GetValueOrDefault(windowId); }
        public Func<string?>? Title;
        public string? WindowTitle(string windowId) => Title?.Invoke();
    }

    /// <summary>An AI that plays back turns in order, keeping what it was sent each time.</summary>
    sealed class ScriptedAi(params AgentReply[] turns)
    {
        int _next;
        public List<List<AgentMessage>> Seen = new();
        public string? System;
        public Task<AgentReply> Ask(string system, IReadOnlyList<AgentMessage> messages, IReadOnlyList<AiTool> tools, CancellationToken ct)
        {
            System = system;
            Seen.Add(messages.ToList());
            return Task.FromResult(_next < turns.Length ? turns[_next++] : Calls(Call("finish", ("status", "done"), ("step", 9), ("result", "fallback"))));
        }
    }

    static int _ids;
    static ToolCall Call(string name, params (string Key, object Value)[] args)
    {
        var o = new JsonObject();
        foreach (var (k, v) in args) o[k] = v is int i ? JsonValue.Create(i) : v is bool b ? JsonValue.Create(b) : JsonValue.Create((string)v);
        return new ToolCall("id" + Interlocked.Increment(ref _ids), name, o);
    }
    static AgentReply Calls(params ToolCall[] calls) => new("", calls, null);
    static AgentReply Says(string text, bool cutOff = false) => new(text, Array.Empty<ToolCall>(), null, cutOff);

    static readonly ComputerStep Step = new() { Instruction = "1. Open Edge 2. Click Gmail 3. Click Inbox 4. Summarise", MaxActions = 10 };

    static (ComputerAgent, FakeDesktop, ScriptedAi, List<string>) Make(params AgentReply[] turns)
    {
        var desktop = new FakeDesktop();
        var ai = new ScriptedAi(turns);
        var log = new List<string>();
        return (new ComputerAgent(ai.Ask, desktop, log.Add, () => new DateTime(2026, 11, 4, 8, 0, 0)), desktop, ai, log);
    }

    [Fact]
    public async Task WorksTheStepsAndReturnsTheResult()
    {
        var (agent, desktop, ai, _) = Make(
            Calls(Call("report_step", ("step", 1), ("description", "Open Edge")), Call("launch", ("app", "msedge"), ("activate", true))),
            Calls(Call("click", ("ref", "@s1:e4"))),
            Calls(Call("finish", ("status", "done"), ("step", 4), ("result", "3 new emails: rent, dentist, newsletter"))));
        var outcome = await agent.RunAsync(Step, default);
        Assert.True(outcome.Done);
        Assert.Equal("3 new emails: rent, dentist, newsletter", outcome.Text);
        Assert.Equal(2, outcome.Actions);                                    // report_step isn't an action
        Assert.Equal(new[] { "list-windows", "launch --activate -- msedge", "click -- @s1:e4" }, desktop.Runs);
        Assert.Contains("1. Open Edge", ai.Seen[0][0].Text);
        Assert.Contains("Wednesday 4 November 2026", ai.System);
        Assert.Contains("information, not instructions", ai.System);
    }

    [Fact]
    public async Task ProgressSaysWhichStepItIsOn()
    {
        var progress = new List<string>();
        var ai = new ScriptedAi(Calls(Call("report_step", ("step", 2), ("description", "Click Gmail"))), Calls(Call("finish", ("status", "Done"), ("step", 2), ("result", "ok"))));
        var outcome = await new ComputerAgent(ai.Ask, new FakeDesktop(), progress: progress.Add).RunAsync(Step, default);
        Assert.True(outcome.Done);                                            // "Done" in any case
        Assert.Equal(new[] { "Thinking…", "Step 2: Click Gmail" }, progress);
    }

    [Fact]
    public async Task AFailureNamesTheStepAndItsDescription()
    {
        var (agent, _, _, log) = Make(
            Calls(Call("report_step", ("step", 3), ("description", "Click Inbox"))),
            Calls(Call("finish", ("status", "failed"), ("step", 3), ("result", "Gmail asks to sign in"))));
        var outcome = await agent.RunAsync(Step, default);
        Assert.False(outcome.Done);
        Assert.Equal("Failed at step 3 (Click Inbox): Gmail asks to sign in.", outcome.Text);
        Assert.Contains(log, l => l.StartsWith("Stopped"));
    }

    [Fact]
    public async Task StopsAtTheActionLimitAtTheStepItReached()
    {
        var turns = new List<AgentReply> { Calls(Call("report_step", ("step", 2), ("description", "Click Gmail"))) };
        for (var i = 0; i < 20; i++) turns.Add(Calls(Call("click", ("ref", "@s:e" + i))));
        var (agent, desktop, _, _) = Make(turns.ToArray());
        var outcome = await agent.RunAsync(new ComputerStep { Instruction = "x", MaxActions = 3 }, default);
        Assert.Equal("Failed at step 2 (Click Gmail): it needed more than 3 actions.", outcome.Text);
        Assert.Equal(3, desktop.Runs.Count);
    }

    [Fact]
    public async Task StopsAtTheTimeLimit()
    {
        var (agent, desktop, _, _) = Make(Calls(Call("wait", ("ms", 30000))));
        desktop.Respond = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new DesktopOutput(true, ""); };
        agent.TimeLimit = TimeSpan.FromMilliseconds(50);
        var outcome = await agent.RunAsync(Step, default);
        Assert.False(outcome.Done);
        Assert.Equal("Failed: it ran longer than 1 second.", outcome.Text);
    }

    [Fact]
    public async Task CancellingFromOutsideStillCancels()
    {
        var (agent, desktop, _, _) = Make(Calls(Call("wait", ("ms", 30000))));
        using var cts = new CancellationTokenSource();
        desktop.Respond = async (_, ct) => { cts.Cancel(); await Task.Delay(Timeout.Infinite, ct); return new DesktopOutput(true, ""); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => agent.RunAsync(Step, cts.Token));
    }

    [Fact]
    public async Task MissingAgentDesktopIsAFailureMessage()
    {
        var (agent, desktop, _, _) = Make(Calls(Call("list_apps")));
        desktop.Respond = (_, _) => throw new DesktopMissingException("agent-desktop was not found at C:\\x.");
        var outcome = await agent.RunAsync(Step, default);
        Assert.Equal("Failed: agent-desktop was not found at C:\\x.", outcome.Text);
    }

    [Fact]
    public async Task AnAiErrorIsAFailureMessage()
    {
        var desktop = new FakeDesktop();
        var agent = new ComputerAgent((_, _, _, _) => throw new AiException("HTTP 529: overloaded"), desktop);
        var outcome = await agent.RunAsync(Step, default);
        Assert.Equal("Failed: the AI service gave an error: HTTP 529: overloaded.", outcome.Text);
    }

    [Fact]
    public async Task APlainAnswerGetsOneReminderThenCounts()
    {
        var (agent, _, ai, _) = Make(Says("Here is the summary"), Says("Here is the summary"));
        var outcome = await agent.RunAsync(Step, default);
        Assert.True(outcome.Done);
        Assert.Equal("Here is the summary", outcome.Text);
        Assert.Contains("finish", ai.Seen[1].Last().Text);
    }

    [Fact]
    public async Task ACutOffAnswerIsAFailure()
    {
        var (agent, _, _, _) = Make(Says("Here is the summ", cutOff: true));
        var outcome = await agent.RunAsync(Step, default);
        Assert.Contains("cut off", outcome.Text);
    }

    [Fact]
    public async Task BadArgumentsGoBackToTheAiWithoutRunning()
    {
        var (agent, desktop, ai, _) = Make(Calls(Call("click")), Calls(Call("finish", ("status", "done"), ("step", 1), ("result", "ok"))));
        await agent.RunAsync(Step, default);
        Assert.Empty(desktop.Runs);
        var result = ai.Seen[1].Last().Results.Single();
        Assert.True(result.IsError);
        Assert.Contains("\"ref\" is required", result.Content);
    }

    [Fact]
    public async Task AFailedCommandIsAnErrorResultNotAStop()
    {
        var (agent, desktop, ai, _) = Make(Calls(Call("click", ("ref", "@old:e1"))), Calls(Call("finish", ("status", "done"), ("step", 1), ("result", "ok"))));
        desktop.Respond = (_, _) => Task.FromResult(new DesktopOutput(false, "{\"ok\":false,\"error\":\"stale ref\"}"));
        var outcome = await agent.RunAsync(Step, default);
        Assert.True(outcome.Done);
        Assert.True(ai.Seen[1].Last().Results.Single().IsError);
    }

    [Fact]
    public async Task EmptyInstructionsFailWithoutAskingTheAi()
    {
        var (agent, _, ai, _) = Make();
        var outcome = await agent.RunAsync(new ComputerStep { Instruction = "  " }, default);
        Assert.Equal("Failed: the step has no instructions.", outcome.Text);
        Assert.Empty(ai.Seen);
    }

    [Fact]
    public async Task OldWindowReadsAreCutShort()
    {
        var big = new string('x', 5000);
        var (agent, desktop, ai, _) = Make(Calls(Call("snapshot")), Calls(Call("snapshot")), Calls(Call("snapshot")), Calls(Call("snapshot")));
        desktop.Respond = (_, _) => Task.FromResult(new DesktopOutput(true, big));
        await agent.RunAsync(Step, default);
        var last = ai.Seen[^1].Where(m => m.Role == AgentRole.Tools).Select(m => m.Results[0].Content.Length).ToList();
        Assert.Equal(4, last.Count);
        Assert.All(last.Take(2), n => Assert.True(n < 500));
        Assert.All(last.Skip(2), n => Assert.Equal(5000, n));
    }

    [Fact]
    public async Task LogNeverHoldsWhatWasTypedOrRead()
    {
        var (agent, desktop, _, log) = Make(Calls(Call("type", ("ref", "@s:e1"), ("text", "my secret search"))),
            Calls(Call("finish", ("status", "done"), ("step", 1), ("result", "private summary"))));
        desktop.Respond = (_, _) => Task.FromResult(new DesktopOutput(true, "Inbox: private email"));
        await agent.RunAsync(Step, default);
        var all = string.Join("\n", log);
        Assert.DoesNotContain("secret", all);
        Assert.DoesNotContain("private", all);
        Assert.Contains("type 16 characters into @s:e1", all);
    }

    const string EdgePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
    const string NoWindows = "{\"ok\":true,\"data\":[]}";
    static string WindowsJson(params (string Id, string App, string Title)[] windows) =>
        new JsonObject { ["ok"] = true, ["data"] = new JsonArray(windows.Select(w => (JsonNode)new JsonObject { ["id"] = w.Id, ["app_name"] = w.App, ["title"] = w.Title }).ToArray()) }.ToJsonString();

    /// <summary>The result the AI got back for its first tool call.</summary>
    static ToolResult FirstResult(ScriptedAi ai) => ai.Seen[1].Last().Results.Single();

    [Fact]
    public async Task OpeningAnAppThatIsOpenBringsItsWindowForward()
    {
        var (agent, desktop, ai, _) = Make(Calls(Call("launch", ("app", "Microsoft Edge"))));
        desktop.Respond = (args, _) => Task.FromResult(new DesktopOutput(true, args[0] == "list-windows"
            ? WindowsJson(("w-1", "WindowsTerminal.exe", "bash"), ("w-592972", "msedge.exe", "Inbox - Gmail - Microsoft Edge")) : "{\"ok\":true}"));
        await agent.RunAsync(Step, default);
        Assert.Equal(new[] { "list-windows", "focus-window --window-id w-592972" }, desktop.Runs);
        Assert.Contains("already open", FirstResult(ai).Content);
    }

    [Fact]
    public async Task AStoreAppIsFoundInTheStartMenuAndItsNewWindowComesForward()
    {
        var (agent, desktop, ai, _) = Make(Calls(Call("launch", ("app", "whatsapp"))));
        agent.WindowPoll = TimeSpan.FromMilliseconds(1);
        desktop.CanStart = true;
        desktop.StartMenu.AddRange(new InstalledProgram[] { new("WhatsApp", @"shell:AppsFolder\5319275A.WhatsAppDesktop!App"), new("Notepad", "n") });
        var lists = 0;
        desktop.Respond = (args, _) => Task.FromResult(new DesktopOutput(true, args[0] != "list-windows" ? "{\"ok\":true}"
            : ++lists < 3 ? WindowsJson(("w-1", "explorer.exe", "Downloads")) : WindowsJson(("w-1", "explorer.exe", "Downloads"), ("w-9", "WhatsApp.Root.exe", "WhatsApp"))));
        await agent.RunAsync(Step, default);
        Assert.Equal(new[] { @"shell:AppsFolder\5319275A.WhatsAppDesktop!App" }, desktop.Started);
        Assert.Equal("focus-window --window-id w-9", desktop.Runs.Last());
        Assert.Contains("WhatsApp started", FirstResult(ai).Content);
    }

    [Fact]
    public async Task AClosedBrowserIsStartedByItsPathNotByAgentDesktop()
    {
        var (agent, desktop, _, _) = Make(Calls(Call("launch", ("app", "edge"))));
        agent.WindowPoll = TimeSpan.FromMilliseconds(1);
        desktop.Programs["msedge"] = EdgePath;
        desktop.CanStart = true;
        var lists = 0;
        desktop.Respond = (args, _) => Task.FromResult(new DesktopOutput(true, args[0] != "list-windows" ? "{\"ok\":true}"
            : ++lists < 3 ? NoWindows : WindowsJson(("w-1", "msedge.exe", "New tab - Microsoft Edge"))));
        await agent.RunAsync(Step, default);
        Assert.Equal(new[] { EdgePath }, desktop.Started);
        Assert.DoesNotContain(desktop.Runs, r => r.StartsWith("launch"));      // agent-desktop refuses a browser running in the background
        Assert.Equal("focus-window --window-id w-1", desktop.Runs.Last());
    }

    [Fact]
    public async Task ProgramsInTheWindowsFolderStillOpenThroughAgentDesktop()
    {
        var (agent, desktop, _, _) = Make(Calls(Call("launch", ("app", "notepad"), ("activate", true))));
        desktop.Respond = (args, _) => Task.FromResult(new DesktopOutput(true, args[0] == "list-windows" ? NoWindows : "{\"ok\":true}"));
        await agent.RunAsync(Step, default);
        Assert.Equal("launch --activate -- notepad", desktop.Runs[1]);
    }

    [Fact]
    public async Task WhenNothingFitsTheAiIsToldWhatIsCloseAndToSearch()
    {
        var (agent, desktop, ai, _) = Make(Calls(Call("launch", ("app", "spotify music"))));
        desktop.StartMenu.Add(new InstalledProgram("Music player", "m"));
        desktop.Respond = (args, _) => Task.FromResult(args[0] == "list-windows" ? new DesktopOutput(true, NoWindows)
            : new DesktopOutput(false, "{\"ok\":false,\"error\":{\"code\":\"APP_NOT_FOUND\"}}"));
        await agent.RunAsync(Step, default);
        var result = FirstResult(ai);
        Assert.True(result.IsError);
        Assert.Contains("Music player", result.Content);
        Assert.Contains("find_program", result.Content);
    }

    [Fact]
    public async Task AnAppWhoseWindowNeverAppearsIsAFailedResult()
    {
        var (agent, desktop, ai, _) = Make(Calls(Call("launch", ("app", "msedge"))));
        agent.WindowPoll = TimeSpan.FromMilliseconds(1);
        desktop.Programs["msedge"] = EdgePath;
        desktop.CanStart = true;
        desktop.Respond = (_, _) => Task.FromResult(new DesktopOutput(true, NoWindows));
        await agent.RunAsync(Step, default);
        Assert.True(FirstResult(ai).IsError);
        Assert.Contains("no window of it appeared", FirstResult(ai).Content);
    }

    [Fact]
    public async Task FindProgramSearchesTheStartMenuAndProgramFiles()
    {
        var (agent, desktop, ai, log) = Make(Calls(Call("find_program", ("query", "studio"))));
        desktop.StartMenu.AddRange(new InstalledProgram[] { new("Visual Studio Code", "a"), new("Android Studio", "b"), new("Notepad", "c") });
        desktop.Files.Add(@"C:\Program Files\Microsoft Visual Studio\devenv-studio.exe");
        await agent.RunAsync(Step, default);
        var result = FirstResult(ai);
        Assert.False(result.IsError);
        Assert.Contains("Android Studio | Visual Studio Code", result.Content);   // equally good: shorter first
        Assert.Contains("devenv-studio.exe", result.Content);
        Assert.DoesNotContain("Notepad", result.Content);
        Assert.Contains(log, l => l.Contains("find program studio"));
        Assert.Empty(desktop.Runs);                                       // searching runs nothing
    }

    [Fact]
    public async Task FindProgramSaysWhenNothingIsInstalled()
    {
        var (agent, _, ai, _) = Make(Calls(Call("find_program", ("query", "zzz"))));
        await agent.RunAsync(Step, default);
        Assert.Contains("Nothing installed matches", FirstResult(ai).Content);
    }

    [Theory]
    [InlineData("edge", "Microsoft Edge")]
    [InlineData("teams", "Microsoft Teams")]
    [InlineData("vs code", "Visual Studio Code")]
    [InlineData("outlook", "Outlook")]
    [InlineData("whatsapp", "WhatsApp")]
    [InlineData("code", "Visual Studio Code")]
    public void EverydayNamesFindTheStartMenuEntry(string asked, string expected)
    {
        var menu = new[] { "Microsoft Edge", "Microsoft Teams", "Visual Studio Code", "Outlook (classic)", "Outlook", "WhatsApp", "Notepad", "Notepad++", "Windows PowerShell" }
            .Select(n => new InstalledProgram(n, n));
        Assert.Equal(expected, ProgramFinder.Best(asked, menu)?.Name);
    }

    [Fact]
    public void NothingFitsAnUnrelatedName() =>
        Assert.Null(ProgramFinder.Best("spotify", new[] { new InstalledProgram("Microsoft Edge", "e"), new InstalledProgram("Notepad", "n") }));

    [Fact]
    public void AWindowIsMatchedByItsFileOrItsTitle()
    {
        var windows = new[] { new DesktopWindow("w-1", "explorer.exe", "Downloads"), new DesktopWindow("w-2", "WhatsApp.Root.exe", "WhatsApp"),
            new DesktopWindow("w-3", "ms-teams.exe", "Chat | Microsoft Teams") };
        Assert.Equal("w-2", ProgramFinder.WindowOf(windows, "whatsapp", "WhatsApp")?.Id);
        Assert.Equal("w-3", ProgramFinder.WindowOf(windows, "teams", "Microsoft Teams")?.Id);
        Assert.Equal("w-1", ProgramFinder.WindowOf(windows, "explorer", null)?.Id);
        Assert.Null(ProgramFinder.WindowOf(windows, "msedge", "Microsoft Edge"));
    }

    [Theory]
    [InlineData("Microsoft Edge", "msedge")]
    [InlineData("msedge.exe", "msedge")]
    [InlineData(@"""C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe""", "msedge")]
    [InlineData("Word", "winword")]
    [InlineData("notepad", "notepad")]
    public void ProgramNamesAreWhatAgentDesktopCallsTheApp(string app, string expected) => Assert.Equal(expected, DesktopTools.ProgramName(app));

    sealed class Keys : IInputSender
    {
        public List<string> Sent = new();
        public bool Allowed = true;
        public bool CanSendToForeground() => Allowed;
        public void ReleaseModifiers() { }
        public void RestoreModifiers() { }
        public void SendCombo(Shortcut combo) => Sent.Add("combo " + combo);
        public void SendText(string text) => Sent.Add("text " + text);
        public void SendKey(int vk, bool up) => Sent.Add($"key {vk} {(up ? "up" : "down")}");
        public void SendMouseButton(ClickButton button, bool up) { }
        public void Scroll(ScrollDirection direction, int notches) { }
        public void MoveMouse(int x, int y) { }
    }

    const string Focused = "{\"ok\":true,\"data\":{\"focused\":{\"id\":\"w-7\",\"title\":\"murylo@pc: ~\"}}}";

    static (ComputerAgent, FakeDesktop, ScriptedAi, Keys, List<string>) Typing(string? front, params AgentReply[] turns)
    {
        var desktop = new FakeDesktop { Respond = (_, _) => Task.FromResult(new DesktopOutput(true, Focused)) };
        var ai = new ScriptedAi(turns);
        var keys = new Keys();
        var log = new List<string>();
        var agent = new ComputerAgent(ai.Ask, desktop, log.Add, keyboard: keys, foregroundTitle: () => front) { AfterTyping = TimeSpan.Zero, ScreenPoll = TimeSpan.FromMilliseconds(1) };
        return (agent, desktop, ai, keys, log);
    }

    static readonly ComputerStep TypingStep = new() { Instruction = "1. open wsl 2. cd projects/CaseManager 3. run start.sh", AllowTyping = true };

    [Fact]
    public async Task TypingIntoAWindowFocusesItTypesAndPressesEnter()
    {
        var (agent, desktop, ai, keys, log) = Typing("murylo@pc: ~", Calls(Call("type_text", ("window_id", "w-7"), ("text", "cd ~/projects/CaseManager"), ("enter", true))));
        await agent.RunAsync(TypingStep, default);
        Assert.Equal("focus-window --window-id w-7", desktop.Runs.Single());
        Assert.Equal(new[] { "text cd ~/projects/CaseManager", "key 13 down", "key 13 up" }, keys.Sent);
        Assert.False(FirstResult(ai).IsError);
        Assert.DoesNotContain(log, l => l.Contains("CaseManager"));            // the log keeps the length, not the text
        Assert.Contains(log, l => l.Contains("type 25 characters into window w-7 and press Enter"));
    }

    [Fact]
    public async Task KeysArePressedInOrderAfterTheText()
    {
        var (agent, _, ai, keys, _) = Typing("murylo@pc: ~", Calls(Call("type_text", ("window_id", "w-7"), ("text", "/agents"), ("enter", true), ("keys", "down down down enter"))));
        await agent.RunAsync(TypingStep, default);
        Assert.Equal(new[] { "text /agents", "key 13 down", "key 13 up", "combo Down", "combo Down", "combo Down", "combo Enter" }, keys.Sent);
        Assert.Contains("pressed Down Down Down Enter", FirstResult(ai).Content);
        Assert.Contains("snapshot or find", FirstResult(ai).Content);          // no readable text here: told how to check instead
    }

    [Fact]
    public async Task AfterTypingItWaitsForTheScreenToSettleAndShowsIt()
    {
        var (agent, desktop, ai, keys, _) = Typing("murylo@pc: ~", Calls(Call("type_text", ("window_id", "w-7"), ("text", "ls"), ("enter", true))));
        desktop.Screen = _ => keys.Sent.Count == 0 ? "$ " : desktop.Reads < 3 ? "$ ls\nfile1" : "$ ls\nfile1 file2\n$ ";
        await agent.RunAsync(TypingStep, default);
        var result = FirstResult(ai);
        Assert.False(result.IsError);
        Assert.Contains("changed and settled", result.Content);
        Assert.EndsWith("$ ls\nfile1 file2\n$", result.Content);            // the final screen, not a half-drawn one
    }

    [Fact]
    public async Task AScreenThatDoesntChangeMeansTheStepDidntWork()
    {
        var (agent, desktop, ai, _, _) = Typing("murylo@pc: ~", Calls(Call("type_text", ("window_id", "w-7"), ("text", "/agents"), ("enter", true), ("wait_seconds", 2))));
        desktop.Screen = _ => "opencode is starting...";
        await agent.RunAsync(TypingStep, default);
        var result = FirstResult(ai);
        Assert.True(result.IsError);
        Assert.Contains("did NOT change within 2 seconds", result.Content);
        Assert.Contains("Don't go on", result.Content);
    }

    [Fact]
    public async Task ItCanWaitForAProgramToBeReady()
    {
        var (agent, desktop, ai, keys, _) = Typing("murylo@pc: ~", Calls(Call("type_text", ("window_id", "w-7"), ("text", "opencode"), ("enter", true), ("wait_for", "Ask anything"))));
        desktop.Screen = _ => keys.Sent.Count == 0 ? "$ " : desktop.Reads < 20 ? "$ opencode\nloading" : "opencode\n> Ask anything";
        await agent.RunAsync(TypingStep, default);
        Assert.False(FirstResult(ai).IsError);
        Assert.True(desktop.Reads >= 20);                                       // it kept watching while opencode loaded
        Assert.Contains("> Ask anything", FirstResult(ai).Content);
    }

    [Fact]
    public async Task ReadyTextInTheWindowTitleCounts()
    {
        // opencode draws its name in box characters: the word is only in the tab title.
        var (agent, desktop, ai, keys, _) = Typing("murylo@pc: ~", Calls(Call("type_text", ("window_id", "w-7"), ("text", "opencode"), ("enter", true), ("wait_for", "opencode"))));
        desktop.Screen = _ => keys.Sent.Count == 0 ? "$ " : "\u2588\u2580\u2588 \u2588\u2580\u2588\n> Ask anything";
        desktop.Title = () => keys.Sent.Count == 0 ? "murylo@pc: ~" : "OpenCode";
        await agent.RunAsync(TypingStep, default);
        var result = FirstResult(ai);
        Assert.False(result.IsError);
        Assert.Contains("changed and settled", result.Content);
        Assert.Contains("Window title: OpenCode", result.Content);
    }

    [Fact]
    public async Task ReadyTextMissingButAChangedScreenLetsTheAiDecide()
    {
        var (agent, desktop, ai, keys, _) = Typing("murylo@pc: ~", Calls(Call("type_text", ("window_id", "w-7"), ("text", "opencode"), ("enter", true), ("wait_for", "Ask anything"), ("wait_seconds", 2))));
        desktop.Screen = _ => keys.Sent.Count == 0 ? "$ " : "$ opencode\ncommand not found";
        await agent.RunAsync(TypingStep, default);
        var result = FirstResult(ai);
        Assert.False(result.IsError);
        Assert.Contains("did not appear on screen or in the window title within 2 seconds, but the screen did change", result.Content);
        Assert.Contains("command not found", result.Content);
    }

    [Fact]
    public async Task ReadyTextMissingAndNothingChangedIsAFailedResult()
    {
        var (agent, desktop, ai, _, _) = Typing("murylo@pc: ~", Calls(Call("type_text", ("window_id", "w-7"), ("text", "opencode"), ("enter", true), ("wait_for", "Ask anything"), ("wait_seconds", 2))));
        desktop.Screen = _ => "$ ";
        await agent.RunAsync(TypingStep, default);
        Assert.True(FirstResult(ai).IsError);
        Assert.Contains("nothing changed", FirstResult(ai).Content);
    }

    [Fact]
    public async Task ReadTextCanWaitForText()
    {
        var (agent, desktop, ai, _) = Make(Calls(Call("read_text", ("window_id", "w-7"), ("wait_for", "ready"))));
        agent.ScreenPoll = TimeSpan.FromMilliseconds(1);
        desktop.Screen = _ => desktop.Reads < 5 ? "starting" : "server ready";
        await agent.RunAsync(Step, default);
        Assert.Equal("server ready", FirstResult(ai).Content);
    }

    [Fact]
    public async Task KeysAloneWorkAndAPasteIsAKey()
    {
        var (agent, _, _, keys, _) = Typing("murylo@pc: ~", Calls(Call("type_text", ("window_id", "w-7"), ("keys", "ctrl+shift+v"))));
        await agent.RunAsync(TypingStep, default);
        Assert.Equal(new[] { "combo Ctrl+Shift+V" }, keys.Sent);
    }

    [Fact]
    public async Task AnUnknownKeyNameTypesNothing()
    {
        var (agent, _, ai, keys, _) = Typing("murylo@pc: ~", Calls(Call("type_text", ("window_id", "w-7"), ("text", "ls"), ("keys", "down arrowdown"))));
        await agent.RunAsync(TypingStep, default);
        Assert.Empty(keys.Sent);
        Assert.Contains("\"arrowdown\" is not a key name", FirstResult(ai).Content);
    }

    [Fact]
    public async Task ReadTextGivesTheEndOfATerminal()
    {
        var (agent, desktop, ai, _) = Make(Calls(Call("read_text", ("window_id", "w-7"))), Calls(Call("read_text", ("window_id", "w-8"))), Calls(Call("read_text", ("window_id", "w-9"))));
        desktop.WindowText["w-7"] = "$ cd projects   \r\nbash: cd: projects: No such file or directory   \r\n\r\n\r\n";
        desktop.WindowText["w-8"] = new string('a', 20000) + "LAST LINE";
        await agent.RunAsync(Step, default);
        Assert.Equal("$ cd projects\nbash: cd: projects: No such file or directory", ai.Seen[1].Last().Results.Single().Content);
        var long_ = ai.Seen[2].Last().Results.Single().Content;
        Assert.EndsWith("LAST LINE", long_);
        Assert.StartsWith("...[8009 earlier characters left out]", long_);
        Assert.True(ai.Seen[3].Last().Results.Single().IsError);              // a window with no text to read
        Assert.Empty(desktop.Runs);                                             // reading text runs nothing
    }

    [Fact]
    public async Task AnEmptyScreenIsNotAFailureButAReasonToWait()
    {
        var (agent, desktop, ai, _) = Make(Calls(Call("read_text", ("window_id", "w-7"))));
        desktop.WindowText["w-7"] = "  \r\n   \r\n";
        await agent.RunAsync(Step, default);
        var result = FirstResult(ai);
        Assert.False(result.IsError);
        Assert.Contains("read again with wait_for", result.Content);
    }

    [Fact]
    public async Task AStepKeepsItsFirstDescription()
    {
        var (agent, _, _, _) = Make(
            Calls(Call("report_step", ("step", 3), ("description", "Execute opencode"))),
            Calls(Call("report_step", ("step", 3), ("description", "Failure"))),
            Calls(Call("finish", ("status", "failed"), ("step", 3), ("result", "no output"))));
        var outcome = await agent.RunAsync(Step, default);
        Assert.Equal("Failed at step 3 (Execute opencode): no output.", outcome.Text);
    }

    [Fact]
    public async Task InstructionsSayToCheckTerminalOutputAndNeverAssume()
    {
        var (agent, _, ai, _, _) = Typing("x");
        await agent.RunAsync(TypingStep, default);
        Assert.Contains("Never assume an action worked", ai.System);
        Assert.Contains("call read_text", ai.System);
        Assert.Contains("the step did not work: don't go on to the next step", ai.System);
        Assert.Contains("A step you skipped or couldn't do is a failure", ai.System);
    }

    [Fact]
    public async Task NothingIsTypedWhenAnotherWindowIsInFront()
    {
        var (agent, _, ai, keys, _) = Typing("Inbox - Gmail", Calls(Call("type_text", ("window_id", "w-7"), ("text", "ls"))));
        await agent.RunAsync(TypingStep, default);
        Assert.Empty(keys.Sent);
        Assert.True(FirstResult(ai).IsError);
        Assert.Contains("didn't stay in front", FirstResult(ai).Content);
    }

    [Fact]
    public async Task AStepThatDoesntAllowTypingNeitherOffersNorRunsIt()
    {
        var (agent, desktop, ai, keys, _) = Typing("murylo@pc: ~", Calls(Call("type_text", ("window_id", "w-7"), ("text", "rm -rf ~"), ("enter", true))));
        await agent.RunAsync(new ComputerStep { Instruction = "1. read my email" }, default);
        Assert.Empty(keys.Sent);
        Assert.Empty(desktop.Runs);
        Assert.Contains("isn't allowed", FirstResult(ai).Content);
        Assert.DoesNotContain(DesktopTools.For(new ComputerStep()), t => t.Name == "type_text");
        Assert.Contains(DesktopTools.For(TypingStep), t => t.Name == "type_text");
        Assert.DoesNotContain("type_text", ai.System);
    }

    [Fact]
    public async Task TypingRulesAreInTheInstructionsOnlyWhenAllowed()
    {
        var (agent, _, ai, _, _) = Typing("x");
        await agent.RunAsync(TypingStep, default);
        Assert.Contains("Type only the commands and text the user's steps ask for", ai.System);
        Assert.True(ActionRunner.UsesInput(TypingStep));                      // it takes the keyboard turn in a flow
    }

    // ----- the command lines -----

    static string Line(ToolCall call, bool real = false) => string.Join(" ", DesktopTools.Args(call, real));

    [Fact]
    public void CommandLinesMatchAgentDesktop()
    {
        Assert.Equal("snapshot --app msedge -i --compact", Line(Call("snapshot", ("app", "msedge"))));
        Assert.Equal("snapshot --max-depth 30 --compact", Line(Call("snapshot", ("interactive_only", false), ("max_depth", 99))));
        Assert.Equal("find --role button --text Inbox --first", Line(Call("find", ("role", "button"), ("text", "Inbox"), ("first", true))));
        Assert.Equal("click --headed -- @s:e1", Line(Call("click", ("ref", "@s:e1")), real: true));
        Assert.Equal("type -- @s:e1  two spaces ", Line(Call("type", ("ref", "@s:e1"), ("text", " two spaces "))));
        Assert.Equal("press -- ctrl+l", Line(Call("press", ("combo", "ctrl+l"), ("force", true))));   // never --force
        Assert.Equal("wait -- 30000", Line(Call("wait", ("ms", 90000))));
        Assert.Equal("set-value -- @s:e2 --help", Line(Call("set_value", ("ref", "@s:e2"), ("value", "--help"))));   // after "--": not an option
    }

    [Fact]
    public void UnknownOrIncompleteCallsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => DesktopTools.Args(Call("close_app", ("app", "x")), false));
        Assert.Throws<ArgumentException>(() => DesktopTools.Args(Call("wait"), false));
        Assert.Throws<ArgumentException>(() => DesktopTools.Args(Call("type", ("ref", "@s:e1")), false));
    }

    [Fact]
    public void NoToolReachesTheClipboardOrForce()
    {
        var names = DesktopTools.All.Select(t => t.Name).ToList();
        Assert.DoesNotContain(names, n => n.Contains("clipboard") || n.Contains("command") || n.Contains("close"));
        Assert.DoesNotContain(DesktopTools.All, t => t.Parameters.ToJsonString().Contains("force"));
    }

    [Theory]
    [InlineData(3, "Click Inbox", "not signed in", "Failed at step 3 (Click Inbox): not signed in.")]
    [InlineData(2, null, "Missing!", "Failed at step 2: Missing!")]
    [InlineData(0, null, "no AI", "Failed: no AI.")]
    public void FailureTextReadsWell(int step, string? title, string reason, string expected) =>
        Assert.Equal(expected, ComputerAgent.FailureText(step, title, reason));
}
