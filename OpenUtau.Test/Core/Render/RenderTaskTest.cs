using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Render;
using Xunit;

namespace OpenUtau.Core {
    public class RenderTaskTest {
        [Fact]
        public async Task CancelingQueuedWorkerDoesNotRunFailureContinuation() {
            using var cancel = new CancellationTokenSource();
            using var gate = new SemaphoreSlim(0);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = RenderTask.Run(() => BoundedRenderQueue.Run(new[] { 0 }, 1,
                async (_, workerCancel) => {
                    started.SetResult();
                    await gate.WaitAsync(workerCancel.Token);
                    return new RenderResult();
                }, cancel.Token).ToArray(), cancel.Token);
            bool reportedFailure = false;
            var notification = task.ContinueWith(_ => reportedFailure = true,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => notification);
            Assert.True(task.IsCanceled);
            Assert.False(reportedFailure);
        }

        [Theory]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(false, false, false)]
        public async Task OnlyExpectedPureCancellationIsQuiet(bool cancelPass, bool realFailure, bool expectedCanceled) {
            using var cancel = new CancellationTokenSource();
            var failure = realFailure
                ? new AggregateException(new OperationCanceledException(), new InvalidOperationException("render failed"))
                : new AggregateException(new AggregateException(new OperationCanceledException()));
            var task = RenderTask.Run(() => {
                if (cancelPass) cancel.Cancel();
                throw failure;
            }, cancel.Token);
            try { await task.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            Assert.Equal(expectedCanceled, task.IsCanceled);
            Assert.Equal(!expectedCanceled, task.IsFaulted);
            if (!expectedCanceled) Assert.Same(failure, task.Exception!.InnerException);
        }
    }
}
