using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HandFleet.BuildTools;
using Unity.XR.OpenXR.Features.PICOSupport;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.XR.OpenXR;
using Debug=UnityEngine.Debug;

namespace HandFleet.Editor
{
    // One click to a PICO APK of HandFleet alone. PicoBuild.Prepare switches on the PICO OpenXR features and generates the
    // portable UnityXR player, which PicoBuild.OnProcessScene puts into HandFleetEntry.StandalonePlayer. This adds 90 Hz,
    // builds HandFleet.unity alone under its own package id and name, then puts back the Quest configuration and the
    // project's id and name, even when the build fails. The shared build list is never touched.
    // Command line, editor closed (add -development and/or -install):
    //   Unity.exe -batchmode -quit -projectPath . -buildTarget Android -executeMethod HandFleet.Editor.HandFleetPicoBuild.BuildFromCommandLine
    public static class HandFleetPicoBuild
    {
        public const string PackageId="com.metahackathon2026.handfleet",ProductName="HandFleet";
        public const string Apk="Builds/HandFleet-Pico.apk";

        [MenuItem("HandFleet/Build PICO APK",false,100)]
        public static void BuildMenu()=>Finish(Build(false),false);
        [MenuItem("HandFleet/Build PICO APK (development)",false,101)]
        public static void BuildDevelopmentMenu()=>Finish(Build(true),false);
        [MenuItem("HandFleet/Build PICO APK and run",false,102)]
        public static void BuildAndRunMenu()=>Finish(Build(false),true);
        [MenuItem("HandFleet/Install last PICO APK and run",false,103)]
        public static void InstallMenu()
        {
            if(!File.Exists(Apk))throw new FileNotFoundException("Build the HandFleet PICO APK first.",Apk);
            InstallAndLaunch();
        }

        public static void BuildFromCommandLine()
        {
            var args=Environment.GetCommandLineArgs();
            var report=Build(args.Contains("-development"));
            if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("HandFleet PICO build failed: "+report.summary.result);
            if(args.Contains("-install"))InstallAndLaunch();
        }

        static void Finish(BuildReport report,bool run)
        {
            if(report.summary.result!=BuildResult.Succeeded||!File.Exists(Apk))return;
            if(run)InstallAndLaunch();else EditorUtility.RevealInFinder(Path.GetFullPath(Apk));
        }

