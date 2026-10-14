namespace RingDeck.Core.Tests;

public class AiConfigTests
{
    static (AppConfig Config, Ring Ring, AiConnection Used, AiConnection Unused) Build()
    {
        var used = new AiConnection
        {
            Name = "Claude", Format = AiFormat.Anthropic, Endpoint = "https://api.example/v1/messages", Model = "m1",
            ApiKey = "dpapi:SECRET", Temperature = 0.3, MaxTokens = 500,
            Headers = { new AiHeader { Name = "X-Team", Value = "blue" }, new AiHeader { Name = "X-Token", Value = "dpapi:HIDDEN", Secret = true } },
        };
        var unused = new AiConnection { Name = "Other", ApiKey = "dpapi:OTHER" };
        var ring = new Ring { Name = "AI", Slices = { new Slice { Label = "Ask", Action = new AiChatAction { ConnectionId = used.Id, Instruction = "Translate", SystemPrompt = "Be brief", Input = AiInput.Clipboard, SendImmediately = false } } } };
        return (new AppConfig { Rings = { ring }, AiConnections = { used, unused } }, ring, used, unused);
    }

    [Fact]
    public void AiFieldsRoundTrip()
    {
        var (config, _, used, _) = Build();
        var back = ConfigStore.FromJson(ConfigStore.ToJson(config))!;
        Assert.Equal(ConfigStore.ToJson(config), ConfigStore.ToJson(back));
        var action = Assert.IsType<AiChatAction>(back.Rings[0].Slices[0].Action);
        Assert.Equal(used.Id, action.ConnectionId);
        Assert.Equal(AiInput.Clipboard, action.Input);
        Assert.False(action.SendImmediately);
        var c = back.FindConnection(used.Id)!;
        Assert.Equal(AiFormat.Anthropic, c.Format);
        Assert.Equal(500, c.MaxTokens);
        Assert.True(c.Headers[1].Secret);
        Assert.Contains("\"type\": \"ai\"", ConfigStore.ToJson(config));
    }

    [Fact]
    public void DefaultsSuitTheCommonCase()
    {
        var action = new AiChatAction();
        Assert.Equal(AiInput.Selection, action.Input);
        Assert.True(action.SendImmediately);
        Assert.Equal(AiFormat.OpenAi, new AiConnection().Format);
        Assert.Null(new AiConnection().Temperature);
    }

    [Fact]
    public void HandEditedConnectionsAreNormalised()
    {
        var id = Guid.NewGuid();
        var config = ConfigStore.FromJson($$"""
            { "aiConnections": [ { "id": "{{id}}", "name": "a", "headers": null }, { "id": "{{id}}", "name": "b" }, null, { "name": null } ] }
            """)!;
        Assert.Equal(3, config.AiConnections.Count);
        Assert.Equal(3, config.AiConnections.Select(c => c.Id).Distinct().Count());
        Assert.All(config.AiConnections, c => Assert.NotNull(c.Headers));
        Assert.Equal("", config.AiConnections[2].Name);
        Assert.Empty(ConfigStore.FromJson("{}")!.AiConnections);
    }

    [Fact]
    public void ExportingOneRingCarriesItsConnectionWithoutSecrets()
    {
        var (config, ring, used, _) = Build();
        var exported = ConfigTransfer.Export(config, ring.Id);
        var c = Assert.Single(exported.AiConnections);
        Assert.Equal(used.Id, c.Id);
        Assert.Equal("", c.ApiKey);
        Assert.Equal("blue", c.Headers[0].Value);
        Assert.Equal("", c.Headers[1].Value);
        Assert.Equal("dpapi:SECRET", used.ApiKey);                     // the live config is untouched
        Assert.DoesNotContain("dpapi:", ConfigStore.ToJson(exported));
    }

    [Fact]
    public void ExportingEverythingKeepsAllConnectionsWithoutSecrets()
    {
        var (config, _, _, _) = Build();
        var exported = ConfigTransfer.ExportAll(config);
        Assert.Equal(2, exported.AiConnections.Count);
        Assert.DoesNotContain("dpapi:", ConfigStore.ToJson(exported));
        Assert.Equal(config.Rings.Count, exported.Rings.Count);
    }

    [Fact]
    public void ImportGivesConnectionsNewIdsAndRepointsSlices()
    {
        var (config, ring, used, _) = Build();
        var target = new AppConfig();
        var added = ConfigTransfer.Import(target, ConfigTransfer.Export(config, ring.Id));
        var c = Assert.Single(target.AiConnections);
        Assert.NotEqual(used.Id, c.Id);
        Assert.Equal(c.Id, Assert.IsType<AiChatAction>(added[0].Slices[0].Action).ConnectionId);
        Assert.Equal("", c.ApiKey);
        Assert.Equal("Claude", c.Name);
    }

    [Fact]
    public void ImportLeavesUnknownConnectionReferencesAlone()
    {
        var target = new AppConfig();
        var missing = Guid.NewGuid();
        var added = ConfigTransfer.Import(target, new AppConfig { Rings = { new Ring { Slices = { new Slice { Action = new AiChatAction { ConnectionId = missing } } } } } });
        Assert.Equal(missing, ((AiChatAction)added[0].Slices[0].Action!).ConnectionId);
        Assert.Empty(target.AiConnections);
    }
}
