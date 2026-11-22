using System.Text.Json;
using System.Text.Json.Nodes;

namespace Quill.Settings;

/// <summary>The value of a store before and after a change.</summary>
public sealed class StoreChangedEventArgs<T>(T previous, T current) : EventArgs
{
    public T Previous { get; } = previous;

    public T Current { get; } = current;
}

/// <summary>
/// A value kept in a versioned JSON file (14-settings.md). Reading never fails: a missing or broken file
/// gives the defaults, a value that cannot be read takes its default, and keys this version does not know
/// are kept as they are when the file is written again. Writing goes to a temporary file that then
/// replaces the old one, so a crash never leaves half a file. A file that cannot be written leaves the
/// value in memory for this session.
/// </summary>
public abstract class JsonStore<T>
    where T : class
{
    public const string VersionKey = "version";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private JsonObject _raw = [];
    private int _fileVersion;

    protected JsonStore(string path)
    {
        FilePath = path;
    }

    /// <summary>Raised after <see cref="Current"/> changed.</summary>
    public event EventHandler<StoreChangedEventArgs<T>>? Changed;

    public string FilePath { get; }

    public T Current { get; private set; } = null!;

    /// <summary>The error of the last write that failed; null after one that succeeded.</summary>
    public Exception? LastWriteError { get; private set; }

    /// <summary>The version this code writes.</summary>
    protected abstract int Version { get; }

    /// <summary>Changes the value; nothing happens (no write, no event) when the result equals the current one.</summary>
    public bool Update(Func<T, T> change)
    {
        var next = Normalize(change(Current));
        if (Equals(next, Current))
        {
            return false;
        }

        var previous = Current;
        Current = next;
        Write();
        Changed?.Invoke(this, new StoreChangedEventArgs<T>(previous, next));
        return true;
    }

    /// <summary>Reads the file; called once by the derived constructor.</summary>
    protected void Load()
    {
        _raw = ReadFile(FilePath);
        _fileVersion = _raw[VersionKey] is JsonValue version && version.TryGetValue(out int number) ? number : 0;
        Current = Normalize(Read(_raw));
    }

    protected abstract T Read(JsonObject json);

    protected abstract void Write(T value, JsonObject json);

    protected abstract T Normalize(T value);

    private void Write()
    {
        // Unknown keys stay; a file from a newer version keeps its version number.
        var json = (JsonObject)_raw.DeepClone();
        json[VersionKey] = Math.Max(Version, _fileVersion);
        Write(Current, json);
        string temporary = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
            File.WriteAllText(temporary, json.ToJsonString(WriteOptions));
            File.Move(temporary, FilePath, overwrite: true);
            _raw = json;
            LastWriteError = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LastWriteError = error;
        }
    }

    private static JsonObject ReadFile(string path)
    {
        try
        {
            return File.Exists(path) && JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) is JsonObject json
                ? json
                : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
