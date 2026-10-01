using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using SonarTask.Services;

namespace SonarTask.UI
{
    public sealed class StartupScreen : SonarScreen
    {
        EditableComboBox locationCombo;
        EditableComboBox operatorCombo;
        InputField subjectInput;

        public override void Build(ScreenManager m)
        {
            base.Build(m);
            var panel = transform.Find("StartupPanel");
            var version = panel.Find("Version").GetComponent<Text>();
            locationCombo = FindField(panel, "LocationRow/LocationCombo", "LocationCombo").GetComponent<EditableComboBox>();
            operatorCombo = FindField(panel, "OperatorRow/OperatorCombo", "OperatorCombo").GetComponent<EditableComboBox>();
            subjectInput = FindField(panel, "SubjectRow/SubjectInput", "SubjectInput").GetComponent<InputField>();
            var status = panel.Find("Status").GetComponent<Text>();
            var cont = panel.Find("ContinueRow/ContinueButton").GetComponent<Button>();
            var settings = panel.Find("Footer/SettingsButton").GetComponent<Button>();
            var exitProgram = panel.Find("Footer/ExitProgramButton").GetComponent<Button>();

            version.text = "Version " + AppState.Version;
            locationCombo.EnsureVisuals();
            operatorCombo.EnsureVisuals();
            locationCombo.SetOptions(LocalHistory.Load("Location").Where(x => !string.IsNullOrWhiteSpace(x)));
            operatorCombo.SetOptions(LocalHistory.Load("Operator").Where(x => !string.IsNullOrWhiteSpace(x)));
            locationCombo.Text = !string.IsNullOrWhiteSpace(AppState.Location) ? AppState.Location : LocalHistory.Last("Location");
            operatorCombo.Text = !string.IsNullOrWhiteSpace(AppState.OperatorId) ? AppState.OperatorId : LocalHistory.Last("Operator");
            subjectInput.text = AppState.SubjectId.Length > 0 ? AppState.SubjectId : LocalHistory.Last("Subject");
            status.text = string.IsNullOrWhiteSpace(DesktopStorage.InitializationError)
                ? string.Empty
                : "Desktop data folder setup error: " + DesktopStorage.InitializationError;

            cont.onClick.RemoveAllListeners();
            cont.onClick.AddListener(() =>
            {
                string l = locationCombo.Text.Trim(), o = operatorCombo.Text.Trim(), s = subjectInput.text.Trim();
                if (string.IsNullOrWhiteSpace(l) || string.IsNullOrWhiteSpace(o) || string.IsNullOrWhiteSpace(s))
                {
                    status.text = "Location, Experimenter ID, and Subject ID are required.";
                    return;
                }
                AppState.Location = l;
                AppState.OperatorId = o;
                AppState.SubjectId = s;
                LocalHistory.Save("Location", l);
                LocalHistory.Save("Operator", o);
                LocalHistory.SetLast("Location", l);
                LocalHistory.SetLast("Operator", o);
                LocalHistory.SetLast("Subject", s);
                m.Selection();
            });

            settings.gameObject.SetActive(AppState.IsWeb);
            settings.onClick.RemoveAllListeners();
            settings.onClick.AddListener(() => m.Settings());

            exitProgram.gameObject.SetActive(!AppState.IsWeb);
            exitProgram.onClick.RemoveAllListeners();
            exitProgram.onClick.AddListener(QuitProgram);

            OfferLegacyMigrationIfNeeded(status);
        }

        void OfferLegacyMigrationIfNeeded(Text status)
        {
#if !UNITY_EDITOR
            if (AppState.IsWeb || !DesktopStorage.MigrationPending) return;
            var destination = DesktopStorage.DataRoot;
            Modal.Show(Manager.Root,
                "Existing SONAR Data Found",
                "Existing Experiments and/or Results were found beside the installed application.\n\n" +
                "Copy them to the new desktop data folder?\n\n" + destination +
                "\n\nThe original files will not be deleted.",
                () => CompleteDesktopSetup(true, status),
                () => CompleteDesktopSetup(false, status),
                "Copy Data", "Skip");
#endif
        }

        static void CompleteDesktopSetup(bool copyLegacy, Text status)
        {
            try
            {
                DesktopStorage.CompleteFirstRun(copyLegacy);
                status.text = copyLegacy
                    ? "Existing SONAR data copied to " + DesktopStorage.DataRoot
                    : "Using desktop data folder: " + DesktopStorage.DataRoot;
            }
            catch (System.Exception e)
            {
                status.text = "Desktop data folder setup error: " + e.Message;
            }
        }

        static Transform FindField(Transform panel, string newPath, string oldPath)
        {
            var t = panel.Find(newPath);
            return t ? t : panel.Find(oldPath);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.tabKey.wasPressedThisFrame) return;

            var fields = new[] { locationCombo?.Input, operatorCombo?.Input, subjectInput };
            int current = -1;
            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                if (f != null && (f.isFocused || (EventSystem.current && EventSystem.current.currentSelectedGameObject == f.gameObject)))
                {
                    current = i;
                    break;
                }
            }
            if (current < 0) return;

            bool reverse = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            int next = (current + (reverse ? fields.Length - 1 : 1)) % fields.Length;
            locationCombo?.ClosePopup();
            operatorCombo?.ClosePopup();
            Focus(fields[next]);
        }

        static void Focus(InputField field)
        {
            if (!field) return;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(field.gameObject);
            field.ActivateInputField();
            field.MoveTextEnd(false);
        }

        static void QuitProgram()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
