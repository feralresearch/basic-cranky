# Initial Prompt for an Agent Working on `basic-cranky`

You are picking up the **basic-cranky** Unity Editor toolset. This document is the briefing — read it before you write any code, run any menu commands, or answer questions about porting VRChat avatars to BasisVR.

## What this repo is

A small, self-contained Unity package: a name-based armature linker, bind-pose rebaker, and jiggle-rig setup tool used to port VRChat-style skinned clothing and props onto **BasisVR** avatars. Named after VRCFury's "Armature Link" but deliberately cranky — it does the unglamorous parts (name-match bones, rebake bindposes, drop in JiggleRigs) and skips the per-source rest-pose-delta magic VRCFury carries. The rest of this document is the playbook the tool was built against.

Layout:

```
basic-cranky/
├── README.md                            user-facing docs (install + menus)
├── INITIAL_PROMPT_FOR_AGENT.md          this file
├── package.json                         UPM manifest (com.an.basiccranky)
├── .gitignore
└── Editor/
    ├── BasicCranky.Editor.asmdef        editor-only, refs JigglePhysics by GUID
    ├── BasicCrankyShared.cs             menu root + GeneratedDir constants
    ├── BasicCrankyArmatureLink.cs       Link + Link+Bake (one step)
    ├── BasicCrankyRebake.cs             Bind poses only
    ├── BasicCrankyFullRebake.cs         Vertices + bind poses
    ├── BasicCrankyShiftToFeet.cs        Full rebake + foot anchor
    ├── BasicCrankyDiagnose.cs           SMR / bone report
    └── BasicCrankyJiggleSetup.cs        Add / Diagnose / Remove JiggleRigs
```

Namespace is `BasicCranky`. All menu items live under `GameObject ▸ Basic Cranky/...`. The only external dependency is `com.gator-dragon-games.jigglephysics` (for the jiggle tool); no Basis SDK references at all, so the package drops into any Unity 6 project.

The rest of this document is target-avatar-agnostic — wherever a previous port hard-coded an avatar name, material name, or absolute path, this version uses placeholders like `<AvatarName>` or `<item>`.

---

## TL;DR — the path that ships

1. Copy the highest-quality `.fbx` and the **raw PBR textures** into
   `Assets/_UserContent/<AvatarName>/`. **Do not** import the VRChat
   `.prefab` / `.unitypackage` — they pull VRChat SDK, VRCFury, Mochie,
   Poiyomi, Furality, etc. that Basis doesn't have.
2. Set the rig to **Humanoid** and verify **every bone in the head-to-spine
   chain is mapped** — especially `Chest`. Basis's `GenerateHeadToSpine` IK
   builder crashes hard on a null transform.
3. Rebuild materials as **URP/Lit** from the source PBR textures. Apply any
   color tints via `_BaseColor` to match what the original VRChat material
   was doing on top of the texture.
4. Add the `Basis Avatar` component. Fill Face Viseme / Blink mesh,
   auto-detect visemes, set eye/mouth positions.
5. Port each clothing item with `basic-cranky`'s **Link Armature to Parent
   Avatar** (name-based bone remap + orphan adoption). For FBXes whose
   wrapper Transform carries a baked `-90° X` rotation and `100×` scale,
   **tick "Bake Axis Conversion"** on the FBX Model tab *before* linking, or
   the SMR loses the conversion the moment it gets reparented.
6. Wire jiggle physics via **Add Jiggle Rigs** on the avatar root. Ears, tail,
   hair, tongue chains are auto-detected. For editor preview only, drop a
   `Jiggle Update Example` on an empty GameObject. Basis drives the
   simulation at runtime.
7. Build the `.bee` for Windows (and Android if you want Quest support).
   Host it somewhere with a stable direct-download URL. Load it in the
   Basis client with the password the build prints.

---

## 1. Project setup

### Unity / SDK
- Use the **exact Unity version the Basis project is locked to** (check
  `ProjectSettings/ProjectVersion.txt`). Drift = shader/asset breakage.
- Do avatar work in `Assets/_UserContent/<AvatarName>/` to stay isolated
  from other content in the Basis project.

### Source assets to copy
For a typical VRChat avatar the useful files are:
- The highest-quality body `.fbx` (often labelled "Excellent" / "PC"
  variant — full polygon count + all blendshapes).
