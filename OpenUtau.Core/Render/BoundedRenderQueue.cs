using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenUtau.Core.Render {
    // Bounded admission preserves priority without queuing a whole song into the
    // native gate. Completion order lets every ready track reach the planner promptly.
    internal static class BoundedRenderQueue {
        internal static IEnumerable<(T item, RenderResult result)> Run<T>(IEnumerable<T> items,
                int concurrency, Func<T, CancellationTokenSource, Task<RenderResult>> render,
                CancellationToken token) {
            if (concurrency < 1) throw new ArgumentOutOfRangeException(nameof(concurrency));
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var source = items.GetEnumerator();
            var pending = new List<(T item, Task<RenderResult> task)>();
            bool exhausted = false;
            try {
                while (true) {
                    token.ThrowIfCancellationRequested();
                    while (!exhausted && pending.Count < concurrency) {
                        token.ThrowIfCancellationRequested();
                        if (!source.MoveNext()) { exhausted = true; break; }
                        var item = source.Current;
                        pending.Add((item, render(item, cancellation)));
                    }
                    if (pending.Count == 0) yield break;
                    var tasks = pending.ConvertAll(p => p.task);
                    var completed = Task.WhenAny(tasks).GetAwaiter().GetResult();
                    int index = pending.FindIndex(p => p.task == completed);
                    var entry = pending[index];
                    // Keep failures in pending so cleanup observes every task.
                    var result = completed.GetAwaiter().GetResult();
                    pending.RemoveAt(index);
                    token.ThrowIfCancellationRequested();
                    yield return (entry.item, result);
                }
            } finally {
                cancellation.Cancel();
                // On failure, cancellation or early disposal, reap all native helpers
                // before handing control back. Preserve the original exception.
                try { Task.WhenAll(pending.ConvertAll(p => p.task)).GetAwaiter().GetResult(); }
                catch (Exception) { }
            }
        }
    }
}
