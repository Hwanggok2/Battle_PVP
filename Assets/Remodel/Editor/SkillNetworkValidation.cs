using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BattlePvp.Remodel.Editor
{
    public static class SkillNetworkValidation
    {
        public static void Build()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scenes first.");
            var previous=EditorSceneManager.GetSceneManagerSetup();
            string scene="Assets/Remodel/Validation/SkillNetworkProbe.unity";
            try
            {
                var created=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var probe=new GameObject("Skill network probe").AddComponent<Validation.SkillNetworkProbe>();
                probe.PlayerPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
                EditorSceneManager.SaveScene(created,scene);
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
            Directory.CreateDirectory("Reports/TrapNetwork");
            File.WriteAllText("Reports/TrapNetwork/build.txt","Building");
        }
        public static void BuildPrepared()
        {
            string scene="Assets/Remodel/Validation/SkillNetworkProbe.unity";
            File.WriteAllText("Reports/TrapNetwork/build.txt","Building player");
            {
                try
                {
                    var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
                        scenes=new[]{scene},locationPathName="Builds/SkillNetworkProbe/SkillNetworkProbe.exe",target=BuildTarget.StandaloneWindows64,
                        options=BuildOptions.Development,extraScriptingDefines=new[]{"BATTLE_PVP_NETWORK_PROBE"}});
                    File.WriteAllText("Reports/TrapNetwork/build.txt",report.summary.result+" errors="+report.summary.totalErrors+" warnings="+report.summary.totalWarnings);
                }
                catch(Exception e) { File.WriteAllText("Reports/TrapNetwork/build.txt",e.ToString()); }
            }
        }
    }
}
