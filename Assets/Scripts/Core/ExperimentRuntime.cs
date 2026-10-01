using System;
using System.Collections.Generic;
using System.Linq;

namespace SonarTask.Core {
public enum RunState { Ready, Running, Paused, Ended }
public enum PhaseTransitionReason { Start, AutoPause, Pause, Resume, End }

public sealed class ActiveSignal {
    public ResolvedSignal Definition;
    public float Bearing;
    public float AgeSec;
}

/// <summary>
/// Frame-rate-independent experiment scheduler. It consumes time exactly to a phase
/// boundary, then carries any remainder into an automatically-started next phase.
/// An automatic pause consumes no time beyond the boundary.
/// </summary>
public sealed class ExperimentRuntime {
    public readonly ResolvedExperiment Experiment;
    public RunState State { get; private set; } = RunState.Ready;
    public int PhaseIndex { get; private set; }
    // Pause-sensitive experiment time. This is the value written as ExpElapsedTime.
    public float ExperimentElapsedSec { get; private set; }
    // Wall-style elapsed time since Start. Manual/automatic pauses do not stop this clock.
    public float TotalElapsedSec { get; private set; }
    public float PhaseElapsedSec { get; private set; }
    public long ScanIndex { get; private set; }
    public ResolvedPhase Phase => Experiment.Phases[PhaseIndex];

    public event Action<PhaseTransitionReason> PhaseTransition;
    public event Action<ResolvedSignal> SignalStarted;
    public event Action<ResolvedSignal> SignalEnded;
    public event Action<ResolvedAlert> AlertShown;

    // A signal ID identifies the source/contact and may intentionally repeat.
    // Track scheduler state by resolved signal occurrence instead of by ID.
    readonly HashSet<ResolvedSignal> started = new();
    readonly HashSet<ResolvedSignal> ended = new();
    readonly HashSet<string> alerted = new();

    public ExperimentRuntime(ResolvedExperiment experiment) => Experiment = experiment;

    public void StartOrResume() {
        if (State == RunState.Ready) {
            State = RunState.Running;
            PhaseTransition?.Invoke(PhaseTransitionReason.Start);
            ProcessEvents(); // catches AppearSec == 0 deterministically
        } else if (State == RunState.Paused) {
            State = RunState.Running;
            PhaseTransition?.Invoke(PhaseTransitionReason.Resume);
            ProcessEvents();
        }
    }

    public void Pause(bool automatic = false) {
        if (State != RunState.Running) return;
        State = RunState.Paused;
        PhaseTransition?.Invoke(automatic ? PhaseTransitionReason.AutoPause : PhaseTransitionReason.Pause);
    }

    public void Advance(float dt) {
        if (State != RunState.Running || dt <= 0) return;
        float left = dt;
        int guard = 0;

        while (left > 0.000001f && State == RunState.Running && guard++ < 64) {
            float phaseRuntimeEnd = RuntimePhaseEndSec;
            float remaining = Math.Max(0, phaseRuntimeEnd - PhaseElapsedSec);
            float step = Math.Min(left, remaining);
            ExperimentElapsedSec += step;
            TotalElapsedSec += step;
            PhaseElapsedSec += step;
            ProcessEvents();
            left -= step;

            if (PhaseElapsedSec >= phaseRuntimeEnd - 0.000001f) {
                bool transitioned = CompletePhase();
                if (!transitioned || State != RunState.Running) break;
            }
        }
    }

    void ProcessEvents() {
        foreach (var s in Phase.Signals) {
            if (!started.Contains(s) && PhaseElapsedSec >= s.AppearSec) {
                started.Add(s);
                SignalStarted?.Invoke(s);
            }
            if (started.Contains(s) && !ended.Contains(s) && PhaseElapsedSec >= s.AppearSec + s.DurationSec) {
                ended.Add(s);
                SignalEnded?.Invoke(s);
            }
        }

        foreach (var a in Phase.Alerts) {
            string key = $"{PhaseIndex}|{a.AppearSec:R}|{a.Text}|{a.Bearing}|{a.SignalID}";
            if (!alerted.Contains(key) && PhaseElapsedSec >= a.AppearSec) {
                alerted.Add(key);
                AlertShown?.Invoke(a);
            }
        }
    }

    bool CompletePhase() {
        if (PhaseIndex >= Experiment.Phases.Count - 1) {
            State = RunState.Ended;
            PhaseTransition?.Invoke(PhaseTransitionReason.End);
            return false;
        }

        PhaseIndex++;
        PhaseElapsedSec = 0;
        started.Clear();
        ended.Clear();
        alerted.Clear();

        // Per the specification, the incoming phase controls whether it auto-starts.
        if (Phase.AutoStart == YesNo.Yes) {
            PhaseTransition?.Invoke(PhaseTransitionReason.Start);
            ProcessEvents();
            return true;
        }

        State = RunState.Paused;
        PhaseTransition?.Invoke(PhaseTransitionReason.AutoPause);
        return false;
    }

    /// <summary>Advances only the pause-insensitive total clock while the experiment is paused.</summary>
    public void AdvancePausedTime(float dt) {
        if (State == RunState.Paused && dt > 0) TotalElapsedSec += dt;
    }

    public void IncrementScan() {
        if (State == RunState.Running) ScanIndex++;
    }

    public List<ActiveSignal> Active() {
        var list = new List<ActiveSignal>();
        foreach (var s in Phase.Signals) {
            float age = PhaseElapsedSec - s.AppearSec;
            if (age < 0 || age > s.DurationSec) continue;
            list.Add(new ActiveSignal {
                Definition = s,
                AgeSec = age,
                Bearing = ExperimentResolver.Norm(s.Bearing + s.RateDegSec * age)
            });
        }
        return list;
    }

    // The configured phase duration remains max(Signal.AppearSec + Signal.DurationSec).
    // Every phase then receives a BTH display-drain interval so its final signal track
    // scrolls completely out of the visible bearing/time-history window before the
    // phase transitions or the experiment ends.
    public static float BthDrainSec(ResolvedPhase phase) {
        if (phase == null) return 0;
        float history = Math.Abs(phase.BTHView.TimeEnd - phase.BTHView.TimeStart);
        // One extra scan interval moves the last signal row beyond the oldest visible line.
        float oneScan = 1f / Math.Max(1f, phase.WaterfallScanRateHz);
        return history + oneScan;
    }

    float RuntimePhaseEndSec => Phase.DurationSec + BthDrainSec(Phase);

    public float TotalDuration => Experiment.Phases.Sum(p => p.DurationSec + BthDrainSec(p));
    public float Remaining => Math.Max(0, TotalDuration - ExperimentElapsedSec);
}
}
