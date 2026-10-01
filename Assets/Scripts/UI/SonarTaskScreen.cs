using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using SonarTask.Core;
using SonarTask.Rendering;
using SonarTask.Services;
using UnityEngine;
using UnityEngine.UI;

namespace SonarTask.UI {
public sealed class SonarTaskScreen : SonarScreen {
    ExperimentRuntime rt;
    ResultRecorder recorder;
    AudioService audioService;
    ResolvedPhase phase;
    Button startPause, exit, confirm, clearSelectionButton;
    Text phaseText, elapsedText, remainingText, feedbackText, tallyText, alertText, trainingInfo;
    GameObject feedbackCorrectIcon, feedbackPartialIcon, feedbackIncorrectIcon;
    Transform left, right, rightContent, classGrid, confidenceRow;
    PopupDropdown trainingDrop;
    readonly List<Button> classButtons = new();
    readonly List<Toggle> confidence = new();
    readonly List<SignalDefinition> trainingSignals = new();
    readonly List<WaterfallDisplay> lofars = new();
    WaterfallDisplay bth;
    bool hasSelection;
    bool awaitingConfidence;
    bool endPopupShown;
    float selectionBearing;
    List<ResolvedSignal> selectedSignals = new();
    string selectedClass;
    string trainingClassification;
    ClassCorrectness selectedCorrect;
    int correct, incorrect, partial, signalsStartedTotal;
    readonly List<ActiveAlertLine> alerts = new();
    float scanAccumulator;
    string definitionJson;

    sealed class ActiveAlertLine { public ResolvedAlert alert; public float expires; }

    public override void Build(ScreenManager m) {
        base.Build(m);
        if (AppState.ResolvedExperiment == null || AppState.LoadedDefinition == null) {
            Modal.Show(Manager.Root, "No experiment", "No experiment is loaded.", () => m.Selection());
            return;
        }

        rt = new ExperimentRuntime(AppState.ResolvedExperiment);
        var runGo = new GameObject("RunRecorder:" + Guid.NewGuid().ToString("N").Substring(0, 8));
        UnityEngine.Object.DontDestroyOnLoad(runGo);
        recorder = runGo.AddComponent<ResultRecorder>();
        audioService = gameObject.AddComponent<AudioService>();
        audioService.Init();
        definitionJson = string.IsNullOrWhiteSpace(AppState.LoadedDefinitionJson)
            ? JsonConvert.SerializeObject(AppState.LoadedDefinition, Formatting.None)
            : AppState.LoadedDefinitionJson;

        try {
            BuildShell();
            HookRuntime();
            ConfigurePhase();
        }
        catch (Exception e) {
            Debug.LogException(e);
            enabled = false;
            if (recorder) Destroy(recorder.gameObject);
            Modal.Show(Manager.Root, "Sonar Task initialization failed",
                e.Message + "\n\nCheck the browser developer console for the full exception.",
                () => Manager.Selection());
            return;
        }

        startPause.interactable = false;
        recorder.Begin(AppState.SelectedPackage, AppState.LoadedDefinition, definitionJson, (ok, e) => {
            startPause.interactable = ok;
            if (!ok) Modal.Show(Manager.Root, "Result storage unavailable", e, () => Manager.Selection());
        });
    }

    void BuildShell() {
        var top = transform.Find("TopBar");
        startPause = top.Find("StartPauseButton").GetComponent<Button>();
        phaseText = top.Find("PhaseText").GetComponent<Text>();
        elapsedText = top.Find("ExperimentTimer/ElapsedText").GetComponent<Text>();
        remainingText = top.Find("ExperimentTimer/RemainingText").GetComponent<Text>();
        var info = top.Find("InfoText").GetComponent<Text>();
        exit = top.Find("ExitButton").GetComponent<Button>();
        left = transform.Find("Left");
        right = transform.Find("Right");
        rightContent = right.Find("RightContent");

        startPause.onClick.RemoveAllListeners();
        startPause.onClick.AddListener(StartPause);
        exit.onClick.RemoveAllListeners();
        exit.onClick.AddListener(Exit);
        info.text = $"Subject: {AppState.SubjectId}    Experiment: {AppState.LoadedDefinition.Name}";
    }

