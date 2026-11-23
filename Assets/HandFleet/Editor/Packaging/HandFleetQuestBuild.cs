using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.OpenXR;
using Unity.XR.OpenXR.Features.PICOSupport;

namespace HandFleet.BuildTools
{
    // Quest APK of the standalone HandFleet project: the HandFleet scene is the only scene, so the app opens
    // straight into HandFleet. Package id and product name are the project's own (see ProjectSettings).
    public static class HandFleetQuestBuild
    {
        public const string Apk="Builds/HandFleet-Quest.apk";
        const int MsaaSamples=4;
        const string MobilePipeline="Assets/Settings/Mobile_RPAsset.asset";

        [MenuItem("HandFleet/Build Quest APK",false,90)]
        public static void BuildMenu()=>Build(BuildOptions.None);
        [MenuItem("HandFleet/Build Quest APK (development)",false,91)]
        public static void BuildDevelopmentMenu()=>Build(BuildOptions.Development);

        public static void BuildFromCommandLine()
        {
            var report=Build(Environment.GetCommandLineArgs().Contains("-development")?BuildOptions.Development:BuildOptions.None);
            if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("HandFleet Quest build failed: "+report.summary.result);
        }

        public static BuildReport Build(BuildOptions options)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||BuildPipeline.isBuildingPlayer)throw new InvalidOperationException("Stop Play mode and wait for any current build first.");
            AndroidBuildValidation.ValidateTarget(BuildTarget.Android,EditorUserBuildSettings.activeBuildTarget);
            var pico=OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android).GetFeature<PICOFeature>();
            if(pico&&pico.enabled)throw new InvalidOperationException("PICO features are on; run HandFleet/Pico/Restore Quest configuration first.");
            ApplyMsaa();
            Directory.CreateDirectory(Path.GetDirectoryName(Apk));if(File.Exists(Apk))File.Delete(Apk);
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/HandFleet/Scenes/HandFleet.unity"},locationPathName=Apk,
                target=BuildTarget.Android,targetGroup=BuildTargetGroup.Android,options=options|BuildOptions.CompressWithLz4});
            var apk=new FileInfo(Apk);
            Debug.Log($"HANDFLEET_QUEST_BUILD result={report.summary.result} errors={report.summary.totalErrors} warnings={report.summary.totalWarnings} "+
                $"seconds={report.summary.totalTime.TotalSeconds:F0} "+(apk.Exists?$"apk={apk.FullName} bytes={apk.Length}":"apk=missing"));
            return report;
        }

        static void ApplyMsaa()
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobilePipeline);
            if(!pipeline)throw new InvalidOperationException("Mobile_RPAsset is required for Quest rendering.");
            if(pipeline.msaaSampleCount==MsaaSamples)return;
            pipeline.msaaSampleCount=MsaaSamples;EditorUtility.SetDirty(pipeline);AssetDatabase.SaveAssets();
        }
    }
}
