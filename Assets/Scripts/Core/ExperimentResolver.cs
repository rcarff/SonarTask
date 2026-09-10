using System;
using System.Collections.Generic;
using System.Linq;

namespace SonarTask.Core {
public static class ExperimentResolver {
    public static ResolvedExperiment Resolve(ExperimentDefinition definition) {
        var errors = ExperimentValidator.Validate(definition);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));

        var result = new ResolvedExperiment { Source = definition, ShowResultsAtEnd = definition.ExperimentSettings.ShowResultsAtEnd };
        int autoId = 1;

        for (int phaseIndex = 0; phaseIndex < definition.ExperimentSettings.PhaseOrder.Count; phaseIndex++) {
            string phaseName = definition.ExperimentSettings.PhaseOrder[phaseIndex];
            var source = definition.Phases[phaseName];
            var defaults = definition.ExperimentSettings;
            var phase = new ResolvedPhase {
                Name = phaseName,
                Index = phaseIndex,
                AutoStart = source.AutoStart ?? defaults.AutoStart,
                BackgroundAudio = source.BackgroundAudio ?? defaults.BackgroundAudio,
                BackgroundAudioVolume = source.BackgroundAudioVolume ?? defaults.BackgroundAudioVolume,
                TrainingAreaEnabled = source.TrainingAreaEnabled ?? defaults.TrainingAreaEnabled,
                FeedbackEnabled = source.FeedbackEnabled ?? defaults.FeedbackEnabled,
                ClassificationTallyEnabled = source.ClassificationTallyEnabled ?? defaults.ClassificationTallyEnabled,
                AlertsEnabled = source.AlertsEnabled ?? defaults.AlertsEnabled,
                BTHOverlapAudio = source.BTHOverlapAudio ?? defaults.BTHOverlapAudio,
                SignalAudioEnabled = source.SignalAudioEnabled ?? defaults.SignalAudioEnabled,
                HideLofarSignalsIfNoSelectedBTH = source.HideLofarSignalsIfNoSelectedBTH ?? defaults.HideLofarSignalsIfNoSelectedBTH,
                JitterFactor = source.JitterFactor ?? defaults.JitterFactor,
                BackgroundNoise = source.BackgroundNoise ?? defaults.BackgroundNoise,
                BTHView = Clone(source.BTHView ?? defaults.BTHView),
                LOFARViews = (source.LOFARViews ?? defaults.LOFARViews).Select(Clone).ToList(),
                SignalDurationSec = source.SignalDurationSec ?? defaults.SignalDurationSec,
                AlertOffsetSec = source.AlertOffsetSec ?? defaults.AlertOffsetSec,
                AlertDurationSec = source.AlertDurationSec ?? defaults.AlertDurationSec,
                ShowElapsedTime = source.ShowElapsedTime ?? defaults.ShowElapsedTime,
                ShowRemainingTime = source.ShowRemainingTime ?? defaults.ShowRemainingTime,
                WaterfallScanRateHz = source.WaterfallScanRateHz ?? defaults.WaterfallScanRateHz,
                WaterfallPalette = source.WaterfallPalette ?? defaults.WaterfallPalette
            };

            foreach (var kv in defaults.SignalDefinitions)
                phase.AvailableSignals[kv.Key] = Clone(kv.Value);

            foreach (var ov in source.SignalDefinitions) {
                if (!phase.AvailableSignals.TryGetValue(ov.Name, out var baseDefinition))
                    baseDefinition = new SignalDefinition { Name = ov.Name };
                var merged = Clone(baseDefinition);
                if (ov.TrainingAllowed.HasValue) merged.TrainingAllowed = ov.TrainingAllowed.Value;
                if (ov.AudioFile != null) merged.AudioFile = ov.AudioFile;
                if (ov.AudioVolume.HasValue) merged.AudioVolume = ov.AudioVolume.Value;
                if (ov.Classification != null) merged.Classification = ov.Classification;
                if (ov.SignalStrength != null) merged.SignalStrength = Clone(ov.SignalStrength);
                if (ov.FreqTuples != null) merged.FreqTuples = ov.FreqTuples.Select(Clone).ToList();
                phase.AvailableSignals[ov.Name] = merged;
            }

            // The Phase SignalDefinitions list defines what appears in Training and the
            // classification palette. If omitted, infer that set from actual signal instances.
            var phaseSignalNames = source.SignalDefinitions.Count > 0
                ? source.SignalDefinitions.Select(x => x.Name).ToList()
                : source.Signals.Select(x => x.Signal).Distinct().ToList();
            foreach (string name in phaseSignalNames) {
                if (!phase.AvailableSignals.TryGetValue(name, out var sd)) continue;
                phase.PhaseSignalDefinitions.Add(Clone(sd));
                if (!string.IsNullOrWhiteSpace(sd.Classification) && !phase.ClassificationOrder.Contains(sd.Classification))
                    phase.ClassificationOrder.Add(sd.Classification);
            }

            foreach (var instance in source.Signals) {
                var sd = Clone(phase.AvailableSignals[instance.Signal]);
                var resolved = new ResolvedSignal {
                    SignalType = instance.Signal,
                    ID = string.IsNullOrWhiteSpace(instance.ID) ? "SIG-" + (autoId++).ToString("D4") : instance.ID,
                    FriendlyName = sd.Name,
                    AppearSec = instance.AppearSec,
                    Bearing = Norm(instance.Bearing),
                    RateDegSec = instance.RateDegSec,
                    DurationSec = instance.DurationSec ?? phase.SignalDurationSec,
                    AudioFile = instance.AudioFile ?? sd.AudioFile,
                    AudioVolume = instance.AudioVolume ?? sd.AudioVolume,
                    Classification = instance.Classification ?? sd.Classification,
                    SignalStrength = Clone(instance.SignalStrength ?? sd.SignalStrength),
                    FreqTuples = (instance.FreqTuples ?? sd.FreqTuples).Select(Clone).ToList(),
                    TrainingAllowed = sd.TrainingAllowed == YesNo.Yes,
                    AlertEnabled = (instance.AlertEnabled ?? YesNo.No) == YesNo.Yes,
                    AlertText = instance.AlertText ?? (instance.Classification ?? sd.Classification),
                    AlertAppearSec = instance.AppearSec + (instance.AlertOffsetSec ?? phase.AlertOffsetSec),
                    AlertBearing = NormInt(instance.AlertBearing ?? (int)Math.Round(instance.Bearing)),
                    AlertDurationSec = instance.AlertDurationSec ?? phase.AlertDurationSec,
                    AlertType = string.IsNullOrWhiteSpace(instance.AlertType) ? "VALID" : instance.AlertType.ToUpperInvariant()
                };
                phase.Signals.Add(resolved);
                if (!phase.ClassificationOrder.Contains(resolved.Classification))
                    phase.ClassificationOrder.Add(resolved.Classification);
                if (resolved.AlertEnabled) phase.Alerts.Add(new ResolvedAlert {
                    Text = resolved.AlertText,
                    AppearSec = resolved.AlertAppearSec,
                    Bearing = resolved.AlertBearing,
                    DurationSec = resolved.AlertDurationSec,
                    Type = resolved.AlertType,
                    SignalID = resolved.ID
                });
            }

            foreach (var a in source.Alerts) phase.Alerts.Add(new ResolvedAlert {
                Text = a.Text,
                AppearSec = a.AppearSec,
                Bearing = NormInt(a.Bearing),
                DurationSec = a.DurationSec ?? phase.AlertDurationSec,
                Type = string.IsNullOrWhiteSpace(a.Type) ? "INVALID" : a.Type.ToUpperInvariant(),
                SignalID = a.SignalID ?? ""
            });

            phase.DurationSec = phase.Signals.Max(s => s.AppearSec + s.DurationSec);
            result.Phases.Add(phase);
        }

