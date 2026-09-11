using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Batch-mode WebGL build entry point:
//   unity build <project> --target WebGL --execute-method WebGLBuild.Build -o <output dir>
// Optional: --args "-webglCompression Disabled" (or Gzip / Brotli) overrides the compression for
// this build only. Disabled builds open from any local HTTP server; the project setting is restored.
public static class WebGLBuild
{
    public static void Build()
    {
        var args = Environment.GetCommandLineArgs();
        var output = ArgValue(args, "-buildOutput") ?? "Build/WebGL";
        var compression = ArgValue(args, "-webglCompression");

        var previousCompression = PlayerSettings.WebGL.compressionFormat;
        if (compression != null)
            PlayerSettings.WebGL.compressionFormat = (WebGLCompressionFormat)Enum.Parse(typeof(WebGLCompressionFormat), compression, true);

        BuildReport report;
        try
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
        }
        finally
        {
            PlayerSettings.WebGL.compressionFormat = previousCompression;
        }

        var summary = report.summary;
        Debug.Log($"[WebGLBuild] result={summary.result} output={output} compression={compression ?? previousCompression.ToString()} " +
                  $"size={summary.totalSize} errors={summary.totalErrors} time={summary.totalTime}");
        if (summary.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }

    private static string ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
