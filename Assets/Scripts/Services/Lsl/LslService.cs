using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SonarTask.Core;
using UnityEngine;

namespace SonarTask.Services {
public sealed class LslService : MonoBehaviour {
    static readonly JsonSerializerSettings MarkerJson = new JsonSerializerSettings { StringEscapeHandling = StringEscapeHandling.EscapeNonAscii };
    public static LslService Instance { get; private set; }
    ILslTransport transport = new NullLslTransport();
    RunMetadata metadata;
    ResolvedExperiment experiment;
    ExperimentRuntime runtime;
    LslStreamConfig config;
    string configKey;
    bool connected;
    double nextState;
    long lastSequence;
    public string OverrideMode { get; private set; } = "Experiment";
    public int BridgePort { get; private set; } = 16571;
    public bool Enabled { get; private set; }
    public string SourceId { get; private set; }
    public string Status => !Enabled ? "LSL: off" : transport.IsConnected ? "LSL: connected" : AppState.IsWeb ? "LSL: waiting for bridge" : "LSL: unavailable / starting";
    public static LslService Ensure() {
        if (!Instance) {
            var go = new GameObject("SONAR LSL");
            DontDestroyOnLoad(go);
            go.AddComponent<LslService>();
        }
        return Instance;
    }
    void Awake() {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SourceId = PlayerPrefs.GetString("SonarLslSourceId", "");
        if (string.IsNullOrEmpty(SourceId)) {
            SourceId = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString("SonarLslSourceId", SourceId);
            PlayerPrefs.Save();
        }
        LoadOverride();
    }
    void LoadOverride() {
        try {
            if (AppState.IsWeb) {
                OverrideMode = PlayerPrefs.GetString("SonarLslMode", "Experiment");
                BridgePort = PlayerPrefs.GetInt("SonarLslPort", 16571);
                if (Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out var url)) {
                    foreach (var part in url.Query.TrimStart('?').Split('&')) {
                        var pair = part.Split('=');
                        if (pair.Length != 2) continue;
                        string value = Uri.UnescapeDataString(pair[1]);
                        if (pair[0] == "lsl") OverrideMode = NormalizeMode(value);
                        if (pair[0] == "lslPort" && int.TryParse(value, out int port) && port > 0 && port <= 65535) BridgePort = port;
                    }
                    PlayerPrefs.SetString("SonarLslMode", OverrideMode);
                    PlayerPrefs.SetInt("SonarLslPort", BridgePort);
                    PlayerPrefs.Save();
                }
            } else {
                var path = Path.Combine(DesktopStorage.DataRoot, "lsl.json");
                if (File.Exists(path)) {
                    var json = JObject.Parse(File.ReadAllText(path));
                    OverrideMode = NormalizeMode((string)json["Mode"]);
                    int port = (int?)json["BridgePort"] ?? 16571;
                    if (port > 0 && port <= 65535) BridgePort = port;
                }
            }
        } catch (Exception e) { Debug.LogWarning("LSL configuration: " + e.Message); }
        if (!AppState.IsWeb) {
            var env = Environment.GetEnvironmentVariable("SONAR_LSL");
            if (!string.IsNullOrWhiteSpace(env)) OverrideMode = NormalizeMode(env);
        }
    }
    static string NormalizeMode(string value) => string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) ? "On" : string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ? "Off" : "Experiment";
    public void Configure(ResolvedExperiment resolved, RunMetadata meta) {
        experiment = resolved;
        Enabled = OverrideMode == "On" || OverrideMode == "Experiment" && resolved.LslEnabled;
        meta.LslEnabled = Enabled;
        meta.LslMode = Enabled ? (AppState.IsWeb ? "Bridge" : "Native") : "Off";
        meta.LslSourceId = SourceId;
        var rates = resolved.Phases.Select(p => p.WaterfallScanRateHz).Distinct().ToArray();
        config = new LslStreamConfig {
            sourceId = SourceId, appVersion = Application.version, platform = Application.platform.ToString(),
            bins = resolved.LslWaterfallBins, views = resolved.Phases.Max(p => p.LOFARViews.Count),
            bridgePort = BridgePort, stateRate = resolved.LslStateRateHz,
            waterfallRate = rates.Length == 1 ? rates[0] : 0, waterfalls = resolved.LslWaterfallStreams
        };
        string key = Enabled + JsonConvert.SerializeObject(config);
        if (key == configKey) return;
        transport.Close();
        transport = new NullLslTransport();
        configKey = key;
        if (!Enabled) return;
        try {
#if UNITY_WEBGL && !UNITY_EDITOR
            transport = new WebSocketLslTransport();
#else
            transport = new NativeLslTransport();
#endif
            transport.Open(config);
        } catch (Exception e) { Debug.LogWarning("LSL unavailable: " + e.Message); transport = new NullLslTransport(); }
    }
    public void BeginRun(RunMetadata meta) {
        metadata = meta;
        while (transport.TryReadCommand(out _)) { }
        runtime = null;
        lastSequence = 0;
        connected = false;
        nextState = 0;
        Marker("Run_Begin", new JObject {
            ["phases"] = JArray.FromObject(experiment.Phases.Select(p => p.Name)),
            ["lsl"] = JObject.FromObject(config)
        });
    }
    public void EndRun(bool completed, string reason) {
        Marker("Run_Finished", new JObject { ["completed"] = completed, ["reason"] = reason });
        metadata = null;
        runtime = null;
    }
    JObject Context(string eventType) => new JObject {
        ["eventType"] = eventType, ["runId"] = metadata.RunId, ["seq"] = lastSequence,
        ["package"] = metadata.ExperimentPackage, ["experimentName"] = metadata.ExperimentName,
        ["experimentHash"] = metadata.ExperimentDefinitionHash, ["seed"] = metadata.RandomizationSeed,
        ["subjectId"] = metadata.SubjectId, ["operatorId"] = metadata.OperatorId,
        ["phaseName"] = runtime?.Phase.Name ?? experiment.Phases[0].Name,
        ["phaseIndex"] = runtime?.PhaseIndex ?? 0,
        ["TotalElapsedTimeSec"] = runtime?.TotalElapsedSec ?? 0,
        ["ExpElapsedTimeSec"] = runtime?.ExperimentElapsedSec ?? 0,
        ["PhaseElapsedTimeSec"] = runtime?.PhaseElapsedSec ?? 0
    };
    public void Marker(string eventType, JObject extra = null, double age = 0) {
        if (!Enabled || metadata == null) return;
        try {
            var json = Context(eventType);
            if (extra != null) json.Merge(extra);
            transport.PushMarker(JsonConvert.SerializeObject(json, Formatting.None, MarkerJson), transport.Clock - Math.Max(0, age));
        } catch (Exception e) { Debug.LogWarning("LSL marker: " + e.Message); }
    }
    public void PublishEvent(ResultEvent e, long sequence) {
        if (!Enabled || metadata == null) return;
        lastSequence = sequence;
        var fields = JObject.FromObject(e);
        fields.Remove("EventType");
        foreach (var property in fields.Properties().ToArray())
            if (property.Value.Type == JTokenType.Null || property.Value.Type == JTokenType.String && (string)property.Value == "") property.Remove();
        // Event clocks are authoritative even when the runtime has already changed phase.
        fields["phaseName"] = e.Phase;
        fields["phaseIndex"] = experiment.Phases.FindIndex(p => p.Name == e.Phase);
        double age = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency - e.CaptureMonotonic;
        Marker(e.EventType, fields, age);
    }
    public void Axes(ExperimentRuntime rt) {
        runtime = rt;
        Marker("Waterfall_Axes", new JObject {
            ["bth"] = JObject.FromObject(new { rt.Phase.BTHView.DegStart, rt.Phase.BTHView.DegEnd }),
            ["lofar"] = JArray.FromObject(rt.Phase.LOFARViews.Select(v => new { v.HzStart, v.HzEnd })),
            ["bins"] = experiment.LslWaterfallBins
        });
    }
    public void Tick(ExperimentRuntime rt, ResultRecorder recorder, float selectedBearing) {
        runtime = rt;
        if (!Enabled || metadata == null) return;
        try {
            if (connected != transport.IsConnected) {
                connected = transport.IsConnected;
                recorder.Record(ResultEvent.Create(rt, connected ? "LSL_Connected" : "LSL_Disconnected"));
                if (connected) Axes(rt);
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (rt.State != RunState.Ended && now >= nextState) {
                nextState = now + 1.0 / experiment.LslStateRateHz;
                transport.PushState(new[] { rt.TotalElapsedSec, rt.ExperimentElapsedSec, (float)rt.PhaseIndex, rt.PhaseElapsedSec, rt.State == RunState.Paused ? 1f : 0f, selectedBearing });
            }
        } catch (Exception e) { Debug.LogWarning("LSL state: " + e.Message); }
    }
    public void PublishScans(ExperimentRuntime rt, float[] bth, System.Collections.Generic.List<float[]> lofar) {
        if (!Enabled || metadata == null || !experiment.LslWaterfallStreams) return;
        try {
            transport.PushWaterfall(WaterfallStream.Bth, new[] { (float)rt.PhaseIndex, rt.ScanIndex }, new[] { bth });
            transport.PushWaterfall(WaterfallStream.Lofar, new[] { (float)rt.PhaseIndex, rt.ScanIndex, (float)lofar.Count }, lofar);
        } catch (Exception e) { Debug.LogWarning("LSL waterfall: " + e.Message); }
    }
    public bool TryReadCommand(out string json) => transport.TryReadCommand(out json);
    public void OnLslCommand(string json) { if (transport is WebSocketLslTransport web) web.Receive(json); }
    public void OnLslStatus(string value) { if (transport is WebSocketLslTransport web) web.SetConnected(value == "connected"); }
    void OnDestroy() { transport.Close(); if (Instance == this) Instance = null; }
}
}
