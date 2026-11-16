namespace LMTrainingDataStudio2.Services.Datasets;

internal sealed record JsonlRawLine(
    long LineNumber,
    byte[]? Bytes,
    long ByteLength,
    bool TooLong);

/// <summary>
/// Byte-level JSONL line scanner. Lines longer than the configured cap are reported
/// as <see cref="JsonlRawLine.TooLong"/> without buffering the whole line, so
/// pathological input cannot exhaust memory.
/// </summary>
internal sealed class JsonlRawLineReader : IAsyncDisposable
{
    private readonly FileStream _stream;
    private readonly int _maxLineBytes;
    private readonly byte[] _readBuffer = new byte[65536];
    private readonly MemoryStream _lineBuffer = new();
    private int _offset;
    private int _length;
    private long _lineNumber;
    private long _byteLength;
    private bool _tooLong;
    private bool _lineStarted;
    private bool _eof;
    private bool _firstLine = true;

    public JsonlRawLineReader(string path, int maxLineBytes)
    {
        _stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        _maxLineBytes = maxLineBytes;
        Length = _stream.Length;
    }

    public long Length { get; }

    public long BytesConsumed { get; private set; }

    public async Task<JsonlRawLine?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_offset == _length && !_eof)
            {
                _length = await _stream.ReadAsync(_readBuffer, cancellationToken);
                _offset = 0;
                if (_length == 0)
                {
                    _eof = true;
                }
            }

            if (_eof && _offset == _length)
            {
                if (!_lineStarted && _lineBuffer.Length == 0)
                {
                    return null;
                }

                return CompleteLine();
            }

            var newline = Array.IndexOf(_readBuffer, (byte)'\n', _offset, _length - _offset);
            if (newline >= 0)
            {
                AppendSegment(_offset, newline - _offset);
                BytesConsumed += newline - _offset + 1;
                _offset = newline + 1;
                return CompleteLine();
            }

            AppendSegment(_offset, _length - _offset);
            BytesConsumed += _length - _offset;
            _offset = _length;
        }
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();

    private void AppendSegment(int offset, int count)
    {
        if (count <= 0)
        {
            return;
        }

        _lineStarted = true;
        _byteLength += count;

        if (_tooLong)
        {
            return;
        }

        if (_lineBuffer.Length + count > _maxLineBytes)
        {
            _tooLong = true;
            _lineBuffer.SetLength(0);
            return;
        }

        _lineBuffer.Write(_readBuffer, offset, count);
    }

    private JsonlRawLine CompleteLine()
    {
        _lineNumber++;
        var byteLength = _byteLength;

        byte[]? bytes = null;
        if (!_tooLong)
        {
            bytes = _lineBuffer.ToArray();
            var length = bytes.Length;
            if (length > 0 && bytes[length - 1] == (byte)'\r')
            {
                Array.Resize(ref bytes, length - 1);
            }

            if (_firstLine && bytes.Length >= 3 &&
                bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                bytes = bytes[3..];
            }
        }

        _firstLine = false;
        _lineBuffer.SetLength(0);
        _byteLength = 0;
        _tooLong = false;
        _lineStarted = false;

        return new JsonlRawLine(_lineNumber, bytes, byteLength, bytes is null);
    }
}
