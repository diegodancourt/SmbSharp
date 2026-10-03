namespace SmbSharp.Business.SmbClient
{
    internal sealed class TemporaryDirectoryStream : Stream
    {
        private readonly Stream _inner;
        private readonly string _directory;
        private bool _disposed;

        public TemporaryDirectoryStream(Stream inner, string directory)
        {
            _inner = inner;
            _directory = directory;
        }

        internal string TemporaryDirectory => _directory;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;
                try
                {
                    _inner.Dispose();
                }
                finally
                {
                    if (Directory.Exists(_directory))
                        Directory.Delete(_directory, recursive: true);
                }
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                try
                {
                    await _inner.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    if (Directory.Exists(_directory))
                        Directory.Delete(_directory, recursive: true);
                }
            }
            GC.SuppressFinalize(this);
        }
    }
}