- Raw Substance / PBR texture PNGs (BaseColor / Normal / Metallic /
  Roughness / Emissive — sometimes a combined `MetallicSmoothness`).
- Per-clothing-item FBX + its textures, including any variant subfolders.

What **not** to copy:
- `.prefab` files — they reference `VRC_AvatarDescriptor`, VRCFury
  components, PhysBones, Contacts, none of which exist in Basis.
- `.unitypackage` files marked PC/Quest — same problem.
- Original `.mat` files — they target Mochie / Poiyomi / lilToon /
  custom shaders. Rebuild from textures.

---

## 2. FBX import — the rig step that breaks everything

For each FBX:

### Model tab
- **Read/Write** → on
- **Legacy Blend Shape Normals** → on
- **Bake Axis Conversion** → see note below

### Rig tab
- **Animation Type** → Humanoid (body) / Generic (clothing)
- Apply, then **Configure…**
- Confirm every required Humanoid bone is green
- **The `Chest` bone is the killer.** If the FBX has a `chest` bone but
  Unity doesn't auto-map it to the Humanoid `Chest` slot, the .bee builds
  fine and Basis crashes at runtime:
  ```
  Loading avatar failed: System.ArgumentNullException: Value cannot be null.
  Parameter name: transform
    at BasisFullBodyJobBinder.GenerateHeadToSpine(...)
    at BasisFullBodyJobBinder.Create(...)
    at BasisLocalRigDriver.BuildBuilder()
  ```
  The avatar will appear to load (you'll see an arm-span line in the
  log), then get torn down and replaced by the placeholder. Re-uploading
  without fixing the rig does nothing — the fix is only in the rig
  configurator.

  Either:
  - In Configure → Body tab, drag the `chest` transform into the Chest
    slot, Apply.
  - Or hand-edit `<fbx>.meta`: in `humanDescription.human:`, insert
    between `spine` and `neck`:
    ```yaml
        - boneName: chest
          humanName: Chest
          limit:
            min: {x: 0, y: 0, z: 0}
            max: {x: 0, y: 0, z: 0}
            value: {x: 0, y: 0, z: 0}
            length: 0
            modified: 0
    ```
  - Then Ctrl+R in Unity to regenerate the Avatar asset.

The same null-transform mode of failure can occur for any required
Humanoid bone Basis's IK builder dereferences. If you see
`GenerateHeadToSpine` or similar in the stack, re-open Configure.

### Bake Axis Conversion — when to enable
Default off. Enable it on FBXes whose dragged-in wrapper Transform shows
a baked `~-90° X` rotation and `(100, 100, 100)` scale — the classic
Blender → Unity export signature.

Without it baked, the wrapper Transform carries the conversion. The
moment `basic-cranky`'s Link reparents the SkinnedMeshRenderer out of
that wrapper, the conversion is lost. The mesh ends up at 1/100 scale,
wrong orientation, and you'll spend hours chasing it.

A body FBX often doesn't need it. Clothing — especially shoes,
accessories, anything authored against the avatar's exact rest pose —
often does. Toggle it per-FBX based on the wrapper's actual transform
values.

---

## 3. Materials — rebuild as URP/Lit

VRChat originals use Mochie / Poiyomi / lilToon / custom shaders. None
of these are present in a stock Basis project. Rebuild each material as
`Universal Render Pipeline/Lit` from the source texture maps.

### Body — Substance-style separate maps
Bodies often ship with separate `Metallic` and `Roughness` PNGs. URP/Lit
wants smoothness in the metallic alpha channel. Two workable approaches:
- Skip wiring roughness; let the `_Smoothness` slider default and tune
  by eye. Materials look matte but acceptable.
- Invert roughness → smoothness in an image editor, paste into the
  metallic PNG's alpha channel, re-import as a combined map.

Texture slot mapping:
- `*_BaseColor.png` → Base Map
- `*_Normal.png` → Normal Map. **Critical:** select the PNG, set Texture
  Type → Normal map, Apply — otherwise normals tint everything green.
- `*_Metallic.png` → Metallic Map
- `*_Emissive.png` → Emission Map (also tick the Emission checkbox)

### Clothing — Substance combined `MetallicSmoothness`
Many clothing packs export metallic + smoothness combined in one PNG —
that maps directly to URP/Lit's Metallic Map slot.

