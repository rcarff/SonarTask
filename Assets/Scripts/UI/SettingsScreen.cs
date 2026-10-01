using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using SonarTask.Services;

namespace SonarTask.UI
{
    public sealed class SettingsScreen : SonarScreen
    {
        Transform body;
        Text status;
        InputField filter;
        Transform experimentList;
        Image uploadProgressFill;
        Text uploadProgressText;
        InputField passwordInput;
        Button unlockButton;
        readonly List<string> selectedResults = new();
        readonly List<ExpRow> editorExperiments = new();
        PopupDropdown providerDrop, experimentDrop;
        InputField externalStudyId, completionUrl, launchPreview;
        Toggle autoStartToggle, showInstructionsToggle, enabledToggle;
        ExternalStudyConfig editingExternalStudy;

        [Serializable] sealed class ExpRow { public string packageName, name, version; }
        [Serializable] sealed class ResultRow { public string fileName, runId, subjectId, experimentPackage, startedUtc, status; }
        [Serializable] sealed class StorageStatus { public bool writable; public string message; }

        public override void Build(ScreenManager m)
        {
            base.Build(m);
            var root = transform.Find("Settings");
            passwordInput = root.Find("PasswordInput").GetComponent<InputField>();
            status = root.Find("Status").GetComponent<Text>();
            unlockButton = root.Find("UnlockButton").GetComponent<Button>();
            var back = root.Find("BackButton").GetComponent<Button>();
            body = root.Find("AdminBody");
            body.gameObject.SetActive(false);
            status.text = AppState.IsLocalWebDevelopment
                ? "LOCAL WEBGL DEVELOPMENT MODE - re-authenticate with the local development password to test Settings."
                : "Re-authentication is required before settings are shown.";

            unlockButton.onClick.RemoveAllListeners();
            unlockButton.onClick.AddListener(() => {
                unlockButton.interactable = false;
                StartCoroutine(AuthService.Reauth(passwordInput.text, r => {
                    unlockButton.interactable = true;
                    if (r.ok) BuildAdmin(); else status.text = r.message;
                }));
            });
            back.onClick.RemoveAllListeners(); back.onClick.AddListener(() => m.Startup());
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || unlockButton == null || passwordInput == null || !unlockButton.interactable) return;
            if (body != null && body.gameObject.activeSelf) return;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                unlockButton.onClick.Invoke();
        }

        void BuildAdmin()
        {
            body.gameObject.SetActive(true);
            passwordInput.gameObject.SetActive(false);
            unlockButton.gameObject.SetActive(false);
            status.text = "Settings unlocked.";
            ShowHome();
        }

        void ShowHome()
        {
            ClearChildren(body);
            var h = UIFactory.Text("Settings", body, 24, TextAnchor.MiddleCenter); UIFactory.Size(h, 44);
            AddHomeButton("Experiments", "Upload and manage experiment packages", ShowExperiments);
            AddHomeButton("Results", "Browse and download result files", ShowResults);
            AddHomeButton("External Studies", "Configure Prolific, CloudResearch Connect, SONA, and generic launches", ShowExternalStudies);
            AddHomeButton("Account", "Change the Web operator password", ShowAccount);
            status.text = AppState.IsLocalWebDevelopment
                ? "LOCAL WEBGL DEVELOPMENT MODE - Settings are testable; server-only operations are disabled."
                : "Choose a settings area.";
        }

        void AddHomeButton(string label, string description, Action click)
        {
            var panel = UIFactory.Panel(label, body, new Color(.07f,.08f,.095f,1)); UIFactory.Size(panel, 78); UIFactory.HLayout(panel.transform, 8, 8);
            var b = UIFactory.Button(label, panel.transform, click, 18); UIFactory.Size(b, 58, 240);
            var t = UIFactory.Text(description, panel.transform, 15, TextAnchor.MiddleLeft); UIFactory.Size(t, 58, -1, -1, 1);
        }

        Button BackToSettings()
        {
            var b = UIFactory.Button("Back to Settings", body, ShowHome); UIFactory.Size(b, 42, 180); return b;
        }

