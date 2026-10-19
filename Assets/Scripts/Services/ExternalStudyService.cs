using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using SonarTask.Core;
using SonarTask.UI;
using UnityEngine;
using UnityEngine.Networking;

namespace SonarTask.Services
{
    [Serializable]
    public sealed class ExternalStudyGroup
    {
        public string id = "";
        public string name = "";
        public int weight = 1;
        public List<string> experiments = new();
        public int assignedCount;
        public int completedCount;
        public int testAssignedCount;
        public int testCompletedCount;
    }

    [Serializable]
    public sealed class ExternalStudyConfig
    {
        public string id = "";
        public string provider = "prolific";
        public string providerStudyId = "";
        public string assignmentMode = "Fixed";
        public string completionUrl = "";
        public bool autoStart = true;
        public bool showInstructions = true;
        public bool enabled = true;
        public List<ExternalStudyGroup> groups = new();
        public int assignedCount;
        public int completedCount;
        public int testAssignedCount;
        public int testCompletedCount;
    }

    [Serializable]
    public sealed class ExternalLaunchReply
    {
        public bool ok;
        public string message = "";
        public string provider = "";
        public string participantId = "";
        public string providerStudyId = "";
        public string sessionId = "";
        public string assignmentId = "";
        public string externalStudyId = "";
        public string assignmentMode = "";
        public string assignmentGroup = "";
        public List<string> experimentSequence = new();
        public int sequenceIndex;
        public int sequenceCount;
        public string experimentPackage = "";
        public string completionUrl = "";
        public bool autoStart = true;
        public bool showInstructions = true;
        public bool completed;
        public bool testMode;
    }

    public static class ExternalStudyService
    {
        public static bool HasExternalLaunchParameters()
        {
            if (!AppState.IsWeb || AppState.ExternalLaunchConsumed) return false;
            var q = Query();
            if (q.Count == 0) return false;
            return !string.IsNullOrWhiteSpace(Get(q, "provider")) ||
                   !string.IsNullOrWhiteSpace(Get(q, "PROLIFIC_PID")) ||
                   !string.IsNullOrWhiteSpace(Get(q, "participantId")) ||
                   !string.IsNullOrWhiteSpace(Get(q, "subject"));
        }

