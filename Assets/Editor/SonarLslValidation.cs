#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SonarTask.Core;
using SonarTask.Services;
using SonarTask.UI;
using UnityEditor;
using UnityEngine;

namespace SonarTask.EditorTools {
public static class SonarLslValidation {
    static void Check(bool value, string message) { if (!value) throw new Exception("LSL validation: " + message); }
    [MenuItem("SONAR/Validate LSL Integration")]
    public static void Validate() {
        SonarLslPluginSetup.Configure();
        var importer = AssetImporter.GetAtPath("Assets/Plugins/x86_64/lsl.dll") as PluginImporter;
        Debug.Log("LSL importer: " + (importer == null ? "missing" : "editor=" + importer.GetCompatibleWithEditor() + " OS=" + importer.GetEditorData("OS") + " CPU=" + importer.GetEditorData("CPU")));
        SonarValidationMenu.ValidateAll();
        SonarValidationMenu.Determinism();
        foreach (string file in Directory.GetFiles("Experiments", "experiment.json", SearchOption.AllDirectories)) {
            var d = JsonConvert.DeserializeObject<ExperimentDefinition>(File.ReadAllText(file));
            Check(ExperimentValidator.Validate(d).Count == 0, file + " invalid");
            var resolved = ExperimentResolver.Resolve(d);
            Check(resolved.LslEnabled == (d.ExperimentSettings.LslEnabled == YesNo.Yes), "enabled resolution");
            d.ExperimentSettings.LslEnabled = null;
            d.ExperimentSettings.LslStateRateHz = 900;
            Check(!ExperimentResolver.Resolve(d).LslEnabled && ExperimentResolver.Resolve(d).LslStateRateHz == 250, "legacy defaults and rate clamping");
            d.ExperimentSettings.LslWaterfallBins = 128;
            Check(ExperimentValidator.Validate(d).Any(e => e.Contains("LslWaterfallBins")), "invalid bins accepted");
        }
        var line = Enumerable.Range(0, 1024).Select(i => i / 1024f).ToArray();
        var packed = LslSamples.Pack(256, 2, new[] { 1f, 23f, 1f }, new[] { line });
        Check(packed.Length == 515 && packed[0] == 1 && packed[1] == 23 && packed[2] == 1, "fixed LOFAR shape/header");
        Check(packed[3] == (line[0] + line[1] + line[2] + line[3]) / 4, "average downsampling");
        Check(packed.Skip(259).All(float.IsNaN), "unused views must be NaN");
        Check(LslSamples.Pack(1024, 1, Array.Empty<float>(), new[] { line }).SequenceEqual(line), "full resolution changed");
        MarkerLifecycle();
        NativeRoundTrip();
        ValidateWebScripts();
        Debug.Log("SONAR LSL integration validation passed.");
    }
    static void ValidateWebScripts() {
        var settings = new UnityEditor.Build.Player.ScriptCompilationSettings {
            target = BuildTarget.WebGL, group = BuildTargetGroup.WebGL,
            options = UnityEditor.Build.Player.ScriptCompilationOptions.None
        };
        var result = UnityEditor.Build.Player.PlayerBuildInterface.CompilePlayerScripts(settings, "Temp/LslWebScripts");
        Check(result.assemblies != null && result.assemblies.Count > 0, "WebGL player script compilation failed");
        Debug.Log("LSL WebGL player script compilation passed.");
    }
    static void MarkerLifecycle() {
        var definition = JsonConvert.DeserializeObject<ExperimentDefinition>(File.ReadAllText("Experiments/LslDemo/experiment.json"));
        var go = new GameObject("LSL validation");
        var service = go.AddComponent<LslService>();
        var field = typeof(LslService).GetField("transport", BindingFlags.NonPublic | BindingFlags.Instance);
        var capture = new Capture();
        try {
            service.Configure(ExperimentResolver.Resolve(definition), new RunMetadata());
            ((ILslTransport)field.GetValue(service)).Close();
            field.SetValue(service, capture);
            typeof(LslService).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, service);
            var meta = new RunMetadata { RunId = "first", ExperimentPackage = "LslDemo", ExperimentName = "First", SubjectId = "subject" };
            service.BeginRun(meta);
            service.PublishEvent(new ResultEvent { EventType = "LSL_Alert_Shown", Phase = definition.ExperimentSettings.PhaseOrder[0], Alert_Text = "<b>x</b>", LslCommandId = "py-42" }, 7);
            service.EndRun(false, "OperatorExit");
            meta = new RunMetadata { RunId = "second", ExperimentPackage = "Other", ExperimentName = "Second" };
            service.BeginRun(meta);
            Check((string)capture.Markers[0]["eventType"] == "Run_Begin", "begin ordering");
            Check((long)capture.Markers[1]["seq"] == 7 && (string)capture.Markers[1]["id"] == "py-42", "sequence/correlation");
            Check((string)capture.Markers[2]["eventType"] == "Run_Finished" && !(bool)capture.Markers[2]["completed"], "abort marker");
            Check((string)capture.Markers[3]["runId"] == "second" && (string)capture.Markers[3]["experimentName"] == "Second", "stale run context");
            string csv = ResultRecorder.ToCsv(new ResultEvent { LslCommandId = "not-in-csv", Alert_Text = "test" });
            Check(!csv.Contains("not-in-csv") && csv.Split(',').Length == ResultRecorder.CsvHeader.Split(',').Length, "CSV header changed");
            Commands(service, capture, definition);
        } finally { UnityEngine.Object.DestroyImmediate(go); }
    }
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
    static void Commands(LslService service, Capture capture, ExperimentDefinition definition) {
        var go = new GameObject("LSL command validation");
        var recorder = go.AddComponent<ResultRecorder>();
        var screen = go.AddComponent<SonarTaskScreen>();
        var rt = new ExperimentRuntime(ExperimentResolver.Resolve(definition));
        var text = new GameObject("alert text", typeof(RectTransform), typeof(UnityEngine.UI.Text));
        var buttons = new List<GameObject>();
        var memory = new MemoryStream();
        using var writer = new StreamWriter(memory);
        try {
            Set(recorder, "<IsReady>k__BackingField", true);
            Set(recorder, "<Metadata>k__BackingField", new RunMetadata { RunId = "commands" });
            Set(recorder, "writer", writer);
            Set(screen, "rt", rt); Set(screen, "phase", rt.Phase); Set(screen, "recorder", recorder);
            Set(screen, "recordingReady", true); Set(screen, "alertText", text.GetComponent<UnityEngine.UI.Text>());
            foreach (string field in new[] { "startPause", "exit", "instructionsButton" }) {
                var button = new GameObject(field, typeof(RectTransform), typeof(UnityEngine.UI.Button));
                buttons.Add(button); Set(screen, field, button.GetComponent<UnityEngine.UI.Button>());
            }
            service.BeginRun(new RunMetadata { RunId = "commands", ExperimentPackage = "LslDemo" });
            var drain = typeof(SonarTaskScreen).GetMethod("DrainLslCommands", BindingFlags.NonPublic | BindingFlags.Instance);
            void Send(string json) { capture.Commands.Enqueue(json); drain.Invoke(screen, null); }
            Send("{\"cmd\":\"start\"}");
            Check(rt.State == RunState.Running, "external start failed");
            Send("{\"cmd\":\"alert\",\"text\":\"<b>x</b>\",\"bearing\":-225,\"durationSec\":900,\"id\":\"test-id\"}");
            var alert = capture.Markers.Last(e => (string)e["eventType"] == "LSL_Alert_Shown");
            Check((string)alert["Alert_Bearing"] == "135" && (string)alert["id"] == "test-id", "alert normalization/correlation failed");
            Check(text.GetComponent<UnityEngine.UI.Text>().text.Contains("<b>x</b>") && !text.GetComponent<UnityEngine.UI.Text>().supportRichText, "external rich text not literal");
            var activeAlerts = (System.Collections.IList)typeof(SonarTaskScreen).GetField("alerts", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(screen);
            var shown = (ResolvedAlert)activeAlerts[0].GetType().GetField("alert").GetValue(activeAlerts[0]);
            Check(shown.DurationSec == 600, "alert duration not clamped");
            Send("{\"cmd\":\"pause\"}");
            Check(rt.State == RunState.Paused, "external pause failed");
            Send("{\"cmd\":\"alert\",\"text\":\"paused\",\"bearing\":135}");
            Check((string)capture.Markers.Last()["Alert_Type"] == "Rejected:Paused", "paused alert accepted");
            Send("{\"cmd\":\"resume\"}");
            rt.Phase.AlertsEnabled = YesNo.No;
            Send("{\"cmd\":\"alert\",\"text\":\"disabled\",\"bearing\":135}");
            Check((string)capture.Markers.Last()["Alert_Type"] == "Rejected:AlertsDisabled", "disabled alert accepted");
            rt.Phase.AlertsEnabled = YesNo.Yes;
            Send("{\"cmd\":\"alert\",\"text\":\"missing bearing\"}");
            Check((string)capture.Markers.Last()["Alert_Type"] == "Rejected:Invalid", "invalid alert accepted");
            rt.Experiment.LslAllowExternalAlerts = false;
            Send("{\"cmd\":\"alert\",\"text\":\"author disabled\",\"bearing\":135}");
            Check((string)capture.Markers.Last()["Alert_Type"] == "Rejected:AlertsDisabled", "author alert permission bypassed");
            rt.Experiment.LslAllowExternalAlerts = true;
            Send("{\"cmd\":\"marker\",\"text\":\"annotation\"}");
            Check((string)capture.Markers.Last()["eventType"] == "LSL_Marker", "annotation event missing");
            rt.Phase.DisablePauseAndExit = true;
            Send("{\"cmd\":\"pause\"}");
            Check(rt.State == RunState.Running, "pause bypassed phase gate");
            typeof(ExperimentRuntime).GetField("<PhaseElapsedSec>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(rt, 600f);
            typeof(SonarTaskScreen).GetMethod("ExpireAlerts", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(screen, null);
            Check(activeAlerts.Count == 0, "external alert did not expire on phase time");
            writer.Flush();
            Check(memory.Length > 0, "commands were not recorded to CSV");
        } finally {
            UnityEngine.Object.DestroyImmediate(text);
            foreach (var button in buttons) UnityEngine.Object.DestroyImmediate(button);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
    static void NativeRoundTrip() {
        var native = new NativeLslTransport();
        string source = "sonar-validation-" + Guid.NewGuid();
        native.Open(new LslStreamConfig { sourceId = source, appVersion = "test", platform = "Editor", bins = 256, views = 1, stateRate = 30, waterfallRate = 20, waterfalls = true });
        try {
            var until = DateTime.UtcNow.AddSeconds(5);
            while (!native.IsConnected && DateTime.UtcNow < until) Thread.Sleep(10);
            Check(native.IsConnected, "native liblsl could not load: " + native.Error);
            var found = LSL.LSL.resolve_stream("source_id", source + "-Markers", 1, 3);
            Check(found.Length > 0, "native marker outlet discovery");
            using var inlet = new LSL.StreamInlet(found[0]);
            foreach (var info in found) info.Dispose();
            native.PushMarker("{\"eventType\":\"before-subscription\"}", native.Clock);
            inlet.open_stream(3);
            var startup = new string[1];
            Check(inlet.pull_sample(startup, 3) > 0 && startup[0].Contains("before-subscription"), "startup marker lost during discovery");
            native.PushMarker("{\"eventType\":\"validation\"}", native.Clock);
            var sample = new string[1];
            Check(inlet.pull_sample(sample, 3) > 0 && sample[0].Contains("validation"), "native marker round trip");
        } finally { native.Close(); }
    }
    sealed class Capture : ILslTransport {
        public readonly List<JObject> Markers = new();
        public readonly Queue<string> Commands = new();
        public bool IsConnected => true;
        public double Clock => 1;
        public void Open(LslStreamConfig info) { }
        public void PushMarker(string json, double timestamp) => Markers.Add(JObject.Parse(json));
        public void PushState(float[] sample) { }
        public void PushWaterfall(WaterfallStream stream, float[] header, IReadOnlyList<float[]> lines) { }
        public bool TryReadCommand(out string command) { command = Commands.Count > 0 ? Commands.Dequeue() : null; return command != null; }
        public void Close() { }
    }
}
}
#endif
