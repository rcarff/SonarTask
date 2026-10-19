using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.UI;
using SonarTask.Core;
using SonarTask.Services;

namespace SonarTask.UI
{
    public sealed class SettingsScreen : SonarScreen
    {
        Transform body;
        Text status;
        InputField passwordInput;
        Button unlockButton;

        InputField filter;
        Transform experimentList;
        Image uploadProgressFill;
        Text uploadProgressText;
        readonly List<string> selectedResults = new();
        readonly List<ExpRow> editorExperiments = new();
        readonly List<GroupEditorRow> groupEditors = new();
        Transform groupEditorList;

        PopupDropdown providerDrop, modeDrop;
        InputField externalStudyId, completionUrl;
        Toggle autoStartToggle, showInstructionsToggle, enabledToggle;
        Text launchPreview;
        string generatedTestUrl = "";
        ExternalStudyConfig editingExternalStudy;

        [Serializable] sealed class ExpRow { public string packageName, name, version; }
        [Serializable] sealed class ResultRow { public string fileName, runId, subjectId, experimentPackage, startedUtc, status; }
        [Serializable] sealed class StorageStatus { public bool writable; public string message; }
        [Serializable] sealed class TestTokenReply { public bool ok; public string token, expiresUtc; }

        sealed class GroupEditorRow
        {
            public string id;
            public GameObject root;
            public InputField name;
            public InputField weight;
            public readonly List<ExperimentChoice> choices = new();
        }

        sealed class ExperimentChoice { public PopupDropdown dropdown; public string packageName; public GameObject root; }

        public override void Build(ScreenManager m)
        {
            base.Build(m);
            var root = transform.Find("Settings");
            passwordInput = root.Find("PasswordInput").GetComponent<InputField>();
            status = root.Find("Status").GetComponent<Text>();
            unlockButton = root.Find("UnlockButton").GetComponent<Button>();
            var back = root.Find("BackButton").GetComponent<Button>();
            body = root.Find("AdminBody");
            UIFactory.Size(body as RectTransform, 610, -1, 1);
            body.gameObject.SetActive(false);

            if (AppState.IsLocalWebDevelopment)
            {
                passwordInput.text = AppState.LocalWebDevPassword;
                status.text = "LOCAL WEBGL DEVELOPMENT MODE - use the pre-filled development password to test Settings.";
            }
            else status.text = "Re-authentication is required before settings are shown.";

            unlockButton.onClick.RemoveAllListeners();
            unlockButton.onClick.AddListener(() => {
                unlockButton.interactable = false;
                StartCoroutine(AuthService.Reauth(passwordInput.text, r => {
                    unlockButton.interactable = true;
                    if (r.ok) ShowSettingsHome(); else status.text = r.message;
                }));
            });
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(() => m.Startup());
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || unlockButton == null || passwordInput == null || !unlockButton.interactable) return;
            if (body != null && body.gameObject.activeSelf) return;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                unlockButton.onClick.Invoke();
        }

        void ShowSettingsHome()
        {
            ClearBody();
            body.gameObject.SetActive(true);
            status.text = AppState.IsLocalWebDevelopment
                ? "LOCAL WEBGL DEVELOPMENT MODE - server-only operations are disabled, but Settings views and External Studies can be tested."
                : "Choose a settings area.";

            Heading("Settings", "Choose one administration area. Re-authentication is not required again until the elevated Settings cookie expires.");
            HomeButton("Experiments", "Upload, inspect, and remove experiment packages.", ShowExperiments);
            HomeButton("Results", "Search and download result files.", ShowResults);
            HomeButton("External Studies", "Configure Prolific, CloudResearch Connect, SONA, random groups, and counterbalanced sequences.", ShowExternalStudies);
            HomeButton("Account", "Change the Web operator password.", ShowAccount);
        }

        void HomeButton(string title, string description, Action click)
        {
            var row = UIFactory.Panel(title + "Card", body, new Color(.07f,.08f,.095f,1));
            UIFactory.Size(row, 92);
            UIFactory.VLayout(row.transform, 3, 8);
            var b = UIFactory.Button(title, row.transform, click, 20); UIFactory.Size(b, 42);
            var d = UIFactory.Text(description, row.transform, 14, TextAnchor.MiddleCenter); UIFactory.Size(d, 28);
        }

        void ShowExperiments()
        {
            ClearBody();
            Heading("Settings > Experiments", AppState.IsLocalWebDevelopment
                ? "Local test uses experiment packages copied from the project at build time. They are read-only here."
                : "Manage installed experiment packages.");

            var up = UIFactory.Button(AppState.IsLocalWebDevelopment ? "Upload Experiment ZIP (server only)" : "Upload Experiment ZIP", body,
                () => WebBrowserBridge.PickZip(gameObject, nameof(OnZipUploadEvent)));
            UIFactory.Size(up, 44);
            if (AppState.IsLocalWebDevelopment) up.interactable = false;

            var progress = UIFactory.Panel("UploadProgress", body, new Color(.08f, .10f, .12f, 1)); UIFactory.Size(progress, 30);
            uploadProgressFill = UIFactory.Panel("Fill", progress.transform, new Color(.32f, .67f, .78f, 1));
            uploadProgressFill.raycastTarget = false;
            uploadProgressFill.rectTransform.anchorMin = Vector2.zero;
            uploadProgressFill.rectTransform.anchorMax = new Vector2(0, 1);
            uploadProgressFill.rectTransform.offsetMin = uploadProgressFill.rectTransform.offsetMax = Vector2.zero;
            uploadProgressText = UIFactory.Text("Ready", progress.transform, 14, TextAnchor.MiddleCenter);
            uploadProgressText.raycastTarget = false;
            SetUploadProgress(0f, AppState.IsLocalWebDevelopment ? "Bundled experiments - rebuild local WebGL after changes" : "Ready to upload");

            experimentList = UIFactory.GO("ExperimentList", body).transform;
            UIFactory.VLayout(experimentList, 4, 0);
            LoadExperiments();
            if (!AppState.IsLocalWebDevelopment)
                StartCoroutine(Get<StorageStatus>("/api/admin/experiments/storage-status", x => {
                    if (!x.writable) status.text = "Experiment storage is not writable: " + x.message;
                }, _ => { }));
            BackToSettings();
        }

