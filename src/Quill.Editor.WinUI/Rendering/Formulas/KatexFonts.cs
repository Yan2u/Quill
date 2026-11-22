using Microsoft.Graphics.Canvas.Text;

namespace Quill.Editor.WinUI.Rendering.Formulas;

/// <summary>
/// The KaTeX fonts RaTeX lays out with (SIL OFL 1.1, bundled under <c>Assets/KaTeX</c>), loaded
/// once per font ID ("Main-Regular" → <c>KaTeX_Main-Regular.ttf</c>). An ID without a bundled file
/// (RaTeX's CJK and Unicode fallbacks) has no face; the painter draws those with the text font.
/// </summary>
internal static class KatexFonts
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, CanvasFontFace?> Faces = new(StringComparer.Ordinal);

    public static string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "KaTeX");

    public static CanvasFontFace? Face(string fontId)
    {
        lock (Gate)
        {
            if (!Faces.TryGetValue(fontId, out var face))
            {
                face = Load(fontId);
                Faces[fontId] = face;
            }

            return face;
        }
    }

    private static CanvasFontFace? Load(string fontId)
    {
        string path = Path.Combine(Directory, $"KaTeX_{fontId}.ttf");
        if (!File.Exists(path))
        {
            return null;
        }

        using var set = new CanvasFontSet(new Uri(path));
        return set.Fonts.Count > 0 ? set.Fonts[0] : null;
    }
}
