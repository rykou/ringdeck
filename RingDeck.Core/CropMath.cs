namespace RingDeck.Core;

/// <summary>Where the picture sits in the crop editor's square view: its scale, and the view position of its
/// top-left corner.</summary>
public readonly record struct CropView(double Zoom, double X, double Y);

/// <summary>Pan and zoom rules for the image crop editor. The picture always covers the whole view, and can be
/// enlarged up to eight times the size that just covers it.</summary>
public static class CropMath
{
    const double MaxZoomFactor = 8;

    static double Cover(double width, double height, double view) => view / Math.Min(width, height);

    public static CropView Start(double width, double height, double view)
    {
        var zoom = Cover(width, height, view);
        return new CropView(zoom, (view - width * zoom) / 2, (view - height * zoom) / 2);
    }

    public static CropView Pan(CropView v, double dx, double dy, double width, double height, double view) =>
        Clamp(v with { X = v.X + dx, Y = v.Y + dy }, width, height, view);

    /// <summary>Changes the zoom keeping the picture point under (px, py) where it is.</summary>
    public static CropView ZoomAt(CropView v, double zoom, double px, double py, double width, double height, double view)
    {
        var cover = Cover(width, height, view);
        zoom = Math.Clamp(zoom, cover, cover * MaxZoomFactor);
        double ix = (px - v.X) / v.Zoom, iy = (py - v.Y) / v.Zoom;
        return Clamp(new CropView(zoom, px - ix * zoom, py - iy * zoom), width, height, view);
    }

    static CropView Clamp(CropView v, double width, double height, double view) =>
        v with { X = Math.Clamp(v.X, view - width * v.Zoom, 0), Y = Math.Clamp(v.Y, view - height * v.Zoom, 0) };

    /// <summary>The square of the picture (in picture pixels) that the view shows.</summary>
    public static (double X, double Y, double Size) SourceSquare(CropView v, double view) => (-v.X / v.Zoom, -v.Y / v.Zoom, view / v.Zoom);
}
