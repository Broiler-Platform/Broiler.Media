using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Broiler.Media.Video.MediaFoundation;

// Native/UI callbacks only post here. Application code runs on the task pool,
// in order, and can await without holding up the native engine's callback thread.
internal sealed class MediaFoundationCallbackQueue
{
    private readonly Channel<Func<ValueTask>> _work = Channel.CreateUnbounded<Func<ValueTask>>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _cancellationToken;
    private int _stopping;
    private readonly Task _processing;
    private readonly Func<Exception, ValueTask> _onError;

    internal MediaFoundationCallbackQueue(Func<Exception, ValueTask> onError)
    {
        _onError = onError;
        _cancellationToken = _stop.Token;
        _processing = Task.Run(ProcessAsync);
    }

    internal CancellationToken CancellationToken => _cancellationToken;

    internal void Post(Func<ValueTask> work) => _work.Writer.TryWrite(work);

    internal Task FlushAsync()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_work.Writer.TryWrite(() => { completed.SetResult(); return ValueTask.CompletedTask; }))
            return _processing;
        return completed.Task.WaitAsync(_cancellationToken);
    }

    internal void Stop()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
            return;
        _work.Writer.TryComplete();
        // CancelAsync marks the token immediately without running application token
        // registrations on the disposing/native thread. Observe registration failures.
        _ = ReleaseCancellationAsync(_stop.CancelAsync());
    }

    private async Task ReleaseCancellationAsync(Task cancellation)
    {
        try { await Task.WhenAll(_processing, cancellation).ConfigureAwait(false); }
        catch { }
        finally { _stop.Dispose(); }
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (Func<ValueTask> work in _work.Reader.ReadAllAsync(_cancellationToken).ConfigureAwait(false))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                try { await AwaitAsync(work()).ConfigureAwait(false); }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    // A failing FailAsync/subscriber must not fault an unobserved pump task
                    // or escape back into a COM callback.
                    try { await AwaitAsync(_onError(ex)).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                    catch { }
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally
        {
            while (_work.Reader.TryRead(out _)) { }
        }
    }

    private async Task AwaitAsync(ValueTask operation)
    {
        if (operation.IsCompletedSuccessfully)
        {
            operation.GetAwaiter().GetResult();
            return;
        }
        Task task = operation.AsTask();
        Observe(task);
        // An output may ignore cancellation. Do not make native disposal depend
        // on its completion, but still observe a later exception from that output.
        await task.WaitAsync(_cancellationToken).ConfigureAwait(false);
    }

    private static void Observe(Task task)
    {
        if (task.IsCompleted)
        {
            _ = task.Exception;
            return;
        }
        _ = task.ContinueWith(completed => _ = completed.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
