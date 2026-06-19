<p align="center">
  <img src="docs/warning-ansi.svg" alt="ANSI warning: this code is more than 90% vibes" height="180">
  &nbsp;&nbsp;
  <img src="docs/warning-iso.svg" alt="ISO 7010 warning: AI-authored code, vibes 90% or higher" height="260">
</p>

How to use: 
- Download this repo, point your agent at `INITIAL_PROMPT_FOR_AGENT.md`
- Also provide:
  - A path to your existing project
  - A path to a fresh basis avatar project
- Good luck!

# BasicCranky

A small Unity Editor toolset for porting VRChat-style skinned clothing/prop meshes onto **BasisVR** avatars, plus one-click jiggle-rig wiring for furry chains (ears/tail/tongue/hair). Cranky little cousin of VRCFury's Armature Link — does the parts that aren't fancy: name-based bone match, bindpose rebake, world-space shift, no UI window beyond a few live sliders.

Self-contained Unity 6 package — depends only on Unity built-ins, with an optional dependency on [JigglePhysics by GatorDragonGames](https://github.com/naelstrof/UnityJigglePhysics) for the jiggle setup tool.

---

## Install

### As a UPM Git package (recommended)

In Unity: `Window ▸ Package Manager ▸ + ▸ Add package from Git URL...` and paste:

```
https://github.com/<your-user>/basic-cranky.git
```

The jiggle setup tool additionally needs JigglePhysics — install it the same way:

```
https://github.com/naelstrof/UnityJigglePhysics.git?path=Packages/com.gator-dragon-games.jigglephysics
```

If JigglePhysics isn't installed, the rest of BasicCranky still works; only `BasicCrankyJiggleSetup.cs` will fail to compile (delete that file or install JigglePhysics).

### As a manual drop-in

Copy this whole folder into `Assets/BasicCranky/` of any Unity 6 project.

### From source / for development

```bash
git clone https://github.com/<your-user>/basic-cranky.git
# In Unity Package Manager: + ▸ Add package from disk... ▸ pick the cloned folder's package.json
```

After install, menus appear under `GameObject ▸ Basic Cranky/...` when you right-click a GameObject in the Hierarchy.

---

## The porting workflow

The path for moving a VRChat outfit onto a BasisVR avatar:

1. **Import the clothing FBX into Unity.** Materials usually need to be rebuilt as URP/Lit from the PBR texture maps. Tint `_BaseColor` to match the original VRChat material if there was one.
2. **For Polycrow / Blender-exported clothing FBXes specifically — leave "Bake Axis Conversion" OFF** in the Model tab. See "Known issues" below for why.
3. Drag the clothing FBX into your scene as a **child of the avatar root**.
4. Right-click the clothing GameObject → `Basic Cranky ▸ Link Clothing (Full Pipeline)`.
   - One-shot: bone-name match (excluding the clothing's own internal armature copy), orphan adoption, wrapper-position-zero, optional bindpose rebake + shift-to-feet (silent no-ops if Unity returns broken vertex data — see Known issues).
5. Assign your URP/Lit material to the resulting SMR's Materials slot.
6. **Furry avatar?** Right-click the avatar root → `Add Jiggle Rigs (Ears + Tail + Tongue + Hair)`.
7. **Shoes for a fluffy-pawed avatar?** Right-click the avatar root → `Set Leg-Fluff Blendshapes to 100 (for shoes)` to shrink the paws so shoes can wrap around them.
8. **Shoes are forward/down of the paws?** Right-click the shoe SMR → `Shift SMR World-Space (open dialog)` — live sliders to tune the offset, then bake.
9. **Backwards clothing?** Right-click the SMR → `Flip Mesh 180° Y (fix backwards clothing)`.
10. Anything weird → `Diagnose SMR Bones`.

---

## Menu reference

All commands live under `GameObject ▸ Basic Cranky/` (right-click target). Lower priority = higher in the menu = earlier in the typical workflow.

| # | Menu | Select | What it does | Output |
|---|---|---|---|---|
| 0 | **Link Clothing (Full Pipeline)** | Clothing root (child of avatar) | Name-matches every SMR's `bones[]` to the avatar's transforms (skipping descendants of the clothing wrapper itself so duplicate bone copies don't poison the lookup). Adopts unmatched bones under nearest matched ancestor. Zeroes the wrapper's *position* (keeps rotation/scale — those carry the Blender→Unity axis conversion). Optionally rebakes bindposes + shifts to foot midpoint. | Mutates scene + may write `*_Linked.asset` |
| 1 | **Flip Mesh 180° Y (fix backwards clothing)** | A `SkinnedMeshRenderer` | Rotates every vertex 180° around the Y axis about the mesh's bounds center. For symmetric items (shoes) this swaps L/R and forward/back. Caveat: works best for accessories authored facing the opposite avatar direction. | New `*_FlippedY.asset` |
| 10 | **Diagnose SMR Bones** | Any GameObject with SMRs underneath | Walks every SMR, reports: bone count, null/inside-root/outside-root counts, rootBone, mesh stats, bindpose translation magnitudes, mesh bounds. | Text report (Console + dialog) |
| 20 | **Add Jiggle Rigs (Ears + Tail + Tongue + Hair)** | Avatar root | For each chain (ear.l, ear.r, tail, tongue, hair), tries name candidates, picks the candidate with the *shortest hierarchy depth* (avoids duplicates from clothing FBXes), adds a `JiggleRig` component with `rootBone` set. Uses `PrefabUtility.RecordPrefabInstancePropertyModifications` so the override sticks. | `JiggleRig` components |
| 21 | **Diagnose Jiggle Chain Bones** | Avatar root | Walks the hierarchy, lists every transform matching a candidate name with its full path and whether it has a JiggleRig already. | Text report |
| 22 | **Remove ALL Jiggle Rigs Under Selection** | Any root | Confirms, then deletes every `JiggleRig` component anywhere underneath. | Removes components |
| 30 | **Set Leg-Fluff Blendshapes to 100 (for shoes)** | Avatar root | Walks every SMR, finds blendshapes named `FlattenBodyFluffLegs` (and common variants like `…LegsP`, `…LegsS`, `…LegsMLV`) and sets them to 100. Shrinks the paws so VRChat shoes — which are sized for a normal foot — can fit around them. | Sets blendshape weights |
| 40 | **Shift SMR World-Space (open dialog)** | A `SkinnedMeshRenderer` | Opens a small window with X / Y / Z sliders. Each change reverts the mesh to its original bindposes and reapplies the total offset via `new_bindpose[i] = bone.invWorld × T(offset) × bone.world × old_bindpose[i]`. The math gives a uniform world-space shift to every vertex regardless of which bone weights it — works even when Unity's broken `Mesh.vertices` API returns zeros, because we never read vertex_local. Default offset is `(0, 0.089, -0.064)`, tuned for Polycrow `FH_FootWear` on Freakhound. | New `*_Shifted.asset` per apply |

---

## Output convention

All generated meshes land in **`Assets/_UserContent/Generated/`** (created on first use). Names are derived from the source mesh + a suffix (`_Linked`, `_FlippedY`, `_Shifted`). Names are uniquified — re-running doesn't overwrite. Clean the folder occasionally if you iterate a lot.

The jiggle and blendshape tools don't write any assets; they only modify components or weights on existing GameObjects.

---

## Known issues, gotchas, and lessons earned the hard way

### **Unity 6 `Mesh.vertices` returns all zeros on Polycrow FBXes when "Bake Axis Conversion" is on**

This was the multi-hour villain. Polycrow's Blender export gives the FBX an Armature node with `Lcl Scaling: 100x` + `Lcl Rotation: -90 X` — a standard Blender→Unity unit/axis pattern. Unity 6's "Bake Axis Conversion" tries to absorb these into mesh data and somewhere in the math zeros every vertex position. `Mesh.vertices`, `Mesh.GetVertices(list)`, and the underlying GPU buffer all return `(0, 0, 0)`. The mesh *still renders correctly in the scene* because Unity has parallel internal data paths, but every script-level access is broken.

**The fix is to disable Bake Axis Conversion on the FBX.** The wrapper Transform then carries the `-89.98° X` rotation and `100x` scale at runtime, which `Link Clothing` handles by zeroing *only the wrapper's position* (keeping rotation+scale).

Verified by writing a Python FBX parser ([documented in /docs/fbx-format-notes.md](docs/fbx-format-notes.md) — TODO) and reading the actual binary: the FBX has 2768 real vertex positions spanning 44 × 27 × 34 cm. Unity 2022 (VRChat's version) reads them fine. Unity 6 with Bake Axis Conversion does not.

### **Shoes (or other footwear) sit below/forward of the avatar's paws**

Polycrow shoes (and any clothing authored for the *bare bone* foot position) end up below the chunky furry paws because the paws are fluff geometry that extends well beyond the foot bones. Two combined fixes:

1. Run **Set Leg-Fluff Blendshapes to 100**. Shrinks the paws to the bare-foot size the shoes were designed for. Looks for `FlattenBodyFluffLegs` and variants — if your avatar uses a different name (`HideFootFluff`, `ShrinkPaws`, etc.) edit `CANDIDATES` in `BasicCrankyFlattenLegs.cs`.
2. Run **Shift SMR World-Space**. Live sliders for the residual offset. For Polycrow `FH_FootWear` on Freakhound the values are `(0, 0.089, -0.064)` — 8.9 cm up, 6.4 cm back — that's the default the window opens with.

### **"I moved the SMR / wrapper Transform but nothing changed visually"**

That's correct behavior in Unity 6. After `Link Clothing` remaps `bones[]` to the avatar's bones, the SkinnedMeshRenderer's rendered position comes entirely from `bones[i].localToWorld × bindpose[i] × vertex_local`. Neither the SMR's own Transform nor its parent wrapper's Transform contributes. To shift the mesh, use **Shift SMR World-Space** (priority 40) — it modifies bindposes, which *do* drive rendering.

### **VRChat clothing FBXes ship with a full copy of the avatar skeleton**

Polycrow / Markcreator FBXes typically embed the entire avatar armature so the clothing previews correctly in the VRChat SDK. This produces duplicate bone names — e.g. both `/avatar/Armature/.../ear.l.1` AND `/avatar/FH_FootWear/Armature/.../ear.l.1` exist. Naive name-lookup picks whichever appears first in the hierarchy traversal (usually the clothing's copy, because clothing wrappers tend to be the first child of the avatar root).

`Link Clothing` skips descendants of the clothing being linked when building the bone lookup. `Add Jiggle Rigs` prefers the candidate with the **shortest hierarchy depth** — body bones are one segment shorter than clothing-baked copies. If you still get the wrong pick, run `Diagnose Jiggle Chain Bones` to see all candidates.

### **Jiggle rigs survive Play mode + scene reloads only with `PrefabUtility.RecordPrefabInstancePropertyModifications`**

The first version of the jiggle setup added components but they disappeared on next scene load because they weren't recorded as prefab overrides. The current version records the override and marks the scene dirty.

### **Mesh collapses to (0,0,0) extent after rebake**

Two known causes:
- Same Unity 6 vertex-API bug as above — the rebake math reads `Mesh.vertices` (zero) and writes back zero. Solution: disable Bake Axis Conversion on the source FBX.
- Source FBX has unusual bindpose / vertex coord conventions and the standard skinning formula doesn't decode. Re-rig in Blender against the target avatar's rest pose.

The `Diagnose SMR Bones` menu reports `oldMesh.vertexCount`, `isReadable`, `bounds`, and `bindpose translation magnitudes` — if `vertexCount > 0` but `bounds.extents = (0,0,0)`, you're hitting the Unity API bug.

### **Bones exist in clothing but not on the avatar**

`Link Clothing` adopts them under their nearest name-matched ancestor (e.g. `toes.1.l` gets adopted under the avatar's `toes.l`). Verify in the Hierarchy afterwards.

### **Avatar has duplicate bone names**

Link uses first-write-wins on dupes and warns in the Console. The exclusion of the clothing's own descendants from the lookup usually handles the common case; if the wrong instance still got picked, rename one in the avatar before running Link.

---

## Repo layout

```
basic-cranky/
├── README.md
├── INITIAL_PROMPT_FOR_AGENT.md
├── package.json                         UPM manifest (com.an.basiccranky)
├── .gitignore
├── docs/                                warning SVGs
└── Editor/
    ├── BasicCranky.Editor.asmdef        editor-only assembly (refs JigglePhysics by GUID)
    ├── BasicCrankyShared.cs             menu root constant
    ├── BasicCrankyClothingLink.cs       full clothing port pipeline (priority 0)
    ├── BasicCrankyFlipMesh.cs           180° Y flip (1)
    ├── BasicCrankyDiagnose.cs           SMR/bone state report (10)
    ├── BasicCrankyJiggleSetup.cs        Add / Diagnose / Remove JiggleRigs (20-22)
    ├── BasicCrankyFlattenLegs.cs        Leg-fluff blendshape activator (30)
    └── BasicCrankyShiftShoes.cs         World-space shift via bindposes (40)
```

Each tool lives in its own static class, namespace `BasicCranky`, no cross-file dependencies. Add a new tool by dropping a `BasicCrankyXyz.cs` file with its own static class and a `[MenuItem]` attribute.

---

## License

Unlicensed for now. Treat as "personal tool, use at your own risk." Drop in a license of your choice when publishing.
