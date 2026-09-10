using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using SonarTask.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace SonarTask.Services {
[Serializable]
public sealed class RunMetadata {
    public int ResultSchemaVersion = 3;
    public string RunId = "";
    public string SubjectId = "";
    public string OperatorId = "";
    public string Location = "";
    public string ExperimentPackage = "";
    public string ExperimentName = "";
    public string ExperimentVersion = "";
    public string ExperimentDefinitionHash = "";
    public string ApplicationVersion = "";
    public string Platform = "";
    public string StartedUtc = "";
    public string EndedUtc = "";
    public string TerminationReason = "";
    public string WaterfallPalette = "";
    public long RandomizationSeed;
    public bool Completed;
}

public sealed class ResultRecorder : MonoBehaviour {
    public const string CsvHeader = "Time,TotalElapsedTime,ExpElapsedTime,Phase,PhaseElapsedTimeSec,EventType,Signal_Type,Signal_ID,Signal_Bearing,Signal_Width,Signal_Alpha,BTH_SelectedSignals,BTH_SelectedBearing,Class_Selected,Class_Types,Class_Correct,Class_Confidence,Class_Feedback,Alert_Type,Alert_Text,Alert_Bearing,Alert_SignalId,RunId";

    public RunMetadata Metadata { get; private set; }
    public bool IsReady { get; private set; }

    StreamWriter writer;
    string metaPath;
    string definitionSnapshot = "";
    long sequence;
    bool finished;
    bool finishPending;
    Coroutine heartbeat, sender;
    readonly Queue<PendingEvent> pending = new();
    readonly List<ResultEvent> localWebEvents = new();

    sealed class PendingEvent { public ResultEvent Event; public long Sequence; }

    public void Begin(string package, ExperimentDefinition definition, string definitionJson, Action<bool,string> done) {
        definitionSnapshot = definitionJson ?? "";
        Metadata = new RunMetadata {
            RunId = Guid.NewGuid().ToString(),
            SubjectId = AppState.SubjectId,
            OperatorId = AppState.OperatorId,
            Location = AppState.Location,
            ExperimentPackage = package,
            ExperimentName = definition.Name,
            ExperimentVersion = definition.Version,
            ExperimentDefinitionHash = Sha256(definitionSnapshot),
            ApplicationVersion = Application.version,
            Platform = Application.platform.ToString(),
            StartedUtc = DateTime.UtcNow.ToString("O"),
            RandomizationSeed = definition.ExperimentSettings.RandomizationSeed,
            WaterfallPalette = definition.ExperimentSettings.WaterfallPalette,
            Completed = false,
            TerminationReason = "Active"
        };
        if (AppState.IsLocalWebDevelopment) {
            IsReady = true;
            Debug.Log("SONAR local WebGL development run started. Result events remain in browser memory and are not sent to the production API.");
            done(true, null);
        }
        else if (AppState.IsWeb) StartCoroutine(BeginWeb(done));
        else BeginDesktop(done);
    }

    void BeginDesktop(Action<bool,string> done) {
        try {
            var root = Path.Combine(RepositoryFactory.DesktopDataRoot, "Results");
            Directory.CreateDirectory(root);
            var stem = Safe(AppState.SubjectId) + "__" + Safe(AppState.SelectedPackage) + "__" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "__" + Metadata.RunId.Substring(0,8);
            var csv = Path.Combine(root, stem + ".csv");
            metaPath = Path.Combine(root, stem + ".meta.json");
            File.WriteAllText(Path.Combine(root, stem + ".experiment.json"), definitionSnapshot, new UTF8Encoding(false));
            writer = new StreamWriter(new FileStream(csv, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
            writer.WriteLine(CsvHeader);
            WriteMeta();
            IsReady = true;
            done(true, null);
        } catch (Exception e) {
            done(false, e.Message);
            Destroy(gameObject);
        }
    }

    IEnumerator BeginWeb(Action<bool,string> done) {
        var body = JsonConvert.SerializeObject(new { metadata = Metadata, definitionJson = definitionSnapshot });
        using var request = JsonRequest("/api/results/runs", "POST", body);
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success) {
            done(false, request.downloadHandler.text + " " + request.error);
            Destroy(gameObject);
            yield break;
        }
        IsReady = true;
        heartbeat = StartCoroutine(Heartbeat());
        done(true, null);
    }

    IEnumerator Heartbeat() {
        var wait = new WaitForSecondsRealtime(10);
        while (!finished) {
            yield return wait;
            using var request = new UnityWebRequest("/api/results/runs/" + Metadata.RunId + "/heartbeat", "POST") { downloadHandler = new DownloadHandlerBuffer() };
            yield return request.SendWebRequest();
        }
    }

    public void Record(ResultEvent e) {
        if (!IsReady || finished || finishPending) return;
        e.RunId = Metadata.RunId;
        sequence++;
        if (AppState.IsLocalWebDevelopment) {
            localWebEvents.Add(e);
        }
        else if (AppState.IsWeb) {
            pending.Enqueue(new PendingEvent { Event = e, Sequence = sequence });
            if (sender == null) sender = StartCoroutine(SendLoop());
        } else writer.WriteLine(ToCsv(e));
    }

    IEnumerator SendLoop() {
        while (pending.Count > 0) {
            var p = pending.Peek();
            var body = JsonConvert.SerializeObject(new { sequence = p.Sequence, @event = p.Event });
            bool sent = false;
            while (!sent && !finished) {
                using var request = JsonRequest("/api/results/runs/" + Metadata.RunId + "/events", "POST", body);
                yield return request.SendWebRequest();
                sent = request.result == UnityWebRequest.Result.Success;
                if (!sent) {
                    Debug.LogWarning("Result event upload retry: " + request.downloadHandler.text + " " + request.error);
                    yield return new WaitForSecondsRealtime(1);
                }
            }
            if (sent) pending.Dequeue(); else break;
        }
        sender = null;
        if (finishPending) yield return FinishWeb();
    }

    public void Finish(bool completed, string reason) {
        if (finished || finishPending || Metadata == null) return;
        Metadata.Completed = completed;
        Metadata.TerminationReason = reason;
        Metadata.EndedUtc = DateTime.UtcNow.ToString("O");
        finishPending = true;
        if (heartbeat != null) StopCoroutine(heartbeat);

        if (AppState.IsLocalWebDevelopment) {
            finished = true;
            finishPending = false;
            Debug.Log($"SONAR local WebGL development run finished: {localWebEvents.Count} result events captured in memory.");
            Destroy(gameObject);
        }
        else if (AppState.IsWeb) {
            if (sender == null) sender = StartCoroutine(SendLoop());
        } else {
            finished = true;
            try { writer?.Flush(); writer?.Dispose(); WriteMeta(); }
            catch (Exception e) { Debug.LogError(e); }
            Destroy(gameObject);
        }
    }

    IEnumerator FinishWeb() {
        if (finished) yield break;
        var body = JsonConvert.SerializeObject(new { completed = Metadata.Completed, terminationReason = Metadata.TerminationReason, endedUtc = Metadata.EndedUtc });
        using var request = JsonRequest("/api/results/runs/" + Metadata.RunId + "/finish", "POST", body);
        yield return request.SendWebRequest();
        if (request.result == UnityWebRequest.Result.Success) {
            finished = true;
            Destroy(gameObject);
        } else {
            Debug.LogWarning("Run finish upload failed; heartbeat timeout will preserve it as aborted: " + request.error);
            finishPending = false; // allow an explicit later retry if the object remains alive
        }
    }

    void WriteMeta() => File.WriteAllText(metaPath, JsonConvert.SerializeObject(Metadata, Formatting.Indented), new UTF8Encoding(false));

    static UnityWebRequest JsonRequest(string url, string method, string json) {
        var request = new UnityWebRequest(url, method);
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        return request;
    }

    public static string ToCsv(ResultEvent e) {
        var values = new[] {
            e.TimeUtc,
            e.TotalElapsedTimeSec.ToString("0.000", CultureInfo.InvariantCulture),
            e.ExpElapsedTimeSec.ToString("0.000", CultureInfo.InvariantCulture),
            e.Phase,
            e.PhaseElapsedTimeSec.ToString("0.000", CultureInfo.InvariantCulture),
            e.EventType, e.Signal_Type, e.Signal_ID, e.Signal_Bearing, e.Signal_Width, e.Signal_Alpha,
            e.BTH_SelectedSignals, e.BTH_SelectedBearing, e.Class_Selected, e.Class_Types, e.Class_Correct, e.Class_Confidence, e.Class_Feedback,
            e.Alert_Type, e.Alert_Text, e.Alert_Bearing, e.Alert_SignalId, e.RunId
        };
        for (int i=0; i<values.Length; i++) values[i] = Csv(values[i]);
        return string.Join(",", values);
    }

    static string Csv(string s) {
        s ??= "";
        return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    static string Safe(string s) {
        var b = new StringBuilder();
        foreach (var c in s ?? "") b.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        return b.Length == 0 ? "unknown" : b.ToString();
    }

    public static string Sha256(string text) {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    void OnApplicationQuit() {
        if (IsReady && !finished && !finishPending && !AppState.IsWeb) Finish(false, "ApplicationClosed");
        // Web cannot reliably complete network writes while a tab is closing. The server heartbeat reaper handles this case.
    }
}
}