        return result;
    }

    public static float Norm(float bearing) {
        bearing %= 360;
        if (bearing < 0) bearing += 360;
        return bearing;
    }

    public static int NormInt(int bearing) {
        bearing %= 360;
        if (bearing < 0) bearing += 360;
        return bearing;
    }

    static BTHView Clone(BTHView x) => new() {
        DegStart=x.DegStart, DegEnd=x.DegEnd, DegTicksEvery=x.DegTicksEvery, DegLabelsEvery=x.DegLabelsEvery,
        TimeStart=x.TimeStart, TimeEnd=x.TimeEnd, TimeTicksEvery=x.TimeTicksEvery, TimeLabelsEvery=x.TimeLabelsEvery,
        SelectionWidth=x.SelectionWidth
    };
    static LOFARView Clone(LOFARView x) => new() {
        HzStart=x.HzStart, HzEnd=x.HzEnd, HzTicksEvery=x.HzTicksEvery, HzLabelsEvery=x.HzLabelsEvery,
        TimeStart=x.TimeStart, TimeEnd=x.TimeEnd, TimeTicksEvery=x.TimeTicksEvery, TimeLabelsEvery=x.TimeLabelsEvery
    };
    static SignalStrength Clone(SignalStrength x) => new() { Width=x.Width, Alpha=x.Alpha };
    static FreqTuple Clone(FreqTuple x) => new() { Frequency=x.Frequency, Width=x.Width, Alpha=x.Alpha };
    static SignalDefinition Clone(SignalDefinition x) => new() {
        Name=x.Name, TrainingAllowed=x.TrainingAllowed, AudioFile=x.AudioFile, AudioVolume=x.AudioVolume,
        Classification=x.Classification, SignalStrength=Clone(x.SignalStrength ?? new SignalStrength()),
        FreqTuples=(x.FreqTuples ?? new List<FreqTuple>()).Select(Clone).ToList()
    };
}

