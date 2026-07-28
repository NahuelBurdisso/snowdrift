using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Snowdrift.Editor
{
    // Windows standalone build. Run via:
    //   Unity.exe -batchmode -projectPath . -executeMethod Snowdrift.Editor.BuildScript.BuildWindows -quit
    public static class BuildScript
    {
        [MenuItem("Snowdrift/Build Windows")]
        public static void BuildWindows()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Main.unity" },
                locationPathName = "Build/Windows/Snowdrift.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[BuildScript] Result={summary.result} Size={summary.totalSize / (1024 * 1024)}MB Errors={summary.totalErrors} Warnings={summary.totalWarnings}");
            if (summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1); // non-zero exit so CI/CLI callers see the failure
        }
    }
}
