using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RingDeck.Core;
using RingDeck.Windows;

namespace RingDeck.App;

/// <summary>Developer aid: `RingDeck.exe --simulate &lt;folder&gt; &lt;scenario&gt;` opens the first ring in the middle of
/// the screen and moves a scripted pointer through it, through the real controller and overlay windows. Real input is
/// ignored and nothing runs: a chosen slice is only written to the log. Every frame of the screen around the rings is
/// saved, with a log of what the session did and a last line saying how it ended.</summary>
sealed class Simulation
{
    /// <summary>One step: at <c>At</c> seconds glide to <c>Target</c> over <c>Glide</c> seconds; or, with no target,
    /// let go of the shortcut.</summary>
    sealed record Step(double At, Func<(double X, double Y)>? Target, double Glide = 0.3);

    readonly RingController _c;
    readonly string _folder;
    readonly Action _done;
    readonly List<BitmapSource> _frames = new();
    readonly List<string> _log = new();
    readonly (int X, int Y) _start;
    readonly Int32Rect _area;
    List<Step> _steps = new();
    (double X, double Y) _from;
    DateTime _t0;
    int _next;
    double _end;
    bool _finished;
    string _ran = "nothing";
    Window? _backdrop;

    public Simulation(RingController controller, string folder, Action done)
    {
        (_c, _folder, _done) = (controller, folder, done);
        var screen = Screens.At(Screens.Cursor().X, Screens.Cursor().Y);
        _start = ((screen.Left + screen.Right) / 2 - 250, (screen.Top + screen.Bottom) / 2);
        _area = new Int32Rect(_start.X - 700, _start.Y - 500, 1600, 1000);
    }

    /// <summary>Where the first slice of the current ring matching <paramref name="which"/> is, in physical pixels.</summary>
    Func<(double X, double Y)> SliceOf(Func<Slice, bool> which) => () =>
    {
        var s = _c.Session;
        var index = Math.Max(0, s.Current?.Slices.FindIndex(x => which(x)) ?? 0);
        var (x, y) = RingGeometry.PointAt(s.Layout.Angle(index), s.Radius);
        return ((s.CenterX + x) * _c.Unit, (s.CenterY + y) * _c.Unit);
    };

    /// <summary>Where a slice of the first ring is, in physical pixels, wherever the session is now.</summary>
    Func<(double X, double Y)> SliceOfFirst(Ring first, Func<Slice, bool> which) => () =>
    {
        var s = _c.Session;
        var trail = s.Trail;
        var (cx, cy, layout, radius) = trail.Count > 0 ? (trail[0].X, trail[0].Y, trail[0].Layout, trail[0].Radius) : (s.CenterX, s.CenterY, s.Layout, s.Radius);
        var (x, y) = RingGeometry.PointAt(layout.Angle(Math.Max(0, first.Slices.FindIndex(x => which(x)))), radius);
        return ((cx + x) * _c.Unit, (cy + y) * _c.Unit);
    };