public static class ExperimentValidator {
    public static List<string> Validate(ExperimentDefinition d) {
        var errors = new List<string>();
        if (d == null) { errors.Add("Experiment is null."); return errors; }
        if (d.SchemaVersion != 1) errors.Add("SchemaVersion 1 is required.");
        if (string.IsNullOrWhiteSpace(d.Name)) errors.Add("Name is required.");
        if (d.ExperimentSettings == null) { errors.Add("ExperimentSettings is required."); return errors; }
        var defaults = d.ExperimentSettings;
        defaults.SignalDefinitions ??= new Dictionary<string, SignalDefinition>();
        defaults.PhaseOrder ??= new List<string>();
        if (defaults.RandomizationSeed == 0) errors.Add("RandomizationSeed must be a non-zero integer.");
        if (defaults.SignalDurationSec <= 0) errors.Add("SignalDurationSec must be > 0.");
        if (defaults.WaterfallScanRateHz <= 0) errors.Add("WaterfallScanRateHz must be > 0.");
        defaults.LOFARViews ??= new List<LOFARView>();
        if (defaults.LOFARViews.Count == 0) errors.Add("ExperimentSettings.LOFARViews must contain at least one view. No implicit LOFAR view is created.");
        else ValidateLofarViews(defaults.LOFARViews, "ExperimentSettings", errors);
        if (defaults.PhaseOrder.Count == 0) errors.Add("PhaseOrder must not be empty.");
        if (d.Phases == null) { errors.Add("Phases is required."); return errors; }

        foreach (var kv in defaults.SignalDefinitions) {
            if (kv.Value == null) { errors.Add($"SignalDefinition '{kv.Key}' is null."); continue; }
            if (string.IsNullOrWhiteSpace(kv.Value.Name)) errors.Add($"SignalDefinition '{kv.Key}' requires Name.");
            if (string.IsNullOrWhiteSpace(kv.Value.Classification)) errors.Add($"SignalDefinition '{kv.Key}' requires Classification.");
            ValidateStrength(kv.Value.SignalStrength, $"SignalDefinition '{kv.Key}'", errors);
            ValidateFreqs(kv.Value.FreqTuples, $"SignalDefinition '{kv.Key}'", errors);
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (string phaseName in defaults.PhaseOrder) {
            if (!d.Phases.TryGetValue(phaseName, out var phase) || phase == null) { errors.Add($"Missing phase '{phaseName}'."); continue; }
            phase.SignalDefinitions ??= new List<SignalOverride>();
            phase.Signals ??= new List<SignalInstance>();
            phase.Alerts ??= new List<AlertDefinition>();
            if (phase.LOFARViews != null) {
                if (phase.LOFARViews.Count == 0) errors.Add($"Phase '{phaseName}' LOFARViews override must contain at least one view.");
                else ValidateLofarViews(phase.LOFARViews, $"Phase '{phaseName}'", errors);
            }

            float effectiveScanRate = phase.WaterfallScanRateHz ?? defaults.WaterfallScanRateHz;
            var effectiveBth = phase.BTHView ?? defaults.BTHView;
            var effectiveLofars = phase.LOFARViews ?? defaults.LOFARViews;
            ValidateWaterfallRows(effectiveBth.TimeStart, effectiveBth.TimeEnd, effectiveScanRate, $"Phase '{phaseName}' BTHView", errors);
            for (int viewIndex = 0; viewIndex < effectiveLofars.Count; viewIndex++)
                ValidateWaterfallRows(effectiveLofars[viewIndex].TimeStart, effectiveLofars[viewIndex].TimeEnd, effectiveScanRate, $"Phase '{phaseName}' LOFARViews[{viewIndex}]", errors);

            if (phase.Signals.Count == 0) { errors.Add($"Phase '{phaseName}' must contain signals so duration can be computed."); continue; }

            var available = new HashSet<string>(defaults.SignalDefinitions.Keys, StringComparer.Ordinal);
            foreach (var ov in phase.SignalDefinitions) {
                if (ov == null || string.IsNullOrWhiteSpace(ov.Name)) { errors.Add($"Phase '{phaseName}' contains a SignalDefinition without Name."); continue; }
                bool isNew = !available.Contains(ov.Name);
                if (isNew) {
                    if (!ov.TrainingAllowed.HasValue || ov.AudioFile == null || !ov.AudioVolume.HasValue || string.IsNullOrWhiteSpace(ov.Classification) || ov.SignalStrength == null || ov.FreqTuples == null)
                        errors.Add($"Phase '{phaseName}' defines new signal '{ov.Name}'; all signal-definition fields must be supplied.");
                    ValidateStrength(ov.SignalStrength, $"Phase '{phaseName}' signal '{ov.Name}'", errors);
                    ValidateFreqs(ov.FreqTuples, $"Phase '{phaseName}' signal '{ov.Name}'", errors);
                }
                available.Add(ov.Name);
            }

            float phaseDefaultDuration = phase.SignalDurationSec ?? defaults.SignalDurationSec;
            float phaseEnd = 0;
            foreach (var s in phase.Signals) {
                if (s == null) { errors.Add($"Phase '{phaseName}' contains a null signal."); continue; }
                if (!available.Contains(s.Signal)) errors.Add($"Phase '{phaseName}' references undefined signal '{s.Signal}'.");
                if (s.AppearSec < 0) errors.Add($"Signal '{s.Signal}' in phase '{phaseName}' has negative AppearSec.");
                if (s.Bearing < 0 || s.Bearing > 360) errors.Add($"Signal '{s.Signal}' in phase '{phaseName}' bearing must be 0..360.");
                float duration = s.DurationSec ?? phaseDefaultDuration;
                if (duration <= 0) errors.Add($"Signal '{s.Signal}' in phase '{phaseName}' duration must be > 0.");
                phaseEnd = Math.Max(phaseEnd, s.AppearSec + Math.Max(0, duration));
                if (!string.IsNullOrWhiteSpace(s.ID) && !ids.Add(s.ID)) errors.Add($"Duplicate Signal ID '{s.ID}'.");
                if (s.AudioVolume.HasValue && (s.AudioVolume < 0 || s.AudioVolume > 1)) errors.Add($"Signal '{s.Signal}' AudioVolume must be 0..1.");
                ValidateStrength(s.SignalStrength, $"Signal '{s.Signal}' instance", errors);
                ValidateFreqs(s.FreqTuples, $"Signal '{s.Signal}' instance", errors, optional:true);
            }

            foreach (var a in phase.Alerts) {
                if (a.AppearSec < 0) errors.Add($"Phase '{phaseName}' alert '{a.Text}' has negative AppearSec.");
                if (a.AppearSec > phaseEnd) errors.Add($"Phase '{phaseName}' alert '{a.Text}' occurs after the computed phase end ({phaseEnd:0.###}s).");
                if (a.Bearing < 0 || a.Bearing > 360) errors.Add($"Phase '{phaseName}' alert '{a.Text}' bearing must be 0..360.");
            }
        }
        return errors;
    }



    static void ValidateWaterfallRows(float timeStart, float timeEnd, float scanRate, string where, List<string> errors) {
        float historySec = Math.Abs(timeEnd - timeStart);
        int rows = (int)Math.Round(historySec * Math.Max(1f, scanRate));
        if (rows < 2) errors.Add($"{where} history at {scanRate:0.###} Hz produces fewer than 2 scan rows.");
        if (rows > 4096) errors.Add($"{where} requires {rows} scan rows ({historySec:0.###} sec at {scanRate:0.###} Hz); maximum supported is 4096.");
    }

    static void ValidateLofarViews(List<LOFARView> views, string where, List<string> errors) {
        for (int i = 0; i < views.Count; i++) {
            var v = views[i];
            if (v == null) { errors.Add($"{where} LOFARViews[{i}] is null."); continue; }
            if (v.HzEnd <= v.HzStart) errors.Add($"{where} LOFARViews[{i}] HzEnd must be greater than HzStart.");
            if (v.TimeEnd <= v.TimeStart) errors.Add($"{where} LOFARViews[{i}] TimeEnd must be greater than TimeStart.");
            if (v.HzTicksEvery <= 0 || v.HzLabelsEvery <= 0) errors.Add($"{where} LOFARViews[{i}] Hz tick/label intervals must be > 0.");
            if (v.TimeTicksEvery <= 0 || v.TimeLabelsEvery <= 0) errors.Add($"{where} LOFARViews[{i}] time tick/label intervals must be > 0.");
        }
    }

    static void ValidateStrength(SignalStrength x, string where, List<string> errors) {
        if (x == null) return;
        if (x.Width <= 0) errors.Add(where + " SignalStrength.Width must be > 0.");
        if (x.Alpha < 0 || x.Alpha > 1) errors.Add(where + " SignalStrength.Alpha must be 0..1.");
    }

    static void ValidateFreqs(List<FreqTuple> list, string where, List<string> errors, bool optional=false) {
        if (list == null) { if (!optional) errors.Add(where + " requires FreqTuples."); return; }
        if (!optional && list.Count == 0) errors.Add(where + " requires at least one FreqTuple.");
        foreach (var f in list) {
            if (f.Frequency < 0) errors.Add(where + " frequency must be >= 0.");
            if (f.Width <= 0) errors.Add(where + " frequency Width must be > 0.");
            if (f.Alpha < 0 || f.Alpha > 1) errors.Add(where + " frequency Alpha must be 0..1.");
        }
    }
}
}
