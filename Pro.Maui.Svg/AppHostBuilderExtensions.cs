using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Pro.Maui.Svg;

public static class AppHostBuilderExtensions
{
    public static MauiAppBuilder UseSvg<T>(this MauiAppBuilder builder)
    {
        Svg.EntryAssembly = typeof(T).Assembly;
        return builder.UseSkiaSharp();
    }
}