using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Wander.Protocol;

namespace Wander.Tests
{
    /// <summary>A temp directory that cleans up after itself.</summary>
    public sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wander-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string WriteFile(string relativePath, string content)
        {
            var full = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
            return full;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort in temp */ }
        }
    }

    public static class FakeGrpc
    {
        /// <summary>A download-call factory streaming the given bytes as one chunk, as a real peer would.</summary>
        public static Func<AsyncServerStreamingCall<FileChunk>> DownloadOf(byte[] content)
        {
            return () =>
            {
                var chunks = new List<FileChunk>
                {
                    new() { Data = ByteString.CopyFrom(content), Offset = 0, IsFinal = true }
                };
                return new AsyncServerStreamingCall<FileChunk>(
                    new ListStreamReader<FileChunk>(chunks),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { });
            };
        }

        private sealed class ListStreamReader<T> : IAsyncStreamReader<T>
        {
            private readonly IEnumerator<T> _enumerator;
            public ListStreamReader(IEnumerable<T> items) => _enumerator = items.GetEnumerator();
            public T Current => _enumerator.Current;
            public Task<bool> MoveNext(CancellationToken cancellationToken) => Task.FromResult(_enumerator.MoveNext());
        }
    }
}
