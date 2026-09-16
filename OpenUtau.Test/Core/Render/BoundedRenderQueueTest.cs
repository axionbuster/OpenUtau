using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Render;
using OpenUtau.Core.VoiSona;
using Xunit;

namespace OpenUtau.Core {
    public class BoundedRenderQueueTest {
        [Theory]
        [InlineData(false, 1, 1)]
        [InlineData(false, 12, 2)]
        [InlineData(true, 2, 1)]
        [InlineData(true, 6, 3)]
        [InlineData(true, 12, 4)]
        public void VoiSonaConcurrencyLeavesCpuHeadroom(bool export, int cpu, int expected)
            => Assert.Equal(expected, VoiSonaRenderer.Concurrency(export, cpu));

        [Fact]
        public async Task AdmitsInOrderPublishesReadyJobAndBoundsLookahead() {
            var started = new ConcurrentQueue<int>();
            var jobs = Enumerable.Range(0, 4).Select(_ => new TaskCompletionSource<RenderResult>(
                TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            var admitted = new SemaphoreSlim(0);
            using var cancel = new CancellationTokenSource();
            using var queue = BoundedRenderQueue.Run(Enumerable.Range(0, 4), 2, (i, c) => {
                started.Enqueue(i); admitted.Release();
                return jobs[i].Task.WaitAsync(c.Token);
            }, cancel.Token).GetEnumerator();
            var first = Task.Run(() => queue.MoveNext());
            await admitted.WaitAsync(TimeSpan.FromSeconds(5));
            await admitted.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { 0, 1 }, started.ToArray());
            jobs[1].SetResult(new RenderResult());
            Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, queue.Current.item);
            var next = Task.Run(() => queue.MoveNext());
            Assert.True(await admitted.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(new[] { 0, 1, 2 }, started.ToArray());
            jobs[2].SetResult(new RenderResult());
            Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(2, queue.Current.item);
            // Disposal cancels and drains job 0 without admitting job 3.
            queue.Dispose();
            Assert.Equal(new[] { 0, 1, 2 }, started.ToArray());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FailureOrCancellationReapsOtherWorkers(bool cancelPass) {
            using var cancel = new CancellationTokenSource();
            var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fail = new TaskCompletionSource<RenderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool reaped = false;
            async Task<RenderResult> Render(int i, CancellationTokenSource c) {
                if (i == 0) return await fail.Task.WaitAsync(c.Token);
                admitted.SetResult();
                try { await Task.Delay(Timeout.Infinite, c.Token); return null!; }
                finally { reaped = true; }
            }
            var run = Task.Run(() => BoundedRenderQueue.Run(new[] { 0, 1, 2 }, 2, Render, cancel.Token).ToArray());
            await admitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (cancelPass) cancel.Cancel(); else fail.SetException(new InvalidOperationException("fixture"));
            if (cancelPass) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            else await Assert.ThrowsAsync<InvalidOperationException>(() => run);
            Assert.True(reaped);
        }
    }
}
