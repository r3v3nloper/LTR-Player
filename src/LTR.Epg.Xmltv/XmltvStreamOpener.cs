using System.IO.Compression;

namespace LTR.Epg.Xmltv;

/// <summary>
/// Presents a guide download as plain XML, whether or not it arrived compressed.
/// </summary>
/// <remarks>
/// <para>
/// Guides are published both ways and the address does not reliably say which: <c>xmltv.php</c> serves
/// gzip from some panels and XML from others, and an <c>.xml.gz</c> URL is sometimes served already
/// decompressed by an intermediary. Deciding from the first two bytes is the only method that is right
/// in all four combinations.
/// </para>
/// <para>
/// The decision has to be made without consuming those bytes, and a network stream cannot be rewound,
/// so they are read and then replayed ahead of the rest.
/// </para>
/// </remarks>
internal static class XmltvStreamOpener
{
    private static readonly byte[] GzipMagicNumber = [0x1F, 0x8B];

    public static async Task<Stream> OpenAsync(Stream source, CancellationToken cancellationToken)
    {
        var header = new byte[GzipMagicNumber.Length];
        var read = await source.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);

        // Leaves the caller's stream open on disposal: whoever opened it — a provider holding an HTTP
        // response — owns its lifetime and closes it when the response is done with.
        var replayed = new ReplayStream(header.AsMemory(0, read), source);

        return read == GzipMagicNumber.Length && header.AsSpan().SequenceEqual(GzipMagicNumber)
            ? new GZipStream(replayed, CompressionMode.Decompress, leaveOpen: false)
            : replayed;
    }

    /// <summary>
    /// A forward-only stream that yields a prefix already read from another stream, then that stream.
    /// </summary>
    /// <remarks>
    /// Written by hand because the base class library has no pushback stream: <c>BufferedStream</c>
    /// buffers ahead but cannot return bytes already handed out, and seeking is not available on a
    /// network stream (§2.14).
    /// </remarks>
    private sealed class ReplayStream : Stream
    {
        private readonly ReadOnlyMemory<byte> _prefix;
        private readonly Stream _inner;
        private int _prefixPosition;

        public ReplayStream(ReadOnlyMemory<byte> prefix, Stream inner)
        {
            _prefix = prefix;
            _inner = inner;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            var fromPrefix = TakeFromPrefix(buffer);
            return fromPrefix > 0 ? fromPrefix : _inner.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var fromPrefix = TakeFromPrefix(buffer.Span);

            return fromPrefix > 0
                ? ValueTask.FromResult(fromPrefix)
                : _inner.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <summary>
        /// Serves as much of the pending prefix as fits, which may be less than was asked for — a
        /// short read is something every stream consumer already has to handle.
        /// </summary>
        private int TakeFromPrefix(Span<byte> buffer)
        {
            var remaining = _prefix.Length - _prefixPosition;

            if (remaining <= 0 || buffer.Length == 0)
            {
                return 0;
            }

            var count = Math.Min(remaining, buffer.Length);
            _prefix.Span.Slice(_prefixPosition, count).CopyTo(buffer);
            _prefixPosition += count;

            return count;
        }
    }
}
