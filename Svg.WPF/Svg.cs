using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Xml;
using System.Xml.Linq;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace Svg.Wpf;

public class Svg : SKElement
{
    private SKPicture _picture;
    private readonly Assembly _entryAssembly;
    private readonly string _assemblyName;

    public Svg()
    {
        _entryAssembly = Assembly.GetEntryAssembly()!;
        _assemblyName = _entryAssembly.GetName().Name!;
        PaintSurface += OnCanvasViewPaintSurface!;
    }

    ~Svg()
    {
        PaintSurface -= OnCanvasViewPaintSurface!;
    }

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(string), typeof(Svg), new PropertyMetadata(OnSourcePropertyChanged));

    public static readonly DependencyProperty LayersProperty = DependencyProperty.Register(
        nameof(Layers), typeof(List<SvgLayer>), typeof(Svg), new PropertyMetadata(new List<SvgLayer>()));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(Svg), new PropertyMetadata(OnCommandPropertyChanged));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(Svg), null);

    public static readonly DependencyProperty ColorMappingProperty = DependencyProperty.Register(
        nameof(ColorMapping), typeof(string), typeof(Svg), new PropertyMetadata(OnColorMappingPropertyChanged));

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

    //protected override void OnPropertyChanged([CallerMemberName] string propertyName = null)
    //{
    //    base.OnPropertyChanged(propertyName);

    //    if (propertyName == HeightProperty.Name ||
    //        propertyName == WidthProperty.Name ||
    //        propertyName == HeightProperty.Name ||
    //        propertyName == WidthProperty.Name ||
    //        propertyName == VerticalAlignmentProperty.Name ||
    //        propertyName == HorizontalAlignmentProperty.Name)
    //        //InvalidateSurface();
    //}

    private static void OnSourcePropertyChanged(DependencyObject bindable, DependencyPropertyChangedEventArgs e)
    {
        if (bindable is not Svg control || e.NewValue == null) return;

        control.CreateLayers();
        control.LoadPicture();
        //control.InvalidateSurface();
    }

    private static void OnColorMappingPropertyChanged(DependencyObject bindable, DependencyPropertyChangedEventArgs e)
    {
        if (bindable is not Svg control || e.NewValue == null) return;

        control.LoadPicture();
        control.InvalidateVisual();
        //control.InvalidateSurface();
    }

    private void OnLayerVisibilityChanged()
    {
        LoadPicture();
        //InvalidateSurface();
    }

    private static void OnCommandPropertyChanged(DependencyObject bindable, DependencyPropertyChangedEventArgs e)
    {
        if (bindable is not Svg svg || e.NewValue is not ICommand command) return;

        //svg.GestureRecognizers.Clear();
        //svg.GestureRecognizers.Add(new TapGestureRecognizer
        //{
        //    Command = command,
        //    CommandParameter = svg.CommandParameter
        //});
    }

    void OnCanvasViewPaintSurface(object sender, SKPaintSurfaceEventArgs args)
    {
        if (_picture == null) return;

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

        using (var stream = _entryAssembly!.GetManifestResourceStream(path))
        {
            if (stream == null)
            {
                throw new Exception($"Error rendering the SVG icon '{path}'. Make sure it's build action is set to 'Embedded resource' and it is present at the specified path");
            }

            using (var reader = XmlReader.Create(stream))
            {
                reader.MoveToContent();

                var root = (XElement)XNode.ReadFrom(reader);

                var layers = new List<SvgLayer>(root.Elements()
                    .Where(x => x.Name.LocalName == "g")
                    .Select(CreateSvgLayer));

                try
                {
                    Layers = layers;
                }
                catch (Exception ex)
                {
                    throw ex;
                }
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
        using (var stream = _entryAssembly.GetManifestResourceStream(path))
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
            data = data.Replace(from, to, StringComparison.InvariantCultureIgnoreCase);
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