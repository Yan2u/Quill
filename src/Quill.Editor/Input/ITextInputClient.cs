using Quill.Editor.Documents;

namespace Quill.Editor.Input;

/// <summary>
/// The editor side of text input (07: core interface): what a text service (CoreTextEditContext in
/// the view, a script in tests) reads and writes. The service sees the source of the leaf holding
/// the head only (07, rule 1), an empty text in a virtual paragraph (rule 10). Offsets are UTF-16
/// offsets in that source.
/// </summary>
public interface ITextInputClient
{
    /// <summary>Changes whenever the active block or its text changed (not for selection or focus changes).</summary>
    long Revision { get; }

    /// <summary>The leaf holding the head; null at a block boundary (a virtual paragraph).</summary>
    BlockId? ActiveBlock { get; }

    /// <summary>The source of the active leaf; empty at a boundary.</summary>
    string ActiveText { get; }

    /// <summary>The selection inside the active leaf: the anchor collapses to the head when it lies elsewhere.</summary>
    (int Anchor, int Head) Selection { get; }

    bool IsComposing { get; }

    /// <summary>
    /// A text update from the text service: replaces <paramref name="start"/>..<paramref name="end"/>
    /// of the active text and selects the requested range. Inside a composition it is a composition
    /// step; outside, an ordinary edit (input rules apply), and a line break splits the block.
    /// </summary>
    bool ReplaceText(int start, int end, string text, int selectionStart, int selectionEnd);

    /// <summary>Typed text that did not come through the text service (Q66: the character path).</summary>
    void InsertText(string text);

    void SetSelection(int anchor, int head);

    void BeginComposition();

    /// <summary>
    /// Ends the composition: one undo item, or a rollback of everything it did when its text is
    /// empty, which is how input methods cancel (Esc in Microsoft Pinyin reports no cancel flag, Q66).
    /// </summary>
    void EndComposition();
}
