using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.Core.Models;

public sealed class StagedBatch
{
    private readonly List<BatchEntry> _entries = new();

    public int Count => _entries.Count;

    public bool IsEmpty => _entries.Count == 0;

    public IReadOnlyList<BatchEntry> Entries => _entries;

    public BatchEntry this[int index] => _entries[index];

    public void Replace(IEnumerable<BatchEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _entries.Clear();
        _entries.AddRange(entries);
    }

    public void Clear()
    {
        _entries.Clear();
    }

    public bool MoveUp(int index)
    {
        if (index < 1 || index >= _entries.Count)
            return false;

        (_entries[index], _entries[index - 1]) = (_entries[index - 1], _entries[index]);
        return true;
    }

    public bool MoveDown(int index)
    {
        if (index < 0 || index >= _entries.Count - 1)
            return false;

        (_entries[index], _entries[index + 1]) = (_entries[index + 1], _entries[index]);
        return true;
    }

    public int RemoveAt(int index)
    {
        if (index < 0 || index >= _entries.Count)
            return -1;

        _entries.RemoveAt(index);

        if (_entries.Count == 0)
            return -1;

        return index < _entries.Count ? index : index - 1;
    }

    public void UpdateEntry(int index, BatchEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (index < 0 || index >= _entries.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        _entries[index] = entry;
    }
}