using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace SonarTask.Services
{
    [Serializable]
    public sealed class ExternalStudyConfig
    {
        public string id = "";
        public string provider = "";
        public string providerStudyId = "";
        public string experimentPackage = "";
        public string completionUrl = "";
        public bool autoStart = true;
        public bool showInstructions = true;
        public bool enabled = true;
    }

    // Browser-only test storage used by Web Local Test (Build and Run).
    // Production deployments continue to use the server-side External Studies configuration.
    public static class LocalWebDevelopmentStore
    {
        const string ExternalStudiesKey = "sonar.localWeb.externalStudies.v1";

        public static List<ExternalStudyConfig> LoadExternalStudies()
        {
            var raw = PlayerPrefs.GetString(ExternalStudiesKey, "");
            if (string.IsNullOrWhiteSpace(raw)) return new List<ExternalStudyConfig>();
            try
            {
                return JsonConvert.DeserializeObject<List<ExternalStudyConfig>>(raw)
                    ?? new List<ExternalStudyConfig>();
            }
            catch
            {
                return new List<ExternalStudyConfig>();
            }
        }

        public static void SaveExternalStudies(List<ExternalStudyConfig> rows)
        {
            PlayerPrefs.SetString(ExternalStudiesKey,
                JsonConvert.SerializeObject(rows ?? new List<ExternalStudyConfig>()));
            PlayerPrefs.Save();
        }

        public static void UpsertExternalStudy(ExternalStudyConfig value)
        {
            var rows = LoadExternalStudies();
            if (string.IsNullOrWhiteSpace(value.id)) value.id = Guid.NewGuid().ToString("N");
            var index = rows.FindIndex(x => string.Equals(x.id, value.id, StringComparison.Ordinal));
            if (index >= 0) rows[index] = value; else rows.Add(value);
            SaveExternalStudies(rows);
        }

        public static void DeleteExternalStudy(string id)
        {
            var rows = LoadExternalStudies();
            rows.RemoveAll(x => string.Equals(x.id, id, StringComparison.Ordinal));
            SaveExternalStudies(rows);
        }

        public static ExternalStudyConfig FindExternalStudy(string provider, string studyId)
        {
            provider = NormalizeProvider(provider);
            return LoadExternalStudies().Find(x => x.enabled
                && NormalizeProvider(x.provider) == provider
                && string.Equals((x.providerStudyId ?? "").Trim(), (studyId ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static string NormalizeProvider(string provider)
        {
            var p = (provider ?? "").Trim().ToLowerInvariant();
            if (p == "cloudresearch" || p == "cloudresearchconnect") return "connect";
            return p;
        }
    }
}
