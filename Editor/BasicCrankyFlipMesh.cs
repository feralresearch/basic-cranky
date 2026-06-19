using UnityEditor;
using UnityEngine;

namespace BasicCranky
{
    // Rotates an SMR's mesh 180° around the Y axis (vertical), about the mesh's bounds center.
    // Use when a clothing item came out of Link Clothing facing the wrong way.
    // Works on a copy of the mesh saved next to the original "_Linked" asset.
    public static class BasicCrankyFlipMesh
    {
        const string MENU = BasicCrankyShared.MenuRoot + "Flip Mesh 180° Y (fix backwards clothing)";

        [MenuItem(MENU, true)]
        static bool Validate()
        {
            var go = Selection.activeGameObject;
            return go != null && go.GetComponent<SkinnedMeshRenderer>() != null;
        }

        [MenuItem(MENU, false, 1)]
        static void FlipSelected()
        {
            var smr = Selection.activeGameObject.GetComponent<SkinnedMeshRenderer>();
            var oldMesh = smr.sharedMesh;
            if (oldMesh == null)
            {
                EditorUtility.DisplayDialog("Flip Mesh", "SMR has no mesh.", "OK");
                return;
            }

            Undo.RecordObject(smr, "Flip Mesh 180 Y");

            var oldVerts = oldMesh.vertices;
            var oldNormals = oldMesh.normals;
            var oldTangents = oldMesh.tangents;

            // Bounds center stays put; rotate around it.
            var min = oldVerts[0]; var max = oldVerts[0];
            for (int v = 1; v < oldVerts.Length; v++)
            { min = Vector3.Min(min, oldVerts[v]); max = Vector3.Max(max, oldVerts[v]); }
            var center = (min + max) * 0.5f;

            Debug.Log($"[BasicCranky FlipMesh] {smr.name}: rotating {oldVerts.Length} verts 180° around Y, pivoting at {center}");

            // 180° around Y about center: (x, y, z) - center -> (-x, y, -z) + center
            var newVerts = new Vector3[oldVerts.Length];
            for (int v = 0; v < oldVerts.Length; v++)
            {
                var p = oldVerts[v] - center;
                newVerts[v] = new Vector3(-p.x, p.y, -p.z) + center;
            }

            Vector3[] newNormals = null;
            if (oldNormals != null && oldNormals.Length == oldVerts.Length)
            {
                newNormals = new Vector3[oldNormals.Length];
                for (int v = 0; v < oldNormals.Length; v++)
                    newNormals[v] = new Vector3(-oldNormals[v].x, oldNormals[v].y, -oldNormals[v].z);
            }
            Vector4[] newTangents = null;
            if (oldTangents != null && oldTangents.Length == oldVerts.Length)
            {
                newTangents = new Vector4[oldTangents.Length];
                for (int v = 0; v < oldTangents.Length; v++)
                {
                    var t = oldTangents[v];
                    newTangents[v] = new Vector4(-t.x, t.y, -t.z, t.w);
                }
            }

            var newMesh = Object.Instantiate(oldMesh);
            newMesh.name = oldMesh.name + "_FlippedY";
            newMesh.vertices = newVerts;
            if (newNormals != null) newMesh.normals = newNormals;
            if (newTangents != null) newMesh.tangents = newTangents;
            newMesh.RecalculateBounds();

            const string gen = "Assets/_UserContent/Generated";
            if (!System.IO.Directory.Exists(gen)) System.IO.Directory.CreateDirectory(gen);
            var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{gen}/{newMesh.name}.asset");
            AssetDatabase.CreateAsset(newMesh, assetPath);
            EditorUtility.SetDirty(newMesh);
            AssetDatabase.SaveAssets();

            smr.sharedMesh = newMesh;
            Debug.Log($"[BasicCranky FlipMesh] Saved {assetPath}, assigned to {smr.name}");
            EditorUtility.DisplayDialog("Flip Mesh",
                $"Flipped {oldVerts.Length} vertices 180° around Y about {center}.\nNew mesh: {assetPath}", "OK");
        }
    }
}
