using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GatorDragonGames.JigglePhysics;
using UnityEditor;
using UnityEngine;

namespace BasicCranky
{
    public static class BasicCrankyJiggleSetup
    {
        const string MENU         = BasicCrankyShared.MenuRoot + "Add Jiggle Rigs (Ears + Tail + Tongue + Hair)";
        const string DIAG_MENU    = BasicCrankyShared.MenuRoot + "Diagnose Jiggle Chain Bones";
        const string CLEAN_MENU   = BasicCrankyShared.MenuRoot + "Remove ALL Jiggle Rigs Under Selection";

        static readonly (string[] candidates, string label)[] CHAIN_ROOTS = new[]
        {
            (new[] { "ear.l.1", "ear.l", "Ear_L_1", "ear_l_1", "LeftEar" },  "Left Ear"),
            (new[] { "ear.r.1", "ear.r", "Ear_R_1", "ear_r_1", "RightEar" }, "Right Ear"),
            (new[] { "tail",    "tail.1", "Tail_1", "tail_1" },              "Tail"),
            (new[] { "tongue",  "tongue.1", "Tongue_1", "tongue_1" },        "Tongue"),
            (new[] { "hair.1",  "hair",  "Hair_1", "hair_1" },               "Hair"),
        };

        static string PathOf(Transform t)
        {
            var sb = new System.Text.StringBuilder();
            while (t != null) { sb.Insert(0, "/" + t.name); t = t.parent; }
            return sb.ToString();
        }

        [MenuItem(DIAG_MENU, true)]
        static bool ValidateDiag() => Selection.activeGameObject != null;

        [MenuItem(DIAG_MENU, false, 21)]
        static void DiagnoseChainBones()
        {
            var root = Selection.activeGameObject;
            if (root == null) return;

            var allTransforms = root.GetComponentsInChildren<Transform>(true);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Selected root: {root.name}");
            sb.AppendLine($"Total transforms scanned: {allTransforms.Length}");
            sb.AppendLine();

            var allCandidates = CHAIN_ROOTS.SelectMany(c => c.candidates).ToHashSet(System.StringComparer.OrdinalIgnoreCase);
            int matchCount = 0;
            foreach (var t in allTransforms)
            {
                if (!allCandidates.Contains(t.name)) continue;
                matchCount++;
                var hasRig = t.GetComponent<JiggleRig>() != null;
                sb.AppendLine($"  {(hasRig ? "[JIGGLE]" : "[      ]")} {PathOf(t)}");
            }
            if (matchCount == 0) sb.AppendLine("  (no candidate bone names found anywhere under this root)");

            Debug.Log("[BasicCranky JiggleDiagnose]\n" + sb.ToString());
            EditorUtility.DisplayDialog("Jiggle Chain Diagnose", sb.ToString(), "OK");
        }

        [MenuItem(CLEAN_MENU, true)]
        static bool ValidateClean() => Selection.activeGameObject != null;

        [MenuItem(CLEAN_MENU, false, 22)]
        static void RemoveAllJiggleRigs()
        {
            var root = Selection.activeGameObject;
            if (root == null) return;
            var rigs = root.GetComponentsInChildren<JiggleRig>(true);
            if (rigs.Length == 0)
            {
                EditorUtility.DisplayDialog("Remove Jiggle Rigs", "No JiggleRigs under this root.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Remove Jiggle Rigs",
                $"Remove {rigs.Length} JiggleRig component(s) under {root.name}?", "Remove", "Cancel"))
                return;
            int removed = 0;
            foreach (var r in rigs)
            {
                if (r == null) continue;
                Undo.DestroyObjectImmediate(r);
                removed++;
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("Remove Jiggle Rigs", $"Removed {removed} JiggleRig components.", "OK");
        }

        [MenuItem(MENU, true)]
        static bool Validate() => Selection.activeGameObject != null;

        [MenuItem(MENU, false, 20)]
        static void AddJiggleRigs()
        {
            var root = Selection.activeGameObject;
            if (root == null) return;

            // Index ALL transforms (don't first-match dedupe — collect all paths per name).
            var allByName = new Dictionary<string, List<Transform>>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!allByName.TryGetValue(t.name, out var list))
                {
                    list = new List<Transform>();
                    allByName[t.name] = list;
                }
                list.Add(t);
            }

