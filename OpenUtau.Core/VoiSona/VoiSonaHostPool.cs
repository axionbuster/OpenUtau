using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenUtau.Core.VoiSona {
    internal sealed class VoiSonaPersistentHostException : Exception {
        public VoiSonaPersistentHostException(string message) : base(message) { }
        public VoiSonaPersistentHostException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// A small pool of long-lived VoiSona AU hosts. A host processes exactly one
    /// request at a time; canceled or untrusted protocol sessions are never reused.
    /// </summary>
    internal sealed class VoiSonaHostPool : IDisposable {
        readonly string helper;
        readonly int maxHosts;
        readonly TimeSpan idleTimeout;
        readonly TimeSpan startupTimeout;
        readonly bool lowPriority;
        readonly SemaphoreSlim leases;
        readonly object sync = new();
        readonly List<Host> hosts = new();
        bool disposed;

        internal VoiSonaHostPool(string helper, int maxHosts = 2, TimeSpan? idleTimeout = null,
                TimeSpan? startupTimeout = null, bool lowPriority = true) {
            if (maxHosts is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(maxHosts));
            this.helper = Path.GetFullPath(helper);
            this.maxHosts = maxHosts;
            this.idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(2);
            this.startupTimeout = startupTimeout ?? TimeSpan.FromSeconds(30);
            this.lowPriority = lowPriority;
            leases = new SemaphoreSlim(maxHosts, maxHosts);
        }

        internal async Task RunAsync(VoiSonaJob job, string directory, CancellationToken token,
                Action<string>? report = null, TimeSpan? timeout = null) {
            if (job.protocolVersion != 2 || string.IsNullOrWhiteSpace(job.generation)) {
                throw new ArgumentException("Persistent VoiSona jobs require protocol 2 and a generation.", nameof(job));
            }
            token.ThrowIfCancellationRequested();
            await leases.WaitAsync(token);
            Host? host = null;
            bool healthy = false;
            try {
                host = LeaseHost();
                await host.RunAsync(job, directory, token, report, timeout, startupTimeout);
                healthy = true;
            } catch (OperationCanceledException) when (token.IsCancellationRequested) {
                throw;
            } catch (VoiSonaPersistentHostException) {
                throw;
            } catch (Exception) when (host?.LastJobCompletedNormally == true) {
                throw;
            } catch (Exception ex) {
                throw new VoiSonaPersistentHostException("Persistent VoiSona host failed.", ex);
            } finally {
                if (host != null) ReturnHost(host, healthy || host.LastJobCompletedNormally);
                leases.Release();
            }
        }

        Host LeaseHost() {
            lock (sync) {
                ObjectDisposedException.ThrowIf(disposed, this);
                foreach (var dead in hosts.Where(host => !host.IsUsable).ToArray()) {
                    hosts.Remove(dead);
                    dead.Terminate();
                }
                var host = hosts.FirstOrDefault(candidate => !candidate.Leased);
                if (host == null) {
                    if (hosts.Count >= maxHosts) throw new InvalidOperationException("No VoiSona host lease is available.");
                    host = new Host(helper, lowPriority);
                    hosts.Add(host);
                }
                host.Leased = true;
                host.CancelIdle();
                return host;
            }
        }

        void ReturnHost(Host host, bool healthy) {
            bool terminate = false;
            lock (sync) {
                host.Leased = false;
                if (!healthy || disposed || !host.IsUsable) {
                    hosts.Remove(host);
                    terminate = true;
                } else {
                    host.ScheduleIdle(idleTimeout, () => Expire(host));
                }
            }
            if (terminate) host.Terminate();
        }

        void Expire(Host host) {
            lock (sync) {
                if (disposed || host.Leased || !hosts.Remove(host)) return;
            }
            host.Terminate();
        }

        public void Dispose() {
            Host[] remaining;
            lock (sync) {
                if (disposed) return;
                disposed = true;
                remaining = hosts.ToArray();
                hosts.Clear();
            }
            foreach (var host in remaining) host.Terminate();
        }

        sealed class Host {
            readonly string helper;
            readonly bool lowPriority;
            readonly object diagnosticsLock = new();
            readonly Queue<string> diagnostics = new();
            Process? process;
            Task? diagnosticsTask;
            Timer? idleTimer;
            bool terminated;

            internal bool Leased { get; set; }
            internal bool LastJobCompletedNormally { get; private set; }
            internal bool IsUsable => !terminated && (process == null || !process.HasExited);

            internal Host(string helper, bool lowPriority) {
                this.helper = helper;
                this.lowPriority = lowPriority;
            }

            internal void CancelIdle() {
                idleTimer?.Dispose();
                idleTimer = null;
            }

            internal void ScheduleIdle(TimeSpan delay, Action expire) {
                CancelIdle();
                idleTimer = new Timer(_ => expire(), null, delay, Timeout.InfiniteTimeSpan);
            }

            internal async Task RunAsync(VoiSonaJob job, string directory, CancellationToken token,
                    Action<string>? report, TimeSpan? timeout, TimeSpan startupTimeout) {
                LastJobCompletedNormally = false;
                await EnsureStarted(directory, token, startupTimeout);
                string request = Path.GetFullPath(Path.Combine(directory, "request.json"));
                await File.WriteAllTextAsync(request, JsonConvert.SerializeObject(job), token);
                using var deadline = new CancellationTokenSource(timeout
                    ?? TimeSpan.FromSeconds(Math.Max(150, job.durationMs / 1000 * 2 + 30) + 10));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
                try {
                    await process!.StandardInput.WriteLineAsync(request.AsMemory(), linked.Token);
                    await process.StandardInput.FlushAsync(linked.Token);
                    while (true) {
                        string? line = await process.StandardOutput.ReadLineAsync(linked.Token);
                        if (line == null) throw Failure("Persistent VoiSona host exited before completing the request.");
                        if (line.StartsWith("Preparing VoiSona", StringComparison.Ordinal)) {
                            report?.Invoke(line);
                            continue;
                        }
                        if (!line.StartsWith("DONE ", StringComparison.Ordinal)) continue;
                        string completedGeneration = line[5..];
                        if (!string.Equals(completedGeneration, job.generation, StringComparison.Ordinal)) {
                            throw Failure($"VoiSona host completed stale generation {completedGeneration} instead of {job.generation}.");
                        }
                        break;
                    }
                } catch (OperationCanceledException) {
                    Terminate();
                    token.ThrowIfCancellationRequested();
                    throw Failure("Persistent VoiSona rendering timed out.");
                } catch (IOException ex) {
                    throw Failure("Persistent VoiSona host connection failed.", ex);
                }

                JObject response;
                try {
                    response = JObject.Parse(await File.ReadAllTextAsync(job.result, token));
                } catch (Exception ex) when (ex is IOException or JsonException) {
                    throw Failure("Persistent VoiSona host returned no valid result transaction.", ex);
                }
                if (response.Value<int>("protocolVersion") != 2
                        || !string.Equals(response.Value<string>("generation"), job.generation, StringComparison.Ordinal)) {
                    throw Failure("Persistent VoiSona host returned a mismatched result transaction.");
                }
                if (response.Value<bool>("ok") != true) {
                    // State-load and reset failures can leave native AU state
                    // ambiguous. Never lease this process to a newer generation.
                    LastJobCompletedNormally = false;
                    throw new InvalidOperationException(response.Value<string>("error") ?? "VoiSona rendering failed.");
                }
                if (response.Value<int>("frames") != (int)Math.Ceiling(job.durationMs * 44.1)
                        || response.Value<int>("sampleRate") != 44100) {
                    LastJobCompletedNormally = false;
                    throw Failure("Persistent VoiSona host returned invalid audio metadata.");
                }
                LastJobCompletedNormally = true;
            }

            async Task EnsureStarted(string directory, CancellationToken token, TimeSpan startupTimeout) {
                if (process != null) return;
                var start = new ProcessStartInfo(lowPriority ? "/usr/bin/nice" : helper) {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(helper) ?? directory,
                };
                if (lowPriority) {
                    start.ArgumentList.Add("-n");
                    start.ArgumentList.Add("10");
                    start.ArgumentList.Add(helper);
                }
                start.ArgumentList.Add("--server");
                process = new Process { StartInfo = start };
                try {
                    if (!process.Start()) throw Failure("Could not start the persistent VoiSona host.");
                    diagnosticsTask = DrainDiagnostics(process.StandardError);
                    using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
                    startup.CancelAfter(startupTimeout);
                    string? ready = await process.StandardOutput.ReadLineAsync(startup.Token);
                    if (!string.Equals(ready, "READY 2", StringComparison.Ordinal)) {
                        throw Failure($"Persistent VoiSona host did not announce protocol 2 readiness: {ready ?? "end of stream"}.");
                    }
                } catch (OperationCanceledException) {
                    Terminate();
                    token.ThrowIfCancellationRequested();
                    throw Failure("Persistent VoiSona host startup timed out.");
                } catch (Exception ex) when (ex is not VoiSonaPersistentHostException) {
                    throw Failure("Could not start the persistent VoiSona host.", ex);
                }
            }

            async Task DrainDiagnostics(StreamReader reader) {
                try {
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null) {
                        lock (diagnosticsLock) {
                            diagnostics.Enqueue(line.Length > 2048 ? line[..2048] : line);
                            while (diagnostics.Count > 12) diagnostics.Dequeue();
                        }
                    }
                } catch (ObjectDisposedException) { }
            }

            VoiSonaPersistentHostException Failure(string message, Exception? inner = null) {
                string detail;
                lock (diagnosticsLock) detail = string.Join("\n", diagnostics);
                if (!string.IsNullOrWhiteSpace(detail)) message += " " + detail;
                return inner == null
                    ? new VoiSonaPersistentHostException(message)
                    : new VoiSonaPersistentHostException(message, inner);
            }

            internal void Terminate() {
                if (terminated) return;
                terminated = true;
                CancelIdle();
                if (process == null) return;
                try {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    process.WaitForExit(1000);
                } catch (InvalidOperationException) {
                } finally {
                    process.Dispose();
                }
            }
        }
    }
}
