using System;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace EasyPipes
{
    public partial class Client
    {
        /// <summary>Sends one frame on a writable connection, with a bounded write.</summary>
        public async Task<bool> TrySendMessageAsync(string message, int timeoutMillis = 5000)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (timeoutMillis <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMillis));
            if (!IsStarted || !await sendGate.WaitAsync(timeoutMillis).ConfigureAwait(false)) return false;
            NamedPipeClientStream stream = null;
            try
            {
                if (!IsStarted || !IsStateReady()) return false;
                stream = PipeClient;
                if (stream == null || !stream.CanWrite) return false;
                await PipeOperation.RunAsync(stream,
                    () => StreamIO.WriteStringAsync(stream, message), timeoutMillis).ConfigureAwait(false);
                return true;
            }
            catch (Exception e)
            {
                FailConnection(stream);
                OnStateMessage(new StateMessageEventArgs($"{PipeName} > Send > ERROR: {e.Message}"));
                return false;
            }
            finally { sendGate.Release(); }
        }

        /// <summary>Connects if necessary before sending; does not replay failed writes.</summary>
        public async Task<bool> TryConnectSendMessageAsync(string message, int timeoutMillis = 5000)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (!IsStarted) return false;
            if (!IsStateReady() && !await TryCreateAndWaitForConnectionAsync().ConfigureAwait(false)) return false;
            return await TrySendMessageAsync(message, timeoutMillis).ConfigureAwait(false);
        }

        private void FailConnection(NamedPipeClientStream stream)
        {
            lock (objectLockPipeConnect)
            {
                if (stream != null && ReferenceEquals(PipeClient, stream))
                {
                    PipeClient = null;
                    IsErrors = true;
                }
            }
            stream?.Dispose();
        }
    }
}
