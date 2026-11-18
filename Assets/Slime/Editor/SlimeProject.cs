using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlimeLab
{
    public static class SlimeProject
    {
        static string Arg(string key,string fallback){var a=Environment.GetCommandLineArgs();for(int i=0;i<a.Length-1;i++)if(a[i]==key)return a[i+1];return fallback;}
        [MenuItem("Slime Lab/Open laboratory")]
        public static void OpenLaboratory()=>EditorSceneManager.OpenScene("Assets/Slime/Scenes/Laboratory.unity");
        [MenuItem("Slime Lab/Run regression checks")]
        public static void RunInEditor()
        {
            try
            {
                int done=0;var report=SlimeChecks.Run(c=>{EditorUtility.DisplayProgressBar("Slime regression",c.name,++done/54f);Debug.Log($"{(c.passed?"PASS":"FAIL")} {c.name}: {c.detail}");});
                File.WriteAllText("Validation.json",JsonUtility.ToJson(report,true));
                EditorUtility.DisplayDialog("Slime regression",report.passed?"全部检查通过。报告：项目根目录 Validation.json":"存在失败项，请检查 Console 和 Validation.json。","OK");
            }
            finally{EditorUtility.ClearProgressBar();}
        }
        public static void Validate()
        {
            var report=SlimeChecks.Run(c=>Debug.Log($"[SLIME-TEST] {(c.passed?"PASS":"FAIL")} {c.name}: {c.detail}"));string path=Arg("-slimeReport",Path.GetFullPath("Validation.json"));File.WriteAllText(path,JsonUtility.ToJson(report,true));
            var colony=SlimeColonyChecks.Run(c=>Debug.Log($"[SLIME-COLONY-TEST] {(c.passed?"PASS":"FAIL")} {c.name}: {c.detail}"));File.WriteAllText(Path.Combine(Path.GetDirectoryName(path),"Colony-validation.json"),JsonUtility.ToJson(colony,true));
            EditorApplication.Exit(report.passed&&colony.passed?0:2);
        }
        [MenuItem("Slime Lab/Run colony checks")]
        public static void RunColonyInEditor(){var report=SlimeColonyChecks.Run(c=>Debug.Log(c.name+": "+c.passed+" / "+c.detail));File.WriteAllText("Colony-validation.json",JsonUtility.ToJson(report,true));EditorUtility.DisplayDialog("Slime colony",report.passed?"分裂、栏杆、跟随与融合检查全部通过。":"存在失败项，请检查 Console。","OK");}
        public static void ValidateColony(){var report=SlimeColonyChecks.Run(c=>Debug.Log($"[SLIME-COLONY-TEST] {(c.passed?"PASS":"FAIL")} {c.name}: {c.detail}"));File.WriteAllText(Arg("-slimeReport",Path.GetFullPath("Colony-validation.json")),JsonUtility.ToJson(report,true));EditorApplication.Exit(report.passed?0:2);}
        public static void Build()
        {
            SlimeHDRPProject.Configure();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("Slime Laboratory");root.AddComponent<SlimeLabController>();
            Directory.CreateDirectory("Assets/Slime/Scenes");EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),"Assets/Slime/Scenes/Laboratory.unity");
            string outPath=Arg("-slimeBuild",Path.GetFullPath("Build/Slime-Simulator.exe"));Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            var result=BuildPipeline.BuildPlayer(new[]{"Assets/Slime/Scenes/Laboratory.unity"},outPath,BuildTarget.StandaloneWindows64,BuildOptions.None);
            Debug.Log("[SLIME-BUILD] "+result.summary.result);EditorApplication.Exit(result.summary.result==UnityEditor.Build.Reporting.BuildResult.Succeeded?0:3);
        }
    }
}
