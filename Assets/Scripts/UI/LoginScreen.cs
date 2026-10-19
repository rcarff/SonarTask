using System.Collections;
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
        Text status;
        bool externalLaunching;

        public override void Build(ScreenManager m)
        {
            base.Build(m);
            var panel = transform.Find("LoginPanel");
            panel.Find("Title").GetComponent<Text>().text = AppState.ProductName;
            passwordInput = panel.Find("PasswordInput").GetComponent<InputField>();
            status = panel.Find("Status").GetComponent<Text>();
            loginButton = panel.Find("LoginButton").GetComponent<Button>();

            if (AppState.IsLocalWebDevelopment)
            {
                passwordInput.text = AppState.LocalWebDevPassword;
                status.text = "LOCAL WEBGL DEVELOPMENT MODE - bundled experiments; Settings and external-study URLs can be tested locally.";
            }
            else status.text = string.Empty;

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

            if (ExternalStudyService.HasExternalLaunchParameters())
                StartCoroutine(BeginExternalLaunch());
        }

        IEnumerator BeginExternalLaunch()
        {
            externalLaunching = true;
            loginButton.interactable = false;
            passwordInput.interactable = false;
            status.text = "Validating external-study launch…";

            ExternalLaunchReply reply = null;
            yield return ExternalStudyService.BeginFromCurrentUrl(x => reply = x);

            if (reply == null || !reply.ok)
            {
                externalLaunching = false;
                loginButton.interactable = true;
                passwordInput.interactable = true;
                status.text = "External-study launch failed: " + (reply?.message ?? "No response from server.") + "\nYou can still use the normal operator login below.";
                yield break;
            }

            ExternalStudyService.Apply(reply);
            if (reply.completed)
            {
                status.text = "This participant has already completed the assigned external-study sequence.";
                if (!string.IsNullOrWhiteSpace(reply.completionUrl))
                {
                    status.text += " Redirecting to the recruitment provider…";
                    yield return new WaitForSecondsRealtime(.5f);
                    WebBrowserBridge.NavigateUrl(reply.completionUrl);
                }
                yield break;
            }

            status.text = $"Assigned {reply.assignmentGroup}: {reply.experimentPackage}";
            string loadError = null;
            bool loaded = false;
            yield return ExternalStudyService.LoadCurrentExperiment(() => loaded = true, e => loadError = e);
            if (!loaded)
            {
                externalLaunching = false;
                loginButton.interactable = true;
                passwordInput.interactable = true;
                status.text = "External-study experiment could not be loaded: " + loadError;
                yield break;
            }

            ExternalStudyService.RouteAfterLoad(Manager);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (externalLaunching || kb == null || loginButton == null || passwordInput == null || !loginButton.interactable) return;
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
