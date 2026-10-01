using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace SonarTask.Services
{
    [Serializable]
    public sealed class ExternalLaunchReply
    {
        public bool ok;
        public string message;
        public string provider;
        public string participantId;
        public string providerStudyId;
        public string sessionId;
        public string assignmentId;
        public string experimentPackage;
        public string completionUrl;
        public bool autoStart;
        public bool showInstructions;
    }

    public static class ExternalStudyService
    {
        public static bool HasExternalLaunchParameters()
        {
            if (!AppState.IsWeb) return false;
            var q = Query();
            return q.TryGetValue("provider", out var p) && !string.IsNullOrWhiteSpace(p);
        }

        public static IEnumerator Launch(Action<ExternalLaunchReply> done)
        {
            ExternalLaunchReply failure(string message) => new ExternalLaunchReply { ok = false, message = message };
            Dictionary<string, string> q;
            try { q = Query(); }
            catch (Exception e) { done(failure("Invalid external study URL: " + e.Message)); yield break; }

            var provider = Get(q, "provider").Trim().ToLowerInvariant();
            string participantId = "", studyId = "", sessionId = "", assignmentId = "";
            switch (provider)
            {
                case "prolific":
                    participantId = Get(q, "PROLIFIC_PID", "prolific_pid", "subject");
                    studyId = Get(q, "STUDY_ID", "study_id", "study");
                    sessionId = Get(q, "SESSION_ID", "session_id");
                    break;
                case "connect":
                case "cloudresearch":
                case "cloudresearchconnect":
                    provider = "connect";
                    participantId = Get(q, "participantId", "participantid", "subject");
                    studyId = Get(q, "projectId", "projectid", "study");
                    assignmentId = Get(q, "assignmentId", "assignmentid");
                    break;
                case "sona":
                    participantId = Get(q, "subject", "id", "survey_code", "surveyCode");
                    studyId = Get(q, "study", "studyId", "studyid");
                    break;
                case "generic":
                    participantId = Get(q, "subject", "participantId", "participantid", "id");
                    studyId = Get(q, "study", "studyId", "studyid", "projectId", "projectid");
                    sessionId = Get(q, "session", "sessionId", "sessionid");
                    assignmentId = Get(q, "assignment", "assignmentId", "assignmentid");
                    break;
                default:
                    done(failure("Unknown external study provider '" + provider + "'."));
                    yield break;
            }

            if (string.IsNullOrWhiteSpace(participantId)) { done(failure("The external study URL did not contain a participant/subject ID.")); yield break; }

            // Web Local Test supports a development-only explicit experiment parameter.
            // Production never trusts the browser to choose the experiment.
            var localExperiment = Get(q, "experiment", "experimentPackage");
            if (string.IsNullOrWhiteSpace(studyId) && !(AppState.IsLocalWebDevelopment && !string.IsNullOrWhiteSpace(localExperiment)))
            {
                done(failure("The external study URL did not contain the provider study/project ID."));
                yield break;
            }
            if (string.IsNullOrWhiteSpace(studyId) && AppState.IsLocalWebDevelopment) studyId = "LOCAL_TEST";

            if (AppState.IsLocalWebDevelopment)
            {
                var map = LocalWebDevelopmentStore.FindExternalStudy(provider, studyId);
                var experimentPackage = map != null && !string.IsNullOrWhiteSpace(map.experimentPackage)
                    ? map.experimentPackage : localExperiment;

                if (string.IsNullOrWhiteSpace(experimentPackage))
                {
                    done(failure("No enabled local External Studies mapping matches this provider/study ID. Configure one under Settings > External Studies, or add &experiment=PACKAGE for a local-only direct test."));
                    yield break;
                }

                List<SonarTask.Core.ExperimentSummary> available = null;
                string listError = null;
                yield return RepositoryFactory.Experiments.List(x => available = x, e => listError = e);
                if (available == null)
                {
                    done(failure("Unable to load local experiments: " + (listError ?? "unknown error")));
                    yield break;
                }
                if (!available.Exists(x => string.Equals(x.PackageName, experimentPackage, StringComparison.OrdinalIgnoreCase)))
                {
                    done(failure("Local external launch refers to an experiment package that is not bundled in this Web Local Test build: " + experimentPackage));
                    yield break;
                }

                bool autoStart = map == null ? true : map.autoStart;
                bool showInstructions = map == null ? true : map.showInstructions;
                var autoText = Get(q, "autostart", "autoStart");
                var instructionsText = Get(q, "instructions", "showInstructions");
                if (!string.IsNullOrWhiteSpace(autoText)) autoStart = ParseBool(autoText, autoStart);
                if (!string.IsNullOrWhiteSpace(instructionsText)) showInstructions = ParseBool(instructionsText, showInstructions);

                done(new ExternalLaunchReply {
                    ok = true,
                    message = "Local external-study launch accepted.",
                    provider = provider,
                    participantId = participantId,
                    providerStudyId = studyId,
                    sessionId = sessionId,
                    assignmentId = assignmentId,
                    experimentPackage = experimentPackage,
                    completionUrl = map == null ? "" : (map.completionUrl ?? ""),
                    autoStart = autoStart,
                    showInstructions = showInstructions
                });
                yield break;
            }

            var payload = new { provider, participantId, providerStudyId = studyId, sessionId, assignmentId };
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            using var r = new UnityWebRequest("/api/external/launch", "POST") {
                uploadHandler = new UploadHandlerRaw(bytes),
                downloadHandler = new DownloadHandlerBuffer()
            };
            r.SetRequestHeader("Content-Type", "application/json");
            yield return r.SendWebRequest();

            ExternalLaunchReply reply = null;
            try { reply = JsonConvert.DeserializeObject<ExternalLaunchReply>(r.downloadHandler.text); } catch { }
            if (reply == null) reply = failure(r.result == UnityWebRequest.Result.Success ? "Invalid external launch response." : (string.IsNullOrWhiteSpace(r.error) ? "External launch failed." : r.error));
            if (r.result != UnityWebRequest.Result.Success) reply.ok = false;
            done(reply);
        }

        public static void Apply(ExternalLaunchReply r)
        {
            AppState.IsExternalStudy = true;
            AppState.ExternalProvider = r.provider ?? "";
            AppState.ExternalParticipantId = r.participantId ?? "";
            AppState.ExternalStudyId = r.providerStudyId ?? "";
            AppState.ExternalSessionId = r.sessionId ?? "";
            AppState.ExternalAssignmentId = r.assignmentId ?? "";
            AppState.ExternalCompletionUrl = r.completionUrl ?? "";
            AppState.ExternalAutoStart = r.autoStart;
            AppState.ExternalShowInstructions = r.showInstructions;
            AppState.SubjectId = AppState.ExternalParticipantId;
            AppState.Location = "Web";
            AppState.OperatorId = ProviderDisplayName(AppState.ExternalProvider);
            AppState.SelectedPackage = r.experimentPackage ?? "";
        }

        public static string ProviderDisplayName(string provider) => (provider ?? "").Trim().ToLowerInvariant() switch {
            "prolific" => "Prolific",
            "connect" => "CloudResearch Connect",
            "sona" => "SONA",
            "generic" => "External Study",
            _ => provider ?? "External Study"
        };

        static Dictionary<string, string> Query()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var url = Application.absoluteURL;
            if (string.IsNullOrWhiteSpace(url)) return result;
            var uri = new Uri(url);
            var raw = uri.Query;
            if (raw.StartsWith("?")) raw = raw.Substring(1);
            foreach (var part in raw.Split('&'))
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                var ix = part.IndexOf('=');
                var k = ix >= 0 ? part.Substring(0, ix) : part;
                var v = ix >= 0 ? part.Substring(ix + 1) : "";
                k = Uri.UnescapeDataString(k.Replace("+", " "));
                v = Uri.UnescapeDataString(v.Replace("+", " "));
                if (!result.ContainsKey(k)) result[k] = v;
            }
            return result;
        }

        static bool ParseBool(string value, bool fallback)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "1": case "true": case "yes": case "y": case "on": return true;
                case "0": case "false": case "no": case "n": case "off": return false;
                default: return fallback;
            }
        }

        static string Get(Dictionary<string, string> q, params string[] names)
        {
            foreach (var n in names) if (q.TryGetValue(n, out var v) && !string.IsNullOrWhiteSpace(v)) return v.Trim();
            return "";
        }
    }
}