    void BuildDisplays() {
        ClearChildren(left);
        lofars.Clear();
        float bthH = .43f;
        int n = phase.LOFARViews.Count;
        if (n == 0) throw new InvalidOperationException($"Phase '{phase.Name}' has no LOFARViews after resolution.");

        // One shared LOFAR heading sits above the entire dynamic LOFAR stack. Individual
        // views show only their axes/ranges so frequency labels never collide with titles.
        var lofarHeader = UIFactory.Text("LOFAR", left, 16, TextAnchor.UpperCenter);
        var headerRect = lofarHeader.rectTransform;
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.pivot = new Vector2(.5f, 1);
        headerRect.sizeDelta = new Vector2(0, 24);
        headerRect.anchoredPosition = new Vector2(0, -2);

        float lofarTop = .955f;
        float lofarTotal = lofarTop - bthH - .012f;
        for (int i = 0; i < n; i++) {
            float y1 = bthH + .012f + lofarTotal * (n - i - 1) / n;
            float y2 = bthH + .012f + lofarTotal * (n - i) / n;
            var r = UIFactory.Rect("LOFAR " + (i + 1), left, new Vector2(0, y1), new Vector2(1, y2), new Vector2(4, 3), new Vector2(-4, -3));
            var w = r.gameObject.AddComponent<WaterfallDisplay>();
            w.Build(WaterfallAxis.Frequency, phase.LOFARViews[i], "", phase.WaterfallScanRateHz);
            lofars.Add(w);
        }
        var br = UIFactory.Rect("BTH", left, new Vector2(0, 0), new Vector2(1, bthH), new Vector2(4, 2), new Vector2(-4, -3));
        bth = br.gameObject.AddComponent<WaterfallDisplay>();
        bth.Build(WaterfallAxis.Bearing, phase.BTHView, "BTH — Bearing / Time History", phase.WaterfallScanRateHz);
        bth.BearingClicked += OnBthClick;

        // This is intentionally outside the Plot rectangle so it sits below the graph,
        // in the lower-right annotation/control band of the BTH display.
        clearSelectionButton = UIFactory.Button("Clear Selection", br, () => ClearSelection(), 13);
        var clearRect = clearSelectionButton.GetComponent<RectTransform>();
        clearRect.anchorMin = new Vector2(1, 0);
        clearRect.anchorMax = new Vector2(1, 0);
        clearRect.pivot = new Vector2(1, 0);
        clearRect.sizeDelta = new Vector2(132, 28);
        clearRect.anchoredPosition = new Vector2(-8, 4);
        clearSelectionButton.interactable = false;
    }

    void BuildRight() {
        var train = rightContent.Find("Training");
        trainingDrop = train.Find("TrainingDropdown").GetComponent<PopupDropdown>();
        trainingDrop.EnsureVisuals("Instructions");
        trainingInfo = train.Find("TrainingInfo").GetComponent<Text>();
        trainingInfo.text = "Select a Signal to view it's default frequency positions in the LOFAR window(s)";
        PopulateTraining();
        train.gameObject.SetActive(phase.TrainingAreaEnabled == YesNo.Yes);

        var alert = rightContent.Find("Alerts");
        alertText = alert.Find("AlertText").GetComponent<Text>();
        alert.gameObject.SetActive(phase.AlertsEnabled == YesNo.Yes);

        var cls = rightContent.Find("Classification");
        classGrid = cls.Find("ClassGrid");
        ClearChildren(classGrid);
        int classRows = Mathf.Max(1, Mathf.CeilToInt(phase.ClassificationOrder.Count / 3f));
        UIFactory.Size(classGrid as RectTransform, classRows * 43 + 4);

        confirm = cls.Find("ConfirmButton").GetComponent<Button>();
        confirm.onClick.RemoveAllListeners();
        confirm.onClick.AddListener(Confirm);
        confirm.interactable = false;

        classButtons.Clear();
        foreach (var c in phase.ClassificationOrder) {
            var name = c;
            // Match Confirm's base visual style and font size. Selection/training highlights are overlays on that style.
            var b = UIFactory.Button(c, classGrid, () => ChooseClass(name), 17);
            if (confirm.targetGraphic && b.targetGraphic) b.targetGraphic.color = confirm.targetGraphic.color;
            b.colors = confirm.colors;
            var nav = b.navigation;
            nav.mode = Navigation.Mode.None;
            b.navigation = nav;
            classButtons.Add(b);
        }
        trainingClassification = null;
        RefreshClassButtonAppearance();

        confidenceRow = cls.Find("ConfidenceRow");
        ClearChildren(confidenceRow);
        confidence.Clear();
        var group = confidenceRow.GetComponent<ToggleGroup>() ?? confidenceRow.gameObject.AddComponent<ToggleGroup>();
        group.allowSwitchOff = true;
        for (int i = 1; i <= 4; i++) {
            int v = i;
            var tg = UIFactory.RadioToggle(i.ToString(), confidenceRow);
            tg.group = group;
            tg.SetIsOnWithoutNotify(false);
            tg.interactable = false;
            tg.onValueChanged.AddListener(on => { if (on) Confidence(v); });
            UIFactory.Size(tg, 42, 76);
            confidence.Add(tg);
        }

        var feedback = rightContent.Find("Feedback");
        var feedbackBox = feedback.Find("FeedbackBody/FeedbackBox");
        feedbackText = feedbackBox.Find("FeedbackText").GetComponent<Text>();
        EnsureFeedbackIcons(feedbackBox);
        tallyText = feedback.Find("FeedbackBody/TallyText").GetComponent<Text>();
        feedbackBox.gameObject.SetActive(phase.FeedbackEnabled == YesNo.Yes);
        tallyText.gameObject.SetActive(phase.ClassificationTallyEnabled == YesNo.Yes);
        feedback.gameObject.SetActive(phase.FeedbackEnabled == YesNo.Yes || phase.ClassificationTallyEnabled == YesNo.Yes);
        feedbackText.text = string.Empty;
        feedbackText.color = Color.white;
        SetFeedbackIcon(null);
        UpdateTally();

        awaitingConfidence = false;
        SetClassificationEnabled(false);
    }

