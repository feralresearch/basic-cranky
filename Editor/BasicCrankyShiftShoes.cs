using UnityEditor;
using UnityEngine;

namespace BasicCranky
{
    // Shifts the rendered mesh of a SkinnedMeshRenderer by a world-space offset, by modifying
    // the bindposes such that every vertex appears at original_position + worldOffset.
    //
    // The math (verified for any bone i with non-trivial rotation):
    //   new_bindpose[i] = bone[i].worldToLocal * T(offset) * bone[i].localToWorld * old_bindpose[i]
    //
    // Then in the skinning formula:
    //   vertex_world = bone.localToWorld * new_bindpose * vertex_local
    //                = bone.localToWorld * bone.worldToLocal * T(offset) * bone.localToWorld * old_bindpose * vertex_local
    //                = T(offset) * bone.localToWorld * old_bindpose * vertex_local
    //                = old_vertex_world + offset
    //
    // Works regardless of vertex_local values (so the broken Unity .vertices = (0,0,0) issue
    // doesn't matter — we never read vertex_local, just modify bindposes).
    public static class BasicCrankyShiftShoes
    {
        const string MENU = BasicCrankyShared.MenuRoot + "Shift SMR World-Space (open dialog)";

        [MenuItem(MENU, true)]
        static bool Validate()
        {
            var go = Selection.activeGameObject;
            return go != null && go.GetComponent<SkinnedMeshRenderer>() != null;
        }

        [MenuItem(MENU, false, 40)]
        static void Run()
        {
            ShiftMeshWindow.Open(Selection.activeGameObject.GetComponent<SkinnedMeshRenderer>());
        }

        public static void Apply(SkinnedMeshRenderer smr, Vector3 worldOffset)
        {
            var mesh = smr.sharedMesh;
            if (mesh == null) { Debug.LogError("[Shift] no mesh"); return; }

            var newMesh = Object.Instantiate(mesh);
            newMesh.name = (mesh.name.EndsWith("_Shifted") ? mesh.name : mesh.name + "_Shifted");

            var bindposes = mesh.bindposes;
            var bones = smr.bones;
            var newBindposes = new Matrix4x4[bindposes.Length];

            var T = Matrix4x4.Translate(worldOffset);
            int applied = 0;
            for (int i = 0; i < bindposes.Length; i++)
            {
                if (bones[i] != null)
                {
                    newBindposes[i] = bones[i].worldToLocalMatrix * T * bones[i].localToWorldMatrix * bindposes[i];
                    applied++;
                }
                else
                {
                    newBindposes[i] = bindposes[i];
                }
            }
            newMesh.bindposes = newBindposes;
            newMesh.RecalculateBounds();

            const string gen = "Assets/_UserContent/Generated";
            if (!System.IO.Directory.Exists(gen)) System.IO.Directory.CreateDirectory(gen);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{gen}/{newMesh.name}.asset");
            AssetDatabase.CreateAsset(newMesh, path);
            EditorUtility.SetDirty(newMesh);
            AssetDatabase.SaveAssets();
            smr.sharedMesh = newMesh;

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Debug.Log($"[BasicCranky ShiftShoes] Applied {worldOffset} to {smr.name} ({applied}/{bindposes.Length} bones). New mesh: {path}");
        }
    }

    public class ShiftMeshWindow : EditorWindow
    {
        SkinnedMeshRenderer smr;
        // Default tuned for Polycrow FH_FootWear on Freakhound: 8.9 cm up, 6.4 cm back.
        // Tweak per-avatar in the sliders.
        Vector3 offset = new Vector3(0f, 0.089f, -0.064f);
        Mesh originalMesh; // so we can preview from a clean state each apply

        public static void Open(SkinnedMeshRenderer target)
        {
            var win = GetWindow<ShiftMeshWindow>("Shift SMR");
            win.smr = target;
            win.originalMesh = target.sharedMesh;
            win.minSize = new Vector2(280, 180);
            win.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Target SMR:", smr != null ? smr.name : "<none>");
            if (smr == null) return;

            EditorGUILayout.HelpBox("Drag the sliders. Each change reverts the mesh to its original bindposes and applies the new total offset. Doesn't compound.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            offset.x = EditorGUILayout.Slider("X (right +)", offset.x, -0.3f, 0.3f);
            offset.y = EditorGUILayout.Slider("Y (up +)", offset.y, -0.3f, 0.3f);
            offset.z = EditorGUILayout.Slider("Z (forward +)", offset.z, -0.3f, 0.3f);
            bool changed = EditorGUI.EndChangeCheck();

            if (GUILayout.Button("Apply (Live)") || changed)
            {
                if (originalMesh != null) smr.sharedMesh = originalMesh;
                BasicCrankyShiftShoes.Apply(smr, offset);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Reset to original mesh"))
            {
                if (originalMesh != null) smr.sharedMesh = originalMesh;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Current offset:", offset.ToString("F3"));
        }
    }
}
