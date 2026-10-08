using System;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace EasyPipes
{
    public partial class Server
    {
        private Task readerTask = Task.CompletedTask;
        public int MessageTimeoutMillis { get; set; } = 30000;
        public event EventHandler<MessageEventArgs> MessageReceived;
        public event EventHandler<StateMessageEventArgs> StateMessage;

        private void StartReader(NamedPipeServerStream stream)
        {
            lock (objectLockPipeConnect)
            {
                if (IsStarted && stream.CanRead && readerTask.IsCompleted)
                    readerTask = Task.Run(ReadLoopAsync);
            }
        }

        private async Task ReadLoopAsync()
        {
            while (IsStarted)
            {
                if (!IsStateReady())
                {
                    if (!await TryCreateAndWaitForConnectionAsync().ConfigureAwait(false))
                        await Task.Delay(250).ConfigureAwait(false);
                    continue;
                }
                var stream = PipeServer;
                if (stream == null) continue;
                if (!stream.CanRead) return;
                try
                {
                    var message = await StreamIO.ReadMessageAsync(stream, MessageTimeoutMillis).ConfigureAwait(false);
                    message.PipeName = PipeName;
                    if (message.IsValid) OnMessageReceived(new MessageEventArgs(message));
                }
                catch (Exception e)
                {
                    FailConnection(stream);
                    if (IsStarted) OnStateMessage(new StateMessageEventArgs($"{PipeName} > Read > ERROR: {e.Message}"));
                }
            }
        }

        private void FailConnection(NamedPipeServerStream stream)
        {
            lock (objectLockPipeConnect)
            {
                if (stream != null && ReferenceEquals(PipeServer, stream))
                {
                    PipeServer = null;
                    IsErrors = true;
                }
            }
            stream?.Dispose();
        }

        protected virtual void OnMessageReceived(MessageEventArgs e)
        {
            foreach (EventHandler<MessageEventArgs> handler in MessageReceived?.GetInvocationList() ?? Array.Empty<Delegate>())
                try { handler(this, e); } catch (Exception ex) { Console.WriteLine(ex); }
        }

        protected virtual void OnStateMessage(StateMessageEventArgs e)
        {
            foreach (EventHandler<StateMessageEventArgs> handler in StateMessage?.GetInvocationList() ?? Array.Empty<Delegate>())
                try { handler(this, e); } catch (Exception ex) { Console.WriteLine(ex); }
        }
    }
}