    void PopulateTraining() {
        trainingSignals.Clear();
        trainingSignals.AddRange(phase.PhaseSignalDefinitions
            .Where(s => s.TrainingAllowed == YesNo.Yes)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase));

        var options = new List<string> { "Instructions" };
        options.AddRange(trainingSignals.Select(s => $"{s.Name} - Classification: {s.Classification}"));
        trainingDrop.ClearValueChangedListeners();
        trainingDrop.SetOptions(options, 0, false);
        trainingDrop.ValueChanged += TrainingChanged;
    }

    void TrainingChanged(int ix) {
        foreach (var l in lofars) l.ClearCarets();
        trainingClassification = null;
        // Keep the explanatory text unchanged; classification is part of each dropdown option.
        trainingInfo.text = "Select a Signal to view it's default frequency positions in the LOFAR window(s)";
        if (ix > 0 && ix - 1 < trainingSignals.Count) {
            var signal = trainingSignals[ix - 1];
            trainingClassification = signal.Classification;
            for (int i = 0; i < lofars.Count; i++) {
                var v = phase.LOFARViews[i];
                var ns = signal.FreqTuples.Where(f => f.Frequency >= v.HzStart && f.Frequency <= v.HzEnd)
                    .Select(f => (f.Frequency - v.HzStart) / (float)Mathf.Max(1, v.HzEnd - v.HzStart));
                lofars[i].ShowCarets(ns);
            }
        }
        RefreshClassButtonAppearance();
    }

    void RefreshClassButtonAppearance() {
        Color confirmBase = confirm && confirm.targetGraphic ? confirm.targetGraphic.color : new Color(.20f, .25f, .30f, 1f);
        ColorBlock confirmColors = confirm ? confirm.colors : ColorBlock.defaultColorBlock;

        for (int i = 0; i < classButtons.Count; i++) {
            var b = classButtons[i];
            string classification = i < phase.ClassificationOrder.Count ? phase.ClassificationOrder[i] : "";
            bool isTraining = !string.IsNullOrWhiteSpace(trainingClassification) && classification == trainingClassification;
            bool isSelected = !string.IsNullOrWhiteSpace(selectedClass) && classification == selectedClass;

            Color baseColor = isSelected
                ? new Color(.10f, .48f, .72f, 1f)
                : isTraining ? new Color(.52f, .48f, .12f, 1f)
                : confirmBase;

            if (b.targetGraphic) b.targetGraphic.color = baseColor;

            // The experiment explicitly owns the selected-classification color. Do not let
            // Unity's persistent EventSystem Selected state leave a blue tint/ring behind.
            var colors = confirmColors;
            colors.normalColor = Color.white;
            colors.selectedColor = Color.white;
            if (isSelected && !b.interactable) colors.disabledColor = Color.white;
            b.colors = colors;
        }
    }

    void HookRuntime() {
        rt.PhaseTransition += OnTransition;
        rt.SignalStarted += s => {
            signalsStartedTotal++;
            var e = ResultEvent.Create(rt, "Signal_Start");
            e.Signal_Type = s.SignalType;
            e.Signal_ID = s.ID;
            e.Signal_Bearing = s.Bearing.ToString("0.###", CultureInfo.InvariantCulture);
            e.Signal_Width = s.SignalStrength.Width.ToString("0.###", CultureInfo.InvariantCulture);
            e.Signal_Alpha = s.SignalStrength.Alpha.ToString("0.###", CultureInfo.InvariantCulture);
            recorder.Record(e);
            UpdateTally();
        };
        rt.SignalEnded += s => {
            var e = ResultEvent.Create(rt, "Signal_End");
            e.Signal_Type = s.SignalType;
            e.Signal_ID = s.ID;
            recorder.Record(e);
        };
        rt.AlertShown += a => {
            if (phase.AlertsEnabled != YesNo.Yes) return;
            alerts.Insert(0, new ActiveAlertLine { alert = a, expires = rt.PhaseElapsedSec + a.DurationSec });
            var e = ResultEvent.Create(rt, "Alert_Shown");
            e.Alert_Type = a.Type;
            e.Alert_Text = a.Text;
            e.Alert_Bearing = a.Bearing.ToString("D3");
            e.Alert_SignalId = a.SignalID;
            recorder.Record(e);
            RefreshAlerts();
        };
    }

    void ConfigurePhase() {
        phase = rt.Phase;
        BuildDisplays();
        BuildRight();
        audioService.SetBackground(AppState.SelectedPackage, phase.BackgroundAudio, phase.BackgroundAudioVolume, rt.State == RunState.Running);
        phaseText.text = "Phase: " + phase.Name;
        alerts.Clear();
        RefreshAlerts();
        ClearSelection(false);
        RebuildLofarHistory();
    }