            var jiggleDataField = typeof(JiggleRig).GetField("jiggleRigData",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var added = new List<string>();
            var skipped = new List<string>();
            var already = new List<string>();

            foreach (var (candidates, label) in CHAIN_ROOTS)
            {
                // For each candidate name, pick all matching transforms and prefer the one whose
                // path contains "Armature" (the body skeleton) over duplicates from baked clothing.
                List<Transform> matches = null;
                string matchedName = null;
                foreach (var name in candidates)
                {
                    if (allByName.TryGetValue(name, out matches) && matches.Count > 0)
                    {
                        matchedName = matches[0].name;
                        break;
                    }
                }
                if (matches == null || matches.Count == 0)
                {
                    skipped.Add($"{label} - none of [{string.Join(", ", candidates)}] found");
                    continue;
                }

                // Prefer the bone with the shortest path from root — VRChat clothing FBXes ship
                // with an internal copy of the avatar skeleton, giving duplicate-named bones at
                // deeper paths (e.g. /root/FH_FootWear/Armature/.../ear.l.1). Body's real bones
                // are at /root/Armature/.../ear.l.1 — one segment shorter.
                Transform bone = matches.OrderBy(t =>
                {
                    int depth = 0;
                    var p = t;
                    while (p != null) { depth++; p = p.parent; }
                    return depth;
                }).First();
                if (matches.Count > 1)
                {
                    var paths = string.Join(" ; ", matches.Select(PathOf));
                    Debug.LogWarning($"[BasicCranky JiggleSetup] {label}: {matches.Count} bones named '{matchedName}' found, picked {PathOf(bone)}. All candidates: {paths}");
                }

                if (bone.GetComponent<JiggleRig>() != null)
                {
                    already.Add($"{label} ({matchedName}) @ {PathOf(bone)}");
                    continue;
                }

                var rig = Undo.AddComponent<JiggleRig>(bone.gameObject);
                if (rig == null)
                {
                    skipped.Add($"{label} ({matchedName}) - Undo.AddComponent returned null");
                    continue;
                }

                JiggleRigData data;
                try { data = (JiggleRigData)jiggleDataField.GetValue(rig); }
                catch { data = JiggleRigData.Default(); }
                if (!data.hasSerializedData) data = JiggleRigData.Default();

                data.rootBone = bone;
                data.excludeRoot = false;
                data.hasSerializedData = true;
                jiggleDataField.SetValue(rig, data);

                EditorUtility.SetDirty(rig);
                if (PrefabUtility.IsPartOfPrefabInstance(rig))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(rig);

                added.Add($"{label} ({matchedName}) @ {PathOf(bone)}");
                Debug.Log($"[BasicCranky JiggleSetup] Added JiggleRig at {PathOf(bone)}");
            }

            if (added.Count > 0)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            var msg = $"Selected root: {root.name}\n\n";
            if (added.Count > 0)   msg += $"Added ({added.Count}):\n - " + string.Join("\n - ", added) + "\n\n";
            if (already.Count > 0) msg += $"Already had JiggleRig ({already.Count}):\n - " + string.Join("\n - ", already) + "\n\n";
            if (skipped.Count > 0) msg += $"Skipped ({skipped.Count}):\n - " + string.Join("\n - ", skipped) + "\n\n";
            EditorUtility.DisplayDialog("Add Jiggle Rigs", msg, "OK");
            Debug.Log("[BasicCranky JiggleSetup] " + msg.Replace("\n", " | "));
        }
    }
}
