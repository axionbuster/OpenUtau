// OpenUtau's isolated VoiSona Song Audio Unit host. Only public Audio Unit APIs.
import Foundation
import AppKit
import AVFoundation
import AudioToolbox
import CoreAudioKit
import Darwin

setbuf(stdout, nil)
func fourCC(_ s: String) -> UInt32 { s.utf8.reduce(0) { ($0 << 8) | UInt32($1) } }
struct Job: Decodable {
    let protocolVersion: Int
    let generation: String?
    let state: Data
    let durationMs: Double
    let output: String
    let result: String
    let cancel: String
    let noteWindows: [[Double]]
}
struct HostError: Error, CustomStringConvertible {
    let description: String
    init(_ description: String) { self.description = description }
}
let setup = CommandLine.arguments.contains("--setup")
let server = CommandLine.arguments.contains("--server")
// Opt-in lifecycle test seam: mimic a plugin that blocks during destruction.
let testTeardownStall = CommandLine.arguments.contains("--test-teardown-stall")
let arguments = CommandLine.arguments.filter { $0 != "--test-teardown-stall" && $0 != "--server" }
var job: Job?
func decodeJob(_ path: String, requireServerProtocol: Bool = false) throws -> Job {
    let value = try JSONDecoder().decode(Job.self, from: Data(contentsOf: URL(fileURLWithPath: path)))
    guard (value.protocolVersion == 1 || value.protocolVersion == 2),
          (!requireServerProtocol || (value.protocolVersion == 2 && !(value.generation ?? "").isEmpty)),
          value.durationMs.isFinite, value.durationMs > 0,
          value.durationMs <= 600_000, !value.state.isEmpty else {
        throw HostError("Unsupported or invalid render request.")
    }
    return value
}
if !setup && !server {
    do {
        guard arguments.count == 2 else { throw HostError("Expected one render request path.") }
        job = try decodeJob(arguments[1])
    } catch { fputs("VoiSona request error: \(error)\n", stderr); exit(2) }
}
// VoiSona's teardown can enumerate the host working directory. Finder-launched
// applications can inherit /, which can stall that enumeration indefinitely.
// Always give the plugin an empty private directory, including direct CLI calls.
let hostWorkingDirectory = (job.map { URL(fileURLWithPath: $0.result).deletingLastPathComponent() }
    ?? FileManager.default.temporaryDirectory).appendingPathComponent(".voisona-host-" + UUID().uuidString)
do {
    try FileManager.default.createDirectory(at: hostWorkingDirectory, withIntermediateDirectories: true)
    guard FileManager.default.changeCurrentDirectoryPath(hostWorkingDirectory.path) else { throw HostError("Cannot enter VoiSona working directory.") }
} catch { fputs("VoiSona working directory error: \(error)\n", stderr); exit(2) }

