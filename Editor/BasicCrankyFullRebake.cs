using UnityEditor;
using UnityEngine;

namespace BasicCranky
{
    public static class BasicCrankyFullRebake
    {
        const string FULL_BAKE_MENU = BasicCrankyShared.MenuRoot + "Full Rebake (Vertices + Bind Poses)";

        [MenuItem(FULL_BAKE_MENU, true)]
        static bool ValidateFullBake()
        {
            var go = Selection.activeGameObject;
            return go != null && go.GetComponent<SkinnedMeshRenderer>() != null;
        }

        [MenuItem(FULL_BAKE_MENU, false, 2)]
        public static void FullBakeSelected()
        {
            var go = Selection.activeGameObject;
            var smr = go == null ? null : go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return;
            var oldMesh = smr.sharedMesh;
            if (oldMesh == null)
            {
                EditorUtility.DisplayDialog("Full Rebake", "SMR has no mesh assigned.", "OK");
                return;
            }

            var bones = smr.bones;
            var oldBindPoses = oldMesh.bindposes;
            var oldVerts = oldMesh.vertices;
            var weights = oldMesh.boneWeights;

            if (weights == null || weights.Length != oldVerts.Length)
            {
                EditorUtility.DisplayDialog("Full Rebake", "Mesh has no per-vertex bone weights — can't rebake.", "OK");
                return;
            }
            if (oldBindPoses.Length != bones.Length)
            {
                EditorUtility.DisplayDialog("Full Rebake",
                    $"Bone count ({bones.Length}) doesn't match bindposes count ({oldBindPoses.Length}). Aborting.", "OK");
                return;
            }

            Undo.RecordObject(smr, "Full Rebake");

            var smrLtoW = smr.transform.localToWorldMatrix;
            var smrWtoL = smr.transform.worldToLocalMatrix;

            // Snapshot each vertex's intended world position using old bind poses + current avatar bones.
            var newVerts = new Vector3[oldVerts.Length];
            for (int v = 0; v < oldVerts.Length; v++)
            {
                var bw = weights[v];
                var vp = new Vector4(oldVerts[v].x, oldVerts[v].y, oldVerts[v].z, 1f);
                Vector3 target = Vector3.zero;

                for (int slot = 0; slot < 4; slot++)
                {
                    int idx;
                    float w;
                    switch (slot)
                    {
                        case 0: idx = bw.boneIndex0; w = bw.weight0; break;
                        case 1: idx = bw.boneIndex1; w = bw.weight1; break;
                        case 2: idx = bw.boneIndex2; w = bw.weight2; break;
                        default: idx = bw.boneIndex3; w = bw.weight3; break;
                    }
                    if (w <= 0f || idx < 0 || idx >= bones.Length || bones[idx] == null) continue;

                    Vector4 boneLocal = oldBindPoses[idx] * vp;
                    Vector4 worldPos = bones[idx].localToWorldMatrix * boneLocal;
                    target += w * new Vector3(worldPos.x, worldPos.y, worldPos.z);
                }
                newVerts[v] = smrWtoL.MultiplyPoint3x4(target);
            }

            // Rebake bind poses against the avatar's current rest.
            var newBindPoses = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                newBindPoses[i] = bones[i] != null
                    ? bones[i].worldToLocalMatrix * smrLtoW
                    : oldBindPoses[i];
            }

            var newMesh = Object.Instantiate(oldMesh);
            newMesh.name = oldMesh.name + "_BoundToAvatar";
            newMesh.vertices = newVerts;
            newMesh.bindposes = newBindPoses;
            newMesh.RecalculateNormals();
            newMesh.RecalculateTangents();
            newMesh.RecalculateBounds();

            if (!System.IO.Directory.Exists(BasicCrankyShared.GeneratedDir))
                System.IO.Directory.CreateDirectory(BasicCrankyShared.GeneratedDir);
            var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{BasicCrankyShared.GeneratedDir}/{newMesh.name}.asset");
            AssetDatabase.CreateAsset(newMesh, assetPath);
            EditorUtility.SetDirty(newMesh);
            AssetDatabase.SaveAssets();

            smr.sharedMesh = newMesh;

            EditorUtility.DisplayDialog("Full Rebake",
                $"Rebaked {oldVerts.Length} vertices + {bones.Length} bind poses against the avatar's current rest pose.\nNew mesh: {assetPath}\n\nIf the mesh still doesn't appear on the avatar, the issue is in the vertex weights / authoring rest pose and needs Blender.",
                "OK");
        }
    }
}