    /// <summary>A plain dark window behind the rings, so the recording shows nothing of what is on the screen.</summary>
    Window Backdrop()
    {
        var dpi = Screens.At(_start.X, _start.Y).DpiScale;
        var w = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
            Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17)), Left = _area.X / dpi, Top = _area.Y / dpi,
            Width = _area.Width / dpi, Height = _area.Height / dpi,
        };
        w.Show();
        return w;
    }

    public void Run(Ring ring, string scenario)
    {
        _backdrop = Backdrop();
        static bool Nested(Slice s) => s.Action is NestedRingAction;
        var timedPlain = ring.Slices.FirstOrDefault(s => s.ConfirmMs > 0 && !Nested(s));
        var plain = ring.Slices.First(s => s.ConfirmMs == 0 && !Nested(s));
        var nestedWait = (ring.Slices.FirstOrDefault(Nested)?.ConfirmMs ?? 0) / 1000.0;
        var plainWait = (timedPlain?.ConfirmMs ?? 0) / 1000.0;
        _steps = scenario switch
        {
            // into the timed nested ring, on to its first slice, back over the first ring, let go on a plain slice
            "tree" => [new(0.25, SliceOf(Nested)), new(0.55 + nestedWait + 0.6, SliceOf(_ => true), 0.35),
                       new(0.55 + nestedWait + 1.6, SliceOfFirst(ring, s => s == plain), 0.35), new(0.55 + nestedWait + 2.4, null)],
            // for the demo video: wait on the timed nested slice, branch one ring deeper, rest, fold back, let go
            "showcase" => [new(0.4, SliceOf(Nested), 0.35), new(0.75 + nestedWait + 0.9, SliceOf(Nested), 0.4),
                           new(0.75 + nestedWait + 2.2, SliceOf(_ => true), 0.35), new(0.75 + nestedWait + 3.3, SliceOfFirst(ring, s => s == plain), 0.5),
                           new(0.75 + nestedWait + 4.6, null)],
            // the reported bug: wait on the timed nested slice until its ring opens, then let go right there
            "release-on-opened" => [new(0.25, SliceOf(Nested)), new(0.55 + nestedWait + 0.7, null)],
            // a timed plain slice, let go after its circle is full: it must run and everything close
            "release-after-wait" => [new(0.25, SliceOf(s => s == timedPlain)), new(0.55 + plainWait + 0.3, null)],
            // the same, let go too early: nothing runs and everything closes
            "release-too-early" => [new(0.25, SliceOf(s => s == timedPlain)), new(0.55 + plainWait * 0.4, null)],
            _ => throw new ArgumentException("unknown scenario " + scenario),
        };
        _end = _steps[^1].At + 0.9;
        _c.Capture = (_, _) => false;               // the real keyboard and mouse pass by untouched and change nothing here
        _c.DryRun = slice => { _ran = slice.Label; _log.Add($"RAN {slice.Label}"); };
        _c.FakePointer = _start;
        _from = _start;
        _c.OpenForSimulation(ring.Id);
        _t0 = DateTime.Now;
        CompositionTarget.Rendering += OnFrame;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        if (_finished) return;
        var t = (DateTime.Now - _t0).TotalSeconds;
        Drive(t);
        var s = _c.Session;
        var (px, py) = _c.FakePointer ?? (0, 0);
        _log.Add($"{t:0.000} ptr={px - _area.X},{py - _area.Y} open={s.IsOpen} depth={s.Depth} selected={s.Selected} hold={s.HoldProgress:0.00} ring={s.Current?.Name ?? "-"} " +
                 string.Join(" ", _c.Windows.Select((w, i) => $"w{i}={w.Current.Ring?.Name ?? "-"}:{w.Current.Animator.Phase}:a{w.Current.Animator.Pose.Alpha:0.00}:dim{w.Current.Dim:0.00}")));
        _frames.Add(ScreenGrab.Grab(_area.X, _area.Y, _area.Width, _area.Height));
        if (t < _end) return;
        _finished = true;
        CompositionTarget.Rendering -= OnFrame;
        var visible = _c.Windows.Count(w => w.Current.Ring is not null);
        _log.Add($"END open={s.IsOpen} windows-showing={visible} ran={_ran}");
        Save();
    }

    void Drive(double t)
    {
        while (_next < _steps.Count && t >= _steps[_next].At)
        {
            var step = _steps[_next];
            if (step.Target is null)
            {
                _log.Add($"{t:0.000} LET GO of the shortcut");
                _c.ReleaseForSimulation();
                _next++;
                continue;
            }
            if (t < step.At + step.Glide) break;
            _from = step.Target();
            _next++;
        }
        if (_next < _steps.Count && _steps[_next] is { Target: { } target } move && t >= move.At)
        {
            var p = Math.Clamp((t - move.At) / move.Glide, 0, 1);
            p = p < .5 ? 4 * p * p * p : 1 - Math.Pow(-2 * p + 2, 3) / 2;
            var (tx, ty) = target();
            _c.FakePointer = ((int)(_from.X + (tx - _from.X) * p), (int)(_from.Y + (ty - _from.Y) * p));
        }
        else _c.FakePointer = ((int)_from.X, (int)_from.Y);
    }

    void Save()
    {
        _backdrop?.Close();
        Directory.CreateDirectory(_folder);
        File.WriteAllLines(Path.Combine(_folder, "log.txt"), _log);
        var frames = _frames.ToList();
        Parallel.For(0, frames.Count, i =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frames[i]));
            using var file = File.Create(Path.Combine(_folder, $"f{i:0000}.png"));
            encoder.Save(file);
        });
        _done();
    }
}
