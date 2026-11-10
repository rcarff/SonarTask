#if UNITY_EDITOR
using UnityEditor;
namespace SonarTask.EditorTools {
public static class SonarLslPluginSetup {
    [InitializeOnLoadMethod]
    static void Initialize() => EditorApplication.delayCall += Configure;
    public static void Configure() {
        Setup("Assets/Plugins/x86_64/lsl.dll", "Windows", BuildTarget.StandaloneWindows64);
        Setup("Assets/Plugins/macOS/liblsl.dylib", "OSX", BuildTarget.StandaloneOSX);
    }
    static void Setup(string path, string os, BuildTarget target) {
        var importer = AssetImporter.GetAtPath(path) as PluginImporter;
        if (importer == null) return;
        // Respect a universal macOS library installed by the provided build script.
        string cpu = importer.GetPlatformData(target, "CPU");
        if (string.IsNullOrEmpty(cpu)) cpu = "x86_64";
        bool otherDesktop = (target != BuildTarget.StandaloneWindows64 && importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64))
            || (target != BuildTarget.StandaloneOSX && importer.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX))
            || importer.GetCompatibleWithPlatform(BuildTarget.StandaloneLinux64) || importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows);
        if (!otherDesktop && !importer.GetCompatibleWithAnyPlatform() && importer.GetCompatibleWithEditor()
            && importer.GetCompatibleWithPlatform(target) && !importer.GetCompatibleWithPlatform(BuildTarget.WebGL)
            && importer.GetEditorData("OS") == os) return;
        importer.SetCompatibleWithAnyPlatform(false);
        foreach (var desktop in new[] { BuildTarget.StandaloneWindows, BuildTarget.StandaloneWindows64, BuildTarget.StandaloneLinux64, BuildTarget.StandaloneOSX })
            importer.SetCompatibleWithPlatform(desktop, desktop == target);
        importer.SetCompatibleWithEditor(true);
        importer.SetEditorData("OS", os);
        importer.SetEditorData("CPU", cpu);
        importer.SetCompatibleWithPlatform(target, true);
        importer.SetPlatformData(target, "CPU", cpu);
        importer.SetCompatibleWithPlatform(BuildTarget.WebGL, false);
        importer.SaveAndReimport();
    }
}
}
#endif
