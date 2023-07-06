using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using System.Xml;
using System.Xml.Linq;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Pro.Maui.Svg;

public class Svg : SKCanvasView
{
    private SKPicture _picture;
    internal static Assembly EntryAssembly { get; set; }
    private string _assemblyName;

    public Svg()
    {
        if (EntryAssembly == null)
        {
            throw new Exception("Before using Svg control, you must call builder.UseSvg<App>() inside your MauiProgram.cs");
        }
        _assemblyName = EntryAssembly.GetName().Name;
        PaintSurface += OnCanvasViewPaintSurface;
    }

    ~Svg()
    {
        PaintSurface -= OnCanvasViewPaintSurface;
    }

    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source), typeof(string), typeof(Svg), propertyChanged: OnSourcePropertyChanged);

    public static readonly BindableProperty LayersProperty = BindableProperty.Create(
        nameof(Layers), typeof(List<SvgLayer>), typeof(Svg), new List<SvgLayer>(), BindingMode.OneWayToSource);

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(Svg), null, propertyChanged: OnCommandPropertyChanged);

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(Svg), null);

    public static readonly BindableProperty ColorMappingProperty =
        BindableProperty.Create(nameof(ColorMapping), typeof(string), typeof(Svg), null, BindingMode.OneWay, propertyChanged: OnColorMappingPropertyChanged);

    public string Source
    {
        get => (string)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public List<SvgLayer> Layers
    {
        get => (List<SvgLayer>)GetValue(LayersProperty);
        set => SetValue(LayersProperty, value);
    }

    public string? ColorMapping
    {
        get => (string?)GetValue(ColorMappingProperty);
        set => SetValue(ColorMappingProperty, value);
    }

    public ICommand Command
    {
        get => (ICommand)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    protected override void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == HeightProperty.PropertyName ||
            propertyName == WidthProperty.PropertyName ||
            propertyName == HeightRequestProperty.PropertyName ||
            propertyName == WidthRequestProperty.PropertyName ||
            propertyName == VerticalOptionsProperty.PropertyName ||
            propertyName == HorizontalOptionsProperty.PropertyName)
        {
            CreateLayers();
            LoadPicture();
            InvalidateSurface();
        }
    }

    private static void OnSourcePropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not Svg control || newValue == null) return;

        control.CreateLayers();
        control.LoadPicture();
        control.InvalidateSurface();
    }

    private static void OnColorMappingPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not Svg control || newValue == null) return;

        control.LoadPicture();
        control.InvalidateSurface();
    }

    private void OnLayerVisibilityChanged()
    {
        LoadPicture();
        InvalidateSurface();
    }

    private static void OnCommandPropertyChanged(BindableObject bindable, object oldvalue, object newvalue)
    {
        if (bindable is not Svg svg || newvalue is not ICommand command) return;

        svg.GestureRecognizers.Clear();
        svg.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = command,
            CommandParameter = svg.CommandParameter
        });
    }

    void OnCanvasViewPaintSurface(object sender, SKPaintSurfaceEventArgs args)
    {
        var canvas = args.Surface.Canvas;
        var info = args.Info;
        canvas.Clear();

        canvas.Save();
        canvas.Translate(info.Width / 2f, info.Height / 2f);

        var bounds = _picture.CullRect;
        var maxHeight = Math.Max(info.Height, bounds.Height);
        var minHeight = Math.Min(info.Height, bounds.Height);
        var minWidth = Math.Min(info.Width, bounds.Width);
        var maxWidth = Math.Max(info.Width, bounds.Width);

        var ratio = bounds.Width >= bounds.Height
             ? info.Width > bounds.Width ? maxWidth / minWidth : minWidth / maxWidth
             : info.Height > bounds.Height ? maxHeight / minHeight : minHeight / maxHeight;

        canvas.Scale(ratio);
        canvas.Translate(-bounds.MidX, -bounds.MidY);
        canvas.DrawPicture(_picture);
        canvas.Restore();
    }

    private void CreateLayers()
    {
        if (Source.EndsWith(".png"))
        {
            throw new Exception($"PNG files are not supported by SVG control. Use .svg instead");
        }

        Layers.Clear();
        var path = $"{_assemblyName}.{Source}";
        using (var stream = EntryAssembly!.GetManifestResourceStream(path))
        {
            if (stream == null)
            {
                throw new Exception($"Error rendering the SVG icon '{path}'. Make sure it's build action is set to 'Embedded resource' and it is present at the specified path");
            }

            using (var reader = XmlReader.Create(stream))
            {
                reader.MoveToContent();

                var root = (XElement)XNode.ReadFrom(reader);

                Layers = new List<SvgLayer>(root.Elements()
                    .Where(x => x.Name.LocalName == "g")
                    .Select(CreateSvgLayer));
            }
        }

        SvgLayer CreateSvgLayer(XElement layerElement)
        {
            var id = layerElement.Attribute("id")?.Value;
            var pathBounds = GetSkPath(layerElement);
            var display = layerElement.Attribute("display");
            var isVisible = display == null || display.Value != "none";

            return new SvgLayer(id, isVisible, OnLayerVisibilityChanged, pathBounds);
        }

        IEnumerable<SKPath> GetSkPath(XElement layerElement)
        {
            return layerElement.Elements()
                .Where(x => x.Name.LocalName == "path")
                .Select(x => SKPath.ParseSvgPathData(x.Attribute("d").Value));
        }
    }

    private void LoadPicture()
    {
        var path = $"{_assemblyName}.{Source}";

        var svg = new SkiaSharp.Extended.Svg.SKSvg();
        using (var stream = EntryAssembly.GetManifestResourceStream(path))
        {
            if (stream == null)
            {
                return;
            }

            using (var ms = ReplaceColors(stream))
            {
                if (Layers.Any())
                {
                    using var filteredStream = FilterLayers(ms);
                    svg.Load(filteredStream);
                }
                else
                {
                    svg.Load(ms);
                }
            }
        }

        _picture = svg.Picture;
    }

    private Stream ReplaceColors(Stream? inputStream)
    {
        if (inputStream == null || ColorMapping == null)
        {
            return inputStream!;
        }

        using var ms = new MemoryStream();
        inputStream!.CopyTo(ms);
        inputStream.Dispose();

        var data = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);

        foreach (var map in ColorMapping.Split(","))
        {
            var hexColors = map.Split("=");

            if (hexColors.Length != 2)
            {
                continue;
            }
            var from = hexColors[0].Trim();
            var to = hexColors[1].Trim();
            data = data.Replace(from, to);
        }

        return new MemoryStream(Encoding.UTF8.GetBytes(data));
    }

    private Stream FilterLayers(Stream stream)
    {
        using (var reader = XmlReader.Create(stream))
        {
            reader.MoveToContent();

            if (!(XNode.ReadFrom(reader) is XElement root)) return null;

            HideInvisibleLayers(root);
            ShowVisibleLayers(root);

            var ms = new MemoryStream();

            root.Save(ms);
            ms.Position = 0;
            return ms;
        }

        void HideInvisibleLayers(XElement root)
        {
            var invisibleLayersIds = Layers.Where(x => !x.IsVisible).Select(x => x.Id);

            var layersToMakeInvisible = root.Elements()
                .Where(x => x.Name.LocalName == "g" &&
                            invisibleLayersIds.Contains(x.Attribute("id")?.Value))
                .ToList();

            foreach (var layer in layersToMakeInvisible)
            {
                layer.SetAttributeValue("display", "none");
            }
        }

        void ShowVisibleLayers(XElement root)
        {
            var visibleLayersIds = Layers.Where(x => x.IsVisible).Select(x => x.Id);

            var layersToMakeVisible = root.Elements()
                .Where(x => x.Name.LocalName == "g" &&
                            visibleLayersIds.Contains(x.Attribute("id")?.Value))
                .ToList();

            foreach (var layer in layersToMakeVisible)
            {
                layer.SetAttributeValue("display", "inline");
            }
        }
    }
}