        void ShowExperiments()
        {
            ClearChildren(body);
            var h = UIFactory.Text("Settings > Experiments", body, 22, TextAnchor.MiddleCenter); UIFactory.Size(h, 42);
            var up = UIFactory.Button(AppState.IsLocalWebDevelopment ? "Upload Experiment ZIP (server only)" : "Upload Experiment ZIP", body, () => WebBrowserBridge.PickZip(gameObject, nameof(OnZipUploadEvent))); UIFactory.Size(up, 44);
            if (AppState.IsLocalWebDevelopment) up.interactable = false;
            var progress = UIFactory.Panel("UploadProgress", body, new Color(.08f, .10f, .12f, 1)); UIFactory.Size(progress, 30);
            uploadProgressFill = UIFactory.Panel("Fill", progress.transform, new Color(.32f, .67f, .78f, 1));
            uploadProgressFill.raycastTarget = false;
            uploadProgressFill.rectTransform.anchorMin = Vector2.zero; uploadProgressFill.rectTransform.anchorMax = new Vector2(0, 1);
            uploadProgressFill.rectTransform.offsetMin = uploadProgressFill.rectTransform.offsetMax = Vector2.zero;
            uploadProgressText = UIFactory.Text("Ready to upload", progress.transform, 14, TextAnchor.MiddleCenter); uploadProgressText.raycastTarget = false;
            SetUploadProgress(0f, AppState.IsLocalWebDevelopment ? "Local test uses experiments captured at build time" : "Ready to upload");
            experimentList = UIFactory.GO("ExperimentList", body).transform; UIFactory.VLayout(experimentList, 4, 0);
            LoadExperiments();
            if (!AppState.IsLocalWebDevelopment)
                StartCoroutine(Get<StorageStatus>("/api/admin/experiments/storage-status", x => { if (!x.writable) status.text = "Experiment storage is not writable: " + x.message; }, e => { }));
            BackToSettings();
            status.text = AppState.IsLocalWebDevelopment
                ? "Local test: bundled experiments are read-only. Rebuild Web Local Test after changing the project Experiments folder."
                : "Manage experiment packages.";
        }

        void ShowResults()
        {
            ClearChildren(body);
            var h = UIFactory.Text("Settings > Results", body, 22, TextAnchor.MiddleCenter); UIFactory.Size(h, 42);
            filter = UIFactory.Input("Filter result filenames", body); UIFactory.Size(filter, 44);
            var buttons = UIFactory.GO("ResultActions", body); UIFactory.Size((RectTransform)buttons.transform, 44); UIFactory.HLayout(buttons.transform, 6, 0);
            var refresh = UIFactory.Button("Search", buttons.transform, LoadResults); UIFactory.Size(refresh, 42, 110);
            var dl = UIFactory.Button("Download Selected", buttons.transform, DownloadSelected); UIFactory.Size(dl, 42, 170);
            var all = UIFactory.Button("Download Visible", buttons.transform, () => WebBrowserBridge.OpenUrl("/api/admin/results/download?filter=" + UnityWebRequest.EscapeURL(filter.text))); UIFactory.Size(all, 42, 170);
            if (AppState.IsLocalWebDevelopment) { dl.interactable = false; all.interactable = false; }
            var results = UIFactory.GO("ResultsList", body); UIFactory.VLayout(results.transform, 3, 0);
            BackToSettings();
            LoadResults();
            status.text = AppState.IsLocalWebDevelopment
                ? "Local test: production result browsing/download is server-only; task result events remain browser-local."
                : "Browse and download results.";
        }

        void ShowAccount()
        {
            ClearChildren(body);
            var h = UIFactory.Text("Settings > Account", body, 22, TextAnchor.MiddleCenter); UIFactory.Size(h, 42);
            var current = UIFactory.Input("Current operator password", body, true); UIFactory.Size(current, 44);
            var np = UIFactory.Input("New password (12+ characters)", body, true); UIFactory.Size(np, 44);
            var confirm = UIFactory.Input("Confirm new password", body, true); UIFactory.Size(confirm, 44);
            var change = UIFactory.Button("Change Password", body, () => {
                if (np.text != confirm.text) { status.text = "New passwords do not match."; return; }
                StartCoroutine(AuthService.ChangePassword(current.text, np.text, r => status.text = r.message));
            }); UIFactory.Size(change, 44);
            if (AppState.IsLocalWebDevelopment) change.interactable = false;
            BackToSettings();
            status.text = AppState.IsLocalWebDevelopment
                ? "Local test uses the fixed development password SonarDev123!; password changes are server-only."
                : "Change the Web operator password.";
        }

