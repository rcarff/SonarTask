using UnityEngine;
using UnityEngine.SceneManagement;
using SonarTask.Services;

namespace SonarTask.UI
{
    public abstract class SonarScreen : MonoBehaviour
    {
        protected ScreenManager Manager;
        public virtual void Build(ScreenManager m) { Manager = m; }
    }

    public sealed class ScreenManager : MonoBehaviour
    {
        Canvas canvas;
        SonarScreen current;

        void Start()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            Route(SceneManager.GetActiveScene().name);
        }

        void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;
        void OnSceneLoaded(Scene s, LoadSceneMode mode) => Route(s.name);

        void Route(string scene)
        {
            if (scene == "Boot")
            {
                SceneManager.LoadScene(AppState.IsWeb ? "LoginWeb" : "Startup");
                return;
            }

            canvas = Object.FindFirstObjectByType<Canvas>();
            if (!canvas) canvas = UIFactory.Canvas(); // Fallback for older generated scenes.
            var root = SceneLayoutBuilder.Ensure(scene, canvas.transform);

            current = scene switch
            {
                "LoginWeb" => root.GetComponent<LoginScreen>() ?? root.gameObject.AddComponent<LoginScreen>(),
                "Startup" => root.GetComponent<StartupScreen>() ?? root.gameObject.AddComponent<StartupScreen>(),
                "ExperimentSelection" => root.GetComponent<SelectionScreen>() ?? root.gameObject.AddComponent<SelectionScreen>(),
                "Instructions" => root.GetComponent<InstructionsScreen>() ?? root.gameObject.AddComponent<InstructionsScreen>(),
                "SettingsWeb" => root.GetComponent<SettingsScreen>() ?? root.gameObject.AddComponent<SettingsScreen>(),
                "SonarTask" => root.GetComponent<SonarTaskScreen>() ?? root.gameObject.AddComponent<SonarTaskScreen>(),
                _ => root.GetComponent<StartupScreen>() ?? root.gameObject.AddComponent<StartupScreen>()
            };
            current.Build(this);
        }

        public Transform Root => canvas ? canvas.transform : transform;
        public void Startup() => SceneManager.LoadScene("Startup");
        public void Selection() => SceneManager.LoadScene("ExperimentSelection");
        public void Instructions() => SceneManager.LoadScene("Instructions");
        public void Settings() => SceneManager.LoadScene("SettingsWeb");
        public void Task() => SceneManager.LoadScene("SonarTask");
    }
}
