# Character assets

Runtime `.mov` clips and FFmpeg decoding are the only animation source used by the desktop app.
PNG frames are not loaded or embedded. See `Assets/Video/README.md` for clip filenames and publish
behavior.

Character frames live here, **one folder per animation**. The folder name is the key
`SpriteAnimator` looks for, and it maps to a `PetState` via the `Animations` table in
`src/DesktopPet/SpriteAnimator.cs`:

| Folder | State | Rate | Frames committed |
| --- | --- | --- | --- |
| `idle` | Idle | 30 fps | 122 |
| `sigh` | Sighing | 30 fps | 122 |
| `walk_left` | Walking left | 30 fps | 93 |
| `walk_right` | Walking right | 30 fps | 159 |
| `respond` | Click response | 8 fps | none |
| `sleep` | Sleeping | 8 fps | none |
| `drag` | Being dragged | 8 fps | none |
| `turn_left` | Turning to face left | 30 fps | 66 |
| `turn_back` | Turning back to face front | 30 fps | 42 |

## Adding or replacing an animation

Name frames `000.png`, `001.png`, … inside the state's folder — plain zero-padded numbers, no
prefix, since the folder already identifies the animation. Numbers must run from 0 **without
gaps**: loading stops at the first missing index, and everything after the gap is silently ignored.

- Keep every image on a **180 × 210** canvas with a **stable feet baseline** (the bottom of the
  shoes must stay on the same row — currently `y = 188` — or the pet will bob as it animates).
- Transparent background, true alpha channel (`Format32bppArgb`), no text or watermarks.
- A state with no frames falls back to the vector placeholder in `PetVisual.cs`; it does not stop
  the app. Adding art for some states but not others is therefore legal but shows as an abrupt
  switch between rendered and placeholder art.
- After adding, renaming or deleting frame files, run a **clean** build (`dotnet clean`, then
  `dotnet build`). An incremental build does not re-embed changed resources, so the old images keep
  being served and the app appears not to change.

`turn_left` / `turn_back` are authored but not yet wired to a state — there is no `TurningLeft` /
`TurningBack` member on `PetState`. See the "Character frames" section of the repository README for
how those arcs were cut and what is needed to hook them up.

## Source photographs

Source photos and reference clips are **not** read at runtime and are **not** committed. Keep them
outside this folder (they are git-ignored), and never copy a photograph into `Assets/Character` —
the folder is embedded into the executable wholesale.