### UDIM
URP/Lit does not support UDIM tiles. If a body mesh ships with multiple
UDIM textures (e.g. a `*.1001.png` + `*.1002.png` pair), either split
the mesh by tile or bake the tiles into a single atlas before assigning.

### Color tints
VRChat materials commonly apply a color tint on top of a neutral
texture. The "pink hoodie" is often a light-gray base texture with a
pink `_Color` / `_BaseColor` multiplier. Replicate that in URP/Lit by
setting `_BaseColor` and `_Color` on the new material.

Easiest fast path: hand-edit the `.mat` YAML. Find
`_BaseColor: {r: 1, g: 1, b: 1, a: 1}` and replace with the target
color in 0–1 floats.

---

## 4. Basis Avatar component

On the avatar root GameObject, Add Component → Basis Avatar.

Fields:
- Avatar Name + Description.
- Animator (auto-fills once the Humanoid rig is applied).
- **Face Viseme Mesh** — drag the body SkinnedMeshRenderer that holds
  the `vrc.v_*` blendshapes (commonly the mesh literally named `Body`).
- **Blink Blendshape Mesh** — usually the same mesh.
- **Viseme Setup → auto-detect** — finds `vrc.v_aa`, `vrc.v_ch`, etc.
- **Eye / Mouth positions** — set via the gizmo or Vector3 fields.

Basis won't accept a plain `MeshRenderer` here — it must be a
`SkinnedMeshRenderer`. If the body mesh imported as plain MeshRenderer,
the rig wasn't applied: re-confirm `animationType: 3` in the FBX `.meta`
and Ctrl+R.

---

## 5. Clothing port — using `basic-cranky`

Most VRChat clothing is `<item>.fbx` + its Textures folder. VRCFury
linked the clothing's internal skeleton onto the avatar's bones at
upload time. Basis has no built-in equivalent — `basic-cranky`'s
**Link Armature to Parent Avatar** is what fills that role here.

What Link does internally:

1. Walks the clothing SMR's `bones[]` array.
2. For each bone, finds a Transform under the parent avatar with the
   same name (case-insensitive, suffix-tolerant).
3. Rewrites `bones[]` to point at the avatar's bones.
4. **Adopts orphan clothing-only bones** (e.g. accessory bones with no
   match on the avatar) by reparenting them under the closest matched
   ancestor.

Full menu reference (priority controls ordering in the right-click menu):

| Priority | Menu | Use |
|---|---|---|
| 0  | Link Armature to Parent Avatar | Name-based bone remap + orphan adoption. Try this first. |
| 1  | Rebake Bind Poses (Current Avatar Pose) | Replace bindposes with `bone.worldToLocal * smr.localToWorld`. Use when clothing renders distorted after Link. |
| 2  | Full Rebake (Vertices + Bind Poses) | Snapshot per-vertex world position via old bindposes × current bones, store as new vertex_local + new bindposes. Use when clothing's source rest pose disagrees with the avatar's. |
| 3  | Full Rebake + Shift to Feet | Full Rebake + uniform shift so bounds center lands on the foot bone midpoint. Intended for shoes. |
| 10 | Diagnose SMR Bones | Reports null / inside / outside bones, bind pose translation magnitudes, mesh bounds. **Run this first** when anything looks wrong. |
| 11 | Link + Full Rebake (One Step) | Convenience combo. |
| 20 | Add Jiggle Rigs (Ears + Tail + Tongue + Hair) | Wires `JiggleRig` on bone chain roots for ears/tail/hair/tongue. Picks shortest-hierarchy-depth match to dodge clothing-wrapper duplicates. |
| 21 | Diagnose Jiggle Chain Bones | Lists every GameObject matching candidate names with full path + JiggleRig status. |
| 22 | Remove ALL Jiggle Rigs Under Selection | Clean slate. |

### Per-item workflow

For each clothing piece:

1. Copy `<item>.fbx` + the variant texture PNGs into
   `Assets/_UserContent/<AvatarName>/Clothing/<item>/`. Skip original
   `.mat` files — rebuild as URP/Lit.
2. Select the FBX, set Rig → **Generic** (clothing has its own armature,
   it is not humanoid). Apply.
