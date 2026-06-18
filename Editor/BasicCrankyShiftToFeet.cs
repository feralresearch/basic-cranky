using UnityEditor;
using UnityEngine;

namespace BasicCranky
{
    public static class BasicCrankyShiftToFeet
    {
        const string FOOT_BAKE_MENU = BasicCrankyShared.MenuRoot + "Full Rebake + Shift to Feet";

        [MenuItem(FOOT_BAKE_MENU, true)]
        static bool ValidateFootBake()
        {
            var go = Selection.activeGameObject;
            return go != null && go.GetComponent<SkinnedMeshRenderer>() != null;
        }

        [MenuItem(FOOT_BAKE_MENU, false, 3)]
        static void FullBakeShiftToFeet()
        {
            var go = Selection.activeGameObject;
            var smr = go == null ? null : go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return;
            var oldMesh = smr.sharedMesh;
            if (oldMesh == null) { EditorUtility.DisplayDialog("Shift to Feet", "No mesh on SMR.", "OK"); return; }

            var bones = smr.bones;
            var oldBindPoses = oldMesh.bindposes;
            var oldVerts = oldMesh.vertices;
            var weights = oldMesh.boneWeights;

            if (oldBindPoses.Length != bones.Length || weights == null || weights.Length != oldVerts.Length)
            {
                EditorUtility.DisplayDialog("Shift to Feet", "Mesh data mismatched (bones / bindposes / weights). Aborting.", "OK");
                return;
            }

            Undo.RecordObject(smr, "Shift to Feet");

            var smrLtoW = smr.transform.localToWorldMatrix;
            var smrWtoL = smr.transform.worldToLocalMatrix;

            // Pass 1: rebake math, producing target world positions per vertex.
            var newVerts = new Vector3[oldVerts.Length];
            for (int v = 0; v < oldVerts.Length; v++)
            {
                var bw = weights[v];
                var vp = new Vector4(oldVerts[v].x, oldVerts[v].y, oldVerts[v].z, 1f);
                Vector3 target = Vector3.zero;
                for (int slot = 0; slot < 4; slot++)
                {
                    int idx; float w;
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

            // Compute current mesh bounds center in SMR-local space.
            var min = newVerts[0]; var max = newVerts[0];
            for (int v = 1; v < newVerts.Length; v++)
            {
                min = Vector3.Min(min, newVerts[v]);
                max = Vector3.Max(max, newVerts[v]);
            }
            var meshCenterLocal = (min + max) * 0.5f;

            // Find anchor bones to compute the desired world position for the mesh center.
            // Prefer foot.l/foot.r (Blender convention) or LeftFoot/RightFoot.
            Transform anchorA = null, anchorB = null;
            foreach (var b in bones)
            {
                if (b == null) continue;
                var n = b.name.ToLowerInvariant();
                if (n == "foot.l" || n == "leftfoot" || n == "foot_l") anchorA = b;
                else if (n == "foot.r" || n == "rightfoot" || n == "foot_r") anchorB = b;
            }
            Vector3 anchorWorld;
            string anchorDesc;
            if (anchorA != null && anchorB != null)
            {
                anchorWorld = (anchorA.position + anchorB.position) * 0.5f;
                anchorDesc = $"midpoint of {anchorA.name} + {anchorB.name}";
            }
            else if (smr.rootBone != null)
            {
                anchorWorld = smr.rootBone.position;
                anchorDesc = $"rootBone ({smr.rootBone.name})";
            }
            else
            {
                anchorWorld = smr.transform.position;
                anchorDesc = "SMR origin";
            }
            var anchorLocal = smrWtoL.MultiplyPoint3x4(anchorWorld);
            var shift = anchorLocal - meshCenterLocal;

            // Pass 2: apply the shift.
            for (int v = 0; v < newVerts.Length; v++) newVerts[v] += shift;

            // Rebake bind poses against the avatar's current rest.
            var newBindPoses = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                newBindPoses[i] = bones[i] != null
                    ? bones[i].worldToLocalMatrix * smrLtoW
                    : oldBindPoses[i];
            }

            var newMesh = Object.Instantiate(oldMesh);
            newMesh.name = oldMesh.name + "_OnFeet";
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

            EditorUtility.DisplayDialog("Shift to Feet",
                $"Rebaked + shifted to {anchorDesc}.\nShift applied: {shift}\nNew mesh: {assetPath}",
                "OK");
        }
    }
}
