using UnityEditor;
using UnityEngine;

namespace BasicCranky
{
    // Quick action: find the body SMR's "FlattenBodyFluffLegs" blendshape (and common variants)
    // and set them to 100. Polycrow shoes are designed to fit OVER the shrunken-fluff version
    // of the body's paws — without this blendshape active, the paws stay full-size and the
    // shoes end up underneath/behind them.
    public static class BasicCrankyFlattenLegs
    {
        const string MENU = BasicCrankyShared.MenuRoot + "Set Leg-Fluff Blendshapes to 100 (for shoes)";

        // Try these in order; whichever ones exist get set to 100.
        static readonly string[] CANDIDATES = new[]
        {
            "FlattenBodyFluffLegs",
            "FlattenBodyFluffLegsMLV",
            "FlattenBodyFluffLegsP",
            "FlattenBodyFluffLegsS",
            "FlattenBodyFluffLegsSbS",
            "FlattenBodyFluffLegstLV",
            "FlattenBodyFluffLegsw",
            "FlattenBodyFluffLegszbS",
        };

        [MenuItem(MENU, true)]
        static bool Validate() => Selection.activeGameObject != null;

        [MenuItem(MENU, false, 30)]
        static void Run()
        {
            var root = Selection.activeGameObject;
            if (root == null) return;
            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int set = 0;
            var report = new System.Text.StringBuilder();
            report.AppendLine($"Walked {smrs.Length} SMR(s) under {root.name}");
            foreach (var smr in smrs)
            {
                if (smr.sharedMesh == null) continue;
                foreach (var name in CANDIDATES)
                {
                    int idx = smr.sharedMesh.GetBlendShapeIndex(name);
                    if (idx < 0) continue;
                    Undo.RecordObject(smr, "Set leg fluff blendshape");
                    smr.SetBlendShapeWeight(idx, 100f);
                    EditorUtility.SetDirty(smr);
                    report.AppendLine($"  {smr.name}.{name} = 100");
                    set++;
                }
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            report.AppendLine();
            report.AppendLine($"Set {set} blendshape(s) total.");
            Debug.Log("[BasicCranky FlattenLegs] " + report.ToString().Replace("\n", " | "));
            EditorUtility.DisplayDialog("Flatten Leg Fluff", report.ToString(), "OK");
        }
    }
}
