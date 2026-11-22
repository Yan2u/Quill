using System.Runtime.InteropServices;
using System.Text;
using Quill.Editor.WinUI.Localization;

namespace Quill.Editor.WinUI.Rendering.Diagrams;

/// <summary>
/// The C ABI of <c>quill_native.dll</c> (native/quill-native): Mermaid source to simplified SVG, text
/// already turned into paths. Thread-safe.
/// </summary>
internal static partial class MermaidNative
{
    private const string Library = "quill_native.dll";

    private const int StatusOk = 0;
    private const int StatusError = 1;

    /// <summary>The SVG and its size in pixels, or the renderer's message when the source does not render.</summary>
    public static (string? Svg, float Width, float Height, string? Error) Render(string source, bool dark)
    {
        byte[] input = Encoding.UTF8.GetBytes(source);
        nint output;
        nuint length;
        float width, height;
        int status;
        unsafe
        {
            fixed (byte* bytes = input)
            {
                status = quill_mermaid_render(bytes, (nuint)input.Length, dark, &output, &length, &width, &height);
            }
        }

        try
        {
            string? text = output == 0 ? null : Marshal.PtrToStringUTF8(output, checked((int)length));
            return status switch
            {
                StatusOk when text is not null => (text, width, height, null),
                StatusError => (null, 0, 0, text ?? UiStrings.Get("editor.diagramFailed")),
                _ => (null, 0, 0, UiStrings.Format("editor.diagramStatusFailed", status)),
            };
        }
        finally
        {
            if (output != 0)
            {
                quill_buffer_free(output, length);
            }
        }
    }

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static unsafe partial int quill_mermaid_render(byte* input, nuint inputLength, [MarshalAs(UnmanagedType.U1)] bool dark, nint* output, nuint* outputLength, float* width, float* height);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void quill_buffer_free(nint data, nuint length);
}