    void OnTransition(PhaseTransitionReason r) {
        string et = r switch {
            PhaseTransitionReason.Start => "EXP_Started",
            PhaseTransitionReason.AutoPause => "EXP_AutoPaused",
            PhaseTransitionReason.Pause => "EXP_Paused",
            PhaseTransitionReason.Resume => "EXP_Resumed",
            _ => "EXP_Ended"
        };
        recorder.Record(ResultEvent.Create(rt, et));
        if (r == PhaseTransitionReason.End) {
            audioService.StopAll();
            startPause.interactable = false;
            startPause.GetComponentInChildren<Text>().text = "Ended";
            exit.interactable = false;
            CompleteRunAndShowEnd();
            return;
        }
        if (r == PhaseTransitionReason.Start) {
            if (rt.PhaseIndex != phase.Index) ConfigurePhase();
            audioService.ResumeAll();
            startPause.GetComponentInChildren<Text>().text = "Pause";
            exit.interactable = false;
        } else if (r == PhaseTransitionReason.AutoPause) {
            ConfigurePhase();
            audioService.PauseAll();
            startPause.GetComponentInChildren<Text>().text = "Resume";
            exit.interactable = true;
        } else if (r == PhaseTransitionReason.Pause) {
            audioService.PauseAll();
            startPause.GetComponentInChildren<Text>().text = "Resume";
            exit.interactable = true;
        } else if (r == PhaseTransitionReason.Resume) {
            audioService.ResumeAll();
            startPause.GetComponentInChildren<Text>().text = "Pause";
            exit.interactable = false;
        }
    }

    void StartPause() {
        if (rt.State == RunState.Ready || rt.State == RunState.Paused) rt.StartOrResume();
        else if (rt.State == RunState.Running) rt.Pause();
    }

    void Exit() {
        if (rt.State == RunState.Ended || rt.State == RunState.Ready) {
            audioService.StopAll();
            if (rt.State == RunState.Ready) recorder.Finish(false, "OperatorExitBeforeStart");
            Manager.Selection();
            return;
        }
        if (rt.State == RunState.Paused)
            Modal.Show(Manager.Root, "Exit experiment", "This experiment has not finished. Preserve it as an aborted run and exit?", () => {
                recorder.Finish(false, "OperatorExit");
                audioService.StopAll();
                Manager.Selection();
            }, () => { }, "Exit", "Cancel");
    }

    void Update() {
        if (rt == null) return;
        float dt = Time.unscaledDeltaTime;
        if (rt.State == RunState.Running) {
            rt.Advance(dt);
            scanAccumulator += dt;
            float scanDt = 1f / Mathf.Max(1, phase.WaterfallScanRateHz);
            while (scanAccumulator >= scanDt && rt.State == RunState.Running) {
                scanAccumulator -= scanDt;
                RenderScan();
                rt.IncrementScan();
            }
            bth?.AdvanceMarkers(dt);
        } else if (rt.State == RunState.Paused) {
            rt.AdvancePausedTime(dt);
        }
        UpdateTimer();
        ExpireAlerts();
    }

    void UpdateTimer() {
        if (rt == null || phase == null) return;

        bool showElapsed = phase.ShowElapsedTime == YesNo.Yes;
        bool showRemaining = phase.ShowRemainingTime == YesNo.Yes;

        elapsedText.gameObject.SetActive(showElapsed);
        remainingText.gameObject.SetActive(showRemaining);
        elapsedText.text = showElapsed ? "Elapsed " + Clock(rt.ExperimentElapsedSec) : "";
        remainingText.text = showRemaining ? "Remaining " + Clock(rt.Remaining) : "";

        phaseText.text = "Phase: " + phase.Name;
    }

    static string Clock(float sec) {
        sec = Mathf.Max(0, sec);
        return $"{Mathf.FloorToInt(sec / 60):00}:{Mathf.FloorToInt(sec % 60):00}";
    }

    void RenderScan() {
        var active = rt.Active();
        bth.PushScanline(BthLine(active, 1024, rt.ScanIndex), phase.WaterfallPalette);
        List<ResolvedSignal> sources;
        if (hasSelection)
            sources = CurrentSignalsInSelection(active);
        else if (phase.HideLofarSignalsIfNoSelectedBTH == YesNo.Yes)
            sources = new List<ResolvedSignal>();
        else
            sources = active.Where(a => BearingInView(a.Bearing)).Select(a => a.Definition).ToList();
        for (int i = 0; i < lofars.Count; i++)
            lofars[i].PushScanline(LofarLine(sources, phase.LOFARViews[i], 1024, i, rt.ScanIndex), phase.WaterfallPalette);
    }

    float[] BthLine(List<ActiveSignal> active, int n, long scanIndex) {
        var o = NoiseLine(n, -1, scanIndex);
        float span = WaterfallDisplay.AngleSpan(phase.BTHView.DegStart, phase.BTHView.DegEnd);
        foreach (var a in active) {
            float jitter = (float)(
                DeterministicNoise.Smooth(AppState.LoadedDefinition.ExperimentSettings.RandomizationSeed, phase.Index, a.Definition.ID, -1, scanIndex, 1, 17) * phase.JitterFactor * 1.4
                + DeterministicNoise.Signed(AppState.LoadedDefinition.ExperimentSettings.RandomizationSeed, phase.Index, a.Definition.ID, -1, scanIndex, 2) * phase.JitterFactor * .25);
            float center = ExperimentResolver.Norm(a.Bearing + jitter);
            float sigma = Mathf.Max(.4f, a.Definition.SignalStrength.Width / 2.355f);
            for (int x = 0; x < n; x++) {
                float bearing = ExperimentResolver.Norm(phase.BTHView.DegStart + span * x / (n - 1f));
                float d = CircularDistance(bearing, center);
                float e = Mathf.Exp(-.5f * (d * d) / (sigma * sigma)) * a.Definition.SignalStrength.Alpha;
                o[x] = Mathf.Clamp01(o[x] + e);
            }
        }
        return o;
    }

