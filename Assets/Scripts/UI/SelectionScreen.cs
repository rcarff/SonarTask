using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using SonarTask.Core;
using SonarTask.Services;

namespace SonarTask.UI
{
    public sealed class SelectionScreen : SonarScreen
    {
        IExperimentRepository repo;
        Transform list;
        Text status;
        ExperimentSummary selected;
        Button instructions, start, back;
        readonly Dictionary<string, Button> experimentButtons = new(StringComparer.OrdinalIgnoreCase);

        public override void Build(ScreenManager m)
        {
            base.Build(m);
            repo = RepositoryFactory.Experiments;
            var root = transform.Find("Selection");
            root.Find("Header").GetComponent<Text>().text = AppState.ExternalStudyActive
                ? $"Subject: {AppState.SubjectId}    Provider: {ExternalStudyService.ProviderDisplay(AppState.ExternalProvider)}    Group: {AppState.ExternalAssignmentGroup}"
                : $"Location: {AppState.Location}    Operator: {AppState.OperatorId}    Subject: {AppState.SubjectId}";
            list = root.Find("Scroll/Viewport/Content");
            ClearChildren(list);
            status = root.Find("Status").GetComponent<Text>();
            back = root.Find("Actions/BackButton").GetComponent<Button>();
            instructions = root.Find("Actions/InstructionsButton").GetComponent<Button>();
            start = root.Find("Actions/StartButton").GetComponent<Button>();

            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(() => m.Startup());
            back.gameObject.SetActive(!AppState.ExternalStudyActive);
            instructions.onClick.RemoveAllListeners(); instructions.onClick.AddListener(OpenInstructions);
            start.onClick.RemoveAllListeners(); start.onClick.AddListener(StartSelected);
            instructions.interactable = start.interactable = false;
            status.text = "Loading experiments…";
            StartCoroutine(repo.List(Populate, e => status.text = e));
        }

        void Populate(List<ExperimentSummary> experiments)
        {
            experimentButtons.Clear();
            if (AppState.ExternalStudyActive)
                experiments = experiments.Where(x => string.Equals(x.PackageName, AppState.SelectedPackage, StringComparison.OrdinalIgnoreCase)).ToList();

            status.text = experiments.Count == 0
                ? (AppState.ExternalStudyActive ? "The assigned experiment is not available." : "No experiment packages found.")
                : (AppState.ExternalStudyActive ? "Your assigned experiment is shown below." : "Select an experiment.");

            foreach (var e in experiments)
            {
                var row = UIFactory.GO("Experiment", list);
                UIFactory.Size((RectTransform)row.transform, 38);
                var rowLayout = UIFactory.HLayout(row.transform, 5, 0);
                rowLayout.childForceExpandHeight = false;

                var b = UIFactory.Button($"{e.Name}  (v{e.Version})", row.transform, () => Select(e));
                UIFactory.Size(b, 36, -1, -1, 1);
                experimentButtons[e.PackageName] = b;

                var completed = UIFactory.Text("", row.transform, 15, TextAnchor.MiddleCenter);
                UIFactory.Size(completed, 36, 140);
                if (!AppState.ExternalStudyActive)
                    StartCoroutine(repo.HasCompleted(AppState.SubjectId, e.PackageName, x => {
                        if (x) { completed.text = "COMPLETED"; completed.color = new Color(.9f,.9f,.9f,1); }
                    }, _ => { }));

                if (e.PackageName == AppState.SelectedPackage || (AppState.ExternalStudyActive && experiments.Count == 1))
                    Select(e);
            }
        }

        void Select(ExperimentSummary e)
        {
            selected = e;
            AppState.SelectedPackage = e.PackageName;
            instructions.interactable = start.interactable = true;
            status.text = $"Selected: {e.Name}";
            RefreshExperimentButtonAppearance();
        }

        void RefreshExperimentButtonAppearance()
        {
            Color normalColor = new Color(.20f, .25f, .30f, 1f);
            Color selectedColor = new Color(.10f, .48f, .72f, 1f);
            foreach (var pair in experimentButtons)
            {
                var button = pair.Value;
                if (button == null || button.targetGraphic == null) continue;
                button.targetGraphic.color = string.Equals(pair.Key, AppState.SelectedPackage, StringComparison.OrdinalIgnoreCase)
                    ? selectedColor : normalColor;
            }
        }

        void OpenInstructions()
        {
            if (selected == null) return;
            LoadSelected(() => Manager.Instructions());
        }

        void StartSelected()
        {
            if (selected == null) return;
            LoadSelected(() => {
                if (AppState.ExternalStudyActive) { Manager.Task(); return; }
                StartCoroutine(repo.HasCompleted(AppState.SubjectId, selected.PackageName, done =>
                {
                    if (done) Modal.Show(Manager.Root, "Experiment already completed", "This subject has already completed this experiment. Start another independent run anyway?", () => Manager.Task(), () => { }, "Continue", "Cancel");
                    else Manager.Task();
                }, e => Modal.Show(Manager.Root, "Error", e)));
            });
        }

        void LoadSelected(Action next)
        {
            StartCoroutine(repo.LoadDefinition(selected.PackageName, d =>
            {
                try { AppState.LoadedDefinition = d; AppState.ResolvedExperiment = ExperimentResolver.Resolve(d); next(); }
                catch (Exception e) { Modal.Show(Manager.Root, "Invalid Experiment", e.Message); }
            }, e => Modal.Show(Manager.Root, "Load failed", e)));
        }

        static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        }
    }
}
