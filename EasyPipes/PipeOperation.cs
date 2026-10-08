using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EasyPipes
{
    internal static class PipeOperation
    {
        // Closing the exact stream also aborts pending I/O on runtimes where
        // cancellation alone does not interrupt a pipe operation.
        internal static async Task<T> RunAsync<T>(Stream stream, Func<Task<T>> action,
            int timeoutMillis, CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutMillis);
            using var registration = timeout.Token.Register(() => stream.Dispose());
            try
            {
                var result = await action().ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                return result;
            }
            catch (Exception) when (timeout.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("Pipe operation timed out; connection closed.");
            }
        }
    }
}
