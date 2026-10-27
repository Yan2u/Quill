using Microsoft.UI.Xaml.Markup;

namespace Quill.Playground.SelfTest;

/// <summary>
/// Isolated reproductions of native crashes found by the fuzz tests. Each case runs only when
/// selected with <c>--only "Crash repro"</c>, because a positive result terminates the process.
/// </summary>
internal static class CrashRepro
{
    private const string Xmlns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";

    public static Task RunAsync(SelfTestContext context)
    {
        string[] cases =
        [
            $"<Border {Xmlns} Background=\"{{ThemeResource CardBackgroundFillColorDefaultBrush}}\"/>",
            $"<Border {Xmlns} Background=\"{{The/xmResource CardBackgroundFillColorDefaultBrush}}\"/>",
            $"<Border {Xmlns} Background=\"{{T=he/xmResource CcardBackgrundFillColo3r&gt;DefaultBrush}}\"/>",
        ];

        foreach (string markup in cases)
        {
            context.Trace($"XamlReader.Load: {markup}");
            try
            {
                XamlReader.Load(markup);
                context.Info($"loaded: {markup}");
            }
            catch (Exception exception)
            {
                context.Info($"threw {exception.GetType().Name}: {markup}");
            }

            context.Trace("  survived");
        }

        return Task.CompletedTask;
    }
}
