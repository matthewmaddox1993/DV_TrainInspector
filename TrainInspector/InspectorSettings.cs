using UnityEngine;
using UnityModManagerNet;

namespace TrainInspector
{
    public sealed class InspectorSettings : UnityModManager.ModSettings
    {
        public bool AssistedMode = true;
        public bool RequirePhysicalWalkaround = true;
        public bool EnableWalletPenalties = true;
        public bool EnableMechanicalFailures = true;
        public bool EnablePreExistingDefects = true;
        public bool AllowCouplerSeparationFaults = false;
        public bool ShowDepartureWarnings = true;
        public KeyBinding ToggleKey = new KeyBinding { keyCode = KeyCode.F7 };

        public float WalkaroundRangeMetres = 9f;
        public float DepartureSpeedKmh = 5f;
        public float LeakageHoldSeconds = 15f;
        public float MaximumLeakageBar = 0.15f;
        public float ReleasedCylinderPressure = 1.10f;
        public float CertificateValidityHours = 8f;
        public float UncheckedItemFailureChancePercent = 2f;
        public float PreExistingDefectChancePercent = 5f;

        public int BaseMissedInspectionFine = 250;
        public int FinePerMissedItem = 25;
        public int BadOrderDepartureFine = 500;
        public int MaximumFaultsPerDeparture = 2;

        public string ToggleKeyDisplay
        {
            get
            {
                if (ToggleKey == null || ToggleKey.keyCode == KeyCode.None)
                {
                    return "Unbound";
                }

                return ToggleKey.ToString();
            }
        }

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Clamp();
            Save(this, modEntry);
        }

        internal void Clamp()
        {
            if (ToggleKey == null)
            {
                ToggleKey = new KeyBinding { keyCode = KeyCode.F7 };
            }

            WalkaroundRangeMetres = Mathf.Clamp(WalkaroundRangeMetres, 4f, 25f);
            DepartureSpeedKmh = Mathf.Clamp(DepartureSpeedKmh, 1f, 30f);
            LeakageHoldSeconds = Mathf.Clamp(LeakageHoldSeconds, 5f, 60f);
            MaximumLeakageBar = Mathf.Clamp(MaximumLeakageBar, 0.02f, 1f);
            if (ReleasedCylinderPressure < 0.95f)
            {
                // In Derail Valley, brake cylinders vent to atmospheric pressure (1.00 bar absolute).
                // Automatically migrate legacy 0.35 bar configs to the realistic 1.10 bar threshold.
                ReleasedCylinderPressure = 1.10f;
            }

            ReleasedCylinderPressure = Mathf.Clamp(ReleasedCylinderPressure, 0.95f, 1.45f);
            CertificateValidityHours = Mathf.Clamp(CertificateValidityHours, 0.25f, 72f);
            UncheckedItemFailureChancePercent = Mathf.Clamp(UncheckedItemFailureChancePercent, 0f, 25f);
            PreExistingDefectChancePercent = Mathf.Clamp(PreExistingDefectChancePercent, 0f, 50f);
            BaseMissedInspectionFine = Mathf.Clamp(BaseMissedInspectionFine, 0, 100000);
            FinePerMissedItem = Mathf.Clamp(FinePerMissedItem, 0, 10000);
            BadOrderDepartureFine = Mathf.Clamp(BadOrderDepartureFine, 0, 100000);
            MaximumFaultsPerDeparture = Mathf.Clamp(MaximumFaultsPerDeparture, 0, 10);
        }
    }
}