        public static IEnumerator BeginFromCurrentUrl(Action<ExternalLaunchReply> done)
        {
            var q = Query();
            string provider = NormalizeProvider(Get(q, "provider"));
            if (string.IsNullOrWhiteSpace(provider))
            {
                if (!string.IsNullOrWhiteSpace(Get(q, "PROLIFIC_PID"))) provider = "prolific";
                else if (!string.IsNullOrWhiteSpace(Get(q, "participantId"))) provider = "connect";
                else provider = "generic";
            }

            string participantId = provider switch {
                "prolific" => Get(q, "PROLIFIC_PID", "subject"),
                "connect" => Get(q, "participantId", "subject"),
                _ => Get(q, "subject", "participantId", "PROLIFIC_PID")
            };
            string studyId = provider switch {
                "prolific" => Get(q, "STUDY_ID", "study"),
                "connect" => Get(q, "projectId", "study", "STUDY_ID"),
                _ => Get(q, "study", "STUDY_ID", "projectId")
            };
            string sessionId = provider switch {
                "prolific" => Get(q, "SESSION_ID", "session"),
                _ => Get(q, "session", "SESSION_ID")
            };
            string assignmentId = provider == "connect" ? Get(q, "assignmentId") : Get(q, "assignmentId");
            string testToken = Get(q, "sonar_test_token", "testToken");
            bool requestedTestMode = AppState.IsLocalWebDevelopment
                ? ParseBool(Get(q, "sonar_test", "test"), false) || !string.IsNullOrWhiteSpace(testToken)
                : !string.IsNullOrWhiteSpace(testToken);

            if (string.IsNullOrWhiteSpace(participantId))
            {
                done(Failure("The external-study URL does not contain a participant/subject ID."));
                yield break;
            }

            var directPackage = Get(q, "experiment", "experimentPackage");
            if (string.IsNullOrWhiteSpace(studyId) && !(AppState.IsLocalWebDevelopment && !string.IsNullOrWhiteSpace(directPackage)))
            {
                done(Failure("The external-study URL does not contain the provider study/project ID."));
                yield break;
            }
            if (string.IsNullOrWhiteSpace(studyId)) studyId = "LOCAL_DIRECT";

            if (AppState.IsLocalWebDevelopment)
            {
                ExternalLaunchReply reply;
                // Local test URLs carry their own study configuration so they work in
                // incognito windows and browser origins without shared PlayerPrefs.
                // Never accept this unsigned configuration outside local development.
                var embedded = Get(q, "sonar_local_study");
                if (requestedTestMode && !string.IsNullOrWhiteSpace(embedded))
                {
                    try
                    {
                        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(embedded.Replace('-', '+').Replace('_', '/').PadRight((embedded.Length + 3) / 4 * 4, '=')));
                        var imported = Newtonsoft.Json.JsonConvert.DeserializeObject<ExternalStudyConfig>(json);
                        if (imported != null && imported.enabled &&
                            NormalizeProvider(imported.provider) == provider &&
                            string.Equals((imported.providerStudyId ?? "").Trim(), studyId.Trim(), StringComparison.OrdinalIgnoreCase))
                            LocalWebDevelopmentStore.UpsertStudy(imported);
                    }
                    catch (Exception ex) { Debug.LogWarning("Invalid local study test URL: " + ex.Message); }
                }
                var study = LocalWebDevelopmentStore.FindStudy(provider, studyId);
                if (study != null)
                {
                    reply = LocalWebDevelopmentStore.GetOrCreateAssignment(study, participantId, sessionId, assignmentId, requestedTestMode);
                }
                else if (!string.IsNullOrWhiteSpace(directPackage))
                {
                    reply = new ExternalLaunchReply {
                        ok = true,
                        message = "Local direct external-study launch accepted.",
                        provider = provider,
                        participantId = participantId,
                        providerStudyId = studyId,
                        sessionId = sessionId,
                        assignmentId = assignmentId,
                        externalStudyId = "LOCAL_DIRECT:" + provider + ":" + studyId,
                        assignmentMode = "Fixed",
                        assignmentGroup = "Local Direct",
                        experimentSequence = new List<string> { directPackage },
                        sequenceIndex = 0,
                        sequenceCount = 1,
                        experimentPackage = directPackage,
                        autoStart = true,
                        showInstructions = true,
                        testMode = requestedTestMode
                    };
                }
                else
                {
                    done(Failure("No enabled local External Studies mapping matches this provider/study ID. Configure one under Settings > External Studies, or add &experiment=PACKAGE for a local-only direct test."));
                    yield break;
                }

                var autoText = Get(q, "autostart", "autoStart");
                var instructionsText = Get(q, "instructions", "showInstructions");
                if (!string.IsNullOrWhiteSpace(autoText)) reply.autoStart = ParseBool(autoText, reply.autoStart);
                if (!string.IsNullOrWhiteSpace(instructionsText)) reply.showInstructions = ParseBool(instructionsText, reply.showInstructions);

                List<ExperimentSummary> available = null;
                string listError = null;
                yield return RepositoryFactory.Experiments.List(x => available = x, e => listError = e);
                if (available == null)
                {
                    done(Failure("Unable to load local experiments: " + (listError ?? "unknown error")));
                    yield break;
                }
                if (!reply.completed && !available.Exists(x => string.Equals(x.PackageName, reply.experimentPackage, StringComparison.OrdinalIgnoreCase)))
                {
                    done(Failure("The assigned experiment package is not bundled in this Web Local Test build: " + reply.experimentPackage));
                    yield break;
                }
                done(reply);
                yield break;
            }

