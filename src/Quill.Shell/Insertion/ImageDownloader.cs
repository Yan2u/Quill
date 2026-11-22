using System.Net.Http.Headers;
using Quill.Editor.WinUI.Localization;
using Quill.Settings.Insertion;

namespace Quill.Shell.Insertion;

/// <summary>Downloads online images for the image rule (14-settings.md): at most 50 MB, within 30 seconds〔默认〕.</summary>
public static class ImageDownloader
{
    private const long MaxBytes = 50L * 1024 * 1024;

    private static readonly HttpClient Client = CreateClient();

    public static async Task<DownloadedImage> DownloadAsync(Uri url, CancellationToken cancellation)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxBytes)
        {
            throw new IOException(UiStrings.Format("insert.imageTooLarge", url, MaxBytes / 1024 / 1024));
        }

        var data = await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
        return new DownloadedImage(data, response.Content.Headers.ContentType?.MediaType);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = MaxBytes };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Quill", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        return client;
    }
}
