using System.Collections.Generic;
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
        Button instructions, start;
        readonly Dictionary<string, Button> experimentButtons = new Dictionary<string, Button>();

        public override void Build(ScreenManager m)
        {
            base.Build(m);
            repo = RepositoryFactory.Experiments;
            var root = transform.Find("Selection");
            root.Find("Header").GetComponent<Text>().text = $"Location: {AppState.Location}    Operator: {AppState.OperatorId}    Subject: {AppState.SubjectId}";
            list = root.Find("Scroll/Viewport/Content");
            ClearChildren(list);
            status = root.Find("Status").GetComponent<Text>();
            var back = root.Find("Actions/BackButton").GetComponent<Button>();
            instructions = root.Find("Actions/InstructionsButton").GetComponent<Button>();
            start = root.Find("Actions/StartButton").GetComponent<Button>();

            back.onClick.RemoveAllListeners(); back.onClick.AddListener(() => m.Startup());
            instructions.onClick.RemoveAllListeners(); instructions.onClick.AddListener(OpenInstructions);
            start.onClick.RemoveAllListeners(); start.onClick.AddListener(StartSelected);
            instructions.interactable = start.interactable = false;
            status.text = "Loading experiments…";
            StartCoroutine(repo.List(Populate, e => status.text = e));
        }

        void Populate(List<ExperimentSummary> experiments)
        {
            experimentButtons.Clear();
            status.text = experiments.Count == 0 ? "No experiment packages found." : "Select an experiment.";
            foreach (var e in experiments)
            {
                var row = UIFactory.GO("Experiment", list); UIFactory.Size((RectTransform)row.transform, 38); var rowLayout = UIFactory.HLayout(row.transform, 5, 0); rowLayout.childForceExpandHeight = false;
                var b = UIFactory.Button($"{e.Name}  (v{e.Version})", row.transform, () => Select(e)); UIFactory.Size(b, 36, -1, -1, 1);
                experimentButtons[e.PackageName] = b;
                var completed = UIFactory.Text("", row.transform, 15, TextAnchor.MiddleCenter); UIFactory.Size(completed, 36, 140);
                StartCoroutine(repo.HasCompleted(AppState.SubjectId, e.PackageName, x => { if (x) { completed.text = "COMPLETED"; completed.color = new Color(.9f,.9f,.9f,1); } }, _ => { }));
                if (e.PackageName == AppState.SelectedPackage) Select(e);
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
            var normalColor = new Color(.20f, .25f, .30f, 1f);
            var selectedColor = new Color(.10f, .48f, .72f, 1f);

            foreach (var pair in experimentButtons)
            {
                var button = pair.Value;
                if (button == null || button.targetGraphic == null) continue;

                bool isSelected = pair.Key == AppState.SelectedPackage;
                button.targetGraphic.color = isSelected ? selectedColor : normalColor;
            }
        }
        void OpenInstructions() { if (selected == null) return; LoadSelected(() => Manager.Instructions()); }
        void StartSelected()
        {
            if (selected == null) return;
            LoadSelected(() => StartCoroutine(repo.HasCompleted(AppState.SubjectId, selected.PackageName, done =>
            {
                if (done) Modal.Show(Manager.Root, "Experiment already completed", "This subject has already completed this experiment. Start another independent run anyway?", () => Manager.Task(), () => { }, "Continue", "Cancel");
                else Manager.Task();
            }, e => Modal.Show(Manager.Root, "Error", e))));
        }
        void LoadSelected(System.Action next)
        {
            StartCoroutine(repo.LoadDefinition(selected.PackageName, d =>
            {
                try { AppState.LoadedDefinition = d; AppState.ResolvedExperiment = ExperimentResolver.Resolve(d); next(); }
                catch (System.Exception e) { Modal.Show(Manager.Root, "Invalid Experiment", e.Message); }
            }, e => Modal.Show(Manager.Root, "Load failed", e)));
        }

        static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--) Object.Destroy(parent.GetChild(i).gameObject);
        }
    }
}