        void ShowResults()
        {
            ClearBody();
            Heading("Settings > Results", AppState.IsLocalWebDevelopment
                ? "Production result browsing/download requires the API server. Local task result events remain in browser memory only."
                : "Browse and download result files.");
            filter = UIFactory.Input("Filter result filenames", body); UIFactory.Size(filter, 44);
            var buttons = UIFactory.GO("ResultActions", body); UIFactory.Size((RectTransform)buttons.transform, 44); UIFactory.HLayout(buttons.transform, 6, 0);
            var refresh = UIFactory.Button("Search", buttons.transform, LoadResults); UIFactory.Size(refresh, 42, 110);
            var dl = UIFactory.Button("Download Selected", buttons.transform, DownloadSelected); UIFactory.Size(dl, 42, 170);
            var all = UIFactory.Button("Download Visible", buttons.transform, () => WebBrowserBridge.OpenUrl("/api/admin/results/download?filter=" + UnityWebRequest.EscapeURL(filter.text))); UIFactory.Size(all, 42, 170);
            if (AppState.IsLocalWebDevelopment) { refresh.interactable = false; dl.interactable = false; all.interactable = false; }
            var results = UIFactory.GO("ResultsList", body); UIFactory.VLayout(results.transform, 3, 0);
            LoadResults();
            BackToSettings();
        }

        void ShowAccount()
        {
            ClearBody();
            Heading("Settings > Account", AppState.IsLocalWebDevelopment
                ? "Local WebGL testing always uses the fixed development password SonarDev123!."
                : "Change the Web operator password.");
            var np = UIFactory.Input("New password (12+ characters)", body, true); UIFactory.Size(np, 44);
            var cp = UIFactory.Button(AppState.IsLocalWebDevelopment ? "Change Password (server only)" : "Change Password", body, () =>
                StartCoroutine(AuthService.ChangePassword(passwordInput.text, np.text, r => status.text = r.message)));
            UIFactory.Size(cp, 44);
            if (AppState.IsLocalWebDevelopment) cp.interactable = false;
            BackToSettings();
        }

        void ShowExternalStudies()
        {
            ClearBody();
            Heading("Settings > External Studies", AppState.IsLocalWebDevelopment
                ? "Local mappings and assignments are stored only in browser-local development storage."
                : "Provider IDs are mapped to server-side participant assignments. Participants cannot choose their own experiment package.");
            var add = UIFactory.Button("Add External Study", body, () => ShowExternalStudyEditor(null)); UIFactory.Size(add, 42);
            var list = UIFactory.GO("ExternalStudyList", body).transform; UIFactory.VLayout(list, 4, 0);
            BackToSettings();

            Action<List<ExternalStudyConfig>> render = rows => {
                ClearChildren(list);
                if (rows == null || rows.Count == 0) {
                    var empty = UIFactory.Text("No external studies are configured.", list, 15, TextAnchor.MiddleCenter); UIFactory.Size(empty, 42);
                    return;
                }
                foreach (var x in rows.OrderBy(x => x.provider).ThenBy(x => x.providerStudyId))
                {
                    var card = UIFactory.Panel("ExternalStudy", list, new Color(.065f,.075f,.09f,1)); UIFactory.Size(card, 110); UIFactory.VLayout(card.transform, 2, 6);
                    var summary = UIFactory.Text(
                        $"{ExternalStudyService.ProviderDisplay(x.provider)}  |  {x.providerStudyId}  |  {ExternalStudyService.AssignmentModeDisplay(x.assignmentMode)}  |  {(x.enabled ? "Enabled" : "Disabled")}\n" +
                        $"Production: {x.assignedCount} assigned / {x.completedCount} completed    Test: {x.testAssignedCount} assigned / {x.testCompletedCount} completed" + GroupCounts(x),
                        card.transform, 13, TextAnchor.MiddleLeft);
                    UIFactory.Size(summary, 66);
                    var actions = UIFactory.GO("Actions", card.transform); UIFactory.Size((RectTransform)actions.transform, 30); var hl = UIFactory.HLayout(actions.transform, 5, 0); hl.childAlignment = TextAnchor.MiddleRight;
                    var edit = UIFactory.Button("Edit", actions.transform, () => ShowExternalStudyEditor(CloneStudy(x)), 13); UIFactory.Size(edit, 28, 68);
                    var test = UIFactory.Button("Test URL", actions.transform, () => { ShowExternalStudyEditor(CloneStudy(x)); GenerateTestLaunchUrl(); }, 13); UIFactory.Size(test, 28, 82);
                    var clearTest = UIFactory.Button("Clear Tests", actions.transform, () => ResetTestData(x), 13); UIFactory.Size(clearTest, 28, 90);
                    var resetProd = UIFactory.Button("Reset Prod", actions.transform, () => ResetProductionData(x, false), 13); UIFactory.Size(resetProd, 28, 90);
                    var toggle = UIFactory.Button(x.enabled ? "Disable" : "Enable", actions.transform, () => ToggleExternalStudy(x), 13); UIFactory.Size(toggle, 28, 78);
                    var del = UIFactory.Button("Delete", actions.transform, () => DeleteExternalStudy(x), 13); UIFactory.Size(del, 28, 68);
                }
            };

            if (AppState.IsLocalWebDevelopment) render(LocalWebDevelopmentStore.StudiesWithCounts());
            else StartCoroutine(Get<List<ExternalStudyConfig>>("/api/admin/external-studies", render, e => status.text = FriendlyServerMessage(e)));
        }

