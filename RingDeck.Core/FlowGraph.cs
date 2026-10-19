namespace RingDeck.Core;

/// <summary>Rules and questions about a flow's cards and wires: what can be linked, what runs, and whether it can run.</summary>
public static class FlowGraph
{
    public static FlowNode? Start(FlowAction flow) => flow.Nodes.FirstOrDefault(n => n.Kind == FlowNodeKind.Start);

    public static FlowNode? Find(FlowAction flow, Guid id) => flow.Nodes.FirstOrDefault(n => n.Id == id);

    public static IEnumerable<FlowNode> Next(FlowAction flow, Guid id) =>
        flow.Wires.Where(w => w.From == id).Select(w => Find(flow, w.To)).OfType<FlowNode>();

    /// <summary>Cards that run: those reached from Start along wires.</summary>
    public static HashSet<Guid> Reachable(FlowAction flow)
    {
        var seen = new HashSet<Guid>();
        if (Start(flow) is not { } start) return seen;
        var todo = new Stack<Guid>();
        todo.Push(start.Id);
        while (todo.Count > 0)
        {
            var id = todo.Pop();
            if (!seen.Add(id)) continue;
            foreach (var next in Next(flow, id)) todo.Push(next.Id);
        }
        return seen;
    }

    /// <summary>True when a path of wires leads from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static bool Leads(FlowAction flow, Guid from, Guid to)
    {
        var seen = new HashSet<Guid>();
        var todo = new Stack<Guid>();
        todo.Push(from);
        while (todo.Count > 0)
        {
            var id = todo.Pop();
            if (id == to) return true;
            if (!seen.Add(id)) continue;
            foreach (var next in Next(flow, id)) todo.Push(next.Id);
        }
        return false;
    }

    /// <summary>A wire may go from any card but a Stop to any card but the Start, once, and never in a loop.</summary>
    public static bool CanLink(FlowAction flow, Guid from, Guid to) =>
        from != to
        && Find(flow, from) is { Kind: not FlowNodeKind.Stop }
        && Find(flow, to) is { Kind: not FlowNodeKind.Start }
        && !flow.Wires.Any(w => w.From == from && w.To == to)
        && !Leads(flow, to, from);

    public static bool Link(FlowAction flow, Guid from, Guid to)
    {
        if (!CanLink(flow, from, to)) return false;
        flow.Wires.Add(new FlowWire { From = from, To = to });
        return true;
    }

    /// <summary>Removes a card and its wires. The Start stays.</summary>
    public static void Remove(FlowAction flow, FlowNode node)
    {
        if (node.Kind == FlowNodeKind.Start) return;
        flow.Nodes.Remove(node);
        flow.Wires.RemoveAll(w => w.From == node.Id || w.To == node.Id);
    }

    /// <summary>Web requests in this card that come after a screenshot (anywhere before it in the flow) but don't
    /// send pictures: almost always a box the user forgot to tick.</summary>
    public static List<WebRequest> ScreenshotNotSent(FlowAction flow, FlowNode node)
    {
        var requests = node.Steps.OfType<WebhookStep>().Select(w => w.Request)
            .Where(r => !r.SendImage && Webhooks.SupportsImages(r.Service)).ToList();
        if (requests.Count == 0) return requests;
        var shotBefore = flow.Nodes.Any(n => n != node && n.Steps.Any(s => s is ScreenshotStep) && Leads(flow, n.Id, node.Id));
        var shotInside = node.Steps.TakeWhile(s => s is not WebhookStep).Any(s => s is ScreenshotStep);
        return shotBefore || shotInside ? requests : new List<WebRequest>();
    }

    /// <summary>Why the flow can't run, in a sentence for the user; null when it can.</summary>
    public static string? Problem(FlowAction flow)
    {
        if (Start(flow) is not { } start) return "The flow has no Start.";
        if (!flow.Wires.Any(w => w.From == start.Id)) return "Link Start to the first card.";
        var reachable = Reachable(flow);
        if (!flow.Nodes.Any(n => n.Kind == FlowNodeKind.Stop && reachable.Contains(n.Id)))
            return "At least one branch must end at a Stop.";
        return null;
    }
}
