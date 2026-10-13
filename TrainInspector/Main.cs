using System;
using System.Reflection;
using DV;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace TrainInspector
{
    public static class Main
    {
        private static GameObject host;
        private static InspectorController controller;
        private static Harmony harmony;

        internal static UnityModManager.ModEntry ModEntry { get; private set; }
        internal static InspectorSettings Settings { get; private set; } = new InspectorSettings();
        internal static bool IsEnabled { get; private set; }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            modEntry.OnToggle = OnToggle;
            modEntry.OnUnload = OnUnload;
            modEntry.OnGUI = DrawModManagerGui;
            modEntry.OnSaveGUI = SaveModManagerGui;

            try
            {
                Settings = UnityModManager.ModSettings.Load<InspectorSettings>(modEntry)
                           ?? new InspectorSettings();
                Settings.Clamp();
                InspectionPersistence.Initialize(modEntry.Path);
                InspectionCertificateRegistry.PruneExpired();
                PenaltyManager.ResetRuntime();

                harmony = new Harmony(modEntry.Info.Id);
                harmony.PatchAll(Assembly.GetExecutingAssembly());

                string build = BuildInfo.BUILD_VERSION_STR;
                string modVersion = modEntry.Info != null ? modEntry.Info.Version : "1.0.1";
                modEntry.Logger.Log("Train Inspector " + modVersion + " loaded for Derail Valley 99.7 (detected " + build + ").");
                if (BuildInfo.BUILD_VERSION_MAJOR != 99)
                {
                    modEntry.Logger.Warning(
                        "This release was tested against Build 99.7. Detected " + build + "; brake API compatibility is not guaranteed.");
                }
            }
            catch (Exception exception)
            {
                modEntry.Logger.LogException("Train Inspector failed to initialize:", exception);
                try
                {
                    if (harmony != null)
                    {
                        harmony.UnpatchAll(harmony.Id);
                        harmony = null;
                    }
                }
                catch
                {
                    // Preserve the original initialization error.
                }

                return false;
            }

            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool enabled)
        {
            try
            {
                IsEnabled = enabled;
                if (enabled)
                {
                    EnsureController();
                }
                else
                {
                    DestroyController();
                }

                return true;
            }
            catch (Exception exception)
            {
                IsEnabled = false;
                modEntry.Logger.LogException("Failed to toggle Train Inspector:", exception);
                return false;
            }
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            IsEnabled = false;
            DestroyController();
            InspectionPersistence.Save();
            Settings.Save(modEntry);
            if (harmony != null)
            {
                harmony.UnpatchAll(harmony.Id);
                harmony = null;
            }

            return true;
        }

        private static void EnsureController()
        {
            if (controller != null)
            {
                return;
            }

            host = new GameObject("TrainInspector.Controller");
            UnityEngine.Object.DontDestroyOnLoad(host);
            controller = host.AddComponent<InspectorController>();
            host.AddComponent<InspectorInventoryItemManager>();
        }

        private static void DestroyController()
        {
            if (host != null)
            {
                UnityEngine.Object.Destroy(host);
            }

            host = null;
            controller = null;
        }

        internal static bool IsInspectorWindowVisible
        {
            get { return controller != null && controller.IsWindowVisible; }
        }

        internal static void SetInspectorWindowVisible(bool visible)
        {
            if (!IsEnabled || controller == null)
            {
                return;
            }

            controller.SetWindowVisible(visible);
        }

        private static void DrawModManagerGui(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("Guided physical train inspection and brake test for Derail Valley Build 99.7.");
            string hotkeyText = Settings != null ? Settings.ToggleKeyDisplay : "F7";
            GUILayout.Label("Equip/use the Inspection Clipboard, or press " + hotkeyText + ", to open or close the inspector.");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Toggle hotkey:", GUILayout.Width(150f));
            UnityModManager.UI.DrawKeybindingSmart(Settings.ToggleKey, "Toggle hotkey");
            if (GUILayout.Button("Reset to F7", GUILayout.Width(100f)))
            {
                Settings.ToggleKey = new KeyBinding { keyCode = KeyCode.F7 };
            }
            GUILayout.EndHorizontal();

            if (controller != null)
            {
                if (GUILayout.Button(controller.IsWindowVisible ? "Close inspector" : "Open inspector",
                        GUILayout.Width(180f)))
                {
                    controller.SetWindowVisible(!controller.IsWindowVisible);
                }
            }

            if (GUILayout.Button("Get Inspection Clipboard in inventory", GUILayout.Width(260f)))
            {
                InspectorInventoryItemManager.EnsureClipboardNow();
            }

            GUILayout.Space(8f);
            GUILayout.Label("Inspection assistance");
            Settings.AssistedMode = GUILayout.Toggle(Settings.AssistedMode,
                "Assisted mode (guide the walk-around car by car)");
            Settings.RequirePhysicalWalkaround = GUILayout.Toggle(Settings.RequirePhysicalWalkaround,
                "Require the player to walk within range of each inspected car");
            Settings.ShowDepartureWarnings = GUILayout.Toggle(Settings.ShowDepartureWarnings,
                "Show departure, failure, and rolling-inspection warnings in the inspector");
            Settings.WalkaroundRangeMetres = DrawSlider("Walk-around range",
                Settings.WalkaroundRangeMetres, 4f, 25f, "0.0", "m");
            Settings.LeakageHoldSeconds = DrawSlider("Leakage hold",
                Settings.LeakageHoldSeconds, 5f, 60f, "0", "s");
            Settings.MaximumLeakageBar = DrawSlider("Maximum leakage",
                Settings.MaximumLeakageBar, 0.02f, 1f, "0.00", "bar");
            Settings.ReleasedCylinderPressure = DrawSlider("Released cylinder threshold",
                Settings.ReleasedCylinderPressure, 0.95f, 1.45f, "0.00", "bar");
            Settings.CertificateValidityHours = DrawSlider("Certificate validity",
                Settings.CertificateValidityHours, 0.25f, 72f, "0.00", "h");

            GUILayout.Space(8f);
            GUILayout.Label("Mechanical faults and equipment defects");
            Settings.EnableMechanicalFailures = GUILayout.Toggle(Settings.EnableMechanicalFailures,
                "Enable mechanical equipment failures and defects");
            Settings.EnablePreExistingDefects = GUILayout.Toggle(Settings.EnablePreExistingDefects,
                "Random pre-existing defects on consist equipment before inspection");
            Settings.PreExistingDefectChancePercent = DrawSlider("Pre-existing defect chance per car",
                Settings.PreExistingDefectChancePercent, 0f, 50f, "0.0", "%");
            Settings.AllowCouplerSeparationFaults = GUILayout.Toggle(Settings.AllowCouplerSeparationFaults,
                "Allow coupler separation defects (high consequence)");

            GUILayout.Space(8f);
            GUILayout.Label("Consequences for uninspected or defective departure");
            Settings.EnableWalletPenalties = GUILayout.Toggle(Settings.EnableWalletPenalties,
                "Deduct inspection and safety violation fines from the player wallet");
            Settings.DepartureSpeedKmh = DrawSlider("Departure trigger speed",
                Settings.DepartureSpeedKmh, 1f, 30f, "0.0", "km/h");
            Settings.UncheckedItemFailureChancePercent = DrawSlider("Fault chance per missed equipment check",
                Settings.UncheckedItemFailureChancePercent, 0f, 25f, "0.0", "%");
            Settings.MaximumFaultsPerDeparture = Mathf.RoundToInt(DrawSlider("Maximum new faults per departure",
                Settings.MaximumFaultsPerDeparture, 0f, 10f, "0", string.Empty));
            Settings.BaseMissedInspectionFine = Mathf.RoundToInt(DrawSlider("Base missed inspection fine",
                Settings.BaseMissedInspectionFine, 0f, 5000f, "0", "$"));
            Settings.FinePerMissedItem = Mathf.RoundToInt(DrawSlider("Fine per missed requirement",
                Settings.FinePerMissedItem, 0f, 500f, "0", "$"));
            Settings.BadOrderDepartureFine = Mathf.RoundToInt(DrawSlider("Fine per bad-order car on departure",
                Settings.BadOrderDepartureFine, 0f, 5000f, "0", "$"));
            Settings.Clamp();
        }

        private static void SaveModManagerGui(UnityModManager.ModEntry modEntry)
        {
            Settings.Save(modEntry);
            InspectionPersistence.Save();
        }

        private static float DrawSlider(string label, float value, float minimum, float maximum,
            string format, string suffix)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(265f));
            float changed = GUILayout.HorizontalSlider(value, minimum, maximum, GUILayout.Width(220f));
            string rendered = changed.ToString(format);
            if (!string.IsNullOrEmpty(suffix))
            {
                rendered = suffix == "$" ? suffix + rendered : rendered + " " + suffix;
            }

            GUILayout.Label(rendered, GUILayout.Width(90f));
            GUILayout.EndHorizontal();
            return changed;
        }
    }
}
