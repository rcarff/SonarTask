#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SonarTask.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SonarTask.EditorTools {
public sealed class SonarLocalWebBuildPostprocessor : IPostprocessBuildWithReport {
    public int callbackOrder => 1000;

    public void OnPostprocessBuild(BuildReport report) {
        if (report.summary.platform != BuildTarget.WebGL) return;

        // Always copy custom-template branding because Unity's ordinary Build And Run
        // path does not execute SonarBuildMenu.Web().
        string output = report.summary.outputPath;
        CopyFile("Assets/WebGLTemplates/SONARResponsive/SonarIcon.png", Path.Combine(output, "SonarIcon.png"));
        CopyFile("Assets/WebGLTemplates/SONARResponsive/SonarSplash.png", Path.Combine(output, "SonarSplash.png"));

        bool autoRun = (report.summary.options & BuildOptions.AutoRunPlayer) != 0;
        if (!autoRun) return;

        string src = Path.GetFullPath("Experiments");
        string dst = Path.Combine(output, "Experiments");
        if (!Directory.Exists(src)) {
            Debug.LogWarning("SONAR local WebGL development build: project Experiments directory was not found.");
            return;
        }

        CopyDir(src, dst);
        WriteManifest(dst);
        Debug.Log("SONAR local WebGL development mode prepared. Login password: " + SonarTask.Services.AppState.LocalWebDevPassword);
    }

    static void WriteManifest(string experimentRoot) {
        var list = new List<ExperimentSummary>();
        foreach (var dir in Directory.GetDirectories(experimentRoot)) {
            string definition = Path.Combine(dir, "experiment.json");
            if (!File.Exists(definition)) continue;
            try {
                var d = JsonConvert.DeserializeObject<ExperimentDefinition>(File.ReadAllText(definition));
                if (d == null) continue;
                var errs = ExperimentValidator.Validate(d);
                if (errs.Count > 0) {
                    Debug.LogWarning($"SONAR local WebGL: skipping invalid experiment {Path.GetFileName(dir)}: {string.Join("; ", errs)}");
                    continue;
                }
                list.Add(new ExperimentSummary { PackageName = Path.GetFileName(dir), Name = d.Name ?? Path.GetFileName(dir), Version = d.Version ?? "" });
            } catch (Exception e) {
                Debug.LogWarning($"SONAR local WebGL: skipping {dir}: {e.Message}");
            }
        }
        list.Sort((a,b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        File.WriteAllText(Path.Combine(experimentRoot, "index.json"), JsonConvert.SerializeObject(list, Formatting.Indented));
    }

    static void CopyFile(string src, string dst) {
        if (!File.Exists(src)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(dst));
        File.Copy(src, dst, true);
    }

    static void CopyDir(string src, string dst) {
        if (Directory.Exists(dst)) Directory.Delete(dst, true);
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(src, dst));
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories)) {
            string target = file.Replace(src, dst);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(file, target, true);
        }
    }
}
}
#endif
