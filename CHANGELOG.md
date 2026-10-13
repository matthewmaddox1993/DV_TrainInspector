# Changelog

All notable changes to the **Train Inspector** mod for Derail Valley will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.1] - 2026-10-12

### Changed
- **Mechanical Fault Processing:** Active brake faults are indexed by car, avoiding repeated full-list searches from every brake-system physics tick.
- **Fault Activation Updates:** Distance-based fault processing now runs at five updates per second instead of rebuilding consist data every frame.
- **Inspection Polling:** Reduced background consist, brake observation, and player-proximity polling while retaining responsive inspection feedback.
- **Persistence Timers:** Certificate and inspection-history countdowns are accumulated and applied in batches instead of iterating every saved record every frame.
- **Optional Mod Compatibility:** Cached `LocoOwnership` integration discovery and limited reflective teleport-state polling to four updates per second.

### Fixed
- **Periodic Gameplay Hitches:** Eliminated the full loaded-assembly scan that ran every two seconds whenever the optional `LocoOwnership` mod was not installed.
- **Clipboard Upkeep Hitches:** Avoided recurring scene-wide object searches while the Inspection Clipboard is already present, and cached the starting-items controller per scene.

---

## [1.0.0] - 2026-10-11

### Added
- **Public Release:** Official 1.0.0 general release for Derail Valley Build 99.7.
- **Random Pre-Existing Equipment Defects:** Cars can now spawn with pre-existing latent mechanical defects (brake-pipe leaks, dragging handbrakes, weak rigging, stuck brake cylinders, hot bearings, and optional coupler defects) prior to inspection, governed by configurable occurrence chance (default 5% per car).
- **Inspection Defect Discovery & Bad Order Tagging:** Discovering a defect during the physical walk-around immediately tags the car as `[BAD ORDER]`, logs the defect in inspection findings, and blocks issuance of an inspection certificate until repaired.
- **Station Terminal Repairs:** Added dedicated in-game terminal repair services to the inspector UI, featuring individual `[🔧 Terminal Repair ($Cost)]` buttons on car inspection cards and a consist-wide `[🔧 Terminal Repair All Equipment ($TotalCost)]` service action that clears defects, restores bad-ordered equipment, and deducts repair costs from the player wallet.
- **Immediate Departure Catastrophic Failure:** Attempting to depart with un-repaired or known Bad Order equipment immediately triggers catastrophic physical failures upon departure (0-metre activation: train line vents to emergency, handbrakes lock, cylinders stick, bearings overheat, or couplers break) alongside safety violation fines.
- **Customizable Toggle Hotkey:** Players can rebind the inspector toggle shortcut directly in the Unity Mod Manager settings menu (Ctrl+F10) with support for custom key codes, modifiers, and a default reset button.
- **Cross-Save Inspection Persistence:** Inspection certificates and walk-around history persist reliably between saved games via `TrainInspectorState.xml`.
- **In-Game Playtime Validity Tracking:** Certificate and car inspection validity now count down active operating hours only while the game is running, preventing certificates from expiring in real time while the PC or game is turned off.
- **Active Certificate Dashboard:** When opening the inspector window, occupied trains with valid certificates now display a prominent **`[★ ACTIVE INSPECTION CERTIFICATE — FORM MP-223 COMPLIANT]`** banner with remaining operating hours, consist details, and a dedicated certificate viewer.
- **In-Progress Inspection Resumption:** In-progress inspections are automatically serialized to disk, allowing players to resume saved walk-arounds and brake tests after reloading.
- **MIT License:** Added formal open-source license documentation.
- **Packaging Automation:** Automated distribution packaging in MSBuild (`PackageModZip`) and PowerShell (`package.ps1` and `deploy.ps1`) bundling DLL, PDB, `Info.json`, `README.md`, and `LICENSE`.
- **Repository Setup:** Added `.gitignore` configured for .NET/C#, Rider, Visual Studio, and local game/mod runtime state files.
- **Changelog:** Added dedicated `CHANGELOG.md` to track version history and feature milestones.

### Changed
- **Physical Clipboard Walk-Around Movement:** When opened via the physical Inspection Clipboard item, character movement (locomotion) remains active, allowing players to walk between cars and inspect trains while holding the clipboard. Opening via keyboard shortcut without holding the clipboard retains stationary menu controls.
- **Immediate Observation Saving:** Walk-around side visits and car equipment checks are committed to persistent history immediately as they are made, preserving progress if the game is saved mid-walk.
- **Pneumatic Threshold Calibration:** Updated default released brake cylinder threshold from legacy `0.35 bar` to `1.10 bar` absolute, accurately reflecting Derail Valley's atmospheric venting physics.
- **Config Auto-Migration:** Automatically migrates existing player configs with legacy cylinder thresholds (< 0.95 bar) to the calibrated 1.10 bar threshold upon loading.
- **Dynamic Version Logging:** Mod initialization log message now dynamically fetches version information from `modEntry.Info.Version` (`Train Inspector 1.0.0 loaded...`).
- **Comprehensive Documentation:** Completely revised `README.md` with protocol comparison tables, step-by-step full departure test walkthrough, and accurate threshold reference.