3. If the wrapper Transform has `~-90° X` + `100×` scale, tick **Bake
   Axis Conversion** in the Model tab before anything else.
4. Drag the FBX into the Hierarchy as a **child of the avatar root**.
5. Build a URP/Lit `.mat` for the variant you picked.
6. Right-click the wrapper → **Basic Cranky ▸ Link Armature to Parent
   Avatar**. The dialog reports `Remapped N bones, Adopted M extra
   clothing bones`.
7. Assign the URP/Lit material to the resulting SMR.
8. If it doesn't render right, run **Diagnose SMR Bones** first — let
   it tell you whether bones are null, outside the avatar root, or have
   degenerate bind-pose translations, before reaching for Rebake.

### When to escalate from Link → Rebake → Full Rebake

- **Link only** is enough when the clothing was authored against the
  same rest pose as the avatar. Hoodies, jackets, simple pants usually
  land here.
- **Rebake Bind Poses** when the clothing renders distorted (skin
  collapsed, weird stretching) after Link. The bones are right, the
  bindposes are stale.
- **Full Rebake (Vertices + Bind Poses)** when the clothing's source
  skeleton geometrically disagrees with the avatar's rest pose. The
  mesh effectively needs to be "re-projected" into the avatar's
  coordinate frame. Risk: degenerate output if the source vertex data
  is in a coordinate convention the formula doesn't decode.
- **Shift to Feet** for shoes specifically — Full Rebake plus a uniform
  shift so the bounds center lands at the foot midpoint.

If you hit the "collapses to ~zero bounds" mode (see Section 9), the
tool needs an extension, not another rebake. The plan is documented
under [[basisvr-clothing-port]] in the user's project memory: add a
diagnostic dump to `BasicCrankyDiagnose.cs` that prints per-bone
bindpose matrices side-by-side with the avatar's `worldToLocal *
smr.localToWorld`, plus a sample of top-2 vertex influences. Compare
those numbers before adding new transforms.

---

## 6. Jiggle physics

Once avatar + clothing are working:

1. Click the avatar root in Hierarchy.
2. Run **Basic Cranky ▸ Add Jiggle Rigs (Ears + Tail + Tongue + Hair)**.

The setup wires `GatorDragonGames.JigglePhysics.JiggleRig` on the bone
chain roots typical for that avatar — common candidates:
- `ear.l.1`, `ear.r.1`
- `tail` / `tail.1`
- `tongue.1`
- `hair.1`
- Anything else fluffy / dangly the avatar ships with.

### The duplicate-bone gotcha

VRChat clothing FBXes often ship with an **internal copy of the avatar
skeleton** so the clothing previews correctly in the VRChat SDK. After
linking, you can end up with bones of the same name in multiple places:

```
/<AvatarRoot>/Armature/.../head/ear.l.1                  ← body's, correct
/<AvatarRoot>/<ClothingItem>/Armature/.../ear.l.1        ← clothing copy
```

A naive first-match-wins lookup will hit the wrong one. `basic-cranky`'s
Add Jiggle Rigs handles this by picking the **shortest hierarchy
depth** match — body bones are one (or more) segments shorter than
clothing-baked copies. Use the **Diagnose Jiggle Chain Bones** menu to
verify which path got selected.

### Editor testing vs runtime

For editor preview only, drop a `Jiggle Update Example` component on an
empty GameObject. It drives the package's static
`ScheduleSimulate / SchedulePose / CompletePose` tick from LateUpdate.

You do **not** need to include the example driver in the shipped `.bee`
— Basis drives the simulation itself in the loaded bundle.

A T-pose with no motion shows nothing. To test: hit Play, drag the
avatar around in Scene view — fluffy chains should lag and overshoot.

---

## 7. Build & ship

In the Basis Avatar component inspector, at the bottom:
- Tick **Windows (StandaloneWindows64)** at minimum.
- Tick **Android** for Quest support.
- Click **Create Avatar .BEE File**.

Output lands in `<project>/AssetBundles/`. The Console prints the
password — save it.

To distribute:
- Upload the `.bee` to any host that gives you a stable direct-download
  URL (Google Drive ≤ 100 MB with "Anyone with link" works:
  `https://drive.google.com/uc?export=download&id=FILE_ID`).
- In the Basis client → Avatar → Load Custom → URL + password.

