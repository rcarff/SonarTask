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
            root.Find("Title").GetComponent<Text>().text = "Instructions — " + (AppState.LoadedDefinition?.Name ?? AppState.SelectedPackage);
            var content = root.Find("Scroll/Viewport/Content");
            for (int i = content.childCount - 1; i >= 0; i--) Object.Destroy(content.GetChild(i).gameObject);
            var md = content.GetComponent<MarkdownRenderer>() ?? content.gameObject.AddComponent<MarkdownRenderer>();
            var back = root.Find("BackButton").GetComponent<Button>();
            back.onClick.RemoveAllListeners(); back.onClick.AddListener(() => m.Selection());
            var file = AppState.LoadedDefinition?.InstructionsFile ?? "instructions.md";
            StartCoroutine(RepositoryFactory.Experiments.LoadTextAsset(AppState.SelectedPackage, file,
                text => md.Render(AppState.SelectedPackage, text),
                e => md.Render(AppState.SelectedPackage, "# Unable to load instructions\n\n" + e)));
        }
    }
}