        static string GroupCounts(ExternalStudyConfig x)
        {
            if (x.groups == null || x.groups.Count == 0) return "";
            return "   |   " + string.Join("   ", x.groups.Select(g => $"{g.name} P:{g.assignedCount}/{g.completedCount} T:{g.testAssignedCount}/{g.testCompletedCount}"));
        }

        void ShowExternalStudyEditor(ExternalStudyConfig row)
        {
            editingExternalStudy = row ?? NewStudy();
            ClearBody();
            Heading("Settings > External Studies > " + (row == null ? "Add" : "Edit"),
                "Assignment is persistent by participant ID. Balanced Random and Counterbalanced keep group allocation approximately even; Random uses group weights independently.");

            providerDrop = UIFactory.PopupDropdown("Provider", body); UIFactory.Size(providerDrop, 40);
            providerDrop.SetOptions(new[] { "Prolific", "CloudResearch Connect", "SONA", "Generic" }, ProviderIndex(editingExternalStudy.provider), false);

            externalStudyId = UIFactory.Input("Provider Study / Project ID", body); UIFactory.Size(externalStudyId, 40); externalStudyId.text = editingExternalStudy.providerStudyId ?? "";

            modeDrop = UIFactory.PopupDropdown("Assignment Mode", body); UIFactory.Size(modeDrop, 40);
            modeDrop.SetOptions(new[] { "Fixed", "Random", "Balanced Random", "Counterbalanced" }, ModeIndex(editingExternalStudy.assignmentMode), false);

            completionUrl = UIFactory.Input("Completion URL (optional)", body); UIFactory.Size(completionUrl, 40); completionUrl.text = editingExternalStudy.completionUrl ?? "";

            var toggles = UIFactory.GO("Flags", body); UIFactory.Size((RectTransform)toggles.transform, 38); var th = UIFactory.HLayout(toggles.transform, 14, 0); th.childForceExpandWidth = false; th.childAlignment = TextAnchor.MiddleCenter;
            autoStartToggle = UIFactory.Toggle("Auto Start", toggles.transform); UIFactory.Size(autoStartToggle, 36, 150); autoStartToggle.isOn = editingExternalStudy.autoStart;
            showInstructionsToggle = UIFactory.Toggle("Show Instructions", toggles.transform); UIFactory.Size(showInstructionsToggle, 36, 180); showInstructionsToggle.isOn = editingExternalStudy.showInstructions;
            enabledToggle = UIFactory.Toggle("Enabled", toggles.transform); UIFactory.Size(enabledToggle, 36, 130); enabledToggle.isOn = editingExternalStudy.enabled;

            var expHelp = UIFactory.Text("Loading installed experiment package names…", body, 13, TextAnchor.MiddleLeft); UIFactory.Size(expHelp, 34);

            var groupHeader = UIFactory.GO("GroupHeader", body); UIFactory.Size((RectTransform)groupHeader.transform, 34); var gh = UIFactory.HLayout(groupHeader.transform, 6, 0); gh.childAlignment = TextAnchor.MiddleCenter;
            var gl = UIFactory.Text("Groups / Sequences", groupHeader.transform, 17, TextAnchor.MiddleLeft); UIFactory.Size(gl, 32, -1, -1, 1);
            var addGroup = UIFactory.Button("+ Add Group", groupHeader.transform, () => AddGroupEditor(null), 14); UIFactory.Size(addGroup, 32, 120);

            var groupScroll = UIFactory.GO("GroupEditorsScroll", body); UIFactory.Size((RectTransform)groupScroll.transform, 170);
            var scroll = groupScroll.AddComponent<ScrollRect>(); scroll.horizontal = false;
            var viewport = UIFactory.Panel("Viewport", groupScroll.transform, new Color(.035f,.045f,.055f,1));
            var vr = viewport.rectTransform; vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one; vr.offsetMin = vr.offsetMax = Vector2.zero;
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            groupEditorList = UIFactory.GO("GroupEditors", viewport.transform).transform;
            var gr = (RectTransform)groupEditorList; gr.anchorMin = new Vector2(0,1); gr.anchorMax = new Vector2(1,1); gr.pivot = new Vector2(.5f,1); gr.sizeDelta = Vector2.zero;
            UIFactory.VLayout(groupEditorList, 4, 0);
            var fitter = groupEditorList.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = vr; scroll.content = gr;
            groupEditors.Clear();
            if (editingExternalStudy.groups == null || editingExternalStudy.groups.Count == 0) editingExternalStudy.groups = NewStudy().groups;
            foreach (var g in editingExternalStudy.groups) AddGroupEditor(g, groupEditorList);

            generatedTestUrl = "";
            launchPreview = UIFactory.Text("Launch URL will appear after the provider/study ID is entered.", body, 13, TextAnchor.MiddleLeft); UIFactory.Size(launchPreview, 68);
            providerDrop.ValueChanged += _ => RefreshLaunchPreview();
            modeDrop.ValueChanged += _ => { RefreshLaunchPreview(); status.text = AssignmentHelp(SelectedMode()); };
            externalStudyId.onValueChanged.AddListener(_ => RefreshLaunchPreview());

            var testActions = UIFactory.GO("TestActions", body); UIFactory.Size((RectTransform)testActions.transform, 40); var tah = UIFactory.HLayout(testActions.transform, 8, 0); tah.childAlignment = TextAnchor.MiddleCenter;
            var genTest = UIFactory.Button(AppState.IsLocalWebDevelopment ? "Save + Generate Local Test URL" : "Save + Generate Secure Test URL", testActions.transform, GenerateTestLaunchUrl, 14); UIFactory.Size(genTest, 38, 190);
            var copyTest = UIFactory.Button("Copy Test URL", testActions.transform, () => { if (string.IsNullOrWhiteSpace(generatedTestUrl)) status.text = "Generate a test URL first."; else { WebBrowserBridge.CopyText(generatedTestUrl); status.text = "Test URL copied to the clipboard. Open it in a private/incognito browser window so the participant cookie does not replace your Settings login."; } }, 14); UIFactory.Size(copyTest, 38, 150);
            var clearTests = UIFactory.Button("Clear Test Data", testActions.transform, () => ResetTestData(editingExternalStudy), 14); UIFactory.Size(clearTests, 38, 150);

            var actions = UIFactory.GO("EditorActions", body); UIFactory.Size((RectTransform)actions.transform, 42); var ah = UIFactory.HLayout(actions.transform, 8, 0); ah.childAlignment = TextAnchor.MiddleCenter;
            var save = UIFactory.Button("Save", actions.transform, SaveExternalStudy); UIFactory.Size(save, 40, 130);
            var resetProd = UIFactory.Button("Reset Production Data", actions.transform, () => ResetProductionData(editingExternalStudy, true), 14); UIFactory.Size(resetProd, 40, 190);
            var cancel = UIFactory.Button("Cancel", actions.transform, ShowExternalStudies); UIFactory.Size(cancel, 40, 130);

            status.text = AssignmentHelp(SelectedMode());
            LoadEditorExperiments(expHelp);
            RefreshLaunchPreview();
        }