        public static BuildReport Build(bool development)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||BuildPipeline.isBuildingPlayer)throw new InvalidOperationException("Stop Play mode and wait for any current build first.");
            if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)throw new InvalidOperationException("Switch the active build target to Android first.");
            if(!File.Exists(HandFleetBuilder.ScenePath))throw new FileNotFoundException("The HandFleet scene is missing.",HandFleetBuilder.ScenePath);
            var scenes=EmptySceneSetup();
            try { return BuildInEmptyScene(development); }
            finally { RestoreScenes(scenes); }
        }
        // PicoBuild generates its player in a scratch scene and refuses while an untitled scene or an XR Origin is open.
        // Build from an empty scene instead (offering to save edits first), then reopen what was open.
        static SceneSetup[] EmptySceneSetup()
        {
            var setup=EditorSceneManager.GetSceneManagerSetup();
            bool dirty=Enumerable.Range(0,SceneManager.sceneCount).Any(i=>SceneManager.GetSceneAt(i).isDirty);
            if(dirty&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())throw new OperationCanceledException("HandFleet PICO build cancelled: open scenes have unsaved changes.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            return setup;
        }
        static void RestoreScenes(SceneSetup[] setup)
        {
            // Untitled scenes cannot be reopened by path; their content was either saved above or unchanged defaults.
            var titled=setup.Where(s=>!string.IsNullOrEmpty(s.path)&&File.Exists(s.path)).ToArray();
            if(!titled.Any(s=>s.isLoaded))return;
            if(!titled.Any(s=>s.isActive&&s.isLoaded)){foreach(var s in titled)s.isActive=false;titled.First(s=>s.isLoaded).isActive=true;}
            EditorSceneManager.RestoreSceneManagerSetup(titled);
        }
        static BuildReport BuildInEmptyScene(bool development)
        {
            var target=NamedBuildTarget.Android;string id=PlayerSettings.GetApplicationIdentifier(target),name=PlayerSettings.productName;
            PicoBuild.Prepare();
            try
            {
                // 90 Hz like the Quest path (HandFleetEntry asks OVRManager, which PICO lacks). Restore() undoes both lines
                // through Prepare's snapshot of every OpenXR feature and of the PICO project config.
                var refresh=OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android).GetFeature<DisplayRefreshRateFeature>();
                if(refresh)
                {
                    refresh.enabled=true;EditorUtility.SetDirty(refresh);
                    var config=PICOProjectSetting.GetProjectConfig();config.displayFrequency=SystemDisplayFrequency.RefreshRate90;EditorUtility.SetDirty(config);
                }
                else Debug.LogWarning("PICO display refresh rate feature not found; the APK runs at the system default rate.");
                PlayerSettings.SetApplicationIdentifier(target,PackageId);PlayerSettings.productName=ProductName;
                AssetDatabase.SaveAssets();
                // An up-to-date incremental build leaves an identical APK untouched, old timestamp and all; start from none.
                Directory.CreateDirectory(Path.GetDirectoryName(Apk));if(File.Exists(Apk))File.Delete(Apk);
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{HandFleetBuilder.ScenePath},target=BuildTarget.Android,
                    targetGroup=BuildTargetGroup.Android,locationPathName=Apk,options=development?BuildOptions.Development:BuildOptions.None});
                var apk=new FileInfo(Apk);
                Debug.Log($"HANDFLEET_PICO_BUILD result={report.summary.result} errors={report.summary.totalErrors} warnings={report.summary.totalWarnings} "+
                    $"seconds={report.summary.totalTime.TotalSeconds:F0} "+(apk.Exists?$"apk={apk.FullName} bytes={apk.Length}":"apk=missing"));
                return report;
            }
            finally
            {
                PlayerSettings.SetApplicationIdentifier(target,id);PlayerSettings.productName=name;
                PicoBuild.Restore();AssetDatabase.SaveAssets();
            }
        }

        // Installs on every connected device that reports a PICO manufacturer or brand (an attached Quest is left alone),
        // then launches the app. A PICO that is not being worn holds the app paused until it is put on.
        public static void InstallAndLaunch()
        {
            var picos=Devices().Where(IsPico).ToArray();
            if(picos.Length==0){Debug.LogWarning("HANDFLEET_PICO_INSTALL no PICO among the connected adb devices.");return;}
            foreach(var serial in picos)
            {
                try
                {
                    EditorUtility.DisplayProgressBar("HandFleet","Installing on PICO "+serial+"...",.5f);
                    string install=Adb($"-s {serial} install -r \"{Path.GetFullPath(Apk)}\"",300).Trim();
                    bool ok=install.Contains("Success");
                    Debug.Log($"HANDFLEET_PICO_INSTALL device={serial} ok={ok} {install}");
                    if(!ok)
                    {
                        if(install.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE"))
                            Debug.LogWarning($"A differently signed HandFleet is installed on {serial}; uninstall it first (adb -s {serial} uninstall {PackageId}). That clears its high scores.");
                        continue;
                    }
                    Adb($"-s {serial} shell monkey -p {PackageId} -c android.intent.category.LAUNCHER 1",30);
                    Debug.Log("HANDFLEET_PICO_LAUNCHED device="+serial);
                }
                finally { EditorUtility.ClearProgressBar(); }
            }
        }
        static string[] Devices()=>Adb("devices",15).Split('\n').Select(l=>l.Trim().Split('\t')).Where(p=>p.Length>=2&&p[1]=="device").Select(p=>p[0]).ToArray();
        static bool IsPico(string serial)=>(Adb($"-s {serial} shell getprop ro.product.manufacturer",15)+Adb($"-s {serial} shell getprop ro.product.brand",15))
            .IndexOf("pico",StringComparison.OrdinalIgnoreCase)>=0;
        // The editor's adb: a second adb (the one on PATH) would restart the server under the editor.
        static string Adb(string arguments,int seconds)
        {
            string sdk=UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath,adb=Path.Combine(sdk??"","platform-tools","adb.exe");
            if(!File.Exists(adb))adb="adb";
            try
            {
                using var process=Process.Start(new ProcessStartInfo(adb,arguments){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true});
                var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
                if(!process.WaitForExit(seconds*1000)){process.Kill();return "adb timed out after "+seconds+" s";}
                return output.Result+error.Result;
            }
            catch(Exception e){Debug.LogWarning("adb "+arguments+" failed: "+e.Message);return "";}
        }
    }
}