        void ShowExternalStudies()
        {
            ClearChildren(body);
            var h = UIFactory.Text("Settings > External Studies", body, 22, TextAnchor.MiddleCenter); UIFactory.Size(h, 42);
            var add = UIFactory.Button("Add External Study", body, () => ShowExternalStudyEditor(null)); UIFactory.Size(add, 42, 190);
            var list = UIFactory.GO("ExternalStudyList", body).transform; UIFactory.VLayout(list, 4, 0);
            BackToSettings();
            status.text = "Loading external study mappings…";
            Action<List<ExternalStudyConfig>> render = rows => {
                ClearChildren(list);
                if (rows.Count == 0) { var empty = UIFactory.Text("No external studies are configured.", list, 15, TextAnchor.MiddleCenter); UIFactory.Size(empty, 38); }
                foreach (var x in rows)
                {
                    var row = UIFactory.GO("ExternalStudy", list); UIFactory.Size((RectTransform)row.transform, 42); UIFactory.HLayout(row.transform, 5, 0);
                    var label = UIFactory.Text($"{ProviderDisplay(x.provider)}   {x.providerStudyId}   →   {x.experimentPackage}   {(x.enabled ? "Enabled" : "Disabled")}", row.transform, 14); UIFactory.Size(label, 40, -1, -1, 1);
                    var edit = UIFactory.Button("Edit", row.transform, () => ShowExternalStudyEditor(x)); UIFactory.Size(edit, 40, 76);
                    var toggle = UIFactory.Button(x.enabled ? "Disable" : "Enable", row.transform, () => ToggleExternalStudy(x)); UIFactory.Size(toggle, 40, 84);
                    var del = UIFactory.Button("Delete", row.transform, () => DeleteExternalStudy(x)); UIFactory.Size(del, 40, 78);
                }
                status.text = AppState.IsLocalWebDevelopment
                    ? "Local test mappings are stored only in this browser origin and are never sent to the production server."
                    : "External study mappings are server-side and protected by Settings authentication.";
            };
            if (AppState.IsLocalWebDevelopment) render(LocalWebDevelopmentStore.LoadExternalStudies());
            else StartCoroutine(Get<List<ExternalStudyConfig>>("/api/admin/external-studies", render, e => status.text = FriendlyServerMessage(e)));
        }

        void ShowExternalStudyEditor(ExternalStudyConfig row)
        {
            editingExternalStudy = row;
            ClearChildren(body);
            var h = UIFactory.Text(row == null ? "External Studies > Add" : "External Studies > Edit", body, 22, TextAnchor.MiddleCenter); UIFactory.Size(h, 42);

            var providers = new[] { "Prolific", "CloudResearch Connect", "SONA", "Generic" };
            providerDrop = UIFactory.PopupDropdown("Provider", body); UIFactory.Size(providerDrop, 42);
            int pIndex = row == null ? 0 : Mathf.Max(0, Array.IndexOf(new[] { "prolific", "connect", "sona", "generic" }, NormalizeProvider(row.provider)));
            providerDrop.SetOptions(providers, pIndex);

            externalStudyId = UIFactory.Input("Provider Study / Project ID (or SONA mapping key)", body); UIFactory.Size(externalStudyId, 42);
            externalStudyId.text = row?.providerStudyId ?? "";

            experimentDrop = UIFactory.PopupDropdown("Loading SONAR experiments…", body); UIFactory.Size(experimentDrop, 42);
            experimentDrop.SetOptions(new[] { "Loading SONAR experiments…" }, 0);

            completionUrl = UIFactory.Input("Completion / return URL (optional)", body); UIFactory.Size(completionUrl, 42);
            completionUrl.text = row?.completionUrl ?? "";

            autoStartToggle = UIFactory.Toggle("Automatically launch the assigned experiment", body); UIFactory.Size(autoStartToggle, 36); autoStartToggle.isOn = row?.autoStart ?? true;
            showInstructionsToggle = UIFactory.Toggle("Show SONAR instructions before starting", body); UIFactory.Size(showInstructionsToggle, 36); showInstructionsToggle.isOn = row?.showInstructions ?? true;
            enabledToggle = UIFactory.Toggle("Enabled", body); UIFactory.Size(enabledToggle, 36); enabledToggle.isOn = row?.enabled ?? true;

            var previewLabel = UIFactory.Text("Launch URL", body, 15); UIFactory.Size(previewLabel, 26);
            var previewRow = UIFactory.GO("LaunchPreview", body); UIFactory.Size((RectTransform)previewRow.transform, 42); UIFactory.HLayout(previewRow.transform, 5, 0);
            launchPreview = UIFactory.Input("Launch URL preview", previewRow.transform); UIFactory.Size(launchPreview, 40, -1, -1, 1); launchPreview.readOnly = true;
            var copy = UIFactory.Button("Copy", previewRow.transform, () => { GUIUtility.systemCopyBuffer = launchPreview.text; status.text = "Launch URL copied to the clipboard."; }); UIFactory.Size(copy, 40, 80);

            var actions = UIFactory.GO("EditorActions", body); UIFactory.Size((RectTransform)actions.transform, 44); UIFactory.HLayout(actions.transform, 6, 0);
            var save = UIFactory.Button("Save", actions.transform, SaveExternalStudy); UIFactory.Size(save, 42, 120);
            var cancel = UIFactory.Button("Cancel", actions.transform, ShowExternalStudies); UIFactory.Size(cancel, 42, 120);

            providerDrop.ValueChanged += _ => RefreshLaunchPreview();
            experimentDrop.ValueChanged += _ => RefreshLaunchPreview();
            externalStudyId.onValueChanged.AddListener(_ => RefreshLaunchPreview());
            RefreshLaunchPreview();
            LoadEditorExperiments(row?.experimentPackage);
            status.text = AppState.IsLocalWebDevelopment
                ? "Local test: save the mapping, then copy/open the generated localhost URL to test provider parameters."
                : "Configure the recruitment provider mapping. Completion URLs are stored on the server, not accepted from participant launch URLs.";
        }

