using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using SonarTask.Services;

namespace SonarTask.UI
{
    public sealed class LoginScreen : SonarScreen
    {
        InputField passwordInput;
        Button loginButton;

        public override void Build(ScreenManager m)
        {
            base.Build(m);
            var panel = transform.Find("LoginPanel");
            panel.Find("Title").GetComponent<Text>().text = AppState.ProductName;
            passwordInput = panel.Find("PasswordInput").GetComponent<InputField>();
            var status = panel.Find("Status").GetComponent<Text>();
            loginButton = panel.Find("LoginButton").GetComponent<Button>();
            if (AppState.IsLocalWebDevelopment)
            {
                passwordInput.text = AppState.LocalWebDevPassword;
                status.text = "LOCAL WEBGL DEVELOPMENT MODE - bundled experiments; results are not sent to the server.";
            }
            else status.text = string.Empty;

            if (ExternalStudyService.HasExternalLaunchParameters())
            {
                status.text = "Validating external study launch…";
                passwordInput.interactable = false;
                loginButton.interactable = false;
                StartCoroutine(ExternalStudyService.Launch(r =>
                {
                    if (r != null && r.ok)
                    {
                        ExternalStudyService.Apply(r);
                        m.Selection();
                        return;
                    }
                    AppState.ClearExternalStudy();
                    passwordInput.interactable = true;
                    loginButton.interactable = true;
                    status.text = r?.message ?? "External study launch failed.";
                }));
                return;
            }

            loginButton.onClick.RemoveAllListeners();
            loginButton.onClick.AddListener(() =>
            {
                status.text = "Signing in…";
                loginButton.interactable = false;
                StartCoroutine(AuthService.Login(passwordInput.text, r =>
                {
                    loginButton.interactable = true;
                    if (r.ok) m.Startup();
                    else status.text = r.message ?? "Login failed.";
                }));
            });
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || loginButton == null || passwordInput == null || !loginButton.interactable) return;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                loginButton.onClick.Invoke();
        }
    }

    static class RectExt
    {
        public static void Fill(this RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        }
    }
}
