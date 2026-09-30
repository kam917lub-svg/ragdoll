using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VITS.EditorTools
{
    // Standalone builds only contain shaders that some asset references. The game makes all its materials from code
    // (Shader.Find), so without this the build has no Lit / Decal / Flesh / X-ray shaders: materials fail and the
    // screen stays black. This keeps one material per shader in Assets/VITS/Resources/BuildMaterials (Resources are
    // always built in), with GPU instancing on so the instanced variants (blood drops, stains) are kept too.
    // It runs by itself when the project opens and again right before every build.
    [InitializeOnLoad]
    public class VITSBuildSetup : IPreprocessBuildWithReport
    {
        public const string Dir = "Assets/VITS/Resources/BuildMaterials";
        static readonly string[] ShaderNames = { "Universal Render Pipeline/Lit", "VITS/Decal", "VITS/Flesh", "VITS/Text", "VITS/XRay" };

        static VITSBuildSetup() { EditorApplication.delayCall += Ensure; }

        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) { Ensure(); }

        [MenuItem("VITS/Prepare build (shaders + scene)")]
        public static void Ensure()
        {
            try
            {
                if (!AssetDatabase.IsValidFolder("Assets/VITS")) return;
                if (!AssetDatabase.IsValidFolder("Assets/VITS/Resources")) AssetDatabase.CreateFolder("Assets/VITS", "Resources");
                if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/VITS/Resources", "BuildMaterials");
                bool made = false;
                foreach (var name in ShaderNames)
                {
                    var sh = Shader.Find(name);
                    if (sh == null) { Debug.LogWarning("VITS: shader not found in the project: " + name); continue; }
                    string path = Dir + "/" + name.Replace('/', '_').Replace(' ', '_') + ".mat";
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m == null)
                    {
                        m = new Material(sh) { enableInstancing = true };
                        AssetDatabase.CreateAsset(m, path);
                        made = true;
                    }
                    else if (m.shader != sh || !m.enableInstancing)
                    {
                        m.shader = sh; m.enableInstancing = true;
                        EditorUtility.SetDirty(m);
                        made = true;
                    }
                }
                if (made) { AssetDatabase.SaveAssets(); Debug.Log("VITS: build materials ready in " + Dir); }

                // the scene you play in has to be in the build
                if (EditorBuildSettings.scenes.Length == 0)
                {
                    var sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                    if (!string.IsNullOrEmpty(sc.path))
                    {
                        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(sc.path, true) };
                        Debug.Log("VITS: added " + sc.path + " to the build scenes");
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("VITS build setup: " + e.Message); }
        }
    }
}
