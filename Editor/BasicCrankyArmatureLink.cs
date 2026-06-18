using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BasicCranky
{
    public static class BasicCrankyArmatureLink
    {
        const string MENU = BasicCrankyShared.MenuRoot + "Link Armature to Parent Avatar";
        const string LINK_AND_BAKE_MENU = BasicCrankyShared.MenuRoot + "Link + Full Rebake (One Step)";

        [MenuItem(MENU, true)]
        static bool Validate()
        {
            var go = Selection.activeGameObject;
            return go != null && go.transform.parent != null;
        }

        [MenuItem(MENU, false, 0)]
        static void LinkSelected()
        {
            var clothing = Selection.activeGameObject;
            if (clothing == null) return;
            if (clothing.transform.parent == null)
            {
                EditorUtility.DisplayDialog("Armature Link",
                    "Drag the clothing GameObject to be a CHILD of the avatar root first, then run this on the clothing.",
                    "OK");
                return;
            }
            Link(clothing, clothing.transform.parent.gameObject);
        }

        [MenuItem(LINK_AND_BAKE_MENU, true)]
        static bool ValidateLinkAndBake()
        {
            var go = Selection.activeGameObject;
            return go != null && go.transform.parent != null;
        }

        [MenuItem(LINK_AND_BAKE_MENU, false, 11)]
        static void LinkAndBakeSelected()
        {
            var clothing = Selection.activeGameObject;
            if (clothing == null || clothing.transform.parent == null) return;
            var avatarRoot = clothing.transform.parent.gameObject;
            var smrsBefore = clothing.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToList();
            Link(clothing, avatarRoot);
            // After Link, SMRs may have moved to avatarRoot. Find them again.
            foreach (var smr in smrsBefore)
            {
                if (smr == null) continue;
                Selection.activeGameObject = smr.gameObject;
                BasicCrankyFullRebake.FullBakeSelected();
            }
        }

        public static void Link(GameObject clothingRoot, GameObject avatarRoot)
        {
            // Index every transform under the avatar by name. First-write-wins on dupes.
            var avatarBones = new Dictionary<string, Transform>();
            var duplicateNames = new HashSet<string>();
            foreach (var t in avatarRoot.GetComponentsInChildren<Transform>(true))
            {
                if (avatarBones.ContainsKey(t.name)) duplicateNames.Add(t.name);
                else avatarBones[t.name] = t;
            }

            var skinned = clothingRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinned.Length == 0)
            {
                EditorUtility.DisplayDialog("Armature Link",
                    "No SkinnedMeshRenderers found under " + clothingRoot.name + ".",
                    "OK");
                return;
            }

            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Basic Cranky Armature Link");

            int totalRemapped = 0;
            int totalAdopted = 0;
            var unmatchedRefs = new HashSet<Transform>();
            var missingNames = new HashSet<string>();

            // Pass 1: name-based remap of every SMR's bones[] array.
            foreach (var smr in skinned)
            {
                Undo.RecordObject(smr, "Armature Link");

                var oldBones = smr.bones;
                var newBones = new Transform[oldBones.Length];
                for (int i = 0; i < oldBones.Length; i++)
                {
                    var ob = oldBones[i];
                    if (ob == null) { newBones[i] = null; continue; }

                    if (avatarBones.TryGetValue(ob.name, out var match))
                    {
                        newBones[i] = match;
                        totalRemapped++;
                    }
                    else
                    {
                        newBones[i] = ob; // keep original reference; we'll try to adopt it next
                        unmatchedRefs.Add(ob);
                        missingNames.Add(ob.name);
                    }
                }
                smr.bones = newBones;

                if (smr.rootBone != null && avatarBones.TryGetValue(smr.rootBone.name, out var newRoot))
                    smr.rootBone = newRoot;
            }

            // Pass 2: adopt orphan bones into the avatar armature under their nearest matched ancestor.
            // Loop until no progress — handles parent-before-child correctly.
            bool madeProgress;
            int adoptionPasses = 0;
            do
            {
                madeProgress = false;
                adoptionPasses++;
                foreach (var orphan in unmatchedRefs.ToList())
                {
                    if (orphan == null) { unmatchedRefs.Remove(orphan); continue; }
                    if (orphan.IsChildOf(avatarRoot.transform)) { unmatchedRefs.Remove(orphan); continue; }
                    if (orphan.parent == null) continue;

                    if (avatarBones.TryGetValue(orphan.parent.name, out var avParent)
                        && avParent.IsChildOf(avatarRoot.transform))
                    {
                        Undo.SetTransformParent(orphan, avParent, "Adopt clothing bone");
                        // Preserve the orphan's local rest pose — the clothing armature was authored
                        // against this skeleton's rest pose, so local pos/rot are correct.
                        avatarBones[orphan.name] = orphan;
                        unmatchedRefs.Remove(orphan);
                        totalAdopted++;
                        madeProgress = true;
                    }
                }
            } while (madeProgress && adoptionPasses < 16);

            // Move each SMR's GameObject directly under the avatar root.
            foreach (var smr in skinned)
            {
                Undo.SetTransformParent(smr.transform, avatarRoot.transform, "Armature Link");
                smr.transform.localPosition = Vector3.zero;
                smr.transform.localRotation = Quaternion.identity;
                smr.transform.localScale = Vector3.one;
            }

            // Delete clothing root if it has no SMRs left and no bones still referenced by our SMRs.
            if (clothingRoot != null)
            {
                bool hasSmrInside = clothingRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0;
                bool stillReferenced = false;
                if (!hasSmrInside)
                {
                    var insideTransforms = new HashSet<Transform>(clothingRoot.GetComponentsInChildren<Transform>(true));
                    foreach (var smr in skinned)
                    {
                        if (smr.bones.Any(b => b != null && insideTransforms.Contains(b)))
                        {
                            stillReferenced = true;
                            break;
                        }
                    }
                }
                if (!hasSmrInside && !stillReferenced)
                    Undo.DestroyObjectImmediate(clothingRoot);
                else if (stillReferenced)
                    Debug.LogWarning("[BasicCranky] Clothing root retained — some bones still referenced and couldn't be adopted (no ancestor match in avatar).");
            }

            Undo.CollapseUndoOperations(undoGroup);

            // Report
            var stillMissing = missingNames.Where(n => !avatarBones.ContainsKey(n)).ToList();
            var msg = $"Remapped {totalRemapped} bone reference(s) across {skinned.Length} mesh(es).";
            if (totalAdopted > 0) msg += $"\nAdopted {totalAdopted} extra clothing bone(s) into the avatar armature.";
            if (stillMissing.Count > 0)
            {
                var preview = stillMissing.Take(15).ToList();
                msg += $"\n\n{stillMissing.Count} bone(s) had no name match and no ancestor match either:\n - "
                       + string.Join("\n - ", preview);
                if (stillMissing.Count > preview.Count) msg += $"\n ...and {stillMissing.Count - preview.Count} more.";
                Debug.LogWarning("[BasicCranky] Truly unmatched bones: " + string.Join(", ", stillMissing));
            }
            if (duplicateNames.Count > 0)
                Debug.LogWarning("[BasicCranky] Avatar has duplicate bone names (first match used): " + string.Join(", ", duplicateNames));

            EditorUtility.DisplayDialog("Armature Link", msg, "OK");
        }
    }
}