---

## 8. What VRChat does that Basis doesn't

| VRChat / VRCFury feature   | Basis equivalent                             |
| -------------------------- | -------------------------------------------- |
| VRC PhysBones              | `GatorDragonGames.JigglePhysics`             |
| VRCFury Armature Link      | `basic-cranky` — this tool                   |
| VRC Expression Menus       | Not supported. Basis avatars are static.     |
| VRC Contact Sender/Receiver| Not supported                                |
| Animation rigging          | Unity's Animation Rigging package, not VRChat Constraints |
| Poiyomi / lilToon shaders  | URP/Lit, or the original shader if installed |

If a prefab snuck in, strip these components before building:
`VRC_AvatarDescriptor`, `VRC PhysBone`, `VRC PhysBone Collider`,
`VRC Contact Sender`, `VRC Contact Receiver`, `VRC Station`,
`VRC Spatial Audio Source`, `Pipeline Manager`.

---

## 9. Failure modes worth knowing in advance

- **Build succeeds, avatar fails to load with `GenerateHeadToSpine`
  null transform.** Required Humanoid bone (almost always `Chest`) is
  unmapped. Fix in Configure, not by re-uploading.
- **Clothing renders at 1/100 scale and wrong orientation after Link.**
  Wrapper had baked `-90° X` + `100×` and Bake Axis Conversion was off.
  Re-import the FBX with it on, re-link.
- **Clothing renders distorted but at the right size.** Bones are
  right; bindposes are stale. Try Rebake Bind Poses.
- **Clothing collapses to ~zero bounds after Full Rebake.** The vertex
  data is in a coordinate convention the rebake formula didn't decode.
  Either re-rig in Blender against the avatar's exact rest pose, or
  extend `BasicCrankyDiagnose.cs` with a bindpose-delta dump (see
  Section 5) before adding new transforms.
- **Normal map tints the model green.** PNG wasn't marked Texture Type
  → Normal map. Fix and reimport.
- **Body imports as plain MeshRenderer, Basis Avatar component
  rejects it.** Rig wasn't applied. Re-confirm `animationType: 3` in
  the `.meta` and Ctrl+R.
- **Jiggle wires on the wrong bone.** Duplicate skeleton inside
  clothing wrapper. `basic-cranky` already prefers shortest hierarchy
  depth; if it still picks wrong, use the diagnose menu to inspect
  candidates manually.
- **Console error: `audioplugin_phonon.dll … Access is denied`.**
  Steam Audio runtime DLL. Unrelated to avatars. Ignore.

---

## 10. The summary that saves the most time

1. **Run Configure on the Humanoid rig and verify `Chest` is mapped**
   before building a `.bee`. Without it the build succeeds and runtime
   fails with `GenerateHeadToSpine` null transform.
2. **Tick Bake Axis Conversion** on any FBX whose wrapper Transform
   shows non-identity rotation + non-1 scale. Otherwise you chase a
   "tiny offset mesh" bug for hours.
3. **Rebuild clothing materials as URP/Lit** from raw textures. Don't
   try to port Mochie / Poiyomi / lilToon materials directly.
4. **Link is usually enough** for clothing. Only reach for Rebake /
   Full Rebake when Diagnose tells you something genuinely degenerate
   is happening.
5. **VRChat clothing FBXes carry duplicate skeletons.** When wiring
   anything by bone name (jiggle, constraints, etc.), prefer the
   shortest hierarchy path — that's the body's real bone, not a
   baked-in clothing copy. `basic-cranky`'s Add Jiggle Rigs already
   does this; if you add new tools, do too.
6. **For jiggle preview in editor: drop `Jiggle Update Example` on an
   empty GameObject.** Basis drives the tick at runtime in the bundle;
   you don't need it in the build.

---

## 11. References — projects and docs an agent will want

### `basic-cranky` itself
- This repo: <https://github.com/feralresearch/basic-cranky>
- Install via Unity Package Manager → Add package from Git URL with
  the URL above.

### BasisVR (the target platform)
- Main repo: <https://github.com/BasisVR/Basis>
- Org page (all sub-repos, including server / demo): <https://github.com/BasisVR>
- Documentation site: <https://docs.basisvr.org/>
- Docs source (good for grepping current behavior): <https://github.com/BasisVR/BasisDocs>
- Demo project (smaller reference scene): <https://github.com/BasisVR/BasisDemo>
- Features overview: <https://basisvr.org/features>

