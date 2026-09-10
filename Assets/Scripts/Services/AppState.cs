using System;
using UnityEngine;

namespace SonarTask.Services {
public static class AppState {
    public const string ProductName = "SONAR Simulator Task";
    public static string Location { get; set; } = "";
    public static string OperatorId { get; set; } = "";
    public static string SubjectId { get; set; } = "";
    public static string SelectedPackage { get; set; } = "";
    public static SonarTask.Core.ExperimentDefinition LoadedDefinition { get; set; }
    public static string LoadedDefinitionJson { get; set; } = "";
    public static SonarTask.Core.ResolvedExperiment ResolvedExperiment { get; set; }
    public static bool IsWeb => Application.platform == RuntimePlatform.WebGLPlayer;
    public const string LocalWebDevPassword = "SonarDev123!";

    // Local WebGL development mode exists only for Unity Build-and-Run on a loopback host.
    // Production builds reached through a normal hostname/IP always use the real server API.
    public static bool IsLocalWebDevelopment {
        get {
            if (!IsWeb) return false;
            try {
                var url = Application.absoluteURL;
                if (string.IsNullOrWhiteSpace(url)) return false;
                var uri = new Uri(url);
                var host = (uri.Host ?? "").Trim().ToLowerInvariant();
                return host == "localhost" || host == "127.0.0.1" || host == "::1";
            } catch { return false; }
        }
    }

    public static string Version => Application.version;
}

public static class LocalHistory {
    const int Max = 30;
    public static string[] Load(string key) {
        var raw = PlayerPrefs.GetString("history:" + key, "");
        return string.IsNullOrWhiteSpace(raw) ? Array.Empty<string>() : raw.Split('\n');
    }
    public static void Save(string key, string value) {
        value = (value ?? "").Trim(); if (value.Length == 0) return;
        var list = new System.Collections.Generic.List<string>(Load(key));
        list.RemoveAll(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, value); if (list.Count > Max) list.RemoveRange(Max, list.Count-Max);
        PlayerPrefs.SetString("history:"+key, string.Join("\n", list)); PlayerPrefs.Save();
    }
    public static string Last(string key) => PlayerPrefs.GetString("last:"+key, "");
    public static void SetLast(string key,string value){PlayerPrefs.SetString("last:"+key,value??"");PlayerPrefs.Save();}
}
}
