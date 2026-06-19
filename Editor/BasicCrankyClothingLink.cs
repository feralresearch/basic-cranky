using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BasicCranky
{
    // Single-step clothing port. Replaces the priority 0/1/2/3/11 ad-hoc menus.
    // Pipeline:
    //   1. Build avatar-bone lookup, EXCLUDING descendants of the clothing being linked
    //      (VRChat clothing FBXes ship with an internal copy of the avatar skeleton).
    //   2. For each SMR under the clothing, remap bones[] to body bones by name.
    //   3. Adopt orphan bones (clothing-only joints like toes.1.l) under their nearest
    //      matched ancestor in the avatar armature.
    //   4. Rebake bindposes so they match the avatar's current rest pose -- this removes
    //      the spike-trail distortion you get when source and target rest poses disagree.
    //   5. Shift vertices uniformly so the mesh's bounds center lands on the midpoint of
    //      foot.l + foot.r (or rootBone, or SMR origin as fallbacks). Mesh ends up on the
    //      avatar's feet, cleanly skinned, no math gymnastics required from the user.
    //   6. Move SMR transforms under the avatar root, delete the orphan clothing wrapper.
    //   7. Detailed Debug.Log of every step so a busted run is debuggable from the Console.
    public static class BasicCrankyClothingLink
    {
        const string MENU = BasicCrankyShared.MenuRoot + "Link Clothing (Full Pipeline)";

        [MenuItem(MENU, true)]
        static bool Validate()
        {
            var go = Selection.activeGameObject;
            return go != null && go.transform.parent != null;
        }

        [MenuItem(MENU, false, 0)]
        static void Run()
        {
            var clothing = Selection.activeGameObject;
            if (clothing == null) return;
            if (clothing.transform.parent == null)
            {
                EditorUtility.DisplayDialog("Link Clothing",
                    "Drag the clothing GameObject to be a CHILD of the avatar root first.", "OK");
                return;
            }
            var avatarRoot = clothing.transform.parent.gameObject;
            LinkClothing(clothing, avatarRoot);
        }

        static string Path(Transform t)
        {
            var sb = new System.Text.StringBuilder();
            while (t != null) { sb.Insert(0, "/" + t.name); t = t.parent; }
            return sb.ToString();
        }

        public static void LinkClothing(GameObject clothingRoot, GameObject avatarRoot)
        {
            Debug.Log($"[BasicCranky LinkClothing] === start ===  clothing={Path(clothingRoot.transform)}  avatar={Path(avatarRoot.transform)}");

            // ---- Step 1: Index avatar bones, EXCLUDING the clothing's own descendants ----
            var clothingTransform = clothingRoot.transform;
            var avatarBones = new Dictionary<string, Transform>();
            int skippedFromClothing = 0;
            foreach (var t in avatarRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.IsChildOf(clothingTransform)) { skippedFromClothing++; continue; }
                if (!avatarBones.ContainsKey(t.name)) avatarBones[t.name] = t;
            }
            Debug.Log($"[BasicCranky LinkClothing] Step 1: indexed {avatarBones.Count} avatar bones (skipped {skippedFromClothing} inside clothing wrapper)");

            // ---- Step 2: Find SMRs ----
            var skinned = clothingRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinned.Length == 0)
            {
                EditorUtility.DisplayDialog("Link Clothing", "No SkinnedMeshRenderers under " + clothingRoot.name, "OK");
                return;
            }
            Debug.Log($"[BasicCranky LinkClothing] Step 2: found {skinned.Length} SMR(s)");

            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Basic Cranky Link Clothing");

            int totalRemapped = 0;
            int totalUnmatched = 0;
            int totalAdopted = 0;
            var unmatchedRefs = new HashSet<Transform>();
            // Cache the original shoe bones[] per SMR BEFORE remapping. We need them later for the
            // shape-recovery math: vertex shape is encoded as (shoe_bone.world * bindpose * vp) and
            // only the SHOE's bones (at their FBX rest positions) reproduce the authored shape.
            // After remap, smr.bones[] points at avatar bones, which would give a different shape.
            var originalBonesPerSmr = new Dictionary<SkinnedMeshRenderer, Transform[]>();
            foreach (var smr in skinned)
            {
                var copy = (Transform[])smr.bones.Clone();
                originalBonesPerSmr[smr] = copy;
                Debug.Log($"[BasicCranky LinkClothing] cached {copy.Length} original bones for {smr.name} (first non-null: {copy.FirstOrDefault(b => b != null)?.name ?? "(none)"})");
            }

            // ---- Step 3: Remap bones[] for each SMR ----
            foreach (var smr in skinned)
            {
                Undo.RecordObject(smr, "Link Clothing");
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
                        newBones[i] = ob;
                        unmatchedRefs.Add(ob);
                        totalUnmatched++;
                    }
                }
                smr.bones = newBones;
                if (smr.rootBone != null && avatarBones.TryGetValue(smr.rootBone.name, out var newRoot))
                    smr.rootBone = newRoot;
            }
            Debug.Log($"[BasicCranky LinkClothing] Step 3: {totalRemapped} bone refs remapped, {totalUnmatched} unmatched (will try to adopt)");

            // ---- Step 4: Adopt orphan bones under their nearest matched ancestor ----
            bool madeProgress;
            int passes = 0;
            do
            {
                madeProgress = false;
                passes++;
                foreach (var orphan in unmatchedRefs.ToList())
                {
                    if (orphan == null) { unmatchedRefs.Remove(orphan); continue; }
                    if (orphan.IsChildOf(avatarRoot.transform) && !orphan.IsChildOf(clothingTransform))
                    { unmatchedRefs.Remove(orphan); continue; }
                    if (orphan.parent == null) continue;
                    if (avatarBones.TryGetValue(orphan.parent.name, out var avParent)
                        && avParent.IsChildOf(avatarRoot.transform))
                    {
                        Undo.SetTransformParent(orphan, avParent, "Adopt orphan bone");
                        avatarBones[orphan.name] = orphan;
                        unmatchedRefs.Remove(orphan);
                        totalAdopted++;
                        madeProgress = true;
                    }
                }
            } while (madeProgress && passes < 16);
            Debug.Log($"[BasicCranky LinkClothing] Step 4: adopted {totalAdopted} orphan bone(s), {unmatchedRefs.Count} truly unmatched after {passes} passes");

            // ---- Step 4.5: Zero only the wrapper's POSITION ----
            // We move the wrapper to the avatar root's origin so the bones inside align with the
            // avatar's bones. But we MUST keep the wrapper's rotation and scale: those carry the
            // FBX's Blender→Unity axis conversion (`-89.98° X` rotation + `100x` scale typical
            // for Polycrow exports). Zeroing those breaks the orientation/scaling and shoes end
            // up tilted 90° / sized wrong.
            Undo.RecordObject(clothingRoot.transform, "Zero clothing wrapper position");
            var preWrapperPos = clothingRoot.transform.localPosition;
            var preWrapperRot = clothingRoot.transform.localRotation;
            var preWrapperScl = clothingRoot.transform.localScale;
            clothingRoot.transform.localPosition = Vector3.zero;
            // Intentionally DO NOT zero rotation/scale.
            Debug.Log($"[BasicCranky LinkClothing] Step 4.5: zeroed wrapper {clothingRoot.name} position (was {preWrapperPos}). Kept rotation={preWrapperRot.eulerAngles} scale={preWrapperScl}");
            // Also try to reparent each SMR to avatar root; Undo.SetTransformParent may be
            // silently ignored on prefab instances, but if it works it's a nice cleanup.
            foreach (var smr in skinned)
            {
                var preParent = smr.transform.parent;
                Undo.SetTransformParent(smr.transform, avatarRoot.transform, "Reparent SMR");
                var postParent = smr.transform.parent;
                Debug.Log($"[BasicCranky LinkClothing] Step 4.5: SMR {smr.name} parent: {(preParent ? preParent.name : "null")} -> {(postParent ? postParent.name : "null")}");
                if (postParent == avatarRoot.transform)
                {
                    smr.transform.localPosition = Vector3.zero;
                    smr.transform.localRotation = Quaternion.identity;
                    smr.transform.localScale = Vector3.one;
                }
                Debug.Log($"[BasicCranky LinkClothing] Step 4.5: SMR {smr.name} world transform = pos {smr.transform.position} rot {smr.transform.rotation.eulerAngles}");
            }

            // ---- Step 5: Rebake bindposes + shift vertices to foot midpoint ----
            // Find foot anchors in the avatar (body bones only, since we excluded clothing).
            Transform footL = null, footR = null;
            foreach (var kv in avatarBones)
            {
                var n = kv.Key.ToLowerInvariant();
                if (n == "foot.l" || n == "leftfoot" || n == "foot_l") footL = kv.Value;
                else if (n == "foot.r" || n == "rightfoot" || n == "foot_r") footR = kv.Value;
            }
            Vector3 anchorWorld;
            string anchorDesc;
            if (footL != null && footR != null)
            {
                anchorWorld = (footL.position + footR.position) * 0.5f;
                anchorDesc = $"midpoint of {footL.name} ({footL.position}) + {footR.name} ({footR.position})";
            }
            else if (skinned[0].rootBone != null)
            {
                anchorWorld = skinned[0].rootBone.position;
                anchorDesc = $"rootBone {skinned[0].rootBone.name} ({anchorWorld})";
            }
            else
            {
                anchorWorld = avatarRoot.transform.position;
                anchorDesc = $"avatar root ({anchorWorld})";
            }
            Debug.Log($"[BasicCranky LinkClothing] Step 5: anchor = {anchorDesc}");

            foreach (var smr in skinned)
            {
                var oldMesh = smr.sharedMesh;
                if (oldMesh == null)
                {
                    Debug.LogWarning($"[BasicCranky LinkClothing] {smr.name} has no mesh, skipping rebake");
                    continue;
                }

                var bones = smr.bones;
                var oldBindPoses = oldMesh.bindposes;
                if (oldBindPoses.Length != bones.Length)
                {
                    Debug.LogWarning($"[BasicCranky LinkClothing] {smr.name}: bone count ({bones.Length}) != bindposes ({oldBindPoses.Length}), skipping rebake");
                    continue;
                }

                var smrLtoW = smr.transform.localToWorldMatrix;
                var smrWtoL = smr.transform.worldToLocalMatrix;
                Debug.Log($"[BasicCranky LinkClothing] {smr.name}: smr.world pos={smr.transform.position}, smrLtoW translation={smrLtoW.GetColumn(3)}, will rebake against this");

                // Diagnostic: probe the mesh data via multiple APIs.
                Debug.Log($"[BasicCranky LinkClothing] {smr.name}: oldMesh asset path = {AssetDatabase.GetAssetPath(oldMesh)}");
                Debug.Log($"[BasicCranky LinkClothing] {smr.name}: oldMesh.vertexCount = {oldMesh.vertexCount}, isReadable = {oldMesh.isReadable}, bounds = {oldMesh.bounds}");
                if (oldMesh.vertexCount > 0)
                {
                    // Probe a single vertex via the indexed accessor.
                    var verts = oldMesh.vertices;
                    Debug.Log($"[BasicCranky LinkClothing] {smr.name}: verts.Length = {verts.Length}, verts[0] = {verts[0]}, verts[last] = {verts[verts.Length-1]}, verts[middle] = {verts[verts.Length/2]}");
                    // Also try the List<Vector3> overload.
                    var vlist = new System.Collections.Generic.List<Vector3>();
                    oldMesh.GetVertices(vlist);
                    Debug.Log($"[BasicCranky LinkClothing] {smr.name}: GetVertices list count = {vlist.Count}, list[0] = {(vlist.Count > 0 ? vlist[0].ToString() : "empty")}");

                    // Blendshape probe: shape data may live in a blendshape applied at 100.
                    int bsCount = oldMesh.blendShapeCount;
                    Debug.Log($"[BasicCranky LinkClothing] {smr.name}: blendShapeCount = {bsCount}");
                    for (int b = 0; b < bsCount; b++)
                    {
                        var bsName = oldMesh.GetBlendShapeName(b);
                        int frameCount = oldMesh.GetBlendShapeFrameCount(b);
                        var smrWeight = smr.GetBlendShapeWeight(b);
                        // Sample blendshape frame 0 deltas at vert 0 and vert middle
                        if (frameCount > 0)
                        {
                            var dv = new Vector3[oldMesh.vertexCount];
                            var dn = new Vector3[oldMesh.vertexCount];
                            var dt = new Vector3[oldMesh.vertexCount];
                            oldMesh.GetBlendShapeFrameVertices(b, frameCount - 1, dv, dn, dt);
                            var minD = dv[0]; var maxD = dv[0];
                            for (int i = 1; i < dv.Length; i++) { minD = Vector3.Min(minD, dv[i]); maxD = Vector3.Max(maxD, dv[i]); }
                            Debug.Log($"[BasicCranky LinkClothing] {smr.name}: blendshape[{b}] '{bsName}' weight={smrWeight}, frame[{frameCount-1}] delta range {minD}..{maxD} (extent {(maxD-minD)*0.5f})");
                        }
                        else
                        {
                            Debug.Log($"[BasicCranky LinkClothing] {smr.name}: blendshape[{b}] '{bsName}' weight={smrWeight}, no frames");
                        }
                    }
                }

                // (5a) Rebake bindposes: snap each one to the avatar bone's current rest pose.
                // With this, vertex_world = bone.world * bone.invWorld * smr.world * vertex_local
                //                        = smr.world * vertex_local.
                // i.e. vertex appears at vertex_local in SMR-local space, regardless of bone rotation.
                // No distortion across multi-bone weights.
                var newBindPoses = new Matrix4x4[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    newBindPoses[i] = bones[i] != null
                        ? bones[i].worldToLocalMatrix * smrLtoW
                        : oldBindPoses[i];
                }

                // (5b) Compute each vertex's authored world position via the FBX preview skinning:
                //   vertex_world = sum(w_i * SHOE_bone[i].localToWorld * bindpose[i] * (0,0,0,1))
                // The SHOE's bones (at their FBX rest positions, BEFORE remap to avatar bones) are
                // what encode the authored shape. Using avatar bones here would collapse the result
                // to the avatar's bone cluster because Polycrow authored shoe bones with shoe-shaped
                // spatial spread that the avatar's compact rest pose doesn't replicate.
                Transform[] shoeBones = originalBonesPerSmr[smr];
                int validShoeBones = 0;
                foreach (var sb in shoeBones) if (sb != null) validShoeBones++;
                Debug.Log($"[BasicCranky LinkClothing] {smr.name}: using {validShoeBones} ORIGINAL shoe bones for shape recovery (vs {bones.Length} remapped bones)");

                var oldVerts = oldMesh.vertices;
                var weights = oldMesh.boneWeights;
                var newVerts = new Vector3[oldVerts.Length];
                int zeroWeightVerts = 0;
                for (int v = 0; v < oldVerts.Length; v++)
                {
                    var bw = weights[v];
                    var vp = new Vector4(oldVerts[v].x, oldVerts[v].y, oldVerts[v].z, 1f);
                    Vector3 worldTarget = Vector3.zero;
                    float weightSum = 0f;
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
                        if (w <= 0f || idx < 0 || idx >= shoeBones.Length || shoeBones[idx] == null) continue;
                        Vector4 boneLocal = oldBindPoses[idx] * vp;
                        Vector4 wpos = shoeBones[idx].localToWorldMatrix * boneLocal;
                        worldTarget += w * new Vector3(wpos.x, wpos.y, wpos.z);
                        weightSum += w;
                    }
                    if (weightSum < 0.001f) zeroWeightVerts++;
                    newVerts[v] = smrWtoL.MultiplyPoint3x4(worldTarget);
                }
                Debug.Log($"[BasicCranky LinkClothing] {smr.name}: zero-weight verts {zeroWeightVerts}/{newVerts.Length}");

                // Bounds of the new (now-shape-carrying) vertex array.
                var nMin = newVerts[0]; var nMax = newVerts[0];
                for (int v = 1; v < newVerts.Length; v++) { nMin = Vector3.Min(nMin, newVerts[v]); nMax = Vector3.Max(nMax, newVerts[v]); }
                var meshCenterLocal = (nMin + nMax) * 0.5f;
                var meshExtentLocal = (nMax - nMin) * 0.5f;
                Debug.Log($"[BasicCranky LinkClothing] {smr.name}: post-skin vertex range {nMin}..{nMax}, center={meshCenterLocal}, extent={meshExtentLocal}");

                // (5c) Shift uniformly so the mesh's bounds center lands on the anchor.
                var anchorLocal = smrWtoL.MultiplyPoint3x4(anchorWorld);
                var shift = anchorLocal - meshCenterLocal;
                Debug.Log($"[BasicCranky LinkClothing] {smr.name}: anchor (local)={anchorLocal}, shift={shift}");
                for (int v = 0; v < newVerts.Length; v++) newVerts[v] += shift;

                // (5d) Persist as a new mesh asset.
                var newMesh = Object.Instantiate(oldMesh);
                newMesh.name = oldMesh.name + "_Linked";
                newMesh.vertices = newVerts;
                newMesh.bindposes = newBindPoses;
                newMesh.RecalculateNormals();
                newMesh.RecalculateTangents();
                newMesh.RecalculateBounds();

                const string gen = "Assets/_UserContent/Generated";
                if (!System.IO.Directory.Exists(gen)) System.IO.Directory.CreateDirectory(gen);
                var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{gen}/{newMesh.name}.asset");
                AssetDatabase.CreateAsset(newMesh, assetPath);
                EditorUtility.SetDirty(newMesh);

                smr.sharedMesh = newMesh;
                Debug.Log($"[BasicCranky LinkClothing] {smr.name}: rebaked + shifted, new mesh at {assetPath}, bounds extent={newMesh.bounds.extents}, center={newMesh.bounds.center}");
            }

            AssetDatabase.SaveAssets();

            // ---- Step 6: Clean up wrapper (SMRs were already moved in Step 4.5) ----
            if (clothingRoot != null
                && clothingRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0)
            {
                Debug.Log($"[BasicCranky LinkClothing] Step 6: deleting empty wrapper {clothingRoot.name}");
                Undo.DestroyObjectImmediate(clothingRoot);
            }
            else
            {
                Debug.Log($"[BasicCranky LinkClothing] Step 6: wrapper retained (still has SMRs or bone refs)");
            }

            Undo.CollapseUndoOperations(undoGroup);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            var summary = $"Done.\n\nRemapped: {totalRemapped}\nAdopted: {totalAdopted}\nUnmatched (kept original ref): {unmatchedRefs.Count}\nAnchor: {anchorDesc}";
            EditorUtility.DisplayDialog("Link Clothing", summary, "OK");
            Debug.Log("[BasicCranky LinkClothing] === done === " + summary.Replace("\n", " | "));
        }
    }
}