        void AddGroupEditor(ExternalStudyGroup group, Transform forcedParent = null)
        {
            var parent = forcedParent ?? groupEditorList;
            if (!parent) return;
            group ??= new ExternalStudyGroup { id = Guid.NewGuid().ToString("N"), name = "Group " + (groupEditors.Count + 1), weight = 1, experiments = new List<string>() };
            var row = UIFactory.Panel("GroupEditor", parent, new Color(.055f,.065f,.078f,1)); UIFactory.Size(row, 100);
            UIFactory.VLayout(row.transform, 3, 3);
            row.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var header = UIFactory.GO("GroupHeader", row.transform); UIFactory.Size((RectTransform)header.transform, 42);
            var h = UIFactory.HLayout(header.transform, 5, 3); h.childAlignment = TextAnchor.MiddleCenter;
            var name = UIFactory.Input("Group name", header.transform); UIFactory.Size(name, 38, 130); name.text = group.name ?? "";
            var weight = UIFactory.Input("Weight", header.transform); UIFactory.Size(weight, 38, 70); weight.contentType = InputField.ContentType.IntegerNumber; weight.text = Mathf.Max(1, group.weight).ToString();
            var add = UIFactory.Button("+ Experiment", header.transform, null, 13); UIFactory.Size(add, 38, 120);
            var remove = UIFactory.Button("Remove Group", header.transform, null, 13); UIFactory.Size(remove, 38, 110);
            var choicesRoot = UIFactory.GO("ExperimentChoices", row.transform);
            UIFactory.Size((RectTransform)choicesRoot.transform, 42);
            UIFactory.VLayout(choicesRoot.transform, 3, 0);
            choicesRoot.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var editor = new GroupEditorRow { id = string.IsNullOrWhiteSpace(group.id) ? Guid.NewGuid().ToString("N") : group.id, root = row.gameObject, name = name, weight = weight };
            groupEditors.Add(editor);
            add.onClick.AddListener(() => AddExperimentChoice(editor, choicesRoot.transform, null));
            foreach (var package in group.experiments ?? new List<string>()) AddExperimentChoice(editor, choicesRoot.transform, package);
            if (editor.choices.Count == 0) AddExperimentChoice(editor, choicesRoot.transform, null);
            remove.onClick.AddListener(() => { groupEditors.Remove(editor); Destroy(editor.root); });
        }

        void AddExperimentChoice(GroupEditorRow group, Transform parent, string selected)
        {
            var line = UIFactory.GO("ExperimentChoice", parent); UIFactory.Size((RectTransform)line.transform, 38);
            var layout = UIFactory.HLayout(line.transform, 5, 0); layout.childAlignment = TextAnchor.MiddleCenter;
            var dropdown = UIFactory.PopupDropdown("Select experiment package", line.transform); UIFactory.Size(dropdown, 36, -1, -1, 1);
            var remove = UIFactory.Button("Remove", line.transform, null, 12); UIFactory.Size(remove, 36, 75);
            var choice = new ExperimentChoice { root = line, dropdown = dropdown, packageName = selected ?? "" };
            group.choices.Add(choice);
            RefreshExperimentChoice(choice);
            dropdown.ValueChanged += index => { if (index >= 0 && index < dropdown.Options.Count) { var value = dropdown.Options[index]; if (!value.StartsWith("Missing: ") && value != "No installed experiments") choice.packageName = value; } };
            remove.onClick.AddListener(() => {
                if (group.choices.Count <= 1) { status.text = "Each group must have at least one experiment."; return; }
                group.choices.Remove(choice); Destroy(line);
            });
        }

        void RefreshExperimentChoice(ExperimentChoice choice)
        {
            var names = editorExperiments.Select(e => e.packageName).ToList();
            if (names.Count == 0) { choice.dropdown.SetOptions(new[] { "No installed experiments" }); return; }
            int index = names.FindIndex(x => string.Equals(x, choice.packageName, StringComparison.OrdinalIgnoreCase));
            if (index < 0 && !string.IsNullOrWhiteSpace(choice.packageName))
            {
                // Keep a missing package visible, but require correction before saving.
                names.Insert(0, "Missing: " + choice.packageName);
                choice.dropdown.SetOptions(names, 0, false);
                return;
            }
            choice.dropdown.SetOptions(names, Mathf.Max(0, index), false);
            if (index < 0) choice.packageName = names[0];
        }

        void LoadEditorExperiments(Text help)
        {
            editorExperiments.Clear();
            Action<List<ExpRow>> loaded = rows => {
                editorExperiments.AddRange(rows ?? new List<ExpRow>());
                help.text = rows == null || rows.Count == 0
                    ? "No experiments are installed."
                    : "Installed package names: " + string.Join(", ", rows.Select(x => x.packageName));
                foreach (var group in groupEditors) foreach (var choice in group.choices) RefreshExperimentChoice(choice);
                RefreshLaunchPreview();
            };
            if (AppState.IsLocalWebDevelopment) StartCoroutine(LoadLocalExperimentRows(loaded));
            else StartCoroutine(Get<List<ExpRow>>("/api/admin/experiments", loaded, e => status.text = "Unable to load experiments: " + FriendlyServerMessage(e)));
        }

