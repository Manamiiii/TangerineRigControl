using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace TangerineRigControl.Services
{
    // One background owner for every COM call. A timeout quarantines this session;
    // it does not pretend to abort a native call or allow another call alongside it.
    internal sealed class AuraSession : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
        private readonly object _gate = new object();
        private readonly TaskCompletionSource<bool> _closed = new TaskCompletionSource<bool>();
        private readonly Func<IAuraBackend> _factory;
        private readonly int _timeout;
        private IAuraBackend _backend;
        private bool _stopping;
        private bool _faulted;

        internal AuraSession() : this(() => new AuraLightingClient(), 15000) { }
        internal AuraSession(Func<IAuraBackend> factory, int timeout)
        {
            _factory = factory;
            _timeout = timeout;
            var thread = new Thread(Work) { IsBackground = true, Name = "Aura session" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        private void Work()
        {
            try
            {
                foreach (var action in _queue.GetConsumingEnumerable()) action();
                if (_backend != null) _backend.Dispose();
                _closed.TrySetResult(true);
            }
            catch (Exception) { _closed.TrySetResult(false); }
            finally { _queue.Dispose(); }
        }

        internal string SetEnabled(bool enabled, CancellationToken token)
        {
            return Execute(delegate
            {
                token.ThrowIfCancellationRequested();
                if (_backend == null) _backend = _factory();
                return _backend.SetEnabled(enabled, token);
            }, token);
        }

        internal void ReleaseExisting()
        {
            Execute(delegate { if (_backend != null) _backend.ReleaseExisting(); return string.Empty; }, CancellationToken.None);
        }

        private string Execute(Func<string> action, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var result = new TaskCompletionSource<string>();
            lock (_gate)
            {
                if (_stopping || _faulted) throw new InvalidOperationException("SDK 会话已停止或超时，不能再次操作；请退出后重启控制台。");
                _queue.Add(delegate
                {
                    try { result.TrySetResult(action()); }
                    catch (OperationCanceledException) { result.TrySetCanceled(); }
                    catch (Exception ex) { result.TrySetException(ex); }
                });
            }
            var started = Environment.TickCount;
            while (!result.Task.IsCompleted)
            {
                if (token.IsCancellationRequested || unchecked(Environment.TickCount - started) >= _timeout)
                {
                    lock (_gate) { _faulted = true; }
                    Dispose();
                    // Observe late failures, while letting the owner release only after its native call returns.
                    result.Task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    throw new InvalidOperationException("已停止等待 SDK；底层调用可能仍在执行，控制权状态未知。本次会话已停用，请退出控制台后检查原厂灯效。");
                }
                Thread.Sleep(20);
            }
            return result.Task.GetAwaiter().GetResult();
        }

        internal async Task<bool> CloseAsync(int timeout)
        {
            Dispose();
            return await Task.WhenAny(_closed.Task, Task.Delay(timeout)) == _closed.Task && await _closed.Task;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_stopping) return;
                _stopping = true;
                _queue.CompleteAdding();
            }
        }
    }
}
