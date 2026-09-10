using System;
using System.IO;
using UnityEngine;

namespace SonarTask.Services
{
    /// <summary>
    /// Resolves and initializes desktop research-data locations.
    /// The Unity Editor is intentionally project-local and never uses Documents.
    /// Built Windows/macOS players default to Documents/SonarTask unless
    /// SONAR_DATA_ROOT is set.
    /// </summary>
    public static class DesktopStorage
    {
        const string MarkerName = ".sonartask-initialized";
        static bool initialized;
        static bool migrationPending;
        static string initializationError = "";

        public static string DataRoot
        {
            get
            {
#if UNITY_EDITOR
                return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
#else
                var overrideRoot = Environment.GetEnvironmentVariable("SONAR_DATA_ROOT");
                if (!string.IsNullOrWhiteSpace(overrideRoot))
                    return Path.GetFullPath(overrideRoot.Trim());

                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrWhiteSpace(documents))
                    documents = Application.persistentDataPath;
                return Path.GetFullPath(Path.Combine(documents, "SonarTask"));
#endif
            }
        }

        public static string ExperimentsPath => Path.Combine(DataRoot, "Experiments");
        public static string ResultsPath => Path.Combine(DataRoot, "Results");
        public static string LogsPath => Path.Combine(DataRoot, "Logs");
        public static string InitializationError => initializationError;
        public static bool MigrationPending => migrationPending;

        public static string LegacyDataRoot
        {
            get
            {
#if UNITY_EDITOR
                return DataRoot;
#elif UNITY_STANDALONE_OSX
                return Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
#else
                return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
#endif
            }
        }

        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;

            try
            {
#if UNITY_EDITOR
                // Editor Play Mode remains completely isolated from Documents.
                Directory.CreateDirectory(ExperimentsPath);
                Directory.CreateDirectory(ResultsPath);
                return;
#else
                if (Application.platform != RuntimePlatform.WindowsPlayer &&
                    Application.platform != RuntimePlatform.OSXPlayer)
                    return;

                var rootExisted = Directory.Exists(DataRoot);
                Directory.CreateDirectory(DataRoot);
                Directory.CreateDirectory(ExperimentsPath);
                Directory.CreateDirectory(ResultsPath);
                Directory.CreateDirectory(LogsPath);

                var marker = Path.Combine(DataRoot, MarkerName);
                if (File.Exists(marker)) return;

                // If a user already created/populated Documents/SonarTask manually,
                // preserve it exactly and simply adopt it as the data root.
                if (rootExisted && (DirectoryHasEntries(ExperimentsPath) || DirectoryHasEntries(ResultsPath)))
                {
                    WriteMarker(marker, "Existing data root adopted; no seed data copied.");
                    return;
                }

                migrationPending = HasLegacyData();
                if (!migrationPending)
                {
                    SeedBundledExperiments();
                    WriteMarker(marker, "Desktop data root initialized from bundled experiments.");
                }
#endif
            }
            catch (Exception e)
            {
                initializationError = e.Message;
                Debug.LogError("SONAR desktop storage initialization failed: " + e);
            }
        }

        public static void CompleteFirstRun(bool copyLegacyData)
        {
#if UNITY_EDITOR
            migrationPending = false;
            return;
#else
            try
            {
                Directory.CreateDirectory(DataRoot);
                Directory.CreateDirectory(ExperimentsPath);
                Directory.CreateDirectory(ResultsPath);
                Directory.CreateDirectory(LogsPath);

                if (copyLegacyData && HasLegacyData())
                {
                    CopyDirectoryIfPresent(Path.Combine(LegacyDataRoot, "Experiments"), ExperimentsPath, false);
                    CopyDirectoryIfPresent(Path.Combine(LegacyDataRoot, "Results"), ResultsPath, false);
                }

                // Seed only files that do not already exist, so migrated/user data wins.
                SeedBundledExperiments();
                WriteMarker(Path.Combine(DataRoot, MarkerName),
                    copyLegacyData ? "Legacy desktop data copied; bundled experiments seeded where missing."
                                   : "Legacy desktop data skipped; bundled experiments seeded.");
                migrationPending = false;
            }
            catch (Exception e)
            {
                initializationError = e.Message;
                Debug.LogError("SONAR desktop first-run setup failed: " + e);
                throw;
            }
#endif
        }

        public static bool HasLegacyData()
        {
#if UNITY_EDITOR
            return false;
#else
            try
            {
                var legacy = LegacyDataRoot;
                if (PathsEqual(legacy, DataRoot)) return false;
                return DirectoryHasEntries(Path.Combine(legacy, "Experiments")) ||
                       DirectoryHasEntries(Path.Combine(legacy, "Results"));
            }
            catch { return false; }
#endif
        }

        static void SeedBundledExperiments()
        {
            var seedRoot = Path.Combine(Application.streamingAssetsPath, "SonarTaskSeed", "Experiments");
            CopyDirectoryIfPresent(seedRoot, ExperimentsPath, false);
        }

        static void CopyDirectoryIfPresent(string source, string destination, bool overwrite)
        {
            if (!Directory.Exists(source)) return;
            Directory.CreateDirectory(destination);
            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                var relative = directory.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(destination, relative));
            }
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (overwrite || !File.Exists(target)) File.Copy(file, target, overwrite);
            }
        }

        static bool DirectoryHasEntries(string path)
        {
            if (!Directory.Exists(path)) return false;
            using var e = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            return e.MoveNext();
        }

        static bool PathsEqual(string a, string b)
        {
            var comparison = Application.platform == RuntimePlatform.WindowsPlayer
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                comparison);
        }

        static void WriteMarker(string path, string note)
        {
            File.WriteAllText(path,
                "SONAR Simulator Task desktop data initialized " + DateTime.UtcNow.ToString("O") + Environment.NewLine + note);
        }
    }
}
