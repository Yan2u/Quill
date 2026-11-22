using Microsoft.Graphics.Canvas;
using Windows.Storage.Streams;

namespace Quill.Editor.WinUI.Rendering;

/// <summary>
/// Online images of the document: downloaded in the background (at most 50 MB, within 30 seconds〔默认〕) and
/// decoded with Win2D, which reads any format Windows has a codec for (WebP included). A failure gives null:
/// the placeholder stays.
/// </summary>
internal static class RemoteImages
{
    private const long MaxBytes = 50L * 1024 * 1024;

    private static readonly HttpClient Client = CreateClient();

    /// <summary>The URL of an online image source (http or https), or null for a local one.</summary>
    public static Uri? UrlOf(string source)
    {
        string trimmed = source.Trim().Trim('<', '>');
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" ? url : null;
    }

    public static async Task<object?> LoadAsync(Uri url, CancellationToken cancellation)
    {
        try
        {
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
            {
                return null;
            }

            byte[] data = await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(data);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            return await CanvasBitmap.LoadAsync(CanvasDevice.GetSharedDevice(), stream);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException
            or System.Runtime.InteropServices.COMException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = MaxBytes };
        client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Quill", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("image/*"));
        return client;
    }
}