Notes for an agent:
- Basis is MIT-licensed, built on Unity 6 + URP, with a .NET 9 dedicated
  server. The avatar SDK lives inside the main `BasisVR/Basis` repo;
  clone the whole thing, don't try to install just an SDK package.
- Custom IK is Burst-compiled. The runtime bone resolution is what
  triggers the `GenerateHeadToSpine` null-transform crash — grep the
  repo for `BasisFullBodyJobBinder` / `BasisLocalRigDriver` if you want
  the exact failure site.

### VRCFury (the source-side tooling `basic-cranky` partially replaces)
- Website + docs: <https://vrcfury.com/>
- Source repo: <https://github.com/VRCFury/VRCFury>
- Docs source: <https://github.com/VRCFury/vrcfury.com>
- Armature Link component docs (the part `basic-cranky` reimplements):
  <https://github.com/VRCFury/vrcfury.com/blob/main/docs/40-components/armature-link.mdx>
- Linking clothes tutorial:
  <https://github.com/VRCFury/vrcfury.com/blob/main/docs/50-tutorials/linking-clothes.mdx>
- Artists guide:
  <https://vrcfury.com/tutorials/artists/>

Notes for an agent:
- Read the Armature Link source under `com.vrcfury.vrcfury/Editor/VF/Feature/`
  when `basic-cranky` starts hitting the "rest pose disagreement"
  failure mode in Section 5. The rest-pose-delta math (per-bone
  bindpose rewriting plus per-vertex re-projection) is the part most
  home-grown link tools — including this one — get wrong.

### Jiggle physics (the Basis-approved replacement for VRC PhysBones)
- Source: <https://github.com/naelstrof/UnityJigglePhysics>
- OpenUPM page (package ID `com.gator-dragon-games.jigglephysics`):
  <https://openupm.com/packages/com.gator-dragon-games.jigglephysics/>
- UPM install URL: `https://github.com/naelstrof/UnityJigglePhysics.git#upm`

Notes for an agent:
- The package is by Naelstrof; the OpenUPM ID uses the
  `gator-dragon-games` namespace, so both names refer to the same code.
- Real-time tunable in Play mode via `JiggleRigBuilder` + `JiggleRig` +
  `JiggleSettings`. Use this for iteration before baking the `.bee`.

### Shaders you may still need from the VRChat side
- Poiyomi Toon: <https://github.com/poiyomi/PoiyomiToonShader> — only
  install if you have a strong reason; URP/Lit is the safer default.
- lilToon: <https://github.com/lilxyzw/lilToon> — same caveat.
- Unity Universal RP docs (what you're rebuilding into):
  <https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest>

### VRChat-side reference (for understanding source assets)
- VRChat Creators docs: <https://creators.vrchat.com/>
- Avatar Descriptor / viseme mesh expectations:
  <https://creators.vrchat.com/avatars/>

### What to search for inside the Basis repo when stuck
- `BasisFullBodyJobBinder` → IK builder, source of the head-to-spine crash
- `BasisLocalRigDriver` → drives the per-frame rig build
- `BasisAvatar` → the component you add to the avatar root
- `vrc.v_` → viseme blendshape naming convention Basis auto-detects
- `.bee` → asset bundle build path; grep for the file-extension handler
  to understand what's actually in the bundle
- `JigglePhysics` → confirm the version Basis ships against

Sources used in compiling this section:
- [BasisVR/Basis on GitHub](https://github.com/BasisVR/Basis)
- [BasisVR Docs](https://docs.basisvr.org/)
- [BasisVR org page](https://github.com/BasisVR)
- [VRCFury home](https://vrcfury.com/)
- [VRCFury/VRCFury on GitHub](https://github.com/VRCFury/VRCFury)
- [VRCFury Armature Link docs](https://github.com/VRCFury/vrcfury.com/blob/main/docs/40-components/armature-link.mdx)
- [naelstrof/UnityJigglePhysics on GitHub](https://github.com/naelstrof/UnityJigglePhysics)
- [JigglePhysics on OpenUPM](https://openupm.com/packages/com.gator-dragon-games.jigglephysics/)