        void LoadEditorExperiments(string selectedPackage)
        {
            editorExperiments.Clear();
            Action<List<ExpRow>> loaded = rows => {
                editorExperiments.AddRange(rows);
                if (rows.Count == 0) { experimentDrop.SetOptions(new[] { "No experiments installed" }, 0); RefreshLaunchPreview(); return; }
                int ix = 0;
                for (int i=0;i<rows.Count;i++) if (rows[i].packageName == selectedPackage) { ix=i; break; }
                experimentDrop.SetOptions(rows.Select(x => $"{x.name} ({x.packageName})").ToArray(), ix);
                RefreshLaunchPreview();
            };
            if (AppState.IsLocalWebDevelopment) StartCoroutine(LoadLocalExperimentRows(loaded));
            else StartCoroutine(Get<List<ExpRow>>("/api/admin/experiments", loaded, e => status.text = "Unable to load experiments: " + FriendlyServerMessage(e)));
        }

        void SaveExternalStudy()
        {
            if (string.IsNullOrWhiteSpace(externalStudyId.text)) { status.text = "Provider Study / Project ID is required."; return; }
            if (editorExperiments.Count == 0 || experimentDrop.Value < 0 || experimentDrop.Value >= editorExperiments.Count) { status.text = "Select an installed SONAR experiment."; return; }
            var x = new ExternalStudyConfig {
                id = editingExternalStudy?.id ?? "",
                provider = ProviderKey(providerDrop.Value),
                providerStudyId = externalStudyId.text.Trim(),
                experimentPackage = editorExperiments[experimentDrop.Value].packageName,
                completionUrl = completionUrl.text.Trim(),
                autoStart = autoStartToggle.isOn,
                showInstructions = showInstructionsToggle.isOn,
                enabled = enabledToggle.isOn
            };
            if (AppState.IsLocalWebDevelopment) {
                LocalWebDevelopmentStore.UpsertExternalStudy(x);
                status.text = "Local external study saved.";
                ShowExternalStudies();
            } else StartCoroutine(PostJson("/api/admin/external-studies", x, _ => { status.text = "External study saved."; ShowExternalStudies(); }, e => status.text = FriendlyServerMessage(e)));
        }

        void ToggleExternalStudy(ExternalStudyConfig x)
        {
            x.enabled = !x.enabled;
            if (AppState.IsLocalWebDevelopment) { LocalWebDevelopmentStore.UpsertExternalStudy(x); ShowExternalStudies(); }
            else StartCoroutine(PostJson("/api/admin/external-studies", x, _ => ShowExternalStudies(), e => status.text = FriendlyServerMessage(e)));
        }

