# Train Inspector

[![Game Version](https://img.shields.io/badge/Derail%20Valley-Build%2099.7-blue.svg)](http://www.derailvalley.com/)
[![Mod Version](https://img.shields.io/badge/Version-1.0.0-brightgreen.svg)](https://github.com/)
[![UMM](https://img.shields.io/badge/Unity%20Mod%20Manager-0.27.3%2B-orange.svg)](https://www.nexusmods.com/derailvalley/mods/plugins)
[![Target Framework](https://img.shields.io/badge/.NET%20Framework-4.8-purple.svg)](https://dotnet.microsoft.com/)

**Train Inspector** is an immersive, hands-on train-inspection and air-brake testing mod for **Derail Valley Build 99.7**.

It puts you in the boots of a real railroad mechanical inspector and locomotive engineer. Instead of automatic one-click checks, Train Inspector requires you to physically walk both sides of your consist, inspect couplers and brake rigging, confirm angle cocks and air hoses, enforce proper locomotive cab brake cutout configurations, conduct timed brake-pipe leakage holds, and verify rear-car brake cylinder response.

---

## Key Features

- **Native Inspection Clipboard Item:** Includes an in-game **Inspection Clipboard** inventory item using Derail Valley's stock clipboard model, inventory icon, grab handling, and hotbar behavior. Equip or use it to bring up the inspector, or use **F7** at any time. Cleanly preserved across saves and automatically excluded from save files so removing the mod leaves no corrupted references.
- **Interactive "What to do now" Guide:** An expandable, live assistant highlights the current required action, tracks unfinished preparation steps, and prevents advancing to testing phases before all necessary walk-arounds or setups are observed.
- **5 Realistic Inspection Types:**
  - **Full Departure Test:** Comprehensive pre-trip inspection including complete dual-side car walk-around, cab brake cutout setup, timed leakage hold, service application, and rear-car confirmation.
  - **Continuity Test:** Fast hose/cock and coupler verification, cab setup, and far-end brake response after picking up, setting out, or rearranging cars.
  - **Cab-Change Test:** Cab brake cutout and valve setup validation plus far-end confirmation when changing the controlling locomotive or cab.
  - **Quick Set-and-Release:** Functional brake application and release verification after brief stops or minor locomotive work.
  - **Post-Departure Rolling Inspection:** Stopped inspection of running gear, wheels, bearings, and brake rigging shortly after getting underway.
- **Live Cab-Setup Checklist:** Real-time monitoring of locomotive cab controls:
  - Explicit **CUT IN / CUT OUT** status and enforcement (ensuring exactly one train-brake valve is active on the brake pipe).
  - Main reservoir pressure verification (minimum 6.5 bar).
  - Train brake valve handle position and independent brake state.
  - Brake cylinder vented verification (at or below 1.10 bar absolute).
  - Player proximity validation to the controlling cab.
- **Accurate Air Brake Testing:**
  - Real-time monitoring of brake-pipe pressure, train-line continuity, and per-car cylinder response.
  - Timed brake-pipe leakage test with configurable hold duration (default 15 seconds) and leakage allowance (default 0.15 bar).
  - Minimum 0.75 bar service reduction requirement.
  - Physical rear-car application and release verification.
- **Official Form MP-223 Certificates:** Completing an inspection issues a formal, stamped inspection certificate tied to the specific consist configuration, car count, and controlling cab, valid for a configurable time (default 8 hours).
- **Realistic Consequences & Failure Simulation (Configurable):**
  - **Wallet Penalties:** Optional fines deducted from the player's wallet when departing without an active certificate or with uncertified cars.
  - **Latent Mechanical Faults:** Skipped equipment checks can produce latent defects (dragging brakes, brake rigging failures, hot wheel bearings, and optional coupler separation).
  - **Defect Repairs:** Discovered defects can be serviced and cleared directly in the field for a repair cost.
  - Fully configurable or completely disableable if you prefer only the procedural walk-around checklist.

---

## Requirements

- **Derail Valley:** Build 99.7
- **Unity Mod Manager (UMM):** Version 0.27.3 or newer (tested with 0.33.0)

---

## Installation

### Via Unity Mod Manager (Recommended)
1. Download `TrainInspector_v1.0.0.zip` (or `TrainInspector-1.0.0.zip`).
2. Open the **Unity Mod Manager** installer GUI for Derail Valley.
3. Switch to the **Mods** tab.
4. Drag and drop the zip file onto the manager window.

### Manual Installation
Extract the contents of the zip archive into your Derail Valley installation directory:
```
<Derail Valley Directory>\Mods\TrainInspector\
    Info.json
    TrainInspector.dll
    TrainInspector.pdb (optional)
    README.md (optional)
```

---

## How to Play

### Opening the Inspector
- **Inspection Clipboard:** Check your inventory upon loading the game. Equip or use the **Inspection Clipboard** to toggle the inspection form.
- **Hotkey:** Press **F7** on your keyboard at any time to open or close the window.

### Running a Full Departure Inspection

1. **Start Inspection:** Equip the **Inspection Clipboard** (or press **F7**), click **Full departure test**, and select your train.
2. **Review Consist Direction:** Confirm the displayed car order. Use **Reverse consist** if you prefer to inspect from the opposite end.
3. **Walk-Around:** Walk both the left and right sides of every car in the consist within inspection range (default 9m). Check and record:
   - Air hoses and angle cocks (both ends of each car)
   - Coupler knuckles and lockpins
   - Handbrake release status
   - Brake rigging and brake shoes
   - Wheel condition and journal/bearing temperatures
4. **Cab Setup:** Climb into the controlling cab:
   - Set the controlling locomotive's train-brake cutout control to **CUT IN**.
   - Ensure all other locomotive cabs on the consist have their train-brake cutout control set to **CUT OUT**.
   - Put the automatic and independent brake handles in **Release**.
   - Ensure main reservoir pressure is charged to at least 6.5 bar.
   - Confirm brake cylinders are fully vented (at or below 1.10 bar absolute).
   - Click **Record cab setup**.
5. **Charge Train Line:** Release brakes, run the compressor, and ensure the brake pipe charges to at least 4.5 bar throughout the reachable train.
6. **Leakage Test:**
   - Click **Begin leakage test**.
   - Move the controlling train-brake cutout control to **CUT OUT** (without moving the brake handle from Release).
   - Hold cutout closed for the timed hold duration (default 15 seconds).
   - Click **Record leakage result**, return the cutout control to **CUT IN**, and recharge the brake pipe.
7. **Service Application & Rear Confirmation:**
   - Click **Begin service application**.
   - Make at least a 0.75 bar reduction with the automatic brake valve.
   - Walk to the rear car and click **Confirm application** once the brake cylinder responds.
8. **Release & Recharge:**
   - Move the brake valve back to **Release**.
   - Wait for the brake pipe to recharge and confirm the rear car's brake cylinder releases.
   - Click **Confirm release**.
9. **Certification:**
   - Review the completed test summary.
   - Click **Complete inspection & issue certificate** to receive your Form MP-223 inspection certificate.

---

## Inspection Protocols Overview

| Inspection Type | When to Use | Requirements |
| :--- | :--- | :--- |
| **Full Departure Test** | Initial train assembly or terminal departure | Dual-side walk-around, full cab setup, timed leakage hold, service application, rear-car application & release verification. |
| **Continuity Test** | Adding/removing cars, switching, consist changes | Hose/cock & coupler walk-around, cab setup verification, far-end application & release check. |
| **Cab-Change Test** | Swapping controlling cab or reversing push-pull power | Controlling cab cutout verification, valve count check, far-end application & release verification. |
| **Quick Set-and-Release** | Minor pauses, locomotive servicing, brief crew relief | Cab check and reachable brake cylinder application & release verification. |
| **Post-Departure Rolling** | Within 5 km/h after initial yard departure | Walk-around of running gear, wheels, bearings, and brake rigging after train movement. |

---

## Mechanical Faults & Repairs

When mechanical failures are enabled in settings:
- **Skipping Checks Has Consequences:** Skipping walk-around items creates a small chance (default 2% per skipped item) of a latent defect manifesting after departure.
- **Defect Types:**
  - **Stuck / Dragging Brakes:** Increases rolling friction and generates heat.
  - **Brake Rigging Defects:** Reduces effective braking power on the affected car.
  - **Hot Wheel Bearings:** Overheats axle journals, risking derailment if ignored.
  - **Coupler Separation (Optional, Disabled by Default):** Can cause train separation under high tractive effort.
- **Field Repairs:** Discovered defects can be diagnosed and repaired through the clipboard interface by paying standard railroad mechanical department fees.
- **Persistence:** Fault states, active certificates, and fine tallies are saved in `TrainInspectorState.xml` in the mod directory.

---

## Configuration & Default Thresholds

All options can be configured in the **Unity Mod Manager** settings menu (**Ctrl + F10**):

| Setting | Default | Description |
| :--- | :--- | :--- |
| **Assisted Mode** | Enabled | Highlights current task and guides the walk-around car by car. |
| **Require Physical Walk-Around** | Enabled | Player must physically be near the car to inspect components. |
| **Walk-Around Range** | 9.0 m | Maximum distance from car center to record walk-around observations. |
| **Show Departure Warnings** | Enabled | Alerts when departing without certification or with defects. |
| **Charged Brake Pipe Threshold** | 4.5 bar | Minimum pipe pressure considered fully charged. |
| **Main Reservoir Threshold** | 6.5 bar | Minimum locomotive main reservoir pressure for cab verification. |
| **Service Reduction Threshold** | 0.75 bar | Minimum pressure drop required for brake application test. |
| **Cylinder Application Response** | 0.35 bar rise | Minimum cylinder pressure increase to register application. |
| **Released Cylinder Threshold** | 1.10 bar | Maximum cylinder pressure considered fully released (atmospheric vented baseline). |
| **Leakage Hold Time** | 15 s | Duration of the brake-pipe leakage hold test. |
| **Maximum Leakage Allowance** | 0.15 bar | Maximum allowable pressure drop during the leakage hold. |
| **Certificate Validity** | 8.0 hours | How long a Form MP-223 certificate remains valid. |
| **Wallet Penalties** | Enabled | Deducts fines from player wallet on uncertified departures. |
| **Base Uninspected Fine** | $250 | Base fine for departing without a valid inspection. |
| **Fine Per Missed Item** | $25 | Additional fine for each skipped inspection component. |
| **Mechanical Failures** | Enabled | Allows skipped checks to spawn latent equipment faults. |
| **Fault Chance per Missed Check** | 2.0% | Probability of generating a latent defect per uninspected item. |
| **Maximum New Faults / Departure** | 2 | Maximum number of new latent defects that can spawn per departure. |
| **Coupler Separation Faults** | Disabled | Allows coupler failures to cause train separations (high difficulty). |

---

## Building from Source

### Prerequisites
- .NET SDK (supporting `net48` targeting pack)
- Derail Valley Build 99.7 installation


---

## Release History

See [CHANGELOG.md](CHANGELOG.md) for detailed version notes.

### [1.0.0] - Official Public Release
- **Official Public Release:** Promoted Train Inspector to 1.0.0 with full feature completeness and stability.
- **Calibrated Atmospheric Brake Baseline:** Updated default cylinder release threshold to 1.10 bar absolute, perfectly matching Derail Valley's atmospheric venting physics. Automatic migration seamlessly upgrades legacy configuration files.
- **Native Inspection Clipboard Item:** Clean inventory integration with stock clipboard prefab, full hotbar support, save-exclusion protection, and F7 keyboard fallback.
- **Interactive Step-by-Step Guidance:** Expandable "What to do now" action panel prevents out-of-order progression and clearly highlights required physical actions.
- **Dynamic Cab Setup Validation:** Live multi-valve detection, CUT IN / CUT OUT compliance, MR pressure checks, and player proximity verification.
- **Form MP-223 Mechanical Certificates:** Stamped, expiring inspection records tied to exact train consist configuration.
- **Configurable Consequences:** Optional uninspected departure wallet fines and latent mechanical defects with field repair capability.
- **Production Packaging:** Automated MSBuild and PowerShell release packaging pipeline.


## Credits & License

- **Author:** Matt M.
- **Game:** Derail Valley by [Altfuture](https://altfuture.gg/)
- **License:** Released under the [MIT License](LICENSE).