    float[] LofarLine(List<ResolvedSignal> signals, LOFARView v, int n, int wi, long scanIndex) {
        var o = NoiseLine(n, wi, scanIndex);
        float hzSpan = Mathf.Max(.001f, v.HzEnd - v.HzStart);
        foreach (var s in signals) {
            foreach (var f in s.FreqTuples) {
                float slow = (float)DeterministicNoise.Smooth(AppState.LoadedDefinition.ExperimentSettings.RandomizationSeed, phase.Index, s.ID, wi, scanIndex, 10, 31);
                float fast = (float)DeterministicNoise.Signed(AppState.LoadedDefinition.ExperimentSettings.RandomizationSeed, phase.Index, s.ID, wi, scanIndex, 11);
                float center = f.Frequency + (slow * .65f + fast * .15f) * phase.JitterFactor * Mathf.Max(1, f.Width);
                float sigma = Mathf.Max(.35f, f.Width / 2.355f);
                // Gaussian energy beyond four sigma is visually negligible. Limiting the
                // pixel range makes full-history reconstruction fast enough for interaction.
                float lowHz = center - 4f * sigma;
                float highHz = center + 4f * sigma;
                int x0 = Mathf.Clamp(Mathf.FloorToInt((lowHz - v.HzStart) / hzSpan * (n - 1)), 0, n - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((highHz - v.HzStart) / hzSpan * (n - 1)), 0, n - 1);
                if (highHz < v.HzStart || lowHz > v.HzEnd || x1 < x0) continue;
                for (int x = x0; x <= x1; x++) {
                    float hz = Mathf.Lerp(v.HzStart, v.HzEnd, x / (n - 1f));
                    float d = hz - center;
                    float e = Mathf.Exp(-.5f * d * d / (sigma * sigma)) * f.Alpha;
                    o[x] = Mathf.Clamp01(o[x] + e);
                }
            }
        }
        return o;
    }

    float[] NoiseLine(int n, int waterfall, long scanIndex) {
        var o = new float[n];
        int bandGroup = -1;
        double band = 0;
        for (int x = 0; x < n; x++) {
            int g = x / 32;
            if (g != bandGroup) {
                bandGroup = g;
                band = DeterministicNoise.Smooth(AppState.LoadedDefinition.ExperimentSettings.RandomizationSeed, phase.Index, "BACKGROUND", waterfall, scanIndex, 2000 + g, 23);
            }
            double grain = DeterministicNoise.Unit(AppState.LoadedDefinition.ExperimentSettings.RandomizationSeed, phase.Index, "BACKGROUND", waterfall, scanIndex, x);
            o[x] = Mathf.Clamp01(phase.BackgroundNoise * (.25f + (float)grain * .65f + (float)(band * .1)));
        }
        return o;
    }

    void OnBthClick(float n) {
        if (rt.State != RunState.Running) return;
        float span = WaterfallDisplay.AngleSpan(phase.BTHView.DegStart, phase.BTHView.DegEnd);
        selectionBearing = ExperimentResolver.Norm(phase.BTHView.DegStart + span * n);
        hasSelection = true;
        if (clearSelectionButton) clearSelectionButton.interactable = true;
        float nw = phase.BTHView.SelectionWidth / span;
        bth.ShowBearingSelection(n, nw);
        // Classification is based on every signal track still visible anywhere in the
        // BTH history window, not only signals that are actively transmitting now.
        selectedSignals = SignalsVisibleInSelection();

        var e = ResultEvent.Create(rt, "BTH_Selection");
        e.BTH_SelectedSignals = ResultEvent.JoinSignals(selectedSignals);
        e.BTH_SelectedBearing = SelectedBearingText();
        recorder.Record(e);

        awaitingConfidence = false;
        SetClassificationEnabled(true);
        selectedClass = null;
        RefreshClassButtonAppearance();
        confirm.interactable = false;
        foreach (var t in confidence) {
            t.isOn = false;
            t.interactable = false;
        }

        // Reconstruct the complete visible LOFAR time window for this bearing aperture.
        // Past scanlines remain historical rather than restarting at the top.
        RebuildLofarHistory();

        // Background audio is an independent channel and continues playing while
        // selected-signal audio is active. BTHOverlapAudio controls only the
        // multiple-contact case: one selected signal always plays when signal
        // audio is enabled; multiple selected signals play together only when
        // overlap audio is enabled.
        if (phase.SignalAudioEnabled == YesNo.Yes && selectedSignals.Count == 1)
            audioService.PlaySelected(AppState.SelectedPackage, selectedSignals);
        else if (phase.SignalAudioEnabled == YesNo.Yes && selectedSignals.Count > 1 && phase.BTHOverlapAudio == YesNo.Yes)
            audioService.PlaySelected(AppState.SelectedPackage, selectedSignals);
        else
            audioService.StopSignalAudio();
    }

