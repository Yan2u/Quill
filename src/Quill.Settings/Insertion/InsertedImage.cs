namespace Quill.Settings.Insertion;

/// <summary>An image downloaded for insertion: its bytes and the media type the server gave, if any.</summary>
public sealed record DownloadedImage(byte[] Data, string? MediaType);

/// <summary>An image ready to insert: its alternative text and its destination, not yet written as Markdown.</summary>
public sealed record InsertedImage(string AltText, string Destination);
