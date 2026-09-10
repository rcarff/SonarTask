#if UNITY_EDITOR
using System.IO; using UnityEditor; using UnityEditor.Build.Reporting; using UnityEngine;
namespace SonarTask.EditorTools {
public static class SonarBuildMenu {
 static string[] Scenes(){var l=new System.Collections.Generic.List<string>();foreach(var s in EditorBuildSettings.scenes)if(s.enabled)l.Add(s.path);return l.ToArray();}
 [MenuItem("SONAR/Build/Web")]
 public static void Web(){SonarProjectSetup.ApplyBranding();Directory.CreateDirectory("Build/Web");PlayerSettings.WebGL.template="PROJECT:SONARResponsive";PlayerSettings.WebGL.compressionFormat=WebGLCompressionFormat.Brotli;PlayerSettings.WebGL.decompressionFallback=true;BuildPipeline.BuildPlayer(Scenes(),"Build/Web",BuildTarget.WebGL,BuildOptions.None);CopyFile("Assets/WebGLTemplates/SONARResponsive/SonarIcon.png","Build/Web/SonarIcon.png");CopyFile("Assets/WebGLTemplates/SONARResponsive/SonarSplash.png","Build/Web/SonarSplash.png");CopyDir("Build/Web","Server/Deploy/WebBuild");}
 [MenuItem("SONAR/Build/Web Local Test (Build and Run)")]
 public static void WebLocalTest(){SonarProjectSetup.ApplyBranding();Directory.CreateDirectory("Build/WebLocalTest");PlayerSettings.WebGL.template="PROJECT:SONARResponsive";PlayerSettings.WebGL.compressionFormat=WebGLCompressionFormat.Brotli;PlayerSettings.WebGL.decompressionFallback=true;var report=BuildPipeline.BuildPlayer(Scenes(),"Build/WebLocalTest",BuildTarget.WebGL,BuildOptions.AutoRunPlayer);if(report.summary.result!=BuildResult.Succeeded)Debug.LogError("SONAR local WebGL build failed: "+report.summary.result);}
 [MenuItem("SONAR/Build/Windows x64")]
 public static void Win(){SonarProjectSetup.ApplyBranding();SyncDesktopSeedExperiments();Directory.CreateDirectory("Build/Windows");BuildPipeline.BuildPlayer(Scenes(),"Build/Windows/SonarTask.exe",BuildTarget.StandaloneWindows64,BuildOptions.None);}
 [MenuItem("SONAR/Build/macOS")]
 public static void Mac(){SonarProjectSetup.ApplyBranding();SyncDesktopSeedExperiments();Directory.CreateDirectory("Build/macOS");BuildPipeline.BuildPlayer(Scenes(),"Build/macOS/SONAR Simulator Task.app",BuildTarget.StandaloneOSX,BuildOptions.None);}
 [MenuItem("SONAR/Build/Sync Desktop Seed Experiments")]
 public static void SyncDesktopSeedExperiments(){const string dst="Assets/StreamingAssets/SonarTaskSeed/Experiments";CopyDir("Experiments",dst);AssetDatabase.Refresh();Debug.Log("SONAR desktop seed experiments synchronized.");}
 static void CopyFile(string src,string dst){if(!File.Exists(src))return;var d=Path.GetDirectoryName(dst);if(!string.IsNullOrEmpty(d))Directory.CreateDirectory(d);File.Copy(src,dst,true);}
 static void CopyDir(string src,string dst){if(!Directory.Exists(src))return;if(Directory.Exists(dst))Directory.Delete(dst,true);foreach(var dir in Directory.GetDirectories(src,"*",SearchOption.AllDirectories))Directory.CreateDirectory(dir.Replace(src,dst));Directory.CreateDirectory(dst);foreach(var f in Directory.GetFiles(src,"*",SearchOption.AllDirectories)){var t=f.Replace(src,dst);Directory.CreateDirectory(Path.GetDirectoryName(t));File.Copy(f,t,true);}}
}
}
#endif
