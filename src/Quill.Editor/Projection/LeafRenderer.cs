using System.Collections.Immutable;
using Quill.Editor.Documents;
using Quill.Editor.Inlines;

namespace Quill.Editor.Projection;

/// <summary>A rendered leaf: its displayed content and its position map.</summary>
internal sealed record RenderedLeaf(InlineContent Content, BlockPositionMap Map);

/// <summary>
/// Phase two of the projection of a leaf: applies the active nodes to the render model, producing the
/// runs for the block control and the position map (05-projection-and-view.md). Cheap; redone when the
/// active set of the leaf changes.
/// </summary>
internal static class LeafRenderer
{
    /// <param name="imageOnly">The leaf is a paragraph holding one image alone (<see cref="InlineContent.IsImageOnly"/>).</param>
    public static RenderedLeaf Render(BlockId block, LeafRenderModel model, IReadOnlySet<int> active, bool imageOnly = false)
    {
        var segments = ImmutableArray.CreateBuilder<Segment>(model.Pieces.Length);
        var runs = ImmutableArray.CreateBuilder<InlineRun>();
        var previews = ImmutableArray.CreateBuilder<InlinePreview>();
        int display = 0;
        int previewOwner = -1;
        int previewStart = 0;
        foreach (var piece in model.Pieces)
        {
            bool ownerActive = piece.Owner >= 0 && active.Contains(piece.Owner);
            if ((piece.Show == PieceShow.WhenActive && !ownerActive)
                || (piece.Show == PieceShow.WhenInactive && ownerActive)
                || (piece.OuterGate >= 0 && !active.Contains(piece.OuterGate)))
            {
                continue;
            }

            // An active inline formula is previewed below its source (10-elements.md).
            if (previewOwner >= 0 && piece.Owner != previewOwner)
            {
                previews.Add(new InlinePreview(new TextRange(previewStart, display), RunRole.Math, model.Pieces.First(p => p.Owner == previewOwner && p.Role == RunRole.Math).Value ?? string.Empty));
                previewOwner = -1;
            }

            if (ownerActive && piece.Style.HasFlag(InlineStyle.Math) && piece.Show == PieceShow.WhenActive && previewOwner < 0)
            {
                previewOwner = piece.Owner;
                previewStart = display;
            }

            string source = model.Source[piece.Source.Start..piece.Source.End];
            switch (piece.Kind)
            {
                case PieceKind.Text:
                    segments.Add(new Segment(piece.Source, new TextRange(display, display + source.Length), SegmentKind.Content, piece.Node));
                    AddRun(runs, new InlineRun(new TextRange(display, display + source.Length), source, RunRole.Text, piece.Style, ownerActive, piece.Value));
                    display += source.Length;
                    break;
                case PieceKind.Marker when ownerActive:
                    segments.Add(new Segment(piece.Source, new TextRange(display, display + source.Length), SegmentKind.Marker, piece.Node));
                    AddRun(runs, new InlineRun(new TextRange(display, display + source.Length), source, RunRole.Marker, piece.Style, true));
                    display += source.Length;
                    break;
                case PieceKind.MarkerBreak when ownerActive:
                case PieceKind.LineBreak:
                    segments.Add(new Segment(piece.Source, new TextRange(display, display + 1), SegmentKind.LineBreak, piece.Node));
                    runs.Add(new InlineRun(new TextRange(display, display + 1), "\n", RunRole.LineBreak, piece.Style, ownerActive));
                    display++;
                    break;
                case PieceKind.Atomic:
                    segments.Add(new Segment(piece.Source, new TextRange(display, display + 1), SegmentKind.Atomic, piece.Node));
                    runs.Add(new InlineRun(new TextRange(display, display + 1), piece.Text ?? string.Empty, piece.Role, piece.Style, ownerActive, piece.Value));
                    display++;
                    break;
                default:
                    // Hidden markers and the leading zero-width space.
                    segments.Add(new Segment(piece.Source, new TextRange(display, display), SegmentKind.Hidden, piece.Node));
                    break;
            }
        }

        if (previewOwner >= 0)
        {
            previews.Add(new InlinePreview(new TextRange(previewStart, display), RunRole.Math, model.Pieces.First(p => p.Owner == previewOwner && p.Role == RunRole.Math).Value ?? string.Empty));
        }

        var content = runs.Count == 0 && previews.Count == 0 ? InlineContent.Empty : new InlineContent(runs.ToImmutable(), previews.ToImmutable(), imageOnly);
        return new RenderedLeaf(content, new BlockPositionMap(block, segments.ToImmutable()));
    }

    /// <summary>Adds a text run, merging it into the previous one when nothing but the text differs.</summary>
    private static void AddRun(ImmutableArray<InlineRun>.Builder runs, InlineRun run)
    {
        if (runs.Count > 0)
        {
            var last = runs[^1];
            if (last.Role == run.Role && last.Style == run.Style && last.IsActive == run.IsActive && last.Value == run.Value && last.Display.End == run.Display.Start)
            {
                runs[^1] = last with { Display = new TextRange(last.Display.Start, run.Display.End), Text = last.Text + run.Text };
                return;
            }
        }

        runs.Add(run);
    }
}
