namespace LMTrainingDataStudio2.Services.Datasets;

/// <summary>
/// One row inserted by the user. <see cref="AnchorRow"/> is the zero-based source row
/// the inserted row is placed before; <c>TotalSourceRows</c> appends at the end.
/// </summary>
public sealed record DatasetInsertedRow(long Id, long AnchorRow, string Json);

/// <summary>
/// Immutable copy of a <see cref="DatasetEditSession"/> used by undo/redo.
/// </summary>
public sealed record DatasetEditSnapshot(
    IReadOnlyDictionary<long, string> Replacements,
    IReadOnlySet<long> Deletions,
    IReadOnlyDictionary<long, IReadOnlyList<DatasetInsertedRow>> Insertions,
    long NextInsertId)
{
    public static DatasetEditSnapshot Empty { get; } = new(
        new Dictionary<long, string>(),
        new HashSet<long>(),
        new Dictionary<long, IReadOnlyList<DatasetInsertedRow>>(),
        1);

    public bool HasChanges =>
        Replacements.Count > 0 || Deletions.Count > 0 || Insertions.Count > 0;

    public int InsertedRows => Insertions.Values.Sum(rows => rows.Count);
}

/// <summary>
/// Row-level edit plan over an existing dataset, with bounded undo/redo history.
/// Edits are keyed by zero-based source row, so they survive page navigation.
/// </summary>
public sealed class DatasetEditSession
{
    public const int MaxHistory = 200;

    private readonly List<DatasetEditSnapshot> _undo = new();
    private readonly List<DatasetEditSnapshot> _redo = new();
    private Dictionary<long, string> _replacements = new();
    private HashSet<long> _deletions = new();
    private Dictionary<long, List<DatasetInsertedRow>> _insertions = new();
    private long _nextInsertId = 1;

