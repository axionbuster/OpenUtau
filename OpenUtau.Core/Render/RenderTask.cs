using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenUtau.Core.Render {
    internal static class RenderTask {
        internal static Task Run(Action render, CancellationToken token) => Task.Run(() => {
            try {
                render();
            } catch (Exception ex) when (token.IsCancellationRequested && IsCancellation(ex)) {
                // Workers use linked tokens, and synchronous waits may wrap their
                // cancellation. Associate it with this task so it is Canceled, not
                // Faulted: the failure continuation must not stop newer playback.
                throw new OperationCanceledException(token);
            }
        }, token);

        static bool IsCancellation(Exception ex) => ex is OperationCanceledException
            || ex is AggregateException aggregate
                && aggregate.Flatten().InnerExceptions.Count > 0
                && aggregate.Flatten().InnerExceptions.All(e => e is OperationCanceledException);
    }
}
