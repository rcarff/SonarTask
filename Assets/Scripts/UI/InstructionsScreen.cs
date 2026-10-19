using UnityEngine;
using UnityEngine.UI;
using SonarTask.Services;

namespace SonarTask.UI
{
    public sealed class InstructionsScreen : SonarScreen
    {
        public override void Build(ScreenManager m)
        {
            base.Build(m);
            var root = transform.Find("Instructions");
            string title = "Instructions — " + (AppState.LoadedDefinition?.Name ?? AppState.SelectedPackage);
            if (AppState.ExternalStudyActive && AppState.ExternalExperimentSequence.Count > 1)
                title += $"    ({AppState.ExternalSequenceIndex + 1} of {AppState.ExternalExperimentSequence.Count})";
            root.Find("Title").GetComponent<Text>().text = title;

            var content = root.Find("Scroll/Viewport/Content");
            for (int i = content.childCount - 1; i >= 0; i--) Object.Destroy(content.GetChild(i).gameObject);
            var md = content.GetComponent<MarkdownRenderer>() ?? content.gameObject.AddComponent<MarkdownRenderer>();
            var back = root.Find("BackButton").GetComponent<Button>();
            back.onClick.RemoveAllListeners();
            if (AppState.ExternalStudyActive)
            {
                var text = back.GetComponentInChildren<Text>();
                if (text) text.text = "Start Experiment";
                back.onClick.AddListener(() => m.Task());
            }
            else
            {
                var text = back.GetComponentInChildren<Text>();
                if (text) text.text = "Back";
                back.onClick.AddListener(() => m.Selection());
            }

            var file = AppState.LoadedDefinition?.InstructionsFile ?? "instructions.md";
            StartCoroutine(RepositoryFactory.Experiments.LoadTextAsset(AppState.SelectedPackage, file,
                text => md.Render(AppState.SelectedPackage, text),
                e => md.Render(AppState.SelectedPackage, "# Unable to load instructions\n\n" + e)));
        }
    }
}