        void SaveExternalStudy()
        {
            var x = CaptureExternalStudyEditor();
            if (x == null) return;

            if (AppState.IsLocalWebDevelopment)
            {
                LocalWebDevelopmentStore.UpsertStudy(x);
                status.text = "Local external study saved.";
                ShowExternalStudies();
            }
            else StartCoroutine(PostJson("/api/admin/external-studies", x,
                _ => { status.text = "External study saved."; ShowExternalStudies(); },
                e => status.text = FriendlyServerMessage(e)));
        }

        ExternalStudyConfig CaptureExternalStudyEditor()
        {
            var x = editingExternalStudy ?? NewStudy();
            x.provider = ProviderKey(providerDrop.Value);
            x.providerStudyId = (externalStudyId.text ?? "").Trim();
            x.assignmentMode = SelectedMode();
            x.completionUrl = (completionUrl.text ?? "").Trim();
            x.autoStart = autoStartToggle.isOn;
            x.showInstructions = showInstructionsToggle.isOn;
            x.enabled = enabledToggle.isOn;
            x.groups = new List<ExternalStudyGroup>();

            foreach (var g in groupEditors)
            {
                int weight = 1; int.TryParse(g.weight.text, out weight); weight = Mathf.Max(1, weight);
                var sequence = g.choices.Select(c => c.packageName?.Trim()).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                x.groups.Add(new ExternalStudyGroup {
                    id = g.id,
                    name = string.IsNullOrWhiteSpace(g.name.text) ? "Group " + (x.groups.Count + 1) : g.name.text.Trim(),
                    weight = weight,
                    experiments = sequence
                });
            }

            var validation = ValidateStudyClient(x);
            if (validation != null) { status.text = validation; return null; }
            editingExternalStudy = x;
            return x;
        }

        string ValidateStudyClient(ExternalStudyConfig x)
        {
            if (string.IsNullOrWhiteSpace(x.providerStudyId)) return "Provider Study / Project ID is required.";
            if (x.groups == null || x.groups.Count == 0) return "Add at least one group/sequence.";
            if (x.assignmentMode == "Fixed" && x.groups.Count != 1) return "Fixed assignment requires exactly one group. Use another assignment mode for multiple groups.";
            if (x.assignmentMode == "Counterbalanced" && x.groups.Count < 2) return "Counterbalanced assignment requires at least two sequences/groups.";
            foreach (var g in x.groups)
            {
                if (g.experiments == null || g.experiments.Count == 0) return $"{g.name} must contain at least one experiment package.";
                foreach (var package in g.experiments)
                    if (editorExperiments.Count > 0 && !editorExperiments.Any(e => string.Equals(e.packageName, package, StringComparison.OrdinalIgnoreCase)))
                        return $"{g.name} references experiment package '{package}', which is not currently installed.";
            }
            if (!string.IsNullOrWhiteSpace(x.completionUrl) && !Uri.TryCreate(x.completionUrl, UriKind.Absolute, out _))
                return "Completion URL must be an absolute URL or left blank.";
            return null;
        }

        void ToggleExternalStudy(ExternalStudyConfig x)
        {
            var copy = CloneStudy(x); copy.enabled = !copy.enabled;
            if (AppState.IsLocalWebDevelopment) { LocalWebDevelopmentStore.UpsertStudy(copy); ShowExternalStudies(); }
            else StartCoroutine(PostJson("/api/admin/external-studies", copy, _ => ShowExternalStudies(), e => status.text = FriendlyServerMessage(e)));
        }

        void DeleteExternalStudy(ExternalStudyConfig x)
        {
            Modal.Show(Manager.Root, "Delete external study", $"Delete mapping '{x.providerStudyId}'?\n\nMappings with participant assignments cannot be deleted; disable them instead.", () => {
                if (AppState.IsLocalWebDevelopment)
                {
                    if (LocalWebDevelopmentStore.DeleteStudy(x.id, out var error)) ShowExternalStudies();
                    else status.text = error;
                }
                else StartCoroutine(DeleteExternal(x.id));
            }, () => { }, "Delete", "Cancel");
        }

        IEnumerator DeleteExternal(string id)
        {
            using var r = new UnityWebRequest("/api/admin/external-studies/" + UnityWebRequest.EscapeURL(id), "DELETE") { downloadHandler = new DownloadHandlerBuffer() };
            yield return r.SendWebRequest();
            if (r.result == UnityWebRequest.Result.Success) ShowExternalStudies();
            else status.text = FriendlyServerMessage(r.downloadHandler.text);
        }


        void GenerateTestLaunchUrl()
        {
            // Test URLs must reflect the values currently visible in the editor.
            // Save first so a checked Enabled box (or changed provider/study ID) cannot
            // disagree with the persisted mapping used by the participant launch API.
            var x = CaptureExternalStudyEditor();
            if (x == null) return;

            if (AppState.IsLocalWebDevelopment)
            {
                LocalWebDevelopmentStore.UpsertStudy(x);
                generatedTestUrl = BuildTestLaunchUrl(x, "");
                launchPreview.text = "LOCAL TEST URL (saved; does not affect production counts): " + generatedTestUrl;
                status.text = "Local External Study saved and test URL generated. Test assignments are counted separately from production assignments.";
                return;
            }

            status.text = "Saving External Study before generating the secure test URL...";
            StartCoroutine(PostJson("/api/admin/external-studies", x, _ =>
            {
                StartCoroutine(PostJson("/api/admin/external-studies/" + UnityWebRequest.EscapeURL(x.id) + "/test-token", new { }, text => {
                    try
                    {
                        var reply = JsonConvert.DeserializeObject<TestTokenReply>(text);
                        if (reply == null || string.IsNullOrWhiteSpace(reply.token)) { status.text = "Server did not return a test token."; return; }
                        generatedTestUrl = BuildTestLaunchUrl(x, reply.token);
                        launchPreview.text = "SECURE TEST URL (saved; expires " + reply.expiresUtc + "): " + generatedTestUrl;
                        status.text = "External Study saved and secure test URL generated. Test assignments/results are separate from production counts. Use Copy Test URL, then open it in a private/incognito window.";
                    }
                    catch (Exception e) { status.text = "Unable to read test-token response: " + e.Message; }
                }, e => status.text = FriendlyServerMessage(e)));
            }, e => status.text = "Unable to save External Study before testing: " + FriendlyServerMessage(e)));
        }