            var payload = new { provider, participantId, providerStudyId = studyId, sessionId, assignmentId, testToken };
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            using var request = new UnityWebRequest("/api/external/launch", "POST") {
                uploadHandler = new UploadHandlerRaw(bytes),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                done(Failure(FriendlyError(request.downloadHandler.text, request.error)));
                yield break;
            }
            try
            {
                var reply = JsonConvert.DeserializeObject<ExternalLaunchReply>(request.downloadHandler.text);
                done(reply ?? Failure("The server returned an empty external-study response."));
            }
            catch (Exception e) { done(Failure("Unable to read external-study response: " + e.Message)); }
        }

        public static void Apply(ExternalLaunchReply reply)
        {
            if (reply == null || !reply.ok) return;
            AppState.ExternalStudyActive = true;
            AppState.ExternalLaunchConsumed = true;
            AppState.ExternalProvider = NormalizeProvider(reply.provider);
            AppState.ExternalParticipantId = reply.participantId ?? "";
            AppState.ExternalProviderStudyId = reply.providerStudyId ?? "";
            AppState.ExternalSessionId = reply.sessionId ?? "";
            AppState.ExternalAssignmentId = reply.assignmentId ?? "";
            AppState.ExternalStudyId = reply.externalStudyId ?? "";
            AppState.ExternalAssignmentMode = reply.assignmentMode ?? "";
            AppState.ExternalAssignmentGroup = reply.assignmentGroup ?? "";
            AppState.ExternalExperimentSequence = reply.experimentSequence ?? new List<string>();
            AppState.ExternalSequenceIndex = reply.sequenceIndex;
            AppState.ExternalCompletionUrl = reply.completionUrl ?? "";
            AppState.ExternalAutoStart = reply.autoStart;
            AppState.ExternalShowInstructions = reply.showInstructions;
            AppState.ExternalTestMode = reply.testMode;
            AppState.SubjectId = reply.participantId ?? "";
            AppState.Location = "Web";
            AppState.OperatorId = ProviderDisplay(reply.provider);
            AppState.SelectedPackage = reply.experimentPackage ?? "";
        }

        public static IEnumerator LoadCurrentExperiment(Action ok, Action<string> fail)
        {
            if (string.IsNullOrWhiteSpace(AppState.SelectedPackage))
            {
                fail("No experiment is assigned to this external-study participant.");
                yield break;
            }
            yield return RepositoryFactory.Experiments.LoadDefinition(AppState.SelectedPackage, d => {
                try {
                    AppState.LoadedDefinition = d;
                    AppState.ResolvedExperiment = ExperimentResolver.Resolve(d);
                    ok();
                }
                catch (Exception e) { fail(e.Message); }
            }, fail);
        }

        public static void RouteAfterLoad(ScreenManager manager)
        {
            if (!AppState.ExternalStudyActive) { manager.Selection(); return; }
            if (!AppState.ExternalAutoStart) manager.Selection();
            else if (AppState.ExternalShowInstructions) manager.Instructions();
            else manager.Task();
        }