    List<ResolvedSignal> CurrentSignalsInSelection(List<ActiveSignal> active) =>
        active.Where(a => CircularDistance(a.Bearing, selectionBearing) <= a.Definition.SignalStrength.Width / 2 + phase.BTHView.SelectionWidth / 2)
              .Select(a => a.Definition).ToList();

    List<ResolvedSignal> SignalsVisibleInSelection() {
        var result = new List<ResolvedSignal>();
        float historySec = BthHistorySec();
        float visibleStart = rt.PhaseElapsedSec - historySec;
        float visibleEnd = rt.PhaseElapsedSec;
        float scanDt = 1f / Mathf.Max(1f, phase.WaterfallScanRateHz);

        foreach (var s in phase.Signals) {
            float start = Mathf.Max(s.AppearSec, visibleStart);
            float end = Mathf.Min(s.AppearSec + s.DurationSec, visibleEnd);
            if (end < start - .0001f) continue;

            int samples = Mathf.Max(1, Mathf.CeilToInt((end - start) / scanDt));
            bool hit = false;
            for (int i = 0; i <= samples; i++) {
                float t = samples == 0 ? start : Mathf.Lerp(start, end, i / (float)samples);
                float age = t - s.AppearSec;
                float bearing = ExperimentResolver.Norm(s.Bearing + s.RateDegSec * age);
                if (CircularDistance(bearing, selectionBearing) <= s.SignalStrength.Width / 2 + phase.BTHView.SelectionWidth / 2) {
                    hit = true;
                    break;
                }
            }
            if (hit) result.Add(s);
        }
        return result;
    }

    float BthHistorySec() => Mathf.Max(1f, Mathf.Abs(phase.BTHView.TimeEnd - phase.BTHView.TimeStart));

    List<ResolvedSignal> LofarSourcesAt(float phaseElapsed) {
        var result = new List<ResolvedSignal>();
        if (phaseElapsed < 0) return result;
        foreach (var s in phase.Signals) {
            float age = phaseElapsed - s.AppearSec;
            if (age < 0 || age > s.DurationSec) continue;
            float bearing = ExperimentResolver.Norm(s.Bearing + s.RateDegSec * age);
            bool include = hasSelection
                ? CircularDistance(bearing, selectionBearing) <= s.SignalStrength.Width / 2 + phase.BTHView.SelectionWidth / 2
                : phase.HideLofarSignalsIfNoSelectedBTH == YesNo.No && BearingInView(bearing);
            if (include) result.Add(s);
        }
        return result;
    }

    void RebuildLofarHistory() {
        if (phase == null || rt == null || lofars.Count == 0) return;
        float scanRate = Mathf.Max(1f, phase.WaterfallScanRateHz);
        long phaseStartScan = rt.ScanIndex - (long)Math.Floor(rt.PhaseElapsedSec * scanRate);

        for (int i = 0; i < lofars.Count; i++) {
            var display = lofars[i];
            var view = phase.LOFARViews[i];
            int rows = Mathf.Max(2, display.HistoryRows);
            float historySec = Mathf.Max(1f, view.TimeEnd - view.TimeStart);
            var history = new List<float[]>(rows);

            // WaterfallDisplay.ReplaceHistory expects oldest row first, newest last.
            for (int row = 0; row < rows; row++) {
                float age = historySec * (rows - 1 - row) / (rows - 1f);
                float historicalPhaseTime = rt.PhaseElapsedSec - age;
                long historicalScan = phaseStartScan + (long)Math.Floor(historicalPhaseTime * scanRate);
                var sources = LofarSourcesAt(historicalPhaseTime);
                history.Add(LofarLine(sources, view, 1024, i, historicalScan));
            }
            display.ReplaceHistory(history, phase.WaterfallPalette);
        }
    }

    void SetClassificationEnabled(bool on) {
        foreach (var b in classButtons) b.interactable = on;
    }

    void ChooseClass(string c) {
        if (!hasSelection || awaitingConfidence) return;
        selectedClass = c;
        selectedCorrect = ClassificationScorer.Score(c, selectedSignals);
        RefreshClassButtonAppearance();
        confirm.interactable = true;
        recorder.Record(ClassificationEvent("Class_Chosen"));
    }

    void Confirm() {
        if (!hasSelection || string.IsNullOrWhiteSpace(selectedClass) || awaitingConfidence) return;
        recorder.Record(ClassificationEvent("Class_Confirmed"));
        awaitingConfidence = true;
        SetClassificationEnabled(false);
        confirm.interactable = false;
        foreach (var t in confidence) t.interactable = true;
    }

    ResultEvent ClassificationEvent(string type) {
        var e = ResultEvent.Create(rt, type);
        e.BTH_SelectedSignals = ResultEvent.JoinSignals(selectedSignals);
        e.BTH_SelectedBearing = SelectedBearingText();
        e.Class_Selected = selectedClass;
        e.Class_Types = ResultEvent.JoinClasses(selectedSignals);
        e.Class_Correct = selectedCorrect.ToString();
        return e;
    }