        string BuildTestLaunchUrl(ExternalStudyConfig x, string token)
        {
            string baseUrl = "https://your-sonar-server.example/";
            try { baseUrl = new Uri(Application.absoluteURL).GetLeftPart(UriPartial.Path); } catch { }
            int qi = baseUrl.IndexOf('?'); if (qi >= 0) baseUrl = baseUrl.Substring(0, qi);
            string sep = baseUrl.Contains("?") ? "&" : "?";
            string study = UnityWebRequest.EscapeURL((x.providerStudyId ?? "").Trim());
            string pid = "SONAR-TEST-" + Guid.NewGuid().ToString("N").Substring(0, 10).ToUpperInvariant();
            string provider = ExternalStudyService.NormalizeProvider(x.provider);
            string test = AppState.IsLocalWebDevelopment ? "&sonar_test=1" : "&sonar_test_token=" + UnityWebRequest.EscapeURL(token ?? "");
            if (AppState.IsLocalWebDevelopment)
            {
                // Self-contained local-only URL: browser private mode has separate PlayerPrefs.
                string json = JsonConvert.SerializeObject(x);
                string payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                test += "&sonar_local_study=" + payload;
            }
            return provider switch {
                "prolific" => baseUrl + sep + "provider=prolific&PROLIFIC_PID=" + pid + "&STUDY_ID=" + study + "&SESSION_ID=TEST-SESSION" + test,
                "connect" => baseUrl + sep + "provider=connect&participantId=" + pid + "&projectId=" + study + "&assignmentId=TEST-ASSIGNMENT" + test,
                "sona" => baseUrl + sep + "provider=sona&study=" + study + "&subject=" + pid + test,
                _ => baseUrl + sep + "provider=generic&study=" + study + "&subject=" + pid + "&session=TEST-SESSION" + test
            };
        }

        void ResetTestData(ExternalStudyConfig x)
        {
            if (x == null || string.IsNullOrWhiteSpace(x.id)) { status.text = "Save the External Study first."; return; }
            Modal.Show(Manager.Root, "Clear external-study test data",
                $"Clear all TEST assignments and TEST result files for '{x.providerStudyId}'?\n\nProduction assignments, production result files, and balancing counts are not changed.",
                () => {
                    if (AppState.IsLocalWebDevelopment)
                    {
                        LocalWebDevelopmentStore.ResetTestData(x.id);
                        status.text = "Local test assignments cleared.";
                        ShowExternalStudies();
                    }
                    else StartCoroutine(PostJson("/api/admin/external-studies/" + UnityWebRequest.EscapeURL(x.id) + "/reset-test-data", new { }, _ => {
                        status.text = "Test assignments and test result files cleared."; ShowExternalStudies();
                    }, e => status.text = FriendlyServerMessage(e)));
                }, () => { }, "Clear Tests", "Cancel");
        }

        void ResetProductionData(ExternalStudyConfig x, bool deleteResults)
        {
            if (x == null || string.IsNullOrWhiteSpace(x.id)) { status.text = "Save the External Study first."; return; }
            string first = deleteResults
                ? $"RESET ALL PRODUCTION assignment records AND associated production result files for '{x.providerStudyId}'?\n\nUse this only before opening the real study. This cannot be undone."
                : $"Reset production assignment counts for '{x.providerStudyId}'?\n\nExisting result files are kept, but returning participant IDs may be assigned again.";
            Modal.Show(Manager.Root, "Reset production study data", first, () => {
                if (deleteResults)
                {
                    Modal.Show(Manager.Root, "Final confirmation",
                        "This will permanently delete the production result files linked to this External Study and reset its production allocation counts to zero.",
                        () => DoResetProductionData(x, true), () => { }, "RESET ALL", "Cancel");
                }
                else DoResetProductionData(x, false);
            }, () => { }, deleteResults ? "Continue" : "Reset Counts", "Cancel");
        }

        void DoResetProductionData(ExternalStudyConfig x, bool deleteResults)
        {
            if (AppState.IsLocalWebDevelopment)
            {
                LocalWebDevelopmentStore.ResetProductionAssignments(x.id);
                status.text = "Local production assignments reset. Local WebGL runs are memory-only, so there are no server result files to delete.";
                ShowExternalStudies();
                return;
            }
            StartCoroutine(PostJson("/api/admin/external-studies/" + UnityWebRequest.EscapeURL(x.id) + "/reset-production-data",
                new { deleteResults }, text => { status.text = deleteResults ? "Production assignments and associated result files reset." : "Production assignment counts reset; result files were retained."; ShowExternalStudies(); },
                e => status.text = FriendlyServerMessage(e)));
        }

