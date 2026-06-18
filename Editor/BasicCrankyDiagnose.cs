using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BasicCranky
{
    public static class BasicCrankyDiagnose
    {
        const string DIAGNOSE_MENU = BasicCrankyShared.MenuRoot + "Diagnose SMR Bones";

        [MenuItem(DIAGNOSE_MENU, true)]
        static bool ValidateDiagnose() => Selection.activeGameObject != null
            && Selection.activeGameObject.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;

        [MenuItem(DIAGNOSE_MENU, false, 10)]
        static void DiagnoseSelected()
        {
            var go = Selection.activeGameObject;
            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var sb = new System.Text.StringBuilder();
            foreach (var smr in smrs)
            {
                sb.AppendLine($"=== SMR: {smr.name} ===");
                var bones = smr.bones;
                sb.AppendLine($"bones.Length = {bones.Length}");
                int nullCount = 0, outsideCount = 0, insideCount = 0;
                var nullIndices = new List<int>();
                var avatarRoot = smr.transform.root;
                for (int i = 0; i < bones.Length; i++)
                {
                    var b = bones[i];
                    if (b == null) { nullCount++; nullIndices.Add(i); continue; }
                    if (b.IsChildOf(avatarRoot)) insideCount++;
                    else outsideCount++;
                }
                sb.AppendLine($"  null/destroyed: {nullCount}");
                sb.AppendLine($"  inside scene root: {insideCount}");
                sb.AppendLine($"  outside scene root: {outsideCount}");
                sb.AppendLine($"rootBone = {(smr.rootBone == null ? "NULL" : smr.rootBone.name + (smr.rootBone.IsChildOf(avatarRoot) ? " (inside)" : " (OUTSIDE)"))}");
                sb.AppendLine($"sharedMesh = {(smr.sharedMesh == null ? "NULL" : smr.sharedMesh.name)}");
                sb.AppendLine($"bounds: center {smr.bounds.center}, extent {smr.bounds.extents}");
                if (smr.sharedMesh != null)
                {
                    var m = smr.sharedMesh;
                    sb.AppendLine($"mesh vertices: {m.vertexCount}, bindposes: {m.bindposes.Length}, boneWeights: {(m.boneWeights == null ? 0 : m.boneWeights.Length)}");
                    var bp = m.bindposes;
                    var ranges = new List<float>();
                    int identity = 0;
                    for (int i = 0; i < bp.Length; i++)
                    {
                        if (bp[i].isIdentity) identity++;
                        var t = bp[i].GetColumn(3);
                        ranges.Add(new Vector3(t.x, t.y, t.z).magnitude);
                    }
                    if (ranges.Count > 0)
                        sb.AppendLine($"bindpose translation magnitudes: min={ranges.Min():F4} max={ranges.Max():F4} mean={ranges.Average():F4} (identity matrices: {identity})");
                }
                if (nullIndices.Count > 0 && nullIndices.Count <= 20)
                    sb.AppendLine($"null bone indices: {string.Join(",", nullIndices)}");
            }
            var report = sb.ToString();
            Debug.Log("[BasicCranky Diagnose]\n" + report);
            EditorUtility.DisplayDialog("Diagnose SMR", report, "OK");
        }
    }
}
