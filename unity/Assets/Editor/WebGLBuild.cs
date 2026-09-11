using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Batch-mode WebGL build entry point:
//   unity build <project> --target WebGL --execute-method WebGLBuild.Build -o <output dir>
public static class WebGLBuild
{
    public static void Build()
    {
        var args = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(args, "-buildOutput");
        var output = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Build/WebGL";

        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        });

        var summary = report.summary;
        Debug.Log($"[WebGLBuild] result={summary.result} output={output} size={summary.totalSize} errors={summary.totalErrors} time={summary.totalTime}");
        if (summary.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }
}
