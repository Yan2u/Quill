namespace Quill.Editor.WinUI.Layout;

/// <summary>
/// Prefix sums with point updates in O(log n) (a Fenwick tree): the tops of the blocks of a long
/// document, kept current as single blocks change height without work proportional to the
/// document (09). Inserting or removing items rebuilds the tree in O(n) managed work, without
/// asking anything of the items.
/// </summary>
internal sealed class PrefixSums
{
    private double[] _tree = [0];
    private double[] _values = [];

    public int Count => _values.Length;

    public double Total { get; private set; }

    public double this[int index] => _values[index];

    public void Reset(IReadOnlyList<double> values)
    {
        _values = values as double[] ?? [.. values];
        _tree = new double[values.Count + 1];
        Total = 0;
        for (int i = 0; i < _values.Length; i++)
        {
            _tree[i + 1] += _values[i];
            Total += _values[i];
            int parent = i + 1 + ((i + 1) & -(i + 1));
            if (parent < _tree.Length)
            {
                _tree[parent] += _tree[i + 1];
            }
        }
    }

    /// <summary>Inserts values before <paramref name="index"/>.</summary>
    public void Insert(int index, IReadOnlyList<double> values)
    {
        var next = new double[_values.Length + values.Count];
        Array.Copy(_values, 0, next, 0, index);
        for (int i = 0; i < values.Count; i++)
        {
            next[index + i] = values[i];
        }

        Array.Copy(_values, index, next, index + values.Count, _values.Length - index);
        Reset(next);
    }

    public void RemoveRange(int index, int count)
    {
        var next = new double[_values.Length - count];
        Array.Copy(_values, 0, next, 0, index);
        Array.Copy(_values, index + count, next, index, _values.Length - index - count);
        Reset(next);
    }

    public void Set(int index, double value)
    {
        double delta = value - _values[index];
        if (delta == 0)
        {
            return;
        }

        _values[index] = value;
        Total += delta;
        for (int i = index + 1; i < _tree.Length; i += i & -i)
        {
            _tree[i] += delta;
        }
    }

    /// <summary>The sum of the values before <paramref name="index"/>.</summary>
    public double Before(int index)
    {
        double sum = 0;
        for (int i = Math.Clamp(index, 0, _values.Length); i > 0; i -= i & -i)
        {
            sum += _tree[i];
        }

        return sum;
    }

    /// <summary>The index whose span [Before(i), Before(i + 1)) holds <paramref name="offset"/>, clamped to the items.</summary>
    public int IndexAt(double offset)
    {
        if (_values.Length == 0)
        {
            return 0;
        }

        int index = 0;
        int step = 1;
        while (step * 2 < _tree.Length)
        {
            step *= 2;
        }

        for (; step > 0; step /= 2)
        {
            int next = index + step;
            if (next < _tree.Length && _tree[next] <= offset)
            {
                index = next;
                offset -= _tree[next];
            }
        }

        return Math.Clamp(index, 0, _values.Length - 1);
    }
}
