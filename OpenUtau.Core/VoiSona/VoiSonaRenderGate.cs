using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenUtau.Core.VoiSona {
    // Running jobs finish and populate the shared cache. Exports get the next
    // available native slot before queued playback/background work.
    internal sealed class VoiSonaRenderGate {
        readonly object sync = new();
        readonly Queue<TaskCompletionSource<bool>> exports = new();
        readonly Queue<TaskCompletionSource<bool>> playback = new();
        int available;
        internal VoiSonaRenderGate(int capacity) { available = capacity; }
        internal async Task WaitAsync(bool exporting, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            TaskCompletionSource<bool> waiter;
            lock (sync) {
                if (available > 0) { available--; return; }
                waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
                (exporting ? exports : playback).Enqueue(waiter);
            }
            using var registration = token.Register(() => waiter.TrySetCanceled(token));
            await waiter.Task;
        }
        internal void Release() {
            lock (sync) {
                while (exports.Count > 0 || playback.Count > 0) {
                    var queue = exports.Count > 0 ? exports : playback;
                    if (queue.Dequeue().TrySetResult(true)) return;
                }
                available++;
            }
        }
    }
}
