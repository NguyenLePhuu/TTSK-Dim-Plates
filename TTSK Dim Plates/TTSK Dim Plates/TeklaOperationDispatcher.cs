using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TTSK_AutoDim_Plates
{
    // One persistent STA owns Tekla operations and their ThreadStatic scopes.
    // Post only one callback at a time so a legacy DoEvents/modal dialog cannot
    // reenter another operation on the worker.
    internal sealed class TeklaOperationDispatcher : IDisposable
    {
        private readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        private readonly TaskCompletionSource<Control> ready = new TaskCompletionSource<Control>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Thread thread;
        private readonly object lifecycle = new object();
        private int scheduled, disposed;
        internal bool IsWorkerThread { get { return Thread.CurrentThread == thread; } }
        internal TeklaOperationDispatcher()
        {
            thread = new Thread(() =>
            {
                try
                {
                    using (var control = new Control())
                    {
                        var handle = control.Handle;
                        ready.SetResult(control);
                        Application.Run();
                    }
                }
                catch (Exception ex) { ready.TrySetException(ex); }
            }) { IsBackground = true, Name = "TTSK Tekla operations" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        internal Task RunAsync(Action action) { return RunAsync(() => { action(); return true; }); }
        internal async Task<T> RunAsync<T>(Func<T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(TeklaOperationDispatcher));
            if (IsWorkerThread) return action();
            var control = await ready.Task.ConfigureAwait(false);
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (lifecycle)
            {
                if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(TeklaOperationDispatcher));
                queue.Enqueue(() =>
                {
                    try
                    {
                        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(TeklaOperationDispatcher));
                        completion.SetResult(action());
                    }
                    catch (Exception ex) { completion.SetException(ex); }
                });
                Schedule(control);
            }
            return await completion.Task.ConfigureAwait(false);
        }
        private void Schedule(Control control)
        {
            if (Interlocked.CompareExchange(ref scheduled, 1, 0) != 0) return;
            control.BeginInvoke((Action)(() =>
            {
                Action next;
                try { if (queue.TryDequeue(out next)) next(); }
                finally
                {
                    Interlocked.Exchange(ref scheduled, 0);
                    if (!queue.IsEmpty) Schedule(control);
                }
            }));
        }
        // Form closing is refused while work is active. Never abort a Tekla write.
        public void Dispose()
        {
            lock (lifecycle)
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0) return;
                ready.Task.ContinueWith(t =>
                {
                    if (t.Status != TaskStatus.RanToCompletion) return;
                    lock (lifecycle)
                    {
                        queue.Enqueue(() => Application.ExitThread());
                        Schedule(t.Result);
                    }
                }, TaskScheduler.Default);
            }
        }
    }
}
