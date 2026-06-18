using UnityEditor;
using UnityEngine;

namespace BasicCranky
{
    public static class BasicCrankyRebake
    {
        const string REBAKE_MENU = BasicCrankyShared.MenuRoot + "Rebake Bind Poses (Current Avatar Pose)";

        [MenuItem(REBAKE_MENU, true)]
        static bool ValidateRebake()
        {
            var go = Selection.activeGameObject;
            return go != null && go.GetComponent<SkinnedMeshRenderer>() != null;
        }

        [MenuItem(REBAKE_MENU, false, 1)]
        static void RebakeSelected()
        {
            var go = Selection.activeGameObject;
            var smr = go == null ? null : go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return;
            var sharedMesh = smr.sharedMesh;
            if (sharedMesh == null)
            {
                EditorUtility.DisplayDialog("Rebake Bind Poses", "SMR has no mesh assigned.", "OK");
                return;
            }

            Undo.RecordObject(smr, "Rebake Bind Poses");

            var newMesh = Object.Instantiate(sharedMesh);
            newMesh.name = sharedMesh.name + "_Rebound";

            if (!System.IO.Directory.Exists(BasicCrankyShared.GeneratedDir))
                System.IO.Directory.CreateDirectory(BasicCrankyShared.GeneratedDir);
            var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{BasicCrankyShared.GeneratedDir}/{newMesh.name}.asset");
            AssetDatabase.CreateAsset(newMesh, assetPath);

            var bones = smr.bones;
            var oldBindPoses = sharedMesh.bindposes;
            var newBindPoses = new Matrix4x4[bones.Length];
            var smrLtoW = smr.transform.localToWorldMatrix;

            int rebaked = 0;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null)
                {
                    newBindPoses[i] = bones[i].worldToLocalMatrix * smrLtoW;
                    rebaked++;
                }
                else
                {
                    newBindPoses[i] = i < oldBindPoses.Length ? oldBindPoses[i] : Matrix4x4.identity;
                }
            }
            newMesh.bindposes = newBindPoses;
            EditorUtility.SetDirty(newMesh);
            AssetDatabase.SaveAssets();

            smr.sharedMesh = newMesh;
            EditorUtility.DisplayDialog("Rebake Bind Poses",
                $"Rebaked {rebaked} bind poses against the avatar's current rest pose.\nNew mesh asset: {assetPath}\n\nIf shoes still look wrong, the source mesh's vertex positions are authored for a different skeleton — that needs Blender to fix.",
                "OK");
        }
    }
}
