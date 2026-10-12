# Changelog

All notable changes to the **Train Inspector** mod for Derail Valley will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0] - 2026-10-11

### Added
- **Public Release:** Official 1.0.0 general release for Derail Valley Build 99.7.
- **MIT License:** Added formal open-source license documentation.
- **Packaging Automation:** Automated distribution packaging in MSBuild (`PackageModZip`) and PowerShell (`package.ps1` and `deploy.ps1`) bundling DLL, PDB, `Info.json`, `README.md`, and `LICENSE`.
- **Repository Setup:** Added `.gitignore` configured for .NET/C#, Rider, Visual Studio, and local game/mod runtime state files.
- **Changelog:** Added dedicated `CHANGELOG.md` to track version history and feature milestones.
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
### Changed
- **Pneumatic Threshold Calibration:** Updated default released brake cylinder threshold from legacy `0.35 bar` to `1.10 bar` absolute, accurately reflecting Derail Valley's atmospheric venting physics.
- **Config Auto-Migration:** Automatically migrates existing player configs with legacy cylinder thresholds (< 0.95 bar) to the calibrated 1.10 bar threshold upon loading.
- **Dynamic Version Logging:** Mod initialization log message now dynamically fetches version information from `modEntry.Info.Version` (`Train Inspector 1.0.0 loaded...`).
- **Comprehensive Documentation:** Completely revised `README.md` with protocol comparison tables, step-by-step full departure test walkthrough, and accurate threshold reference.

### Fixed
- Corrected solution build command reference in build documentation from obsolete `Solution1.sln` to `DVTrainInspect.sln`.
- Ensured mod packaging scripts resolve version dynamically from `Info.json` to avoid hardcoded version drift.

---