- **LocoOwnership Mod Compatibility & Spawn Settling Protection:** Fixed spurious uninspected departure penalties, fines, and immediate failures triggering when spawning or teleporting locomotives via the `LocoOwnership` mod (or comms radio / rerail tools). Implemented multi-layered departure protection:
  - Added dynamic reflection check for `LocoOwnership.Shared.CarTeleporter.isTeleportingTrain` to completely suppress departure evaluation during teleportation operations.
  - Added Harmony hook on `TrainCar.MoveToTrack` providing an 8.0-second spawn settling grace period.
  - Added consist stabilization cooldown (5.0s) upon any consist or tender coupling change.
  - Enforced a settled-at-rest requirement (`speed <= 0.25 m/s`) before train departures can be armed.
  - Required sustained movement (at least 2.0s continuously above departure speed AND at least 12.0m of physical roll distance) to eliminate single-tick PhysX impulse triggers.
  - Added teleportation velocity spike filter (> 45 m/s) to prevent physics anomalies from triggering departures or accumulating wear.
- **Cursor Release on Window Close:** Fixed mouse cursor remaining visible and unlocked on screen after closing the inspector menu, properly restoring locked and hidden first-person mouse look.
- **Inspection Clipboard Retention Across Saved Games:** Fixed clipboard missing from inventory after loading a saved game. Custom mod items are excluded from vanilla `.save` files to prevent save corruption; the manager now actively detects save loads and scene transitions, verifying and restoring the clipboard to an open inventory slot automatically.
- **On-Demand Clipboard Spawning:** Added fallback **"📋 Add Clipboard to Inventory"** buttons in the inspector window header, train selection screen, and Unity Mod Manager (Ctrl+F10) settings menu.
- **Accurate Inventory Detection:** Fixed clipboard detection so ungrabbed world instances or destroyed references do not block spawning when the clipboard is missing from both hands and inventory slots.
- **Certificate Reload Disconnect:** Fixed an issue where reloading a game save caused the UI to prompt for a new inspection from scratch because in-memory session state was null and active certificates were not rendered in the selection view.
- **Real-Time Wall Clock Expiration:** Fixed certificates expiring while offline due to UTC wall-clock time comparisons.
- Corrected solution build command reference in build documentation from obsolete `Solution1.sln` to `DVTrainInspect.sln`.
- Ensured mod packaging scripts resolve version dynamically from `Info.json` to avoid hardcoded version drift.

---

## [0.2.3]

### Fixed
- Fixed **Inspection Clipboard** creation on Build 99.7 by loading the actual stock inventory resource prefab instead of its configuration entry.
- Added single-instance component error reporting if future game updates alter the stock clipboard prefab hierarchy.

---

## [0.2.2]

### Added
- **"What to do now" Live Guide:** Expandable interactive assistance panel tracking current required physical actions and preventing premature advancement past incomplete observations.
- **Live Cab-Setup Checklist:** Real-time checklist displaying explicit **CUT IN / CUT OUT** instructions, train-brake valve counts, main reservoir pressure, independent brake state, cylinder pressure, and player proximity.

---

## [0.2.1]

### Added
- **Inspection Clipboard Item:** In-game inventory item utilizing Derail Valley's stock clipboard model, icon, grab handling, and hotbar behavior.
- **Safe Save Handling:** Excluded clipboard from save file serialization so removing the mod leaves no orphaned item references.
- **Auto-Restoration:** Automatically gives clipboard to player on load, or defers until an inventory slot opens.
- Maintained **F7** keyboard shortcut as an instant fallback toggle.

---

## [0.2.0]

### Added
- **5 Inspection Protocols:**
  - Full Departure Test (dual-side walk-around, cab setup, leakage hold, service application, rear-car confirmation).
  - Continuity Test (quick consist-change verification).
  - Cab-Change Test (controlling locomotive/cab transfer verification).
  - Quick Set-and-Release (short stop brake response verification).
  - Post-Departure Rolling Inspection (running gear check shortly after movement).
- **Dual-Side Walk-Around:** Close-up observation checklist tracking independent left and right side inspections per car.
- **Timed Leakage Hold Test:** Real-time brake-pipe leakage monitoring with configurable hold duration and allowable loss.
- **Rear-Car Brake Verification:** Physical application and release checks verified at the far end of the consist.
- **Form MP-223 Certificates:** Stamped inspection certificates tied to exact consist configuration, car count, and controlling cab, with configurable expiration time.
- **Wallet Penalties:** Optional fines deducted for departing without valid certification or with uncertified cars.
- **Latent Mechanical Faults:** Optional defect generation for skipped equipment checks (dragging brakes, brake rigging failures, hot wheel bearings, coupler separation).
- **Field Defect Repairs:** Ability to diagnose and clear discovered mechanical defects for a fee.
- **Persistence:** Saved certificate, fine, and defect state to `TrainInspectorState.xml`.

---

## [0.1.0]

### Added
- Initial prototype release with core brake pipe observation, UI overlay, and inspection verification framework.