    public DatasetEditSession(long totalSourceRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalSourceRows);
        TotalSourceRows = totalSourceRows;
    }

    public long TotalSourceRows { get; }

    public event Action? Changed;

    public bool HasChanges =>
        _replacements.Count > 0 || _deletions.Count > 0 || _insertions.Count > 0;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int ReplacedRows => _replacements.Keys.Count(row => !_deletions.Contains(row));

    public int DeletedRows => _deletions.Count;

    public int InsertedRows => _insertions.Values.Sum(rows => rows.Count);

    /// <summary>Row count after pending edits are written.</summary>
    public long EffectiveRows => TotalSourceRows - DeletedRows + InsertedRows;

    public long NetRowDelta => InsertedRows - DeletedRows;

    public string? GetReplacement(long sourceRow) =>
        _replacements.TryGetValue(sourceRow, out var json) ? json : null;

    public bool IsDeleted(long sourceRow) => _deletions.Contains(sourceRow);

    public long? FindInsertedRowAnchor(long insertId)
    {
        foreach (var pair in _insertions)
        {
            if (pair.Value.Any(row => row.Id == insertId))
            {
                return pair.Key;
            }
        }

        return null;
    }

    public IReadOnlyList<DatasetInsertedRow> GetInsertions(long anchorRow) =>
        _insertions.TryGetValue(anchorRow, out var rows)
            ? rows
            : Array.Empty<DatasetInsertedRow>();

    public IEnumerable<DatasetInsertedRow> EnumerateInsertions()
    {
        foreach (var pair in _insertions.OrderBy(pair => pair.Key))
        {
            foreach (var row in pair.Value)
            {
                yield return row;
            }
        }
    }

    public void SetRowJson(long sourceRow, string json)
    {
        ValidateSourceRow(sourceRow, nameof(sourceRow));
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (_deletions.Contains(sourceRow))
        {
            throw new InvalidOperationException(
                $"Row {sourceRow + 1} is deleted; restore it before editing.");
        }

        Record();
        _replacements[sourceRow] = json;
        Changed?.Invoke();
    }

    public void DeleteSourceRow(long sourceRow)
    {
        ValidateSourceRow(sourceRow, nameof(sourceRow));
        if (_deletions.Contains(sourceRow))
        {
            return;
        }

        Record();
        _deletions.Add(sourceRow);
        Changed?.Invoke();
    }

    public void RestoreSourceRow(long sourceRow)
    {
        ValidateSourceRow(sourceRow, nameof(sourceRow));
        if (!_deletions.Contains(sourceRow))
        {
            return;
        }

        Record();
        _deletions.Remove(sourceRow);
        Changed?.Invoke();
    }

    /// <summary>Inserts a row before <paramref name="anchorRow"/> and returns its id.</summary>
    public long InsertBefore(long anchorRow, string json)
    {
        ValidateAnchor(anchorRow, nameof(anchorRow));
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        Record();
        var row = new DatasetInsertedRow(_nextInsertId++, anchorRow, json);
        if (!_insertions.TryGetValue(anchorRow, out var rows))
        {
            rows = new List<DatasetInsertedRow>();
            _insertions[anchorRow] = rows;
        }

        rows.Add(row);
        Changed?.Invoke();
        return row.Id;
    }

    public void UpdateInsertedRow(long insertId, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var anchor = FindInsertedRowAnchor(insertId)
                     ?? throw new InvalidOperationException($"Inserted row {insertId} no longer exists.");
        var rows = _insertions[anchor];
        var index = rows.FindIndex(row => row.Id == insertId);

        Record();
        rows[index] = rows[index] with { Json = json };
        Changed?.Invoke();
    }

    public void DeleteInsertedRow(long insertId)
    {
        var anchor = FindInsertedRowAnchor(insertId) ?? throw new InvalidOperationException(
            $"Inserted row {insertId} no longer exists.");
        var rows = _insertions[anchor];
        var index = rows.FindIndex(row => row.Id == insertId);

        Record();
        rows.RemoveAt(index);
        if (rows.Count == 0)
        {
            _insertions.Remove(anchor);
        }

        Changed?.Invoke();
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        _redo.Add(Snapshot());
        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Restore(previous);
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        RecordSnapshot(Snapshot());
        var next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Restore(next);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Drops every pending edit and the whole history.</summary>
    public void Clear()
    {
        if (!HasChanges && _undo.Count == 0)
        {
            return;
        }

        _undo.Clear();
        _redo.Clear();
        _replacements = new Dictionary<long, string>();
        _deletions = new HashSet<long>();
        _insertions = new Dictionary<long, List<DatasetInsertedRow>>();
        _nextInsertId = 1;
        Changed?.Invoke();
    }

    public DatasetEditSnapshot Snapshot() => new(
        new Dictionary<long, string>(_replacements),
        new HashSet<long>(_deletions),
        _insertions.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<DatasetInsertedRow>)pair.Value.ToArray()),
        _nextInsertId);

    private void Record()
    {
        RecordSnapshot(Snapshot());
        _redo.Clear();
    }

    private void RecordSnapshot(DatasetEditSnapshot snapshot)
    {
        _undo.Add(snapshot);
        if (_undo.Count > MaxHistory)
        {
            _undo.RemoveAt(0);
        }
    }

    private void Restore(DatasetEditSnapshot snapshot)
    {
        _replacements = new Dictionary<long, string>(snapshot.Replacements);
        _deletions = new HashSet<long>(snapshot.Deletions);
        _insertions = snapshot.Insertions.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToList());
        _nextInsertId = snapshot.NextInsertId;
    }

    private void ValidateSourceRow(long row, string parameterName)
    {
        if (row < 0 || row >= TotalSourceRows)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, row, $"Source row must be between 0 and {TotalSourceRows - 1}.");
        }
    }

    private void ValidateAnchor(long anchorRow, string parameterName)
    {
        if (anchorRow < 0 || anchorRow > TotalSourceRows)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, anchorRow, $"Insert anchor must be between 0 and {TotalSourceRows}.");
        }
    }
}