    string SelectedBearingText() => selectionBearing.ToString("0.###", CultureInfo.InvariantCulture);

    void Confidence(int value) {
        // Defensive gate: startup/rebuild ToggleGroup activity must never create research events.
        if (!awaitingConfidence || !hasSelection || string.IsNullOrWhiteSpace(selectedClass)) return;
        awaitingConfidence = false;

        var e = ClassificationEvent("Class_Confidence");
        e.Class_Confidence = value.ToString(CultureInfo.InvariantCulture);
        e.Class_Feedback = phase.FeedbackEnabled == YesNo.Yes
            ? (selectedCorrect == ClassCorrectness.YES ? "CORRECT" : selectedCorrect == ClassCorrectness.PARTIAL ? "PARTIAL" : "INCORRECT")
            : "NONE";
        recorder.Record(e);

        if (selectedCorrect == ClassCorrectness.YES) correct++;
        else if (selectedCorrect == ClassCorrectness.PARTIAL) partial++;
        else incorrect++;
        if (phase.FeedbackEnabled == YesNo.Yes) StartCoroutine(FeedbackFlash(selectedCorrect));
        UpdateTally();

        float span = WaterfallDisplay.AngleSpan(phase.BTHView.DegStart, phase.BTHView.DegEnd);
        float n = UnwrapFromStart(selectionBearing, phase.BTHView.DegStart) / span;

        // The S marker represents the classification of the selected visible track(s).
        // Keep it until the latest selected signal has completely drained from the BTH.
        float markerLife = BthHistorySec();
        if (selectedSignals.Count > 0) {
            float lastVisible = selectedSignals.Max(s => s.AppearSec + s.DurationSec)
                + BthHistorySec()
                + 1f / Mathf.Max(1f, phase.WaterfallScanRateHz);
            markerLife = Mathf.Max(1f / Mathf.Max(1f, phase.WaterfallScanRateHz), lastVisible - rt.PhaseElapsedSec);
        }
        bth.AddSMarker(n, markerLife);
        ClearSelection();
    }

    void EnsureFeedbackIcons(Transform feedbackBox) {
        var existing = feedbackBox.Find("FeedbackIconRoot");
        GameObject root;
        if (existing) root = existing.gameObject;
        else {
            root = UIFactory.GO("FeedbackIconRoot", feedbackBox);
            var rr = (RectTransform)root.transform;
            rr.anchorMin = rr.anchorMax = new Vector2(0, .5f);
            rr.pivot = new Vector2(0, .5f);
            rr.sizeDelta = new Vector2(44, 44);
            rr.anchoredPosition = new Vector2(8, 0);
        }

        feedbackCorrectIcon = root.transform.Find("Correct")?.gameObject;
        if (!feedbackCorrectIcon) {
            feedbackCorrectIcon = UIFactory.GO("Correct", root.transform);
            Stretch(feedbackCorrectIcon.transform);
            UIFactory.LineGraphic("Short", feedbackCorrectIcon.transform, Color.green, new Vector2(14, 4), new Vector2(-7, -2), -42);
            UIFactory.LineGraphic("Long", feedbackCorrectIcon.transform, Color.green, new Vector2(28, 4), new Vector2(6, 4), 43);
        }

        feedbackPartialIcon = root.transform.Find("Partial")?.gameObject;
        if (!feedbackPartialIcon) {
            feedbackPartialIcon = UIFactory.GO("Partial", root.transform);
            Stretch(feedbackPartialIcon.transform);
            var yellow = new Color(1f, .82f, .15f, 1f);
            UIFactory.LineGraphic("Left", feedbackPartialIcon.transform, yellow, new Vector2(26, 4), new Vector2(-6, 4), 60);
            UIFactory.LineGraphic("Right", feedbackPartialIcon.transform, yellow, new Vector2(26, 4), new Vector2(6, 4), -60);
            UIFactory.LineGraphic("Bottom", feedbackPartialIcon.transform, yellow, new Vector2(27, 4), new Vector2(0, -8), 0);
        }

        feedbackIncorrectIcon = root.transform.Find("Incorrect")?.gameObject;
        if (!feedbackIncorrectIcon) {
            feedbackIncorrectIcon = UIFactory.GO("Incorrect", root.transform);
            Stretch(feedbackIncorrectIcon.transform);
            UIFactory.LineGraphic("Slash1", feedbackIncorrectIcon.transform, Color.red, new Vector2(32, 4), Vector2.zero, 45);
            UIFactory.LineGraphic("Slash2", feedbackIncorrectIcon.transform, Color.red, new Vector2(32, 4), Vector2.zero, -45);
        }

        // Leave room for the explicit graphic rather than relying on a font glyph.
        feedbackText.rectTransform.offsetMin = new Vector2(54, 4);
        SetFeedbackIcon(null);
    }

