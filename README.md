# HandFleet

HandFleet is a standalone hand-tracked arcade shooter for Meta Quest and PICO. Grab the ship with either hand,
fly it with your hand, and point it at enemies: the guns fire on their own.

This branch (`/main/handfleet/standalone`) contains HandFleet only. The other MetaHackathon2026 levels
(car workshop, Cloud Garden, Shadow Workshop, Emberdeep, Creek Diorama) are not part of it. The app opens
directly into the HandFleet scene, and its exit control quits the app.

- Unity 6000.3.21f1, URP, Android target. Package `com.metahackathon2026.handfleet`, product name HandFleet.
- Code: `Assets/HandFleet` (`Runtime` = rules, `Integration` = scene entry, input and XR rig helpers in
  `Integration/XR`, `Editor` = authoring and builds in `Editor/Packaging`, `Tests`).
- Quest APK: **HandFleet > Build Quest APK** writes `Builds/HandFleet-Quest.apk`.
- PICO APK: **HandFleet > Build PICO APK** writes `Builds/HandFleet-Pico.apk`; **Build PICO APK and run**
  also installs and launches it on connected PICO headsets.
- Command line (editor closed): `Unity.exe -batchmode -quit -projectPath . -buildTarget Android -executeMethod
  HandFleet.BuildTools.HandFleetQuestBuild.BuildFromCommandLine`, or
  `HandFleet.Editor.HandFleetPicoBuild.BuildFromCommandLine` for PICO.

Design notes: [Docs/HandFleet.md](Docs/HandFleet.md), [Docs/HandFleetValidation.md](Docs/HandFleetValidation.md),
[Docs/HandFleetClassicReference.md](Docs/HandFleetClassicReference.md).