        void DeleteExternalStudy(ExternalStudyConfig x) => Modal.Show(Manager.Root, "Delete external study", $"Delete the {ProviderDisplay(x.provider)} mapping '{x.providerStudyId}'?", () => StartCoroutine(DeleteExternal(x.id)), () => { }, "Delete", "Cancel");
        IEnumerator DeleteExternal(string id)
        {
            if (AppState.IsLocalWebDevelopment) {
                LocalWebDevelopmentStore.DeleteExternalStudy(id);
                status.text = "Local external study deleted.";
                ShowExternalStudies();
                yield break;
            }
            using var r = new UnityWebRequest("/api/admin/external-studies/" + UnityWebRequest.EscapeURL(id), "DELETE") { downloadHandler = new DownloadHandlerBuffer() };
            yield return r.SendWebRequest();
            if (r.result == UnityWebRequest.Result.Success) { status.text = "External study deleted."; ShowExternalStudies(); }
            else status.text = FriendlyServerMessage(r.downloadHandler.text);
        }

        void RefreshLaunchPreview()
        {
            if (launchPreview == null || providerDrop == null || externalStudyId == null) return;
            string baseUrl = "https://YOUR-SONAR-SERVER/";
            try { var u = new Uri(Application.absoluteURL); baseUrl = u.GetLeftPart(UriPartial.Path).Split('?')[0]; }
            catch { }
            var sep = baseUrl.Contains("?") ? "&" : "?";
            var study = Uri.EscapeDataString(externalStudyId.text.Trim());
            string experiment = "";
            if (editorExperiments.Count > 0 && experimentDrop != null && experimentDrop.Value >= 0 && experimentDrop.Value < editorExperiments.Count)
                experiment = Uri.EscapeDataString(editorExperiments[experimentDrop.Value].packageName);
            var localExperiment = AppState.IsLocalWebDevelopment && !string.IsNullOrWhiteSpace(experiment) ? "&experiment=" + experiment : "";

            if (AppState.IsLocalWebDevelopment)
            {
                launchPreview.text = ProviderKey(providerDrop.Value) switch {
                    "prolific" => baseUrl + sep + "provider=prolific&PROLIFIC_PID=TEST-PARTICIPANT&STUDY_ID=" + study + "&SESSION_ID=TEST-SESSION" + localExperiment,
                    "connect" => baseUrl + sep + "provider=connect&participantId=TEST-PARTICIPANT&projectId=" + study + "&assignmentId=TEST-ASSIGNMENT" + localExperiment,
                    "sona" => baseUrl + sep + "provider=sona&study=" + study + "&subject=TEST-SUBJECT" + localExperiment,
                    _ => baseUrl + sep + "provider=generic&study=" + study + "&subject=TEST-SUBJECT&session=TEST-SESSION" + localExperiment
                };
            }
            else
            {
                launchPreview.text = ProviderKey(providerDrop.Value) switch {
                    "prolific" => baseUrl + sep + "provider=prolific",
                    "connect" => baseUrl + sep + "provider=connect",
                    "sona" => baseUrl + sep + "provider=sona&study=" + study + "&subject=%SURVEY_CODE%",
                    _ => baseUrl + sep + "provider=generic&study=" + study + "&subject=SUBJECT_ID"
                };
            }
        }

        static string ProviderKey(int index) => index switch { 0 => "prolific", 1 => "connect", 2 => "sona", _ => "generic" };
        static string NormalizeProvider(string p) => (p ?? "").Trim().ToLowerInvariant() switch { "cloudresearch" or "cloudresearchconnect" => "connect", var x => x };
        static string ProviderDisplay(string p) => NormalizeProvider(p) switch { "prolific" => "Prolific", "connect" => "CloudResearch Connect", "sona" => "SONA", _ => "Generic" };