// These guards run away from the plugin's native message loop. They still fire
// if AU initialization or destruction blocks it, or the OpenUtau parent exits.
// _exit intentionally bypasses plugin destructors only on emergency shutdown.
let originalParent = getppid()
let parentWatchdog = DispatchSource.makeTimerSource(queue: DispatchQueue.global(qos: .utility))
parentWatchdog.schedule(deadline: .now() + 1, repeating: 1)
parentWatchdog.setEventHandler {
    if originalParent != 1 && getppid() != originalParent { _exit(125) }
}
parentWatchdog.resume()
if let j = job, !server {
    DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + max(150, j.durationMs / 1000 * 2 + 30) + 5) { _exit(124) }
}
final class Host: NSObject, NSApplicationDelegate, NSWindowDelegate {
    var node: AVAudioUnit?
    var engine: AVAudioEngine?
    var window: NSWindow?
    var editor: NSViewController?
    var position = 0.0
    var moving = false
    var exitCode: Int32 = 0
    var finishing = false
    var renderCommitted = false
    var started = Date()
    var buffer: AVAudioPCMBuffer?
    var format: AVAudioFormat?
    var rendering = false
    var completedJobs = 0
    var instantiateSeconds = 0.0
    func applicationDidFinishLaunching(_ notification: Notification) {
        let desc = AudioComponentDescription(componentType: fourCC("aumu"), componentSubType: fourCC("VSSi"),
            componentManufacturer: fourCC("Tesp"), componentFlags: 0, componentFlagsMask: 0)
        var lookup = desc
        guard AudioComponentFindNext(nil, &lookup) != nil else {
            finish("VoiSona Song Audio Unit is missing. Install the Audio Unit plugin and restart OpenUtau."); return
        }
        AVAudioUnit.instantiate(with: desc, options: [.loadInProcess]) { node, error in
            DispatchQueue.main.async {
                guard let node = node else { self.finish("Could not load VoiSona: \(String(describing: error))"); return }
                self.node = node
                self.instantiateSeconds = Date().timeIntervalSince(self.started)
                node.auAudioUnit.transportStateBlock = { [weak self = self] flags, pos, start, end in
                    guard let self = self else { return false }
                    flags?.pointee = self.moving ? [.moving] : []
                    pos?.pointee = self.position; start?.pointee = 0; end?.pointee = 0
                    return true
                }
                node.auAudioUnit.musicalContextBlock = { [weak self = self] tempo, num, den, beat, offset, downbeat in
                    let b = (self?.position ?? 0) / 44100 * 2
                    tempo?.pointee = 120; num?.pointee = 4; den?.pointee = 4
                    beat?.pointee = b; offset?.pointee = 0; downbeat?.pointee = floor(b / 4) * 4
                    return true
                }
                if setup { self.showSetup() }
                else {
                    do { try self.initializeEngine() }
                    catch { self.finish("Could not start VoiSona: \(error)"); return }
                    DispatchQueue.main.asyncAfter(deadline: .now() + 2) {
                        if server { self.startServer() } else { self.started = Date(); self.render() }
                    }
                }
            }
        }
    }
    func showSetup() {
        node!.auAudioUnit.requestViewController { vc in
            DispatchQueue.main.async {
                guard let vc = vc else { self.finish("VoiSona did not provide its setup window."); return }
                self.editor = vc
                let w = NSWindow(contentRect: NSRect(x: 100, y: 100, width: 1200, height: 800),
                    styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
                w.title = "VoiSona — Sign in / Manage voices"
                w.contentViewController = vc; w.isReleasedWhenClosed = false; w.delegate = self
                self.window = w
                w.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true); w.makeFirstResponder(vc.view)
            }
        }
    }
    func windowWillClose(_ notification: Notification) { finish(nil) }
    func initializeEngine() throws {
        guard let node = node else { throw HostError("VoiSona is not loaded.") }
        let e = AVAudioEngine(); engine = e; e.attach(node)
        let fmt = AVAudioFormat(standardFormatWithSampleRate: 44100, channels: 2)!
        format = fmt
        e.connect(node, to: e.mainMixerNode, format: fmt)
        try e.enableManualRenderingMode(.offline, format: fmt, maximumFrameCount: 1024)
        node.auAudioUnit.isRenderingOffline = true
        try e.start()
        buffer = AVAudioPCMBuffer(pcmFormat: fmt, frameCapacity: 1024)!
    }
    func startServer() {
        print("READY 2")
        DispatchQueue.global(qos: .utility).async {
            while let line = readLine() {
                let path = line.trimmingCharacters(in: .whitespacesAndNewlines)
                if path.isEmpty { continue }
                DispatchQueue.main.async { self.startServerJob(path) }
            }
            DispatchQueue.main.async { if !self.rendering { self.finish(nil) } }
        }
    }
    func startServerJob(_ path: String) {
        guard !rendering else { finish("VoiSona server received overlapping jobs."); return }
        do {
            job = try decodeJob(path, requireServerProtocol: true)
            started = Date(); rendering = true; render()
        } catch {
            fputs("VoiSona request error: \(error)\n", stderr)
            print("DONE invalid")
        }
    }
    func check() throws {
        if let j = job, FileManager.default.fileExists(atPath: j.cancel) { throw HostError("Render canceled.") }
        if Date().timeIntervalSince(started) > max(150, (job?.durationMs ?? 0) / 1000 * 2 + 30) { throw HostError("VoiSona timed out. Open Sign in / Manage voices and check the selected voice is available.") }
    }
    func warm(_ seconds: Double, _ buffer: AVAudioPCMBuffer) throws {
        moving = false; position = 0
        let until = Date().addingTimeInterval(seconds)
        while Date() < until {
            try check()
            _ = try engine!.renderOffline(1024, to: buffer)
            RunLoop.current.run(until: Date().addingTimeInterval(0.025))
        }
    }
    func capture(_ j: Job, _ buffer: AVAudioPCMBuffer, _ format: AVAudioFormat, _ path: String) throws -> [Float] {
        let total = Int(ceil(j.durationMs * 44.1))
        let file = try AVAudioFile(forWriting: URL(fileURLWithPath: path), settings: format.settings)
        var mono = [Float](); mono.reserveCapacity(total)
        position = 0; moving = true
        var transient = 0
        while mono.count < total {
            try check()
            let frames = AVAudioFrameCount(min(1024, total - mono.count))
            let status = try engine!.renderOffline(frames, to: buffer)
            if status != .success {
                transient += 1
                if transient > 100 { throw HostError("VoiSona could not supply audio (status \(status.rawValue)).") }
                RunLoop.current.run(until: Date().addingTimeInterval(0.01)); continue
            }
            transient = 0
            guard buffer.frameLength > 0, let channels = buffer.floatChannelData else { throw HostError("VoiSona returned an empty buffer.") }
            for i in 0..<Int(buffer.frameLength) {
                let v = (channels[0][i] + channels[1][i]) / 2
                guard v.isFinite else { throw HostError("VoiSona returned invalid audio.") }
                mono.append(v)
            }
            try file.write(from: buffer)
            position += Double(buffer.frameLength)
            // Pump the plugin's message loop without real-time throttling.
            RunLoop.current.run(until: Date())
        }
        moving = false
        return mono
    }
    func hasCoverage(_ samples: [Float], _ j: Job) -> Bool {
        // A sounding window is required for every note, not just somewhere in the phrase.
        // The broad window allows consonants, breath sounds and natural note boundaries.
        for w in j.noteWindows where w.count == 2 {
            let a = max(0, Int(w[0] * 44.1)); let b = min(samples.count, Int(w[1] * 44.1))
            if b <= a || !samples[a..<b].contains(where: { abs($0) > 0.000001 }) { return false }
        }
        return samples.contains(where: { abs($0) > 0.000001 })
    }
    func stable(_ a: [Float], _ b: [Float]) -> Bool {
        guard a.count == b.count else { return false }
        var error = 0.0, energy = 0.0
        for i in a.indices { let d = Double(a[i] - b[i]); error += d*d; energy += Double(b[i])*Double(b[i]) }
        return error <= max(1e-12, energy * 1e-8)
    }
    func render() {
        guard let j = job, let node = node, engine != nil, let fmt = format, let buffer = buffer else { return }
        let scratch = j.output + ".capture.wav"
        defer { try? FileManager.default.removeItem(atPath: scratch) }
        var timings: [String: Double] = ["instantiate": instantiateSeconds, "startupDelay": 2.0]
        do {
            var phase = Date()
            var state = node.auAudioUnit.fullState ?? [:]
            state["jucePluginState"] = j.state
            node.auAudioUnit.fullState = state
            timings["stateLoad"] = Date().timeIntervalSince(phase)
            var previous: [Float]?
            for attempt in 0..<8 {
                print("Preparing VoiSona (\(attempt + 1)/8)")
                phase = Date()
                let warmSeconds = completedJobs > 0 && attempt < 2 ? 0.25 : (attempt == 0 ? 3 : 2)
                try warm(warmSeconds, buffer)
                timings["warm\(attempt + 1)"] = Date().timeIntervalSince(phase)
                phase = Date()
                let samples = try capture(j, buffer, fmt, scratch)
                timings["capture\(attempt + 1)"] = Date().timeIntervalSince(phase)
                let coverage = hasCoverage(samples, j)
                if coverage, let old = previous, stable(old, samples) {
                    try check()
                    phase = Date()
                    try? FileManager.default.removeItem(atPath: j.output)
                    try FileManager.default.moveItem(atPath: scratch, toPath: j.output)
                    timings["commit"] = Date().timeIntervalSince(phase)
                    let response: [String: Any] = [
                        "protocolVersion": j.protocolVersion,
                        "generation": j.generation ?? "",
                        "ok": true,
                        "frames": samples.count,
                        "sampleRate": 44100,
                        "attempts": attempt + 1,
                        "timings": timings,
                    ]
                    try JSONSerialization.data(withJSONObject: response).write(to: URL(fileURLWithPath: j.result), options: .atomic)
                    renderCommitted = true
                    completeServerJobOrFinish(); return
                }
                previous = coverage ? samples : nil
            }
            throw HostError("VoiSona did not produce complete, stable audio. Use Tools → VoiSona → Sign in / Manage voices to sign in and confirm this voice is installed and licensed, then try again. No silent audio was cached.")
        } catch {
            let detail = String(describing: error)
            fputs("\(detail)\n", stderr)
            try? JSONSerialization.data(withJSONObject: [
                "protocolVersion": j.protocolVersion,
                "generation": j.generation ?? "",
                "ok": false,
                "error": detail,
                "timings": timings,
            ]).write(to: URL(fileURLWithPath: j.result), options: .atomic)
            if server { completeServerJobOrFinish() } else { finish(detail) }
        }
    }
    func completeServerJobOrFinish() {
        guard server else { finish(nil); return }
        let generation = job?.generation ?? "invalid"
        completedJobs += 1
        rendering = false
        job = nil
        print("DONE \(generation)")
    }
    func finish(_ error: String?) {
        guard !finishing else { return }; finishing = true
        exitCode = error == nil && (setup || server || renderCommitted) ? 0 : 1
        // A completed WAV and atomic success manifest are the transaction boundary.
        // VoiSona can block in its destructor under a GUI parent. Try normal cleanup,
        // then let the OS reclaim this isolated host without discarding valid audio.
        // Capture the terminal code now; a failed or canceled render cannot become success.
        let terminalCode = exitCode
        DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + 5) { _exit(terminalCode) }
        if let error = error {
            fputs("\(error)\n", stderr)
            if let j = job, !FileManager.default.fileExists(atPath: j.result) {
                try? JSONSerialization.data(withJSONObject: ["protocolVersion": 1, "ok": false, "error": error])
                    .write(to: URL(fileURLWithPath: j.result), options: .atomic)
            }
        }
        if testTeardownStall { Thread.sleep(forTimeInterval: 60) }
        moving = false
        engine?.stop()
        if let n = node {
            engine?.disconnectNodeOutput(n); engine?.detach(n)
            n.auAudioUnit.deallocateRenderResources()
            n.auAudioUnit.transportStateBlock = nil; n.auAudioUnit.musicalContextBlock = nil
        }
        window?.orderOut(nil); window?.contentViewController = nil
        editor = nil; window = nil; node = nil; engine = nil
        // ONNX teardown completes on the native message loop before process exit.
        DispatchQueue.main.asyncAfter(deadline: .now() + 1) {
            try? FileManager.default.removeItem(at: hostWorkingDirectory)
            exit(self.exitCode)
        }
    }
}
let app = NSApplication.shared
app.setActivationPolicy(setup ? .regular : .prohibited)
let host = Host(); app.delegate = host
app.run()
exit(host.exitCode)