        void RefreshLaunchPreview()
        {
            if (launchPreview == null || externalStudyId == null || providerDrop == null) return;
            string baseUrl = "https://your-sonar-server.example/";
            try
            {
                var uri = new Uri(Application.absoluteURL);
                baseUrl = uri.GetLeftPart(UriPartial.Path);
            }
            catch { }
            int q = baseUrl.IndexOf('?'); if (q >= 0) baseUrl = baseUrl.Substring(0, q);
            string sep = baseUrl.Contains("?") ? "&" : "?";
            string study = UnityWebRequest.EscapeURL((externalStudyId.text ?? "").Trim());
            string provider = ProviderKey(providerDrop.Value);

            if (AppState.IsLocalWebDevelopment)
            {
                launchPreview.text = "Local test URL: " + (provider switch {
                    "prolific" => baseUrl + sep + "provider=prolific&PROLIFIC_PID=TEST-PARTICIPANT&STUDY_ID=" + study + "&SESSION_ID=TEST-SESSION&sonar_test=1",
                    "connect" => baseUrl + sep + "provider=connect&participantId=TEST-PARTICIPANT&projectId=" + study + "&assignmentId=TEST-ASSIGNMENT&sonar_test=1",
                    "sona" => baseUrl + sep + "provider=sona&study=" + study + "&subject=TEST-SUBJECT&sonar_test=1",
                    _ => baseUrl + sep + "provider=generic&study=" + study + "&subject=TEST-SUBJECT&session=TEST-SESSION&sonar_test=1"
                });
            }
            else
            {
                launchPreview.text = "Provider launch URL: " + (provider switch {
                    "prolific" => baseUrl + sep + "provider=prolific",
                    "connect" => baseUrl + sep + "provider=connect",
                    "sona" => baseUrl + sep + "provider=sona&study=" + study + "&subject=%SURVEY_CODE%",
                    _ => baseUrl + sep + "provider=generic&study=" + study + "&subject=SUBJECT_ID"
                });
            }
        }

        void LoadResults()
        {
            if (body == null || !body.gameObject.activeSelf) return;
            var list = body.Find("ResultsList"); if (!list) return;
            ClearChildren(list);
            selectedResults.Clear();
            if (AppState.IsLocalWebDevelopment)
            {
                var info = UIFactory.Text("Local WebGL test runs do not create production server result files.", list, 15, TextAnchor.MiddleCenter); UIFactory.Size(info, 52);
                return;
            }
            StartCoroutine(Get<List<ResultRow>>("/api/admin/results?filter=" + UnityWebRequest.EscapeURL(filter?.text ?? ""), rows =>
            {
                foreach (var x in rows)
                {
                    var row = UIFactory.GO("Result", list); UIFactory.Size((RectTransform)row.transform, 38); UIFactory.HLayout(row.transform, 4, 0);
                    var tg = UIFactory.Toggle(x.fileName, row.transform); UIFactory.Size(tg, 36, -1, -1, 1);
                    tg.onValueChanged.AddListener(v => { if (v) { if (!selectedResults.Contains(x.fileName)) selectedResults.Add(x.fileName); } else selectedResults.Remove(x.fileName); });
                    var one = UIFactory.Button("Download", row.transform, () => WebBrowserBridge.OpenUrl("/api/admin/results/file/" + UnityWebRequest.EscapeURL(x.fileName))); UIFactory.Size(one, 36, 100);
                }
            }, e => status.text = FriendlyServerMessage(e)));
        }

        void DownloadSelected()
        {
            if (AppState.IsLocalWebDevelopment) { status.text = "Production result downloads are server-only."; return; }
            if (selectedResults.Count == 0) { status.text = "Select one or more result files first."; return; }
            StartCoroutine(PostJson("/api/admin/results/download-selected", new { files = selectedResults }, text =>
                WebBrowserBridge.OpenUrl("/api/admin/results/download-token/" + text.Trim('"')), e => status.text = FriendlyServerMessage(e)));
        }

        void LoadExperiments()
        {
            if (experimentList == null) return;
            ClearChildren(experimentList);
            Action<List<ExpRow>> render = rows => {
                foreach (var x in rows ?? new List<ExpRow>())
                {
                    var row = UIFactory.GO("Exp", experimentList); UIFactory.Size((RectTransform)row.transform, 40); UIFactory.HLayout(row.transform, 5, 0);
                    var t = UIFactory.Text($"{x.name} ({x.packageName}) v{x.version}", row.transform, 15); UIFactory.Size(t, 38, -1, -1, 1);
                    var del = UIFactory.Button(AppState.IsLocalWebDevelopment ? "Read only" : "Delete", row.transform, () => DeleteExperiment(x.packageName)); UIFactory.Size(del, 38, 100);
                    if (AppState.IsLocalWebDevelopment) del.interactable = false;
                }
            };
            if (AppState.IsLocalWebDevelopment) StartCoroutine(LoadLocalExperimentRows(render));
            else StartCoroutine(Get<List<ExpRow>>("/api/admin/experiments", render, e => status.text = "Unable to refresh experiment list: " + FriendlyServerMessage(e)));
        }

        IEnumerator LoadLocalExperimentRows(Action<List<ExpRow>> done)
        {
            List<ExperimentSummary> source = null; string error = null;
            yield return RepositoryFactory.Experiments.List(x => source = x, e => error = e);
            if (source == null) { status.text = "Unable to load local experiments: " + error; done(new List<ExpRow>()); yield break; }
            done(source.Select(x => new ExpRow { packageName = x.PackageName, name = x.Name, version = x.Version }).ToList());
        }