    static void Stretch(Transform t) {
        var r = (RectTransform)t;
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    void SetFeedbackIcon(ClassCorrectness? c) {
        if (feedbackCorrectIcon) feedbackCorrectIcon.SetActive(c == ClassCorrectness.YES);
        if (feedbackPartialIcon) feedbackPartialIcon.SetActive(c == ClassCorrectness.PARTIAL);
        if (feedbackIncorrectIcon) feedbackIncorrectIcon.SetActive(c == ClassCorrectness.NO);
    }

    System.Collections.IEnumerator FeedbackFlash(ClassCorrectness c) {
        SetFeedbackIcon(c);
        if (c == ClassCorrectness.YES) { feedbackText.text = "CORRECT"; feedbackText.color = Color.green; }
        else if (c == ClassCorrectness.PARTIAL) { feedbackText.text = "PARTIAL"; feedbackText.color = new Color(1f, .82f, .15f, 1f); }
        else { feedbackText.text = "INCORRECT"; feedbackText.color = Color.red; }
        yield return new WaitForSecondsRealtime(1);
        feedbackText.text = "";
        feedbackText.color = Color.white;
        SetFeedbackIcon(null);
    }

    void CompleteRunAndShowEnd() {
        recorder.Finish(true, "Completed", (ok, error) => {
            if (ok) { ShowExperimentEnded(); return; }
            Modal.Show(Manager.Root, "Unable to finalize results",
                "The completed run could not yet be finalized on the server. Results must be saved before the participant is returned to the recruitment service.\n\n" + (error ?? "Unknown server error."),
                CompleteRunAndShowEnd, null, "Retry");
        });
    }

    void ShowExperimentEnded() {
        if (endPopupShown) return;
        endPopupShown = true;

        string message = "The experiment has ended.";
        if (AppState.ResolvedExperiment.ShowResultsAtEnd == YesNo.Yes) {
            int responses = correct + partial + incorrect;
            message += "\n\nResults"
                + $"\nCorrect: {correct} ({Percent(correct, responses):0.0}%)"
                + $"\nPartial: {partial} ({Percent(partial, responses):0.0}%)"
                + $"\nIncorrect: {incorrect} ({Percent(incorrect, responses):0.0}%)"
                + $"\nResponses: {responses}";
        }

        System.Action done = () => Manager.Selection();
        if (AppState.IsExternalStudy)
        {
            done = string.IsNullOrWhiteSpace(AppState.ExternalCompletionUrl)
                ? () => { }
                : () => WebBrowserBridge.NavigateUrl(AppState.ExternalCompletionUrl);
            if (string.IsNullOrWhiteSpace(AppState.ExternalCompletionUrl))
                message += "\n\nYour results have been saved. You may close this browser window.";
            else
                message += "\n\nPress OK to return to " + ExternalStudyService.ProviderDisplayName(AppState.ExternalProvider) + ".";
        }
        Modal.Show(Manager.Root, "Experiment Ended", message, done, null, "OK");
    }

    static float Percent(int value, int total) => total <= 0 ? 0f : value * 100f / total;

    void UpdateTally() {
        if (tallyText) tallyText.text = $"Correct: {correct}\nPartial: {partial}\nIncorrect: {incorrect}";
    }

    void ClearSelection(bool rebuildLofar = true) {
        bool changed = hasSelection;
        hasSelection = false;
        awaitingConfidence = false;
        if (clearSelectionButton) clearSelectionButton.interactable = false;
        selectedSignals.Clear();
        selectedClass = null;
        RefreshClassButtonAppearance();
        bth?.ClearSelection();
        audioService?.StopSignalAudio();
        SetClassificationEnabled(false);
        if (confirm) confirm.interactable = false;
        foreach (var t in confidence) {
            t.isOn = false;
            t.interactable = false;
        }
        if (changed && rebuildLofar) RebuildLofarHistory();
    }

    void ExpireAlerts() {
        if (alerts.Count == 0) return;
        bool changed = false;
        for (int i = alerts.Count - 1; i >= 0; i--)
            if (rt.PhaseElapsedSec >= alerts[i].expires || alerts[i].alert == null) {
                alerts.RemoveAt(i);
                changed = true;
            }
        if (changed) RefreshAlerts();
    }

    void RefreshAlerts() {
        if (!alertText) return;
        var lines = alerts.Take(5).Select(x => $"{x.alert.Text} at {x.alert.Bearing:D3}").ToList();
        while (lines.Count < 5) lines.Add("");
        alertText.text = string.Join("\n", lines);
    }

    static void ClearChildren(Transform parent) {
        if (!parent) return;
        for (int i = parent.childCount - 1; i >= 0; i--) {
            var g = parent.GetChild(i).gameObject;
            g.SetActive(false);
            UnityEngine.Object.Destroy(g);
        }
    }

    bool BearingInView(float b) {
        float span = WaterfallDisplay.AngleSpan(phase.BTHView.DegStart, phase.BTHView.DegEnd);
        return UnwrapFromStart(b, phase.BTHView.DegStart) <= span + .001f;
    }

    static float UnwrapFromStart(float b, float start) => ExperimentResolver.Norm(b - start);
    static float CircularDistance(float a, float b) {
        float d = Mathf.Abs(ExperimentResolver.Norm(a) - ExperimentResolver.Norm(b));
        return Mathf.Min(d, 360 - d);
    }
}
}
