using System;
using System.Collections;
using System.Collections.Generic;
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
            status.text = "Re-authentication is required before settings are shown.";

            unlockButton.onClick.RemoveAllListeners();
            unlockButton.onClick.AddListener(() => {
                unlockButton.interactable = false;
                StartCoroutine(AuthService.Reauth(passwordInput.text, r => {
                    unlockButton.interactable = true;
                    if (r.ok) BuildAdmin(passwordInput); else status.text = r.message;
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

        void BuildAdmin(InputField password)
        {
            ClearChildren(body);
            body.gameObject.SetActive(true);
            status.text = "Settings unlocked.";

            var h = UIFactory.Text("Change password", body, 21); UIFactory.Size(h, 38);
            var np = UIFactory.Input("New password (12+ characters)", body, true); UIFactory.Size(np, 44);
            var cp = UIFactory.Button("Change Password", body, () => StartCoroutine(AuthService.ChangePassword(password.text, np.text, r => status.text = r.message))); UIFactory.Size(cp, 44);

            h = UIFactory.Text("Experiment packages", body, 21); UIFactory.Size(h, 38);
            var up = UIFactory.Button("Upload Experiment ZIP", body, () => WebBrowserBridge.PickZip(gameObject, nameof(OnZipUploadEvent))); UIFactory.Size(up, 44);

            var progress = UIFactory.Panel("UploadProgress", body, new Color(.08f, .10f, .12f, 1)); UIFactory.Size(progress, 30);
            uploadProgressFill = UIFactory.Panel("Fill", progress.transform, new Color(.32f, .67f, .78f, 1));
            uploadProgressFill.raycastTarget = false;
            uploadProgressFill.rectTransform.anchorMin = Vector2.zero;
            uploadProgressFill.rectTransform.anchorMax = new Vector2(0, 1);
            uploadProgressFill.rectTransform.offsetMin = uploadProgressFill.rectTransform.offsetMax = Vector2.zero;
            uploadProgressText = UIFactory.Text("Ready to upload", progress.transform, 14, TextAnchor.MiddleCenter);
            uploadProgressText.raycastTarget = false;
            SetUploadProgress(0f, "Ready to upload");

            experimentList = UIFactory.GO("ExperimentList", body).transform; UIFactory.VLayout(experimentList, 4, 0);
            LoadExperiments();
            StartCoroutine(Get<StorageStatus>("/api/admin/experiments/storage-status", x =>
            {
                if (!x.writable) status.text = "Experiment storage is not writable: " + x.message;
            }, e => { }));

            h = UIFactory.Text("Results", body, 21); UIFactory.Size(h, 38);
            filter = UIFactory.Input("Filter result filenames", body); UIFactory.Size(filter, 44);
            var buttons = UIFactory.GO("ResultActions", body); UIFactory.Size((RectTransform)buttons.transform, 44); UIFactory.HLayout(buttons.transform, 6, 0);
            var refresh = UIFactory.Button("Search", buttons.transform, LoadResults); UIFactory.Size(refresh, 42, 110);
            var dl = UIFactory.Button("Download Selected", buttons.transform, DownloadSelected); UIFactory.Size(dl, 42, 170);
            var all = UIFactory.Button("Download Visible", buttons.transform, () => WebBrowserBridge.OpenUrl("/api/admin/results/download?filter=" + UnityWebRequest.EscapeURL(filter.text))); UIFactory.Size(all, 42, 170);
            var results = UIFactory.GO("ResultsList", body); UIFactory.VLayout(results.transform, 3, 0);
            LoadResults();
        }

        void LoadResults()
        {
            if (body == null || !body.gameObject.activeSelf) return;
            var old = body.Find("ResultsList"); if (old) ClearChildren(old);
            selectedResults.Clear();
            StartCoroutine(Get<List<ResultRow>>("/api/admin/results?filter=" + UnityWebRequest.EscapeURL(filter?.text ?? ""), rows =>
            {
                var list = body.Find("ResultsList");
                foreach (var x in rows)
                {
                    var row = UIFactory.GO("Result", list); UIFactory.Size((RectTransform)row.transform, 38); UIFactory.HLayout(row.transform, 4, 0);
                    var tg = UIFactory.Toggle(x.fileName, row.transform); UIFactory.Size(tg, 36, -1, -1, 1);
                    tg.onValueChanged.AddListener(v => { if (v) { if (!selectedResults.Contains(x.fileName)) selectedResults.Add(x.fileName); } else selectedResults.Remove(x.fileName); });
                    var one = UIFactory.Button("Download", row.transform, () => WebBrowserBridge.OpenUrl("/api/admin/results/file/" + UnityWebRequest.EscapeURL(x.fileName))); UIFactory.Size(one, 36, 100);
                }
            }, e => status.text = e));
        }

        void DownloadSelected()
        {
            if (selectedResults.Count == 0) { status.text = "Select one or more result files first."; return; }
            StartCoroutine(PostJson("/api/admin/results/download-selected", new { files = selectedResults }, text => WebBrowserBridge.OpenUrl("/api/admin/results/download-token/" + text.Trim('"')), e => status.text = e));
        }

        void LoadExperiments()
        {
            if (experimentList == null) return;
            ClearChildren(experimentList);
            StartCoroutine(Get<List<ExpRow>>("/api/admin/experiments", rows =>
            {
                foreach (var x in rows)
                {
                    var row = UIFactory.GO("Exp", experimentList); UIFactory.Size((RectTransform)row.transform, 40); UIFactory.HLayout(row.transform, 5, 0);
                    var t = UIFactory.Text($"{x.name} ({x.packageName}) v{x.version}", row.transform, 15); UIFactory.Size(t, 38, -1, -1, 1);
                    var del = UIFactory.Button("Delete", row.transform, () => DeleteExperiment(x.packageName)); UIFactory.Size(del, 38, 100);
                }
            }, e => status.text = "Unable to refresh experiment list: " + e));
        }

        void SetUploadProgress(float value, string text)
        {
            if (uploadProgressFill != null)
            {
                var r = uploadProgressFill.rectTransform;
                r.anchorMax = new Vector2(Mathf.Clamp01(value), 1);
            }
            if (uploadProgressText != null) uploadProgressText.text = text;
        }

        public void OnZipUploadEvent(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return;
            var parts = payload.Split(new[] { '|' }, 4);
            switch (parts[0])
            {
                case "START":
                {
                    SetUploadProgress(0f, "Uploading " + (parts.Length > 1 ? parts[1] : "experiment ZIP") + "... 0%");
                    status.text = "Uploading experiment package...";
                    break;
                }
                case "PROGRESS":
                {
                    int pct = 0; if (parts.Length > 1) int.TryParse(parts[1], out pct);
                    SetUploadProgress(pct / 100f, $"Uploading... {pct}%");
                    break;
                }
                case "PROCESSING":
                {
                    SetUploadProgress(1f, "Upload complete - validating/installing...");
                    status.text = "Validating and installing experiment package...";
                    break;
                }
                case "OK":
                {
                    SetUploadProgress(1f, "Upload successful");
                    status.text = "Experiment uploaded successfully.";
                    LoadExperiments();
                    break;
                }
                case "ERR":
                {
                    SetUploadProgress(0f, "Upload failed");
                    var code = parts.Length > 1 ? parts[1] : "";
                    var msg = parts.Length > 2 ? parts[2] : payload;
                    if (code == "413")
                    {
                        status.text = "Upload failed (HTTP 413): The experiment ZIP is too large for the server upload limit. The default package limit is 64 MiB.";
                    }
                    else
                    {
                        status.text = "Upload failed" + (string.IsNullOrEmpty(code) || code == "0" ? "" : $" (HTTP {code})") + ": " + FriendlyServerMessage(msg);
                    }
                    break;
                }
            }
        }

        static string FriendlyServerMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "No response from server.";
            try
            {
                var r = JsonConvert.DeserializeObject<Dictionary<string, object>>(text);
                if (r != null && r.TryGetValue("message", out var m) && m != null) return m.ToString();
            }
            catch { }
            return text;
        }
        void DeleteExperiment(string p) => Modal.Show(Manager.Root, "Delete experiment", $"Delete experiment package '{p}'?", () => StartCoroutine(Delete("/api/admin/experiments/" + UnityWebRequest.EscapeURL(p))), () => { }, "Delete", "Cancel");
        IEnumerator Delete(string url) { using var r = new UnityWebRequest(url, "DELETE") { downloadHandler = new DownloadHandlerBuffer() }; yield return r.SendWebRequest(); if (r.result == UnityWebRequest.Result.Success) { status.text = "Experiment deleted."; LoadExperiments(); } else status.text = r.downloadHandler.text; }
        IEnumerator Get<T>(string url, Action<T> ok, Action<string> fail) { using var r = UnityWebRequest.Get(url); yield return r.SendWebRequest(); if (r.result != UnityWebRequest.Result.Success) { fail(r.downloadHandler.text); yield break; } try { ok(JsonConvert.DeserializeObject<T>(r.downloadHandler.text)); } catch (Exception e) { fail(e.Message); } }
        IEnumerator PostJson(string url, object data, Action<string> ok, Action<string> fail) { var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data)); using var r = new UnityWebRequest(url, "POST") { uploadHandler = new UploadHandlerRaw(bytes), downloadHandler = new DownloadHandlerBuffer() }; r.SetRequestHeader("Content-Type", "application/json"); yield return r.SendWebRequest(); if (r.result == UnityWebRequest.Result.Success) ok(r.downloadHandler.text); else fail(r.downloadHandler.text); }
        static void ClearChildren(Transform p) { for (int i = p.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(p.GetChild(i).gameObject); }
    }
}
