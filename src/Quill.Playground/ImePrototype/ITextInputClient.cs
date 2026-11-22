namespace Quill.Playground.ImePrototype;

/// <summary>How the text service formats a segment of the composition (CoreTextFormatUpdatingReason).</summary>
internal enum CompositionSegmentKind
{
    None,
    Unconverted,
    Converted,
    TargetUnconverted,
    TargetConverted,
}

/// <summary>A formatted segment of the composition, in offsets of the active block.</summary>
internal readonly record struct CompositionSegment(int Start, int End, CompositionSegmentKind Kind, bool ThickUnderline);

/// <summary>
/// The editor side of text input (07: <c>ITextInputClient</c>), reduced to what the Q6 prototype
/// needs. The text service only ever sees the source of the block holding the head (07, rule 1).
/// </summary>
internal interface ITextInputClient
{
    /// <summary>Increases with every text change.</summary>
    long Revision { get; }

    /// <summary>The identity of the block exposed to the text service.</summary>
    int ActiveBlockId { get; }

    /// <summary>The source of the active block (UTF-16).</summary>
    string ActiveText { get; }

    /// <summary>The selection inside the active block.</summary>
    (int Anchor, int Head) Selection { get; }

    bool IsComposing { get; }

    /// <summary>
    /// Applies a text update from the text service: replaces <paramref name="start"/>..<paramref name="end"/>
    /// of the active block and selects the requested range. The result may differ from the plain
    /// replacement (input rules); the controller then resynchronizes after the callback (07, rule 4).
    /// </summary>
    bool ReplaceText(int start, int end, string text, int selectionStart, int selectionEnd);

    /// <summary>
    /// Inserts typed text that did not come through the text service (<c>CharacterReceived</c>:
    /// keys the input method passes through, such as Microsoft Pinyin in English mode), replacing
    /// the selection.
    /// </summary>
    void InsertText(string text);

    void SetSelection(int anchor, int head);

    void BeginComposition();

    /// <summary>Ends the composition; its steps become one undo item, or none when it changed nothing (03).</summary>
    void EndComposition(bool canceled);

    /// <summary>Sets the format of a composition segment, shown by the projection (07, rule 7).</summary>
    void SetCompositionSegment(CompositionSegment segment);
}