        void SetUploadProgress(float value, string text)
        {
            if (uploadProgressFill != null) uploadProgressFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(value), 1);
            if (uploadProgressText != null) uploadProgressText.text = text;
        }

        public void OnZipUploadEvent(string payload)
        {
            if (AppState.IsLocalWebDevelopment) { status.text = "Experiment upload is disabled in Local WebGL Test."; return; }
            if (string.IsNullOrEmpty(payload)) return;
            var parts = payload.Split(new[] { '|' }, 4);
            switch (parts[0])
            {
                case "START": SetUploadProgress(0f, "Uploading " + (parts.Length > 1 ? parts[1] : "experiment ZIP") + "... 0%"); status.text = "Uploading experiment package..."; break;
                case "PROGRESS": int pct = 0; if (parts.Length > 1) int.TryParse(parts[1], out pct); SetUploadProgress(pct / 100f, $"Uploading... {pct}%"); break;
                case "PROCESSING": SetUploadProgress(1f, "Upload complete - validating/installing..."); status.text = "Validating and installing experiment package..."; break;
                case "OK": SetUploadProgress(1f, "Upload successful"); status.text = "Experiment uploaded successfully."; LoadExperiments(); break;
                case "ERR":
                    SetUploadProgress(0f, "Upload failed");
                    var code = parts.Length > 1 ? parts[1] : ""; var msg = parts.Length > 2 ? parts[2] : payload;
                    status.text = code == "413"
                        ? "Upload failed (HTTP 413): The experiment ZIP exceeds the 64 MiB experiment upload limit."
                        : "Upload failed" + (string.IsNullOrEmpty(code) || code == "0" ? "" : $" (HTTP {code})") + ": " + FriendlyServerMessage(msg);
                    break;
            }
        }

        void DeleteExperiment(string package)
        {
            if (AppState.IsLocalWebDevelopment) { status.text = "Bundled local-test experiments are read-only. Change the project Experiments folder and rebuild."; return; }
            Modal.Show(Manager.Root, "Delete experiment", $"Delete experiment package '{package}'?", () => StartCoroutine(DeleteExperimentRequest(package)), () => { }, "Delete", "Cancel");
        }

        IEnumerator DeleteExperimentRequest(string package)
        {
            using var r = new UnityWebRequest("/api/admin/experiments/" + UnityWebRequest.EscapeURL(package), "DELETE") { downloadHandler = new DownloadHandlerBuffer() };
            yield return r.SendWebRequest();
            if (r.result == UnityWebRequest.Result.Success) { status.text = "Experiment deleted."; LoadExperiments(); }
            else status.text = FriendlyServerMessage(r.downloadHandler.text);
        }

        void Heading(string title, string subtitle)
        {
            var h = UIFactory.Text(title, body, 22, TextAnchor.MiddleCenter); UIFactory.Size(h, 34);
            var s = UIFactory.Text(subtitle, body, 13, TextAnchor.MiddleCenter); UIFactory.Size(s, 42);
        }

        void BackToSettings()
        {
            var b = UIFactory.Button("Back to Settings", body, ShowSettingsHome); UIFactory.Size(b, 40);
        }

        void ClearBody()
        {
            ClearChildren(body);
            body.gameObject.SetActive(true);
            experimentList = null; filter = null; uploadProgressFill = null; uploadProgressText = null;
            groupEditors.Clear(); editorExperiments.Clear(); groupEditorList = null;
        }

        static ExternalStudyConfig NewStudy() => new() {
            id = Guid.NewGuid().ToString("N"),
            provider = "prolific",
            assignmentMode = "Fixed",
            autoStart = true,
            showInstructions = true,
            enabled = true,
            groups = new List<ExternalStudyGroup> {
                new() { id = Guid.NewGuid().ToString("N"), name = "Group 1", weight = 1, experiments = new List<string>() }
            }
        };

        static ExternalStudyConfig CloneStudy(ExternalStudyConfig x) => JsonConvert.DeserializeObject<ExternalStudyConfig>(JsonConvert.SerializeObject(x));
        static int ProviderIndex(string provider) => ExternalStudyService.NormalizeProvider(provider) switch { "connect" => 1, "sona" => 2, "generic" => 3, _ => 0 };
        static string ProviderKey(int ix) => ix switch { 1 => "connect", 2 => "sona", 3 => "generic", _ => "prolific" };
        static int ModeIndex(string mode) => ExternalStudyService.NormalizeAssignmentMode(mode) switch { "Random" => 1, "BalancedRandom" => 2, "Counterbalanced" => 3, _ => 0 };
        string SelectedMode() => modeDrop == null ? "Fixed" : modeDrop.Value switch { 1 => "Random", 2 => "BalancedRandom", 3 => "Counterbalanced", _ => "Fixed" };
        static string AssignmentHelp(string mode) => ExternalStudyService.NormalizeAssignmentMode(mode) switch {
            "Random" => "Random: each new participant is assigned independently using group weights.",
            "BalancedRandom" => "Balanced Random: the server chooses among the least-filled weighted groups; equal-weight group counts remain approximately even.",
            "Counterbalanced" => "Counterbalanced: each group is an experiment sequence (for example A,B versus B,A); sequence groups are allocated in balanced randomized order.",
            _ => "Fixed: every participant receives the single configured group/sequence."
        };

        static string FriendlyServerMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "No response from server.";
            try {
                var r = JsonConvert.DeserializeObject<Dictionary<string, object>>(text);
                if (r != null && r.TryGetValue("message", out var m) && m != null) return m.ToString();
            } catch { }
            return text;
        }

        IEnumerator Get<T>(string url, Action<T> ok, Action<string> fail)
        {
            using var r = UnityWebRequest.Get(url); yield return r.SendWebRequest();
            if (r.result != UnityWebRequest.Result.Success) { fail(r.downloadHandler.text); yield break; }
            try { ok(JsonConvert.DeserializeObject<T>(r.downloadHandler.text)); } catch (Exception e) { fail(e.Message); }
        }

        IEnumerator PostJson(string url, object data, Action<string> ok, Action<string> fail)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data));
            using var r = new UnityWebRequest(url, "POST") { uploadHandler = new UploadHandlerRaw(bytes), downloadHandler = new DownloadHandlerBuffer() };
            r.SetRequestHeader("Content-Type", "application/json");
            yield return r.SendWebRequest();
            if (r.result == UnityWebRequest.Result.Success) ok(r.downloadHandler.text); else fail(r.downloadHandler.text);
        }

        static void ClearChildren(Transform p)
        {
            if (!p) return;
            for (int i = p.childCount - 1; i >= 0; i--) {
                var g = p.GetChild(i).gameObject; g.SetActive(false); UnityEngine.Object.Destroy(g);
            }
        }
    }
}