        public static IEnumerator RefreshProgress(Action<ExternalLaunchReply> done)
        {
            if (!AppState.ExternalStudyActive)
            {
                done(Failure("No external study is active."));
                yield break;
            }

            if (AppState.IsLocalWebDevelopment)
            {
                if (AppState.ExternalStudyId.StartsWith("LOCAL_DIRECT:", StringComparison.Ordinal))
                {
                    done(new ExternalLaunchReply {
                        ok = true,
                        completed = true,
                        message = "Local direct test completed.",
                        provider = AppState.ExternalProvider,
                        participantId = AppState.ExternalParticipantId,
                        providerStudyId = AppState.ExternalProviderStudyId,
                        externalStudyId = AppState.ExternalStudyId,
                        assignmentMode = AppState.ExternalAssignmentMode,
                        assignmentGroup = AppState.ExternalAssignmentGroup,
                        experimentSequence = AppState.ExternalExperimentSequence,
                        sequenceIndex = AppState.ExternalExperimentSequence.Count,
                        sequenceCount = AppState.ExternalExperimentSequence.Count,
                        completionUrl = AppState.ExternalCompletionUrl,
                        autoStart = AppState.ExternalAutoStart,
                        showInstructions = AppState.ExternalShowInstructions,
                        testMode = AppState.ExternalTestMode
                    });
                }
                else done(LocalWebDevelopmentStore.Progress(AppState.ExternalStudyId, AppState.ExternalParticipantId, AppState.ExternalTestMode));
                yield break;
            }

            using var request = UnityWebRequest.Get("/api/external/progress");
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                done(Failure(FriendlyError(request.downloadHandler.text, request.error)));
                yield break;
            }
            try { done(JsonConvert.DeserializeObject<ExternalLaunchReply>(request.downloadHandler.text) ?? Failure("Empty progress response.")); }
            catch (Exception e) { done(Failure(e.Message)); }
        }

        public static ExternalLaunchReply CompleteLocalCurrentStep()
        {
            if (!AppState.IsLocalWebDevelopment || !AppState.ExternalStudyActive)
                return Failure("Local external-study completion is not active.");
            if (AppState.ExternalStudyId.StartsWith("LOCAL_DIRECT:", StringComparison.Ordinal))
                return new ExternalLaunchReply { ok = true, completed = true, sequenceIndex = 1, sequenceCount = 1 };
            return LocalWebDevelopmentStore.CompleteCurrent(
                AppState.ExternalStudyId,
                AppState.ExternalParticipantId,
                AppState.ExternalSequenceIndex + 1,
                AppState.ExternalTestMode);
        }

        public static string ProviderDisplay(string provider) => NormalizeProvider(provider) switch {
            "prolific" => "Prolific",
            "connect" => "CloudResearch Connect",
            "sona" => "SONA",
            _ => "External Study"
        };

        public static string NormalizeProvider(string provider) => (provider ?? "").Trim().ToLowerInvariant() switch {
            "cloudresearch" => "connect",
            "cloudresearch connect" => "connect",
            "cloudresearchconnect" => "connect",
            "sona systems" => "sona",
            var x => x
        };

        public static string NormalizeAssignmentMode(string mode) => (mode ?? "").Trim().ToLowerInvariant() switch {
            "random" => "Random",
            "balancedrandom" => "BalancedRandom",
            "balanced random" => "BalancedRandom",
            "counterbalanced" => "Counterbalanced",
            "counterbalance" => "Counterbalanced",
            _ => "Fixed"
        };

        public static string AssignmentModeDisplay(string mode) => NormalizeAssignmentMode(mode) switch {
            "BalancedRandom" => "Balanced Random",
            "Counterbalanced" => "Counterbalanced",
            "Random" => "Random",
            _ => "Fixed"
        };

        static Dictionary<string, string> Query()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var uri = new Uri(Application.absoluteURL);
                var q = uri.Query;
                if (string.IsNullOrWhiteSpace(q)) return result;
                if (q.StartsWith("?")) q = q.Substring(1);
                foreach (var pair in q.Split('&'))
                {
                    if (string.IsNullOrWhiteSpace(pair)) continue;
                    var bits = pair.Split(new[] { '=' }, 2);
                    var key = UnityWebRequest.UnEscapeURL(bits[0].Replace("+", " "));
                    var value = bits.Length > 1 ? UnityWebRequest.UnEscapeURL(bits[1].Replace("+", " ")) : "";
                    result[key] = value;
                }
            }
            catch { }
            return result;
        }

        static string Get(Dictionary<string, string> q, params string[] names)
        {
            foreach (var name in names)
                if (q.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)) return value.Trim();
            return "";
        }

        static bool ParseBool(string value, bool fallback) => (value ?? "").Trim().ToLowerInvariant() switch {
            "1" or "true" or "yes" or "y" or "on" => true,
            "0" or "false" or "no" or "n" or "off" => false,
            _ => fallback
        };

        static ExternalLaunchReply Failure(string message) => new() { ok = false, message = message ?? "External-study launch failed." };

        static string FriendlyError(string body, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    var map = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
                    if (map != null && map.TryGetValue("message", out var m) && m != null) return m.ToString();
                }
                catch { }
                return body;
            }
            return string.IsNullOrWhiteSpace(fallback) ? "External-study request failed." : fallback;
        }
    }
}
