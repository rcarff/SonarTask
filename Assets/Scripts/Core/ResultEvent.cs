using System;
using System.Collections.Generic;
using System.Linq;

namespace SonarTask.Core {
[Serializable]
public sealed class ResultEvent {
    public string TimeUtc = "";
    public float TotalElapsedTimeSec;
    public float ExpElapsedTimeSec;
    public float PhaseElapsedTimeSec;
    public string Phase = "";
    public string EventType = "";
    public string Signal_Type = "";
    public string Signal_ID = "";
    public string Signal_Bearing = "";
    public string Signal_Width = "";
    public string Signal_Alpha = "";
    public string BTH_SelectedSignals = "";
    public string BTH_SelectedBearing = "";
    public string Class_Selected = "";
    public string Class_Types = "";
    public string Class_Correct = "";
    public string Class_Confidence = "";
    public string Class_Feedback = "";
    public string Alert_Type = "";
    public string Alert_Text = "";
    public string Alert_Bearing = "";
    public string Alert_SignalId = "";
    public string RunId = "";

    public static ResultEvent Create(ExperimentRuntime runtime, string eventType) => new() {
        TimeUtc = DateTime.UtcNow.ToString("O"),
        TotalElapsedTimeSec = runtime.TotalElapsedSec,
        ExpElapsedTimeSec = runtime.ExperimentElapsedSec,
        PhaseElapsedTimeSec = runtime.PhaseElapsedSec,
        Phase = runtime.Phase.Name,
        EventType = eventType
    };

    public static string JoinSignals(IEnumerable<ResolvedSignal> signals) =>
        string.Join(";", signals.Select(x => x.ID));

    public static string JoinClasses(IEnumerable<ResolvedSignal> signals) =>
        string.Join(";", signals.Select(x => x.Classification));
}
}
