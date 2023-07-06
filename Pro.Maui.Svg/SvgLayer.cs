using SkiaSharp;

namespace Pro.Maui.Svg;

public class SvgLayer
{
    private readonly Action _notifyIsVisibleChanged;
    private bool _isVisible;
    public IEnumerable<SKPath> _pathBounds;

    public SvgLayer(string id, bool isVisible, Action notifyIsVisibleChanged, IEnumerable<SKPath> pathBounds)
    {
        Id = id;
        _isVisible = isVisible;
        _pathBounds = pathBounds;
        _notifyIsVisibleChanged = notifyIsVisibleChanged;
    }

    public string Id { get; set; }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            _isVisible = value;
            _notifyIsVisibleChanged?.Invoke();
        }
    }
}