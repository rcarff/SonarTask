#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using LSL;

namespace SonarTask.Services {
// All native discovery, outlet creation and publication stays off Unity's thread.
public sealed class NativeLslTransport : ILslTransport {
    readonly ConcurrentQueue<(int stream, object data, double timestamp)> reliable = new();
    readonly ConcurrentQueue<(float[] data, double timestamp)> states = new();
    readonly ConcurrentQueue<(int stream, float[] data, double timestamp)> waterfall = new();
    readonly ConcurrentQueue<string> commands = new();
    volatile bool stopped, connected;
    double clockOffset;
    LslStreamConfig config;
    public bool IsConnected => connected;
    public string Error { get; private set; }
    static double Monotonic => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    public double Clock => Monotonic;
    public void Open(LslStreamConfig info) {
        config = info;
        new Thread(Work) { IsBackground = true, Name = "Sonar LSL" }.Start();
    }
    StreamOutlet Outlet(string name, string type, int count, double rate, channel_format_t format) {
        using var info = new StreamInfo("SonarTask-" + name, type, count, rate, format, config.sourceId + "-" + name);
        info.desc().append_child_value("app_version", config.appVersion);
        info.desc().append_child_value("platform", config.platform);
        info.desc().append_child_value("schema_version", "1");
        return new StreamOutlet(info, 0, 10);
    }
    void Work() {
        var outlets = new List<StreamOutlet>();
        StreamInlet inlet = null;
        try {
            clockOffset = LSL.LSL.local_clock() - Monotonic;
            outlets.Add(Outlet("Markers", "Markers", 1, 0, channel_format_t.cf_string));
            outlets.Add(Outlet("State", "SonarState", 6, config.stateRate, channel_format_t.cf_float32));
            if (config.waterfalls) {
                outlets.Add(Outlet("BTH", "SonarWaterfall", 2 + config.bins, config.waterfallRate, channel_format_t.cf_float32));
                outlets.Add(Outlet("LOFAR", "SonarWaterfall", 3 + config.bins * config.views, config.waterfallRate, channel_format_t.cf_float32));
            }
            connected = true;
            double nextResolve = 0;
            var sample = new string[1];
            while (!stopped || !reliable.IsEmpty && outlets[0].have_consumers()) {
                // Bounded discovery timeout on a background thread; never on the run loop.
                if (!stopped && inlet == null && Monotonic >= nextResolve) {
                    var found = LSL.LSL.resolve_stream("name", "SonarTask-Control", 1, .05);
                    foreach (var info in found) {
                        if (inlet == null && info.channel_count() == 1 && info.channel_format() == channel_format_t.cf_string)
                            inlet = new StreamInlet(info, 1, 1, false);
                        info.Dispose();
                    }
                    nextResolve = Monotonic + 2;
                }
                int budget = 256;
                // Keep startup markers until discovery/subscription completes, preserving
                // their capture timestamps. This does not delay the experiment.
                while (budget-- > 0 && outlets[0].have_consumers() && reliable.TryDequeue(out var item))
                    outlets[0].push_sample(new[] { (string)item.data }, item.timestamp + clockOffset);
                while (states.TryDequeue(out var state)) outlets[1].push_sample(state.data, state.timestamp + clockOffset);
                while (waterfall.TryDequeue(out var item)) outlets[item.stream].push_sample(item.data, item.timestamp + clockOffset);
                if (inlet != null) {
                    try {
                        for (int i = 0; i < 64 && inlet.pull_sample(sample, 0) > 0; i++)
                            if (commands.Count < 256) commands.Enqueue(sample[0]);
                    } catch { inlet.Dispose(); inlet = null; }
                }
                Thread.Sleep(2);
            }
        } catch (Exception e) { Error = e.ToString(); connected = false; }
        finally {
            connected = false;
            inlet?.Dispose();
            foreach (var outlet in outlets) outlet.Dispose();
        }
    }
    public void PushMarker(string json, double timestamp) => reliable.Enqueue((0, json, timestamp));
    public void PushState(float[] sample) {
        if (!connected) return;
        while (states.Count >= 2) states.TryDequeue(out _);
        states.Enqueue((sample, Clock));
    }
    public void PushWaterfall(WaterfallStream stream, float[] header, IReadOnlyList<float[]> lines) {
        if (!connected || !config.waterfalls) return;
        while (waterfall.Count >= 8) waterfall.TryDequeue(out _);
        waterfall.Enqueue(((int)stream + 1, LslSamples.Pack(config.bins, stream == WaterfallStream.Bth ? 1 : config.views, header, lines), Clock));
    }
    public bool TryReadCommand(out string command) => commands.TryDequeue(out command);
    public void Close() { stopped = true; connected = false; }
}
}
#endif