        void LoadResults()
        {
            if (body == null || !body.gameObject.activeSelf) return;
            var old = body.Find("ResultsList"); if (old) ClearChildren(old);
            selectedResults.Clear();
            if (AppState.IsLocalWebDevelopment) {
                var list = body.Find("ResultsList");
                if (list != null) {
                    var info = UIFactory.Text("Local WebGL test runs do not create server result files. Production result browsing/download is intentionally unavailable here.", list, 15, TextAnchor.MiddleCenter);
                    UIFactory.Size(info, 52);
                }
                return;
            }
            StartCoroutine(Get<List<ResultRow>>("/api/admin/results?filter=" + UnityWebRequest.EscapeURL(filter?.text ?? ""), rows => {
                var list = body.Find("ResultsList");
                foreach (var x in rows) {
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
            StartCoroutine(PostJson("/api/admin/results/download-selected", new { files = selectedResults }, text => WebBrowserBridge.OpenUrl("/api/admin/results/download-token/" + text.Trim('"')), e => status.text = FriendlyServerMessage(e)));
        }

        void LoadExperiments()
        {
            if (experimentList == null) return;
            ClearChildren(experimentList);
            Action<List<ExpRow>> render = rows => {
                foreach (var x in rows) {
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
            List<SonarTask.Core.ExperimentSummary> source = null;
            string error = null;
            yield return RepositoryFactory.Experiments.List(x => source = x, e => error = e);
            if (source == null) {
                status.text = "Unable to load local experiments: " + error;
                done(new List<ExpRow>());
                yield break;
            }
            done(source.Select(x => new ExpRow { packageName = x.PackageName, name = x.Name, version = x.Version }).ToList());
        }

        void SetUploadProgress(float value, string text)
        {
            if (uploadProgressFill != null) uploadProgressFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(value), 1);
            if (uploadProgressText != null) uploadProgressText.text = text;
        }

        public void OnZipUploadEvent(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return;
            var parts = payload.Split(new[] { '|' }, 4);
            switch (parts[0]) {
                case "START": SetUploadProgress(0f, "Uploading " + (parts.Length > 1 ? parts[1] : "experiment ZIP") + "... 0%"); status.text = "Uploading experiment package..."; break;
                case "PROGRESS": int pct = 0; if (parts.Length > 1) int.TryParse(parts[1], out pct); SetUploadProgress(pct / 100f, $"Uploading... {pct}%"); break;
                case "PROCESSING": SetUploadProgress(1f, "Upload complete - validating/installing..."); status.text = "Validating and installing experiment package..."; break;
                case "OK": SetUploadProgress(1f, "Upload successful"); status.text = "Experiment uploaded successfully."; LoadExperiments(); break;
                case "ERR":
                    SetUploadProgress(0f, "Upload failed"); var code = parts.Length > 1 ? parts[1] : ""; var msg = parts.Length > 2 ? parts[2] : payload;
                    status.text = code == "413" ? "Upload failed (HTTP 413): The experiment ZIP is too large for the server upload limit. The default package limit is 64 MiB." : "Upload failed" + (string.IsNullOrEmpty(code) || code == "0" ? "" : $" (HTTP {code})") + ": " + FriendlyServerMessage(msg); break;
            }
        }

        static string FriendlyServerMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "No response from server.";
            try { var r = JsonConvert.DeserializeObject<Dictionary<string, object>>(text); if (r != null && r.TryGetValue("message", out var m) && m != null) return m.ToString(); } catch { }
            return text;
        }

        void DeleteExperiment(string p)
        {
            if (AppState.IsLocalWebDevelopment) { status.text = "Bundled local-test experiments are read-only. Change the project Experiments folder and rebuild."; return; }
            Modal.Show(Manager.Root, "Delete experiment", $"Delete experiment package '{p}'?", () => StartCoroutine(Delete("/api/admin/experiments/" + UnityWebRequest.EscapeURL(p))), () => { }, "Delete", "Cancel");
        }
        IEnumerator Delete(string url) { using var r = new UnityWebRequest(url, "DELETE") { downloadHandler = new DownloadHandlerBuffer() }; yield return r.SendWebRequest(); if (r.result == UnityWebRequest.Result.Success) { status.text = "Experiment deleted."; LoadExperiments(); } else status.text = FriendlyServerMessage(r.downloadHandler.text); }
        IEnumerator Get<T>(string url, Action<T> ok, Action<string> fail) { using var r = UnityWebRequest.Get(url); yield return r.SendWebRequest(); if (r.result != UnityWebRequest.Result.Success) { fail(r.downloadHandler.text); yield break; } try { ok(JsonConvert.DeserializeObject<T>(r.downloadHandler.text)); } catch (Exception e) { fail(e.Message); } }
        IEnumerator PostJson(string url, object data, Action<string> ok, Action<string> fail) { var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data)); using var r = new UnityWebRequest(url, "POST") { uploadHandler = new UploadHandlerRaw(bytes), downloadHandler = new DownloadHandlerBuffer() }; r.SetRequestHeader("Content-Type", "application/json"); yield return r.SendWebRequest(); if (r.result == UnityWebRequest.Result.Success) ok(r.downloadHandler.text); else fail(r.downloadHandler.text); }
        static void ClearChildren(Transform p) { for (int i = p.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(p.GetChild(i).gameObject); }
    }
}
