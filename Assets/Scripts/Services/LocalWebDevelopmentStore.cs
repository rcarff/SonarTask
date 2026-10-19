using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace SonarTask.Services
{
    [Serializable]
    public sealed class LocalExternalAssignment
    {
        public string externalStudyId = "";
        public string participantId = "";
        public string sessionId = "";
        public string providerAssignmentId = "";
        public string groupId = "";
        public string groupName = "";
        public List<string> experimentSequence = new();
        public int sequenceIndex;
        public bool completed;
        public string assignedUtc = "";
        public string completedUtc = "";
    }

    // Development-only browser storage. Production external-study configuration and
    // participant assignments are stored server-side in SQLite.
    public static class LocalWebDevelopmentStore
    {
        const string StudiesKey = "sonar.localWeb.externalStudies.v2";
        const string AssignmentsKey = "sonar.localWeb.externalAssignments.v2";
        const string TestAssignmentsKey = "sonar.localWeb.externalTestAssignments.v1";

        public static List<ExternalStudyConfig> LoadStudies()
        {
            var raw = PlayerPrefs.GetString(StudiesKey, "");
            if (string.IsNullOrWhiteSpace(raw)) return new List<ExternalStudyConfig>();
            try { return JsonConvert.DeserializeObject<List<ExternalStudyConfig>>(raw) ?? new List<ExternalStudyConfig>(); }
            catch { return new List<ExternalStudyConfig>(); }
        }

        public static void SaveStudies(List<ExternalStudyConfig> rows)
        {
            PlayerPrefs.SetString(StudiesKey, JsonConvert.SerializeObject(rows ?? new List<ExternalStudyConfig>()));
            PlayerPrefs.Save();
        }

        public static void UpsertStudy(ExternalStudyConfig value)
        {
            var rows = LoadStudies();
            if (string.IsNullOrWhiteSpace(value.id)) value.id = Guid.NewGuid().ToString("N");
            Normalize(value);
            int ix = rows.FindIndex(x => string.Equals(x.id, value.id, StringComparison.Ordinal));
            if (ix >= 0) rows[ix] = value; else rows.Add(value);
            SaveStudies(rows);
        }

        public static bool DeleteStudy(string id, out string error)
        {
            if (LoadAssignments(false).Any(x => string.Equals(x.externalStudyId, id, StringComparison.Ordinal)) ||
                LoadAssignments(true).Any(x => string.Equals(x.externalStudyId, id, StringComparison.Ordinal)))
            {
                error = "This local study already has participant assignments. Clear Test Data and/or Reset Production Data before deleting it.";
                return false;
            }
            var rows = LoadStudies();
            rows.RemoveAll(x => string.Equals(x.id, id, StringComparison.Ordinal));
            SaveStudies(rows);
            error = null;
            return true;
        }

        public static ExternalStudyConfig FindStudy(string provider, string providerStudyId)
        {
            provider = ExternalStudyService.NormalizeProvider(provider);
            return LoadStudies().FirstOrDefault(x =>
                x.enabled &&
                ExternalStudyService.NormalizeProvider(x.provider) == provider &&
                string.Equals((x.providerStudyId ?? "").Trim(), (providerStudyId ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static ExternalLaunchReply GetOrCreateAssignment(
            ExternalStudyConfig study,
            string participantId,
            string sessionId,
            string providerAssignmentId,
            bool testMode = false)
        {
            Normalize(study);
            var assignments = LoadAssignments(testMode);
            var existing = assignments.FirstOrDefault(x =>
                string.Equals(x.externalStudyId, study.id, StringComparison.Ordinal) &&
                string.Equals(x.participantId, participantId, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                var group = ChooseGroup(study, assignments);
                existing = new LocalExternalAssignment {
                    externalStudyId = study.id,
                    participantId = participantId,
                    sessionId = sessionId ?? "",
                    providerAssignmentId = providerAssignmentId ?? "",
                    groupId = group.id,
                    groupName = group.name,
                    experimentSequence = new List<string>(group.experiments ?? new List<string>()),
                    sequenceIndex = 0,
                    completed = false,
                    assignedUtc = DateTime.UtcNow.ToString("O")
                };
                assignments.Add(existing);
                SaveAssignments(assignments, testMode);
            }
            return BuildReply(study, existing, testMode);
        }

        public static ExternalLaunchReply Progress(string studyId, string participantId, bool testMode = false)
        {
            var study = LoadStudies().FirstOrDefault(x => x.id == studyId);
            var assignment = LoadAssignments(testMode).FirstOrDefault(x => x.externalStudyId == studyId && x.participantId == participantId);
            return study == null || assignment == null
                ? new ExternalLaunchReply { ok = false, message = "Local external-study assignment was not found." }
                : BuildReply(study, assignment, testMode);
        }

        public static ExternalLaunchReply CompleteCurrent(string studyId, string participantId, int sequenceIndexOneBased, bool testMode = false)
        {
            var studies = LoadStudies();
            var study = studies.FirstOrDefault(x => x.id == studyId);
            var assignments = LoadAssignments(testMode);
            var a = assignments.FirstOrDefault(x => x.externalStudyId == studyId && x.participantId == participantId);
            if (study == null || a == null)
                return new ExternalLaunchReply { ok = false, message = "Local external-study assignment was not found." };

            if (!a.completed && sequenceIndexOneBased == a.sequenceIndex + 1)
            {
                a.sequenceIndex++;
                if (a.sequenceIndex >= a.experimentSequence.Count)
                {
                    a.completed = true;
                    a.completedUtc = DateTime.UtcNow.ToString("O");
                }
                SaveAssignments(assignments, testMode);
            }
            return BuildReply(study, a, testMode);
        }

        public static List<ExternalStudyConfig> StudiesWithCounts()
        {
            var studies = LoadStudies();
            var assignments = LoadAssignments(false);
            var testAssignments = LoadAssignments(true);
            foreach (var s in studies)
            {
                Normalize(s);
                var mine = assignments.Where(a => a.externalStudyId == s.id).ToList();
                var testMine = testAssignments.Where(a => a.externalStudyId == s.id).ToList();
                s.assignedCount = mine.Count;
                s.completedCount = mine.Count(a => a.completed);
                s.testAssignedCount = testMine.Count;
                s.testCompletedCount = testMine.Count(a => a.completed);
                foreach (var g in s.groups)
                {
                    g.assignedCount = mine.Count(a => a.groupId == g.id);
                    g.completedCount = mine.Count(a => a.groupId == g.id && a.completed);
                    g.testAssignedCount = testMine.Count(a => a.groupId == g.id);
                    g.testCompletedCount = testMine.Count(a => a.groupId == g.id && a.completed);
                }
            }
            return studies;
        }

        public static void ResetTestData(string studyId)
        {
            var rows = LoadAssignments(true);
            rows.RemoveAll(x => string.Equals(x.externalStudyId, studyId, StringComparison.Ordinal));
            SaveAssignments(rows, true);
        }

        public static void ResetProductionAssignments(string studyId)
        {
            var rows = LoadAssignments(false);
            rows.RemoveAll(x => string.Equals(x.externalStudyId, studyId, StringComparison.Ordinal));
            SaveAssignments(rows, false);
        }

        static List<LocalExternalAssignment> LoadAssignments(bool testMode = false)
        {
            var raw = PlayerPrefs.GetString(testMode ? TestAssignmentsKey : AssignmentsKey, "");
            if (string.IsNullOrWhiteSpace(raw)) return new List<LocalExternalAssignment>();
            try { return JsonConvert.DeserializeObject<List<LocalExternalAssignment>>(raw) ?? new List<LocalExternalAssignment>(); }
            catch { return new List<LocalExternalAssignment>(); }
        }

        static void SaveAssignments(List<LocalExternalAssignment> rows, bool testMode = false)
        {
            PlayerPrefs.SetString(testMode ? TestAssignmentsKey : AssignmentsKey, JsonConvert.SerializeObject(rows ?? new List<LocalExternalAssignment>()));
            PlayerPrefs.Save();
        }

        static ExternalStudyGroup ChooseGroup(ExternalStudyConfig study, List<LocalExternalAssignment> assignments)
        {
            var groups = study.groups.Where(g => g.experiments != null && g.experiments.Count > 0).ToList();
            if (groups.Count == 0) throw new InvalidOperationException("External study has no experiment groups.");
            string mode = ExternalStudyService.NormalizeAssignmentMode(study.assignmentMode);
            if (mode == "Fixed") return groups[0];

            if (mode == "Random")
            {
                int total = groups.Sum(g => Mathf.Max(1, g.weight));
                int pick = UnityEngine.Random.Range(0, total);
                foreach (var g in groups)
                {
                    pick -= Mathf.Max(1, g.weight);
                    if (pick < 0) return g;
                }
                return groups[^1];
            }

            // Balanced Random and Counterbalanced both choose among the least-filled
            // weighted groups, with a random tie break. Equal weights stay within one.
            var studyAssignments = assignments.Where(a => a.externalStudyId == study.id).ToList();
            double best = double.MaxValue;
            var ties = new List<ExternalStudyGroup>();
            foreach (var g in groups)
            {
                int count = studyAssignments.Count(a => a.groupId == g.id);
                double score = count / (double)Mathf.Max(1, g.weight);
                if (score < best - 1e-9) { best = score; ties.Clear(); ties.Add(g); }
                else if (Math.Abs(score - best) < 1e-9) ties.Add(g);
            }
            return ties[UnityEngine.Random.Range(0, ties.Count)];
        }

        static ExternalLaunchReply BuildReply(ExternalStudyConfig study, LocalExternalAssignment a, bool testMode)
        {
            int count = a.experimentSequence?.Count ?? 0;
            bool completed = a.completed || a.sequenceIndex >= count;
            string package = completed || count == 0 ? "" : a.experimentSequence[a.sequenceIndex];
            return new ExternalLaunchReply {
                ok = true,
                message = completed ? "External study sequence is complete." : "Local external-study assignment ready.",
                provider = study.provider,
                participantId = a.participantId,
                providerStudyId = study.providerStudyId,
                sessionId = a.sessionId,
                assignmentId = a.providerAssignmentId,
                externalStudyId = study.id,
                assignmentMode = study.assignmentMode,
                assignmentGroup = a.groupName,
                experimentSequence = a.experimentSequence ?? new List<string>(),
                sequenceIndex = a.sequenceIndex,
                sequenceCount = count,
                experimentPackage = package,
                completionUrl = testMode ? "" : study.completionUrl,
                autoStart = study.autoStart,
                showInstructions = study.showInstructions,
                completed = completed,
                testMode = testMode
            };
        }

        static void Normalize(ExternalStudyConfig study)
        {
            study.provider = ExternalStudyService.NormalizeProvider(study.provider);
            study.assignmentMode = ExternalStudyService.NormalizeAssignmentMode(study.assignmentMode);
            study.groups ??= new List<ExternalStudyGroup>();
            for (int i = 0; i < study.groups.Count; i++)
            {
                var g = study.groups[i] ?? new ExternalStudyGroup();
                study.groups[i] = g;
                if (string.IsNullOrWhiteSpace(g.id)) g.id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(g.name)) g.name = "Group " + (i + 1);
                if (g.weight < 1) g.weight = 1;
                g.assignedCount = 0;
                g.completedCount = 0;
                g.testAssignedCount = 0;
                g.testCompletedCount = 0;
                g.experiments ??= new List<string>();
                g.experiments = g.experiments.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            }
            study.assignedCount = 0;
            study.completedCount = 0;
            study.testAssignedCount = 0;
            study.testCompletedCount = 0;
        }
    }
}
