namespace Quill.Playground.VariableXaml.Model;

/// <summary>
/// A property element such as <c>&lt;Grid.RowDefinitions&gt;</c> or <c>&lt;Border.Background&gt;</c>:
/// a member whose value is written as child elements instead of an attribute string.
/// </summary>
public sealed class VPropertyElement
{
    internal VPropertyElement(XamlMemberName member, VNode[] values)
    {
        Member = member;
        ValuesArray = values;

        ulong hash = NodeHash.Combine(NodeHash.Seed, member);
        foreach (VNode value in values)
        {
            hash = NodeHash.Combine(hash, value.Hash);
        }

        Hash = hash;
    }

    public XamlMemberName Member { get; }

    public IReadOnlyList<VNode> Values => ValuesArray;

    public ulong Hash { get; }

    internal VNode[] ValuesArray { get; }

    public static VPropertyElement Create(XamlMemberName member, IEnumerable<VNode> values) => new(member, values.ToArray());

    internal VPropertyElement WithValue(int index, VNode value)
    {
        var values = (VNode[])ValuesArray.Clone();
        values[index] = value;
        return new VPropertyElement(Member, values);
    }
}
