using System;
using System.Collections.Generic;
using DV.Simulation.Brake;
using UnityEngine;

namespace TrainInspector
{
    internal enum InspectionPhase
    {
        Charging,
        Leakage,
        Applying,
        Releasing,
        Complete,
        Invalidated
    }

    internal enum CarStatusKind
    {
        Normal,
        Ready,
        Warning,
        Failure,
        NotTested
    }

    internal sealed class CarInspectionRecord
    {
        internal TrainCar Car;
        internal int Number;
        internal int InstanceId;
        internal string Id;
        internal string Guid;
        internal WalkaroundItem RequiredItems;
        internal WalkaroundItem CheckedItems;
        internal readonly List<string> PhysicalFindings = new List<string>();

        internal float CurrentPipe;
        internal float CurrentCylinder;
        internal float CurrentHandbrake;
        internal float CurrentTemperature;
        internal float CurrentBrakingFactor;
        internal float MassTonnes;
        internal float DistanceToPlayer = float.MaxValue;

        internal float ChargedPipe;
        internal float BaselineCylinder;
        internal float PeakCylinder;
        internal float LowestReleaseCylinder;
        internal float MaxHandbrakeDuringTest;
        internal float ApplicationResponseSeconds = -1f;
        internal float ReleaseResponseSeconds = -1f;

        internal bool ReachableDuringTest;
        internal bool BadOrdered;
        internal Brakeset TestBrakeSet;

        internal bool HasBrakeSystem
        {
            get { return Car != null && Car.brakeSystem != null; }
        }

        internal bool Applied(float minimumRise)
        {
            return HasBrakeSystem && PeakCylinder - BaselineCylinder >= minimumRise;
        }

        internal bool Released(float releasedPressure)
        {
            float acceptable = Mathf.Max(releasedPressure, BaselineCylinder + 0.15f);
            return Applied(InspectionSession.MinimumCylinderRise) && LowestReleaseCylinder <= acceptable;
        }

        internal bool IsChecked(WalkaroundItem item)
        {
            return (CheckedItems & item) == item;
        }

        internal int MissingItemCount
        {
            get { return InspectionTypeRules.Expand(RequiredItems & ~CheckedItems).Count; }
        }

        internal string Label
        {
            get { return "car " + Number + " (" + Id + ")"; }
        }
    }

    internal sealed class ContinuityBreak
    {
        internal int AfterCarNumber;
        internal string BeforeId;
        internal string AfterId;

        internal string Description
        {
            get
            {
                return "between car " + AfterCarNumber + " (" + BeforeId + ") and car "
                       + (AfterCarNumber + 1) + " (" + AfterId + ")";
            }
        }
    }

    internal sealed class CabSetupSnapshot
    {
        internal CarInspectionRecord ControlRecord;
        internal bool CanRecord;
        internal string RecordBlockReason;
        internal bool HasBrakeSystem;
        internal bool CompressorAvailable;
        internal float MainReservoirPressure;
        internal bool MainReservoirReady;
        internal bool HasTrainBrake;
        internal bool ControllingValveCutIn;
        internal int EnabledTrainBrakeValves;
        internal bool ExactlyOneTrainBrakeValveCutIn;
        internal bool HasIndependentBrake;
        internal float IndependentBrakePosition;
        internal float BrakeCylinderPressure;
        internal bool IndependentBrakeReleased;

        internal bool AllEquipmentReady
        {
            get
            {
                return HasBrakeSystem && CompressorAvailable && MainReservoirReady
                       && ControllingValveCutIn && ExactlyOneTrainBrakeValveCutIn
                       && IndependentBrakeReleased;
            }
        }
    }

    internal sealed class InspectionReport
    {
        internal bool Passed;
        internal string Summary;
        internal DateTime CompletedAt;
        internal float TotalMassTonnes;
        internal float TestedBrakeMassPercent;
        internal float LeakageDropBar;
        internal readonly List<string> Findings = new List<string>();
    }

    internal sealed class InspectionSession : IDisposable
    {
        internal const float ChargedPipePressure = 4.5f;
        internal static float ReleasedCylinderPressure
        {
            get { return Main.Settings != null ? Main.Settings.ReleasedCylinderPressure : 1.10f; }
        }
        internal const float MinimumCylinderRise = 0.35f;
        internal const float MinimumServiceReduction = 0.75f;
        internal const float MinimumMainReservoirPressure = 6.5f;
        internal const float SlowApplicationSeconds = 45f;
        internal const float SlowReleaseSeconds = 60f;

        private readonly List<CarInspectionRecord> cars;
        private readonly HashSet<int> originalCarIds;
        private readonly string originalConsistSignature;
        private readonly Action<Trainset> trainsetChangedHandler;
        private readonly List<ContinuityBreak> testedContinuityBreaks = new List<ContinuityBreak>();
        private readonly List<string> cabFindings = new List<string>();

        private Trainset originalTrainset;
        private TrainCar preferredCar;
        private bool disposed;
        private bool subscribed;
        private bool controlCutoutDuringTest;
        private bool testBrakeSetWasLeaking;
        private float phaseElapsed;
        private float observationElapsed;
        private float proximityElapsed;
        private float consistCheckElapsed;
        private float chargedControlPipe;
        private float leakageStartPipe;
        private float leakageLowestPipe;
        private float leakageElapsed;
        private bool leakageCutoutObserved;
        private float applicationReductionStartTime = -1f;
        private float releaseRechargeStartTime = -1f;

        internal InspectionPhase Phase { get; private set; }
        internal InspectionType Type { get; private set; }
        internal int ControlIndex { get; private set; }
        internal bool IsReversed { get; private set; }
        internal string InvalidReason { get; private set; }
        internal InspectionReport Report { get; private set; }
        internal InspectionCertificateData Certificate { get; private set; }
        internal CabSetupItem CabPassedItems { get; private set; }
        internal bool CabSetupInspected { get; private set; }
        internal bool LeakageCompleted { get; private set; }
        internal bool RearApplicationConfirmed { get; private set; }
        internal bool RearReleaseConfirmed { get; private set; }

        internal IList<CarInspectionRecord> Cars
        {
            get { return cars.AsReadOnly(); }
        }

        internal IList<string> CabFindings
        {
            get { return cabFindings.AsReadOnly(); }
        }

        internal TrainCar PreferredCar
        {
            get { return preferredCar; }
        }

        internal TrainCar ControlCar
        {
            get
            {
                if (ControlIndex < 0 || ControlIndex >= cars.Count)
                {
                    return null;
                }

                return cars[ControlIndex].Car;
            }
        }

        internal CarInspectionRecord ControlRecord
        {
            get
            {
                if (ControlIndex < 0 || ControlIndex >= cars.Count)
                {
                    return null;
                }

                return cars[ControlIndex];
            }
        }

        internal CarInspectionRecord RearRecord
        {
            get
            {
                if (cars.Count == 0)
                {
                    return null;
                }

                // Rear verification belongs at the end farthest from the controlling valve,
                // regardless of the numbering direction selected by the player.
                return ControlIndex <= (cars.Count - 1) / 2 ? cars[cars.Count - 1] : cars[0];
            }
        }

        internal int CarCount
        {
            get { return cars.Count; }
        }

        internal float PhaseElapsed
        {
            get { return phaseElapsed; }
        }

        internal float ChargedControlPipe
        {
            get { return chargedControlPipe; }
        }

        internal float LeakageElapsed
        {
            get { return leakageElapsed; }
        }

        internal bool LeakageCutoutObserved
        {
            get { return leakageCutoutObserved; }
        }

        internal float LeakageDrop
        {
            get { return Mathf.Max(0f, leakageStartPipe - leakageLowestPipe); }
        }

        internal float CurrentControlPipe
        {
            get
            {
                BrakeSystem brake = GetControlBrake();
                if (brake == null || brake.brakeset == null)
                {
                    return 0f;
                }

                return brake.brakeset.pipePressure;
            }
        }

        internal float ServiceReduction
        {
            get { return Mathf.Max(0f, chargedControlPipe - CurrentControlPipe); }
        }

        internal int WalkaroundCheckedCount
        {
            get
            {
                int result = 0;
                for (int index = 0; index < cars.Count; index++)
                {
                    WalkaroundItem checkedRequired = cars[index].CheckedItems & cars[index].RequiredItems;
                    result += InspectionTypeRules.Expand(checkedRequired).Count;
                }

                return result;
            }
        }

        internal int WalkaroundRequiredCount
        {
            get
            {
                int result = 0;
                for (int index = 0; index < cars.Count; index++)
                {
                    result += InspectionTypeRules.Expand(cars[index].RequiredItems).Count;
                }

                return result;
            }
        }

        private InspectionSession(Trainset trainset, TrainCar preferred, bool reverse, InspectionType type)
        {
            originalTrainset = trainset;
            preferredCar = preferred;
            IsReversed = reverse;
            Type = type;
            cars = new List<CarInspectionRecord>();
            originalCarIds = new HashSet<int>();
            originalConsistSignature = TrainIdentity.BuildConsistSignature(trainset);
            trainsetChangedHandler = OnSelectedCarTrainsetChanged;
            ControlIndex = -1;
            Phase = InspectionPhase.Charging;

            List<TrainCar> ordered = BuildAutomaticOrder(trainset, preferred);
            if (reverse)
            {
                ordered.Reverse();
            }

            WalkaroundItem required = Main.Settings.RequirePhysicalWalkaround
                ? InspectionTypeRules.RequiredWalkaroundItems(type)
                : WalkaroundItem.None;
            for (int index = 0; index < ordered.Count; index++)
            {
                TrainCar car = ordered[index];
                WalkaroundItem previouslyChecked = CarInspectionHistory.GetValidCheckedItems(car);
                CarInspectionRecord record = new CarInspectionRecord
                {
                    Car = car,
                    Number = index + 1,
                    InstanceId = car.GetInstanceID(),
                    Id = TrainIdentity.SafeCarId(car),
                    Guid = TrainIdentity.SafeCarGuid(car),
                    RequiredItems = required,
                    CheckedItems = previouslyChecked & required
                };
                cars.Add(record);
                originalCarIds.Add(record.InstanceId);
            }

            ControlIndex = FindControlIndex(preferred);
            RefreshObservations();
            UpdatePlayerProximity();
            SubscribeToConsist();
        }

        internal static InspectionSession Create(Trainset trainset, TrainCar preferred, bool reverse,
            InspectionType type)
        {
            if (trainset == null || trainset.cars == null || trainset.cars.Count == 0)
            {
                return null;
            }

            FaultRegistry.RollForPreExistingDefects(trainset, Main.Settings);

            InspectionSession session = new InspectionSession(trainset, preferred, reverse, type);
            session.SaveActiveState();
            return session;
        }

        internal static InspectionSession Restore(Trainset trainset, ActiveSessionData data)
        {
            if (trainset == null || trainset.cars == null || trainset.cars.Count == 0 || data == null)
            {
                return null;
            }

            TrainCar preferred = null;
            if (!string.IsNullOrEmpty(data.PreferredCarGuid))
            {
                for (int i = 0; i < trainset.cars.Count; i++)
                {
                    if (TrainIdentity.SafeCarGuid(trainset.cars[i]) == data.PreferredCarGuid)
                    {
                        preferred = trainset.cars[i];
                        break;
                    }
                }
            }

            if (preferred == null)
            {
                preferred = TrainIdentity.FindControllingCar(trainset);
            }

            InspectionType type = (InspectionType)data.TypeValue;
            InspectionSession session = new InspectionSession(trainset, preferred, data.IsReversed, type);
            session.CabSetupInspected = data.CabSetupInspected;
            session.CabPassedItems = (CabSetupItem)data.CabPassedItemsValue;
            session.LeakageCompleted = data.LeakageCompleted;
            session.RearApplicationConfirmed = data.RearApplicationConfirmed;
            session.RearReleaseConfirmed = data.RearReleaseConfirmed;

            InspectionPhase savedPhase = (InspectionPhase)data.PhaseValue;
            if (savedPhase == InspectionPhase.Applying || savedPhase == InspectionPhase.Releasing)
            {
                BrakeSystem control = session.GetControlBrake();
                if (control != null && control.brakeset != null)
                {
                    session.chargedControlPipe = ChargedPipePressure;
                    session.controlCutoutDuringTest = control.trainBrakeCutout;
                    session.testBrakeSetWasLeaking = control.brakeset.isLeaking;
                }

                for (int i = 0; i < session.cars.Count; i++)
                {
                    CarInspectionRecord record = session.cars[i];
                    BrakeSystem brake = GetBrake(record);
                    record.ChargedPipe = ChargedPipePressure;
                    record.BaselineCylinder = 0f;
                    record.PeakCylinder = record.CurrentCylinder;
                    record.LowestReleaseCylinder = record.CurrentCylinder;
                    record.ReachableDuringTest = session.IsReachableNow(record);
                    record.TestBrakeSet = brake == null ? null : brake.brakeset;
                }

                session.Phase = savedPhase;
                session.phaseElapsed = data.PhaseElapsed;
            }
            else if (savedPhase == InspectionPhase.Leakage)
            {
                session.Phase = InspectionPhase.Leakage;
                session.phaseElapsed = data.PhaseElapsed;
                session.leakageStartPipe = session.CurrentControlPipe;
                session.leakageLowestPipe = session.leakageStartPipe;
            }
            else
            {
                session.Phase = InspectionPhase.Charging;
                session.phaseElapsed = data.PhaseElapsed;
            }

            session.SaveActiveState();
            return session;
        }

        internal void SaveActiveState()
        {
            if (Phase == InspectionPhase.Complete || Phase == InspectionPhase.Invalidated || disposed)
            {
                ClearActiveState();
                return;
            }

            if (InspectionPersistence.Data == null)
            {
                return;
            }

            InspectionPersistence.Data.ActiveSession = new ActiveSessionData
            {
                ConsistSignature = originalConsistSignature,
                PreferredCarGuid = TrainIdentity.SafeCarGuid(preferredCar),
                TypeValue = (int)Type,
                PhaseValue = (int)Phase,
                IsReversed = IsReversed,
                CabSetupInspected = CabSetupInspected,
                CabPassedItemsValue = (int)CabPassedItems,
                LeakageCompleted = LeakageCompleted,
                RecordedLeakage = LeakageDrop,
                ServiceReduction = ServiceReduction,
                RearApplicationConfirmed = RearApplicationConfirmed,
                RearReleaseConfirmed = RearReleaseConfirmed,
                PhaseElapsed = phaseElapsed
            };
            InspectionPersistence.Save();
        }

        internal static void ClearActiveState()
        {
            if (InspectionPersistence.Data != null && InspectionPersistence.Data.ActiveSession != null)
            {
                InspectionPersistence.Data.ActiveSession = null;
                InspectionPersistence.Save();
            }
        }

        internal Trainset GetCurrentTrainset()
        {
            for (int index = 0; index < cars.Count; index++)
            {
                TrainCar car = cars[index].Car;
                if (car != null && car.trainset != null)
                {
                    return car.trainset;
                }
            }

            return null;
        }

        internal void Tick(float deltaTime)
        {
            if (disposed || Phase == InspectionPhase.Invalidated)
            {
                return;
            }

            float elapsed = Mathf.Max(0f, deltaTime);

            consistCheckElapsed += elapsed;
            if (consistCheckElapsed >= 0.2f)
            {
                consistCheckElapsed = 0f;
                if (!ConsistStillMatches())
                {
                    if (Phase == InspectionPhase.Charging)
                    {
                        SyncConsist();
                    }
                    else if (Phase == InspectionPhase.Complete)
                    {
                        SyncConsist();
                        Phase = InspectionPhase.Charging;
                        Report = null;
                        Certificate = null;
                        Main.ModEntry.Logger.Log("Consist changed after inspection: new cars attached. Conduct brake test for updated consist.");
                    }
                    else
                    {
                        Invalidate("The consist changed. Couple/uncouple activity invalidates this active brake test.");
                        return;
                    }
                }
            }

            observationElapsed += elapsed;
            if (observationElapsed >= 0.05f)
            {
                observationElapsed = 0f;
                RefreshObservations();
            }

            proximityElapsed += elapsed;
            if (proximityElapsed >= 0.1f)
            {
                proximityElapsed = 0f;
                UpdatePlayerProximity();
            }

            if (Phase == InspectionPhase.Complete && Certificate != null)
            {
                TrainCar controlling = TrainIdentity.FindControllingCar(GetCurrentTrainset());
                string controlGuid = TrainIdentity.SafeCarGuid(controlling);
                if (controlGuid != Certificate.ControlCarGuid && !ConsistContainsCarGuid(Certificate.ControlCarGuid))
                {
                    Invalidate("The controlling cab is no longer part of this consist. This inspection certificate is no longer valid.");
                    return;
                }
            }

            if ((Phase == InspectionPhase.Applying || Phase == InspectionPhase.Releasing)
                && !BrakeConfigurationStillMatches())
            {
                Invalidate("Brake-pipe connections or the controlling brake cutout changed during the test. Restart the check.");
                return;
            }

            phaseElapsed += elapsed;
            if (Phase == InspectionPhase.Leakage)
            {
                TickLeakage(elapsed);
            }
        }

        internal bool InspectCabSetup(out string result)
        {
            cabFindings.Clear();
            CabPassedItems = CabSetupItem.None;
            CabSetupSnapshot status = GetCabSetupSnapshot();
            if (status.ControlRecord == null || status.ControlRecord.Car == null)
            {
                result = "No controlling locomotive was found.";
                cabFindings.Add(result);
                CabSetupInspected = true;
                return false;
            }

            if (!status.CanRecord)
            {
                result = status.RecordBlockReason;
                return false;
            }

            CabSetupInspected = true;
            if (!status.HasBrakeSystem)
            {
                cabFindings.Add("The selected control car has no brake system.");
                result = cabFindings[0];
                return false;
            }

            if (status.CompressorAvailable)
            {
                CabPassedItems |= CabSetupItem.CompressorAvailable;
            }
            else
            {
                cabFindings.Add("No compressor is available on the controlling car.");
            }

            if (status.MainReservoirReady)
            {
                CabPassedItems |= CabSetupItem.MainReservoir;
            }
            else
            {
                cabFindings.Add("Main reservoir is only " + status.MainReservoirPressure.ToString("0.0")
                                + " bar; charge it above " + MinimumMainReservoirPressure.ToString("0.0") + " bar.");
            }

            if (status.ControllingValveCutIn)
            {
                CabPassedItems |= CabSetupItem.ControllingValve;
            }
            else
            {
                cabFindings.Add("Set the controlling cab's automatic/train-brake cutout to CUT IN (active).");
            }

            if (status.ExactlyOneTrainBrakeValveCutIn)
            {
                CabPassedItems |= CabSetupItem.TrailingValvesCutOut;
            }
            else
            {
                cabFindings.Add("Exactly one automatic/train-brake valve must be CUT IN across the connected brake "
                                + "pipe; detected " + status.EnabledTrainBrakeValves
                                + ". Set every trailing cab's valve to CUT OUT.");
            }

            if (status.IndependentBrakeReleased)
            {
                CabPassedItems |= CabSetupItem.IndependentBrakeReleased;
            }
            else
            {
                if (status.HasIndependentBrake && status.IndependentBrakePosition > 0.05f)
                {
                    cabFindings.Add("Move the independent brake fully to RELEASE; it is currently "
                                    + (status.IndependentBrakePosition * 100f).ToString("0") + "% applied.");
                }

                if (status.BrakeCylinderPressure > ReleasedCylinderPressure)
                {
                    cabFindings.Add("Wait for the controlling brake cylinder to fall to "
                                    + ReleasedCylinderPressure.ToString("0.00") + " bar or below; it is currently "
                                    + status.BrakeCylinderPressure.ToString("0.00")
                                    + " bar. Ensure both automatic and independent brakes are released.");
                }
            }

            bool passed = CabPassedItems == CabSetupItem.All;
            result = passed ? "Cab setup verified." : string.Join(" ", cabFindings.ToArray());
            SaveActiveState();
            return passed;
        }

        internal CabSetupSnapshot GetCabSetupSnapshot()
        {
            CabSetupSnapshot status = new CabSetupSnapshot
            {
                ControlRecord = ControlRecord,
                RecordBlockReason = string.Empty
            };

            if (status.ControlRecord == null || status.ControlRecord.Car == null)
            {
                status.RecordBlockReason = "No controlling locomotive was found.";
                return status;
            }

            status.CanRecord = CanInspectRecord(status.ControlRecord, out string reason);
            status.RecordBlockReason = reason;

            BrakeSystem control = GetControlBrake();
            if (control == null)
            {
                return status;
            }

            status.HasBrakeSystem = true;
            status.CompressorAvailable = control.hasCompressor;
            status.MainReservoirPressure = control.mainReservoirPressure;
            status.MainReservoirReady = status.MainReservoirPressure >= MinimumMainReservoirPressure;
            status.HasTrainBrake = control.hasTrainBrake;
            status.ControllingValveCutIn = status.HasTrainBrake && control.trainBrakeCutout;
            status.EnabledTrainBrakeValves = control.brakeset == null
                ? 0
                : CountEnabledTrainBrakeValves(control.brakeset);
            status.ExactlyOneTrainBrakeValveCutIn = status.EnabledTrainBrakeValves == 1;
            status.HasIndependentBrake = control.hasIndependentBrake;
            status.IndependentBrakePosition = control.independentBrakePosition;
            status.BrakeCylinderPressure = control.brakeCylinderPressure;
            status.IndependentBrakeReleased = (!status.HasIndependentBrake
                                                || status.IndependentBrakePosition <= 0.05f)
                                               && status.BrakeCylinderPressure <= ReleasedCylinderPressure;
            return status;
        }

        internal bool TrySetControlCar(TrainCar car, out string reason)
        {
            if (Phase != InspectionPhase.Charging)
            {
                reason = "The controlling locomotive can only be changed during the initial charging/preparation phase.";
                return false;
            }

            if (car == null)
            {
                reason = "No locomotive specified.";
                return false;
            }

            int index = FindCarIndex(car);
            if (index < 0)
            {
                reason = "Car " + TrainIdentity.SafeCarId(car) + " is not part of this consist.";
                return false;
            }

            if (!TrainIdentity.HasTrainBrake(car))
            {
                reason = "Car " + TrainIdentity.SafeCarId(car) + " has no train-brake valve.";
                return false;
            }

            ControlIndex = index;
            CabPassedItems = CabSetupItem.None;
            CabSetupInspected = false;
            cabFindings.Clear();
            reason = "Controlling locomotive set to " + cars[index].Label + ".";
            return true;
        }

        internal bool InspectItem(CarInspectionRecord record, WalkaroundItem item, out string result)
        {
            result = string.Empty;
            if (record == null || record.Car == null)
            {
                result = "That car is no longer available.";
                return false;
            }

            if (item == WalkaroundItem.LeftSide || item == WalkaroundItem.RightSide || item == WalkaroundItem.None)
            {
                result = "Side visits are recorded automatically while you walk beside the car.";
                return false;
            }

            if (!CanInspectRecord(record, out result))
            {
                return false;
            }

            record.CheckedItems |= item;
            CarInspectionHistory.RecordCarChecked(record.Car, record.CheckedItems);
            ClearPreviousIssuesForItem(record, item);
            List<string> findings = FindPhysicalIssues(record, item);
            List<MechanicalFaultData> revealed = FaultRegistry.RevealForItem(record.Car, item);
            for (int index = 0; index < revealed.Count; index++)
            {
                AddFinding(record, "Simulated defect found: " + FaultRegistry.Describe(revealed[index]) + ".");
            }

            if (findings.Count == 0 && revealed.Count == 0)
            {
                if (record.PhysicalFindings.Count == 0 && FaultRegistry.GetFaults(record.Car, false).Count == 0)
                {
                    record.BadOrdered = false;
                }

                result = InspectionTypeRules.ItemLabel(item) + " checked - no visible defect.";
            }
            else
            {
                record.BadOrdered = true;
                result = string.Join(" ", findings.ToArray());
                if (revealed.Count > 0)
                {
                    result += (result.Length == 0 ? string.Empty : " ") + revealed.Count
                              + " mechanical defect" + (revealed.Count == 1 ? "" : "s") + " found.";
                }
            }

            SaveActiveState();
            return true;
        }

        private static void ClearPreviousIssuesForItem(CarInspectionRecord record, WalkaroundItem item)
        {
            if (record == null || record.PhysicalFindings == null)
            {
                return;
            }

            for (int i = record.PhysicalFindings.Count - 1; i >= 0; i--)
            {
                string finding = record.PhysicalFindings[i];
                bool match = false;
                if (item == WalkaroundItem.HoseAndAngleCocks)
                {
                    match = finding.Contains("brake hose") || finding.Contains("angle cock")
                            || finding.Contains("brake-pipe leakage");
                }
                else if (item == WalkaroundItem.Coupler)
                {
                    match = finding.Contains("mechanically coupled") || finding.Contains("defective coupler");
                }
                else if (item == WalkaroundItem.Handbrake)
                {
                    match = finding.Contains("Handbrake is applied") || finding.Contains("dragging handbrake");
                }
                else if (item == WalkaroundItem.BrakeRigging)
                {
                    match = finding.Contains("Brake cylinder remains pressurized")
                            || finding.Contains("weak brake rigging") || finding.Contains("sticking brake cylinder");
                }
                else if (item == WalkaroundItem.WheelsAndBearings)
                {
                    match = finding.Contains("temperature is") || finding.Contains("overheating wheel bearing");
                }

                if (match)
                {
                    record.PhysicalFindings.RemoveAt(i);
                }
            }
        }

        internal void SyncBadOrderState(CarInspectionRecord record)
        {
            if (record == null)
            {
                return;
            }

            List<MechanicalFaultData> remainingFaults = FaultRegistry.GetFaults(record.Car, false);
            if (remainingFaults.Count == 0)
            {
                for (int i = record.PhysicalFindings.Count - 1; i >= 0; i--)
                {
                    if (record.PhysicalFindings[i].StartsWith("Simulated defect found:"))
                    {
                        record.PhysicalFindings.RemoveAt(i);
                    }
                }
            }
            else
            {
                for (int i = record.PhysicalFindings.Count - 1; i >= 0; i--)
                {
                    string finding = record.PhysicalFindings[i];
                    if (finding.StartsWith("Simulated defect found:"))
                    {
                        bool stillPresent = false;
                        for (int j = 0; j < remainingFaults.Count; j++)
                        {
                            if (finding.Contains(FaultRegistry.Describe(remainingFaults[j])))
                            {
                                stillPresent = true;
                                break;
                            }
                        }

                        if (!stillPresent)
                        {
                            record.PhysicalFindings.RemoveAt(i);
                        }
                    }
                }
            }

            if (record.PhysicalFindings.Count == 0 && remainingFaults.Count == 0)
            {
                record.BadOrdered = false;
            }

            SaveActiveState();
        }

        internal void ToggleBadOrder(CarInspectionRecord record)
        {
            if (record != null)
            {
                record.BadOrdered = !record.BadOrdered;
            }
        }

        internal bool CanInspect(CarInspectionRecord record, out string reason)
        {
            return CanInspectRecord(record, out reason);
        }

        internal bool CanBeginLeakage(out string reason)
        {
            if (Phase != InspectionPhase.Charging)
            {
                reason = "The charge step is not active.";
                return false;
            }

            if (!InspectionTypeRules.RequiresLeakageTest(Type))
            {
                reason = "This inspection type does not require a leakage hold.";
                return false;
            }

            if (LeakageCompleted)
            {
                reason = "The leakage hold is already complete.";
                return false;
            }

            if (!CanLeavePreparation(out reason))
            {
                return false;
            }

            return CanUseChargedTrain(out reason);
        }

        internal bool BeginLeakage()
        {
            string ignored;
            if (!CanBeginLeakage(out ignored))
            {
                return false;
            }

            leakageStartPipe = CurrentControlPipe;
            leakageLowestPipe = leakageStartPipe;
            leakageElapsed = 0f;
            leakageCutoutObserved = false;
            Phase = InspectionPhase.Leakage;
            phaseElapsed = 0f;
            return true;
        }

        internal bool CanCompleteLeakage(out string reason)
        {
            if (Phase != InspectionPhase.Leakage)
            {
                reason = "The leakage hold is not active.";
                return false;
            }

            BrakeSystem control = GetControlBrake();
            if (control == null)
            {
                reason = "The controlling brake system is unavailable.";
                return false;
            }

            if (control.trainBrakeCutout)
            {
                reason = leakageCutoutObserved
                    ? "Keep the automatic brake valve cut out until the timer finishes."
                    : "Cut out the automatic brake valve to start the pressure-hold timer.";
                return false;
            }

            if (leakageElapsed < Main.Settings.LeakageHoldSeconds)
            {
                reason = "Hold pressure for "
                         + Mathf.CeilToInt(Main.Settings.LeakageHoldSeconds - leakageElapsed) + " more seconds.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        internal bool CompleteLeakage()
        {
            string ignored;
            if (!CanCompleteLeakage(out ignored))
            {
                return false;
            }

            LeakageCompleted = true;
            Phase = InspectionPhase.Charging;
            phaseElapsed = 0f;
            SaveActiveState();
            return true;
        }

        internal bool CanBeginApplication(out string reason)
        {
            if (Phase != InspectionPhase.Charging)
            {
                reason = "The charge step is not active.";
                return false;
            }

            if (!InspectionTypeRules.RequiresBrakeCycle(Type))
            {
                reason = "This inspection type uses a stopped walk-around instead of a brake cycle.";
                return false;
            }

            if (InspectionTypeRules.RequiresLeakageTest(Type) && !LeakageCompleted)
            {
                reason = "Complete the leakage hold before the service application.";
                return false;
            }

            if (!CanLeavePreparation(out reason))
            {
                return false;
            }

            return CanUseChargedTrain(out reason);
        }

        internal bool BeginApplication()
        {
            string ignored;
            if (!CanBeginApplication(out ignored))
            {
                return false;
            }

            BrakeSystem control = GetControlBrake();
            chargedControlPipe = control.brakeset.pipePressure;
            controlCutoutDuringTest = control.trainBrakeCutout;
            testBrakeSetWasLeaking = control.brakeset.isLeaking;
            testedContinuityBreaks.Clear();
            applicationReductionStartTime = -1f;

            for (int index = 0; index < cars.Count; index++)
            {
                CarInspectionRecord record = cars[index];
                BrakeSystem brake = GetBrake(record);
                record.ChargedPipe = record.CurrentPipe;
                record.BaselineCylinder = record.CurrentCylinder;
                record.PeakCylinder = record.CurrentCylinder;
                record.LowestReleaseCylinder = record.CurrentCylinder;
                record.MaxHandbrakeDuringTest = record.CurrentHandbrake;
                record.ApplicationResponseSeconds = -1f;
                record.ReleaseResponseSeconds = -1f;
                record.ReachableDuringTest = IsReachableNow(record);
                record.TestBrakeSet = brake == null ? null : brake.brakeset;
            }

            CaptureContinuityBreaks();
            Phase = InspectionPhase.Applying;
            phaseElapsed = 0f;
            SaveActiveState();
            return true;
        }

        internal bool CanConfirmRearApplication(out string reason)
        {
            if (Phase != InspectionPhase.Applying)
            {
                reason = "The service application is not active.";
                return false;
            }

            CarInspectionRecord rear = RearRecord;
            if (!CanInspectRecord(rear, out reason))
            {
                return false;
            }

            if (!rear.ReachableDuringTest || !rear.Applied(MinimumCylinderRise))
            {
                reason = rear.Label + " has not produced a confirmed brake-cylinder response.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        internal bool ConfirmRearApplication(out string reason)
        {
            if (!CanConfirmRearApplication(out reason))
            {
                return false;
            }

            RearApplicationConfirmed = true;
            reason = "Rear-car application confirmed on " + RearRecord.Label + ".";
            SaveActiveState();
            return true;
        }

        internal bool CanBeginRelease(out string reason)
        {
            if (Phase != InspectionPhase.Applying)
            {
                reason = "The application step is not active.";
                return false;
            }

            if (phaseElapsed < 2f)
            {
                reason = "Hold the service application for at least two seconds.";
                return false;
            }

            if (ServiceReduction < MinimumServiceReduction)
            {
                reason = "Reduce brake-pipe pressure by at least "
                         + MinimumServiceReduction.ToString("0.00") + " bar (currently "
                         + ServiceReduction.ToString("0.00") + " bar).";
                return false;
            }

            if (InspectionTypeRules.RequiresRearVerification(Type) && !RearApplicationConfirmed)
            {
                reason = "Physically confirm the brake application on " + RearRecord.Label
                         + " before beginning the release.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        internal bool BeginRelease()
        {
            string ignored;
            if (!CanBeginRelease(out ignored))
            {
                return false;
            }

            RefreshObservations();
            for (int index = 0; index < cars.Count; index++)
            {
                CarInspectionRecord record = cars[index];
                record.LowestReleaseCylinder = record.CurrentCylinder;
                record.ReleaseResponseSeconds = -1f;
            }

            releaseRechargeStartTime = -1f;
            Phase = InspectionPhase.Releasing;
            phaseElapsed = 0f;
            SaveActiveState();
            return true;
        }

        internal bool CanConfirmRearRelease(out string reason)
        {
            if (Phase != InspectionPhase.Releasing)
            {
                reason = "The release step is not active.";
                return false;
            }

            CarInspectionRecord rear = RearRecord;
            if (!CanInspectRecord(rear, out reason))
            {
                return false;
            }

            if (!rear.Released(ReleasedCylinderPressure))
            {
                reason = rear.Label + " has not produced a confirmed cylinder release.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        internal bool ConfirmRearRelease(out string reason)
        {
            if (!CanConfirmRearRelease(out reason))
            {
                return false;
            }

            RearReleaseConfirmed = true;
            reason = "Rear-car release confirmed on " + RearRecord.Label + ".";
            SaveActiveState();
            return true;
        }

        internal bool CanFinish(out string reason)
        {
            if (Type == InspectionType.Rolling)
            {
                if (Phase != InspectionPhase.Charging)
                {
                    reason = "The rolling inspection is not active.";
                    return false;
                }

                if (WalkaroundCheckedCount < WalkaroundRequiredCount)
                {
                    CarInspectionRecord next = GetNextUncheckedRecord();
                    reason = "Complete the walk-around"
                             + (next == null ? "." : " on " + next.Label + ".");
                    return false;
                }

                reason = string.Empty;
                return true;
            }

            if (Phase != InspectionPhase.Releasing)
            {
                reason = "The release step is not active.";
                return false;
            }

            if (phaseElapsed < 2f)
            {
                reason = "Allow at least two seconds to observe the release.";
                return false;
            }

            float target = Mathf.Max(ChargedPipePressure, chargedControlPipe - 0.2f);
            if (CurrentControlPipe < target)
            {
                reason = "Recharge the brake pipe to at least " + target.ToString("0.0")
                         + " bar (currently " + CurrentControlPipe.ToString("0.0") + " bar).";
                return false;
            }

            if (InspectionTypeRules.RequiresRearVerification(Type) && !RearReleaseConfirmed)
            {
                reason = "Physically confirm the brake release on " + RearRecord.Label
                         + " before finishing the inspection.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        internal bool Finish()
        {
            string ignored;
            if (!CanFinish(out ignored))
            {
                return false;
            }

            RefreshObservations();
            Report = BuildReport();
            Phase = InspectionPhase.Complete;
            phaseElapsed = 0f;
            for (int i = 0; i < cars.Count; i++)
            {
                if (cars[i].Car != null)
                {
                    CarInspectionHistory.RecordCarChecked(cars[i].Car, cars[i].CheckedItems);
                }
            }

            if (Report.Passed)
            {
                Certificate = InspectionCertificateRegistry.Issue(this);
                PenaltyManager.NotifyCertificateIssued(Certificate, GetCurrentTrainset());
            }

            ClearActiveState();
            return true;
        }

        internal int ReachableNowCount()
        {
            int count = 0;
            for (int index = 0; index < cars.Count; index++)
            {
                if (IsReachableNow(cars[index]))
                {
                    count++;
                }
            }

            return count;
        }

        internal int TestedReachableCount()
        {
            int count = 0;
            for (int index = 0; index < cars.Count; index++)
            {
                if (cars[index].ReachableDuringTest)
                {
                    count++;
                }
            }

            return count;
        }

        internal int RespondedCount()
        {
            int count = 0;
            for (int index = 0; index < cars.Count; index++)
            {
                CarInspectionRecord record = cars[index];
                if (record.ReachableDuringTest && record.Applied(MinimumCylinderRise))
                {
                    count++;
                }
            }

            return count;
        }

        internal int ReleasedCount()
        {
            int count = 0;
            for (int index = 0; index < cars.Count; index++)
            {
                CarInspectionRecord record = cars[index];
                if (record.ReachableDuringTest && record.Released(ReleasedCylinderPressure))
                {
                    count++;
                }
            }

            return count;
        }

        internal List<ContinuityBreak> GetLiveContinuityBreaks()
        {
            List<ContinuityBreak> breaks = new List<ContinuityBreak>();
            for (int index = 0; index < cars.Count - 1; index++)
            {
                BrakeSystem before = GetBrake(cars[index]);
                BrakeSystem after = GetBrake(cars[index + 1]);
                if (before == null || after == null || !ReferenceEquals(before.brakeset, after.brakeset))
                {
                    breaks.Add(new ContinuityBreak
                    {
                        AfterCarNumber = index + 1,
                        BeforeId = cars[index].Id,
                        AfterId = cars[index + 1].Id
                    });
                }
            }

            return breaks;
        }

        internal List<MissedInspectionItem> GetMissedEquipmentItems()
        {
            List<MissedInspectionItem> result = new List<MissedInspectionItem>();
            for (int carIndex = 0; carIndex < cars.Count; carIndex++)
            {
                CarInspectionRecord record = cars[carIndex];
                for (int itemIndex = 0; itemIndex < InspectionTypeRules.EquipmentItems.Length; itemIndex++)
                {
                    WalkaroundItem item = InspectionTypeRules.EquipmentItems[itemIndex];
                    if ((record.RequiredItems & item) != 0 && !record.IsChecked(item))
                    {
                        result.Add(new MissedInspectionItem
                        {
                            Car = record.Car,
                            CarNumber = record.Number,
                            CarId = record.Id,
                            CarGuid = record.Guid,
                            Item = item
                        });
                    }
                }
            }

            return result;
        }

        internal int GetMissedRequirementCount()
        {
            int missed = WalkaroundRequiredCount - WalkaroundCheckedCount;
            if (InspectionTypeRules.RequiresCabSetup(Type))
            {
                missed += CountCabMissing();
            }

            if (InspectionTypeRules.RequiresLeakageTest(Type) && !LeakageCompleted)
            {
                missed++;
            }

            if (InspectionTypeRules.RequiresBrakeCycle(Type) && Phase != InspectionPhase.Complete)
            {
                missed += 2;
            }

            if (InspectionTypeRules.RequiresRearVerification(Type))
            {
                if (!RearApplicationConfirmed)
                {
                    missed++;
                }

                if (!RearReleaseConfirmed)
                {
                    missed++;
                }
            }

            return Mathf.Max(0, missed);
        }

        internal CarInspectionRecord GetNextUncheckedRecord()
        {
            for (int index = 0; index < cars.Count; index++)
            {
                if ((cars[index].RequiredItems & ~cars[index].CheckedItems) != 0)
                {
                    return cars[index];
                }
            }

            return null;
        }

        internal string GetCarStatus(CarInspectionRecord record, out CarStatusKind kind)
        {
            string readings = "pipe " + record.CurrentPipe.ToString("0.00") + " bar | cyl "
                              + record.CurrentCylinder.ToString("0.00") + " bar | handbrake "
                              + (record.CurrentHandbrake > 0.05f
                                  ? (record.CurrentHandbrake * 100f).ToString("0") + "%"
                                  : "off") + " | " + record.MassTonnes.ToString("0.0") + " t";

            if (!record.HasBrakeSystem)
            {
                kind = CarStatusKind.Failure;
                return readings + " | NO BRAKE SYSTEM";
            }

            if (record.BadOrdered || record.PhysicalFindings.Count > 0)
            {
                kind = CarStatusKind.Failure;
                return readings + " | BAD ORDER";
            }

            if (Phase == InspectionPhase.Charging || Phase == InspectionPhase.Leakage)
            {
                if (record.MissingItemCount > 0)
                {
                    kind = CarStatusKind.Warning;
                    return readings + " | " + record.MissingItemCount + " walk-around item(s) left";
                }

                if (Type == InspectionType.Rolling)
                {
                    kind = CarStatusKind.Ready;
                    return readings + " | INSPECTED";
                }

                if (!IsReachableNow(record))
                {
                    kind = CarStatusKind.NotTested;
                    return readings + " | NOT REACHED";
                }

                if (record.CurrentPipe >= ChargedPipePressure
                    && record.CurrentCylinder <= ReleasedCylinderPressure)
                {
                    kind = record.CurrentHandbrake > 0.05f ? CarStatusKind.Warning : CarStatusKind.Ready;
                    return readings + (record.CurrentHandbrake > 0.05f ? " | HANDBRAKE" : " | READY");
                }

                kind = CarStatusKind.Warning;
                return readings + " | CHARGING";
            }

            if (!record.ReachableDuringTest)
            {
                kind = CarStatusKind.NotTested;
                return readings + " | NOT TESTED";
            }

            if (Phase == InspectionPhase.Applying)
            {
                bool applied = record.Applied(MinimumCylinderRise);
                kind = applied ? CarStatusKind.Ready : CarStatusKind.Warning;
                return readings + " | peak " + record.PeakCylinder.ToString("0.00")
                       + (applied ? " | APPLIED " + record.ApplicationResponseSeconds.ToString("0.0") + " s" : " | WAITING");
            }

            bool released = record.Released(ReleasedCylinderPressure);
            if (Phase == InspectionPhase.Releasing)
            {
                kind = released ? CarStatusKind.Ready : CarStatusKind.Warning;
                return readings + " | low " + record.LowestReleaseCylinder.ToString("0.00")
                       + (released ? " | RELEASED " + record.ReleaseResponseSeconds.ToString("0.0") + " s" : " | WAITING");
            }

            if (!record.Applied(MinimumCylinderRise) || !released || record.MaxHandbrakeDuringTest > 0.05f)
            {
                kind = CarStatusKind.Failure;
                return readings + " | FAILED";
            }

            kind = CarStatusKind.Ready;
            return readings + " | PASSED";
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            UnsubscribeFromConsist();
        }

        private bool CanUseChargedTrain(out string reason)
        {
            RefreshObservations();
            BrakeSystem control = GetControlBrake();
            if (control == null || !control.hasTrainBrake)
            {
                reason = "No locomotive train-brake valve was found in this consist.";
                return false;
            }

            if (!control.trainBrakeCutout)
            {
                reason = "Cut in the train-brake valve on " + ControlRecord.Label + ".";
                return false;
            }

            if (control.brakeset == null)
            {
                reason = "The controlling car has no active brake-pipe segment.";
                return false;
            }

            if (control.brakeset.isLeaking)
            {
                reason = "The reachable brake pipe is venting. Close the open end angle cock.";
                return false;
            }

            int enabledTrainBrakeValves = CountEnabledTrainBrakeValves(control.brakeset);
            if (enabledTrainBrakeValves != 1)
            {
                reason = "Exactly one automatic/train-brake valve must be cut in on the reachable segment; detected "
                         + enabledTrainBrakeValves + ".";
                return false;
            }

            List<CarInspectionRecord> uncharged = new List<CarInspectionRecord>();
            List<CarInspectionRecord> cylindersApplied = new List<CarInspectionRecord>();
            for (int index = 0; index < cars.Count; index++)
            {
                CarInspectionRecord record = cars[index];
                if (!IsReachableNow(record))
                {
                    continue;
                }

                if (record.CurrentPipe < ChargedPipePressure)
                {
                    uncharged.Add(record);
                }

                if (record.CurrentCylinder > ReleasedCylinderPressure)
                {
                    cylindersApplied.Add(record);
                }
            }

            if (uncharged.Count > 0)
            {
                reason = "Keep charging: " + FormatRecordList(uncharged) + " below "
                         + ChargedPipePressure.ToString("0.0") + " bar.";
                return false;
            }

            if (cylindersApplied.Count > 0)
            {
                reason = "Release the air/independent brakes first: " + FormatRecordList(cylindersApplied)
                         + " still has cylinder pressure.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private bool CanLeavePreparation(out string reason)
        {
            if (WalkaroundCheckedCount < WalkaroundRequiredCount)
            {
                CarInspectionRecord next = GetNextUncheckedRecord();
                reason = "Complete the walk-around"
                         + (next == null ? "." : " on " + next.Label + ".");
                return false;
            }

            if (InspectionTypeRules.RequiresCabSetup(Type)
                && (!CabSetupInspected || CabPassedItems != CabSetupItem.All))
            {
                reason = "Record a passing cab setup before starting the brake test.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void TickLeakage(float deltaTime)
        {
            BrakeSystem control = GetControlBrake();
            if (control == null)
            {
                return;
            }

            if (!control.trainBrakeCutout)
            {
                if (!leakageCutoutObserved)
                {
                    leakageCutoutObserved = true;
                    leakageStartPipe = CurrentControlPipe;
                    leakageLowestPipe = leakageStartPipe;
                    leakageElapsed = 0f;
                }

                leakageElapsed += deltaTime;
                leakageLowestPipe = Mathf.Min(leakageLowestPipe, CurrentControlPipe);
            }
        }

        private bool CanInspectRecord(CarInspectionRecord record, out string reason)
        {
            if (record == null || record.Car == null)
            {
                reason = "The selected car is unavailable.";
                return false;
            }

            if (SafeSpeed(record.Car) > 0.3f)
            {
                reason = "Stop the train before performing a close inspection.";
                return false;
            }

            if (record.DistanceToPlayer > Main.Settings.WalkaroundRangeMetres)
            {
                reason = "Walk within " + Main.Settings.WalkaroundRangeMetres.ToString("0") + " m of "
                         + record.Label + " (currently " + record.DistanceToPlayer.ToString("0") + " m).";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private List<string> FindPhysicalIssues(CarInspectionRecord record, WalkaroundItem item)
        {
            List<string> result = new List<string>();
            BrakeSystem brake = GetBrake(record);
            if (brake == null)
            {
                AddIssue(result, record, "No usable brake system is present.");
                return result;
            }

            if (item == WalkaroundItem.HoseAndAngleCocks)
            {
                InspectHose(record, record.Car.frontCoupler, result);
                InspectHose(record, record.Car.rearCoupler, result);
            }
            else if (item == WalkaroundItem.Coupler)
            {
                if (cars.Count > 1 && record.Car.frontCoupler != null && record.Car.rearCoupler != null
                                   && !record.Car.frontCoupler.IsCoupled() && !record.Car.rearCoupler.IsCoupled())
                {
                    AddIssue(result, record, "Car is not mechanically coupled to the selected consist.");
                }
            }
            else if (item == WalkaroundItem.Handbrake && brake.handbrakePosition > 0.05f)
            {
                AddIssue(result, record, "Handbrake is applied "
                                         + (brake.handbrakePosition * 100f).ToString("0") + "%.");
            }
            else if (item == WalkaroundItem.BrakeRigging)
            {
                // Physical walkaround check for brake rigging and cylinders.
                // If the train brake pipe on this car is charged to operating pressure (>= 4.5 bar)
                // and any independent brake is released, a pressurized cylinder indicates a stuck valve / rigging.
                // However, on an uncharged train (cold walkaround before startup) or while held by independent brake,
                // cylinder pressure is expected and normal.
                bool pipeCharged = brake.brakePipePressure >= ChargedPipePressure;
                bool locoHolding = brake.hasIndependentBrake && brake.independentBrakePosition > 0.05f;
                if (pipeCharged && !locoHolding && brake.brakeCylinderPressure > ReleasedCylinderPressure
                    && Phase == InspectionPhase.Charging)
                {
                    AddIssue(result, record, "Brake cylinder remains pressurized ("
                                             + brake.brakeCylinderPressure.ToString("0.00")
                                             + " bar) despite charged brake pipe.");
                }
            }
            else if (item == WalkaroundItem.WheelsAndBearings && brake.heatController != null
                                                                  && brake.heatController.temperature >= 600f)
            {
                AddIssue(result, record, "Wheel/brake temperature is "
                                         + brake.heatController.temperature.ToString("0") + " C.");
            }

            return result;
        }

        private static void InspectHose(CarInspectionRecord record, Coupler coupler, List<string> result)
        {
            if (coupler == null || coupler.hoseAndCock == null)
            {
                return;
            }

            if (coupler.IsCoupled())
            {
                if (!coupler.hoseAndCock.IsHoseConnected)
                {
                    AddIssue(result, record, (coupler.isFrontCoupler ? "Front" : "Rear")
                                             + " coupler has no connected brake hose.");
                }
                else if (!coupler.hoseAndCock.IsFullyConnected)
                {
                    AddIssue(result, record, (coupler.isFrontCoupler ? "Front" : "Rear")
                                             + " brake-hose angle cock is closed.");
                }
            }
            else if (coupler.hoseAndCock.cockOpen)
            {
                AddIssue(result, record, (coupler.isFrontCoupler ? "Front" : "Rear")
                                         + " end angle cock is open to atmosphere.");
            }
        }

        private static void AddIssue(List<string> result, CarInspectionRecord record, string issue)
        {
            result.Add(issue);
            AddFinding(record, issue);
        }

        private static void AddFinding(CarInspectionRecord record, string finding)
        {
            if (!record.PhysicalFindings.Contains(finding))
            {
                record.PhysicalFindings.Add(finding);
            }
        }

        private void UpdatePlayerProximity()
        {
            Transform player = null;
            try
            {
                player = PlayerManager.PlayerTransform;
            }
            catch
            {
                // Player objects can be absent while a scene is loading.
            }

            for (int index = 0; index < cars.Count; index++)
            {
                CarInspectionRecord record = cars[index];
                if (player == null || record.Car == null)
                {
                    record.DistanceToPlayer = float.MaxValue;
                    continue;
                }

                record.DistanceToPlayer = Vector3.Distance(player.position, record.Car.transform.position);
                if (record.DistanceToPlayer > Main.Settings.WalkaroundRangeMetres)
                {
                    continue;
                }

                Vector3 local = record.Car.transform.InverseTransformPoint(player.position);
                if (Mathf.Abs(local.x) < 0.75f)
                {
                    continue;
                }

                WalkaroundItem sideItem = local.x < 0f ? WalkaroundItem.LeftSide : WalkaroundItem.RightSide;
                if ((record.CheckedItems & sideItem) == 0)
                {
                    record.CheckedItems |= sideItem;
                    CarInspectionHistory.RecordCarChecked(record.Car, record.CheckedItems);
                    SaveActiveState();
                }
            }
        }

        private void RefreshObservations()
        {
            for (int index = 0; index < cars.Count; index++)
            {
                CarInspectionRecord record = cars[index];
                BrakeSystem brake = GetBrake(record);
                if (brake == null)
                {
                    record.CurrentPipe = 0f;
                    record.CurrentCylinder = 0f;
                    record.CurrentHandbrake = 0f;
                    record.CurrentTemperature = 0f;
                    record.CurrentBrakingFactor = 0f;
                    continue;
                }

                record.CurrentPipe = brake.brakePipePressure;
                record.CurrentCylinder = brake.brakeCylinderPressure;
                record.CurrentHandbrake = brake.handbrakePosition;
                record.CurrentTemperature = brake.heatController == null ? 0f : brake.heatController.temperature;
                record.CurrentBrakingFactor = brake.brakingFactor;
                record.MassTonnes = record.Car.massController == null
                    ? 0f
                    : record.Car.massController.TotalMass / 1000f;

                if (Phase == InspectionPhase.Applying || Phase == InspectionPhase.Releasing)
                {
                    record.PeakCylinder = Mathf.Max(record.PeakCylinder, record.CurrentCylinder);
                    record.MaxHandbrakeDuringTest = Mathf.Max(record.MaxHandbrakeDuringTest,
                        record.CurrentHandbrake);
                }

                if (Phase == InspectionPhase.Applying)
                {
                    if (applicationReductionStartTime < 0f && ServiceReduction >= 0.15f)
                    {
                        applicationReductionStartTime = phaseElapsed;
                    }

                    if (record.ApplicationResponseSeconds < 0f && record.Applied(MinimumCylinderRise))
                    {
                        record.ApplicationResponseSeconds = applicationReductionStartTime >= 0f
                            ? Mathf.Max(0f, phaseElapsed - applicationReductionStartTime)
                            : 0f;
                    }
                }

                if (Phase == InspectionPhase.Releasing)
                {
                    if (releaseRechargeStartTime < 0f && (CurrentControlPipe >= chargedControlPipe - 0.25f || CurrentControlPipe >= ChargedPipePressure))
                    {
                        releaseRechargeStartTime = phaseElapsed;
                    }

                    record.LowestReleaseCylinder = Mathf.Min(record.LowestReleaseCylinder, record.CurrentCylinder);
                    if (record.ReleaseResponseSeconds < 0f && record.Released(ReleasedCylinderPressure))
                    {
                        record.ReleaseResponseSeconds = releaseRechargeStartTime >= 0f
                            ? Mathf.Max(0f, phaseElapsed - releaseRechargeStartTime)
                            : 0f;
                    }
                }
            }
        }

        private void CaptureContinuityBreaks()
        {
            for (int index = 0; index < cars.Count - 1; index++)
            {
                Brakeset before = cars[index].TestBrakeSet;
                Brakeset after = cars[index + 1].TestBrakeSet;
                if (before == null || after == null || !ReferenceEquals(before, after))
                {
                    testedContinuityBreaks.Add(new ContinuityBreak
                    {
                        AfterCarNumber = index + 1,
                        BeforeId = cars[index].Id,
                        AfterId = cars[index + 1].Id
                    });
                }
            }
        }

        private InspectionReport BuildReport()
        {
            InspectionReport report = new InspectionReport
            {
                CompletedAt = DateTime.Now,
                LeakageDropBar = LeakageDrop
            };

            List<CarInspectionRecord> handbrakes = new List<CarInspectionRecord>();
            List<CarInspectionRecord> missingBrakeSystems = new List<CarInspectionRecord>();
            List<CarInspectionRecord> notReached = new List<CarInspectionRecord>();
            List<CarInspectionRecord> applicationFailures = new List<CarInspectionRecord>();
            List<CarInspectionRecord> releaseFailures = new List<CarInspectionRecord>();
            List<CarInspectionRecord> slowApplications = new List<CarInspectionRecord>();
            List<CarInspectionRecord> slowReleases = new List<CarInspectionRecord>();
            float totalMass = 0f;
            float testedBrakeMass = 0f;

            for (int index = 0; index < cars.Count; index++)
            {
                CarInspectionRecord record = cars[index];
                totalMass += record.MassTonnes;
                List<WalkaroundItem> missing = InspectionTypeRules.Expand(record.RequiredItems & ~record.CheckedItems);
                if (missing.Count > 0)
                {
                    List<string> labels = new List<string>();
                    for (int itemIndex = 0; itemIndex < missing.Count; itemIndex++)
                    {
                        labels.Add(InspectionTypeRules.ItemLabel(missing[itemIndex]));
                    }

                    report.Findings.Add(record.Label + " was not fully inspected: "
                                        + string.Join(", ", labels.ToArray()) + ".");
                }

                for (int findingIndex = 0; findingIndex < record.PhysicalFindings.Count; findingIndex++)
                {
                    report.Findings.Add(record.Label + ": " + record.PhysicalFindings[findingIndex]);
                }

                List<MechanicalFaultData> faults = FaultRegistry.GetFaults(record.Car, false);
                for (int faultIndex = 0; faultIndex < faults.Count; faultIndex++)
                {
                    string faultText = record.Label + " has " + FaultRegistry.Describe(faults[faultIndex]) + ".";
                    if (!report.Findings.Contains(faultText))
                    {
                        report.Findings.Add(faultText);
                    }
                }

                if (record.BadOrdered && record.PhysicalFindings.Count == 0 && faults.Count == 0)
                {
                    report.Findings.Add(record.Label + " is tagged bad order.");
                }

                if (Type == InspectionType.Rolling)
                {
                    continue;
                }

                if (!record.HasBrakeSystem)
                {
                    missingBrakeSystems.Add(record);
                    continue;
                }

                if (record.MaxHandbrakeDuringTest > 0.05f)
                {
                    handbrakes.Add(record);
                }

                if (!record.ReachableDuringTest)
                {
                    notReached.Add(record);
                    continue;
                }

                if (!record.Applied(MinimumCylinderRise))
                {
                    applicationFailures.Add(record);
                    continue;
                }

                if (record.ApplicationResponseSeconds > SlowApplicationSeconds)
                {
                    slowApplications.Add(record);
                }

                if (!record.Released(ReleasedCylinderPressure))
                {
                    releaseFailures.Add(record);
                    continue;
                }

                if (record.ReleaseResponseSeconds > SlowReleaseSeconds)
                {
                    slowReleases.Add(record);
                }

                testedBrakeMass += record.MassTonnes;
            }

            report.TotalMassTonnes = totalMass;
            report.TestedBrakeMassPercent = totalMass <= 0.01f ? 0f : testedBrakeMass / totalMass * 100f;

            if (InspectionTypeRules.RequiresCabSetup(Type))
            {
                if (!CabSetupInspected)
                {
                    report.Findings.Add("Cab setup was not inspected.");
                }
                else if (CabPassedItems != CabSetupItem.All)
                {
                    for (int index = 0; index < cabFindings.Count; index++)
                    {
                        report.Findings.Add("Cab setup: " + cabFindings[index]);
                    }
                }
            }

            if (InspectionTypeRules.RequiresLeakageTest(Type))
            {
                if (!LeakageCompleted)
                {
                    report.Findings.Add("The brake-pipe leakage hold was not completed.");
                }
                else if (LeakageDrop > Main.Settings.MaximumLeakageBar)
                {
                    report.Findings.Add("Leakage test lost " + LeakageDrop.ToString("0.00")
                                        + " bar; maximum allowed is "
                                        + Main.Settings.MaximumLeakageBar.ToString("0.00") + " bar.");
                }
            }

            if (InspectionTypeRules.RequiresRearVerification(Type))
            {
                if (!RearApplicationConfirmed)
                {
                    report.Findings.Add("Rear-car brake application was not physically confirmed.");
                }

                if (!RearReleaseConfirmed)
                {
                    report.Findings.Add("Rear-car brake release was not physically confirmed.");
                }
            }

            for (int index = 0; index < testedContinuityBreaks.Count; index++)
            {
                report.Findings.Add("Brake continuity is open " + testedContinuityBreaks[index].Description
                                    + ". Check both hose connections and angle cocks.");
            }

            if (handbrakes.Count > 0)
            {
                report.Findings.Add("Applied handbrake detected: " + FormatRecordList(handbrakes) + ".");
            }

            if (notReached.Count > 0)
            {
                report.Findings.Add("Not reached by the controlling brake pipe: " + FormatRecordList(notReached) + ".");
            }

            if (missingBrakeSystems.Count > 0)
            {
                report.Findings.Add("No usable brake system was exposed by the game for "
                                    + FormatRecordList(missingBrakeSystems) + ".");
            }

            if (applicationFailures.Count > 0)
            {
                report.Findings.Add("Failed to apply during the service reduction: "
                                    + FormatRecordList(applicationFailures) + ".");
            }

            if (releaseFailures.Count > 0)
            {
                report.Findings.Add("Failed to release after the pipe recharged: "
                                    + FormatRecordList(releaseFailures) + ".");
            }

            if (slowApplications.Count > 0)
            {
                report.Findings.Add("Advisory: sluggish brake cylinder application (" + FormatRecordList(slowApplications) + ").");
            }

            if (slowReleases.Count > 0)
            {
                report.Findings.Add("Advisory: sluggish brake cylinder release (" + FormatRecordList(slowReleases) + ").");
            }

            if (Type != InspectionType.Rolling && totalMass > 0f && report.TestedBrakeMassPercent < 90f)
            {
                report.Findings.Add("Only " + report.TestedBrakeMassPercent.ToString("0")
                                    + "% of train mass has a confirmed applying and releasing brake response.");
            }

            bool hasCriticalFailures = false;
            for (int i = 0; i < report.Findings.Count; i++)
            {
                if (!report.Findings[i].StartsWith("Advisory:"))
                {
                    hasCriticalFailures = true;
                    break;
                }
            }

            report.Passed = !hasCriticalFailures;
            if (report.Passed)
            {
                report.Summary = InspectionTypeRules.DisplayName(Type) + " passed: " + cars.Count
                                 + " cars, " + totalMass.ToString("0.0") + " t, "
                                 + (Type == InspectionType.Rolling
                                     ? "walk-around complete"
                                     : report.TestedBrakeMassPercent.ToString("0")
                                       + "% brake-mass response confirmed") + ".";
            }
            else
            {
                report.Summary = InspectionTypeRules.DisplayName(Type) + " failed with "
                                 + report.Findings.Count + " finding" + (report.Findings.Count == 1 ? "" : "s") + ".";
            }

            return report;
        }

        private static List<TrainCar> BuildAutomaticOrder(Trainset trainset, TrainCar preferred)
        {
            List<TrainCar> result = new List<TrainCar>();
            for (int index = 0; index < trainset.cars.Count; index++)
            {
                TrainCar car = trainset.cars[index];
                if (car != null)
                {
                    result.Add(car);
                }
            }

            if (result.Count < 2)
            {
                return result;
            }

            int preferredIndex = preferred == null ? -1 : result.IndexOf(preferred);
            if (preferredIndex == result.Count - 1)
            {
                result.Reverse();
                return result;
            }

            if (preferredIndex == 0)
            {
                return result;
            }

            bool firstIsLoco = SafeIsLoco(result[0]);
            bool lastIsLoco = SafeIsLoco(result[result.Count - 1]);
            if (!firstIsLoco && lastIsLoco)
            {
                result.Reverse();
            }

            return result;
        }

        private int FindControlIndex(TrainCar preferred)
        {
            TrainCar control = TrainIdentity.FindControllingCar(originalTrainset, preferred);
            return FindCarIndex(control);
        }

        private int FindCarIndex(TrainCar car)
        {
            if (car == null)
            {
                return -1;
            }

            for (int index = 0; index < cars.Count; index++)
            {
                if (cars[index].Car == car)
                {
                    return index;
                }
            }

            return -1;
        }

        private int CountEnabledTrainBrakeValves(Brakeset brakeSet)
        {
            return TrainIdentity.CountEnabledTrainBrakeValves(originalTrainset, brakeSet);
        }

        private BrakeSystem GetControlBrake()
        {
            CarInspectionRecord record = ControlRecord;
            return record == null ? null : GetBrake(record);
        }

        private static BrakeSystem GetBrake(CarInspectionRecord record)
        {
            return record == null || record.Car == null ? null : record.Car.brakeSystem;
        }

        private bool IsReachableNow(CarInspectionRecord record)
        {
            BrakeSystem control = GetControlBrake();
            BrakeSystem brake = GetBrake(record);
            return control != null && brake != null && control.brakeset != null
                   && ReferenceEquals(control.brakeset, brake.brakeset);
        }

        private bool BrakeConfigurationStillMatches()
        {
            BrakeSystem control = GetControlBrake();
            if (control == null || control.brakeset == null || control.trainBrakeCutout != controlCutoutDuringTest
                || control.brakeset.isLeaking != testBrakeSetWasLeaking
                || CountEnabledTrainBrakeValves(control.brakeset) != 1)
            {
                return false;
            }

            for (int index = 0; index < cars.Count; index++)
            {
                BrakeSystem brake = GetBrake(cars[index]);
                if (brake == null)
                {
                    if (cars[index].TestBrakeSet != null)
                    {
                        return false;
                    }

                    continue;
                }

                if (!ReferenceEquals(brake.brakeset, cars[index].TestBrakeSet))
                {
                    return false;
                }
            }

            return true;
        }

        private bool ConsistStillMatches()
        {
            if (cars.Count == 0)
            {
                return false;
            }

            Trainset current = GetCurrentTrainset();
            if (current == null || current.cars == null || current.cars.Count != originalCarIds.Count)
            {
                return false;
            }

            for (int index = 0; index < current.cars.Count; index++)
            {
                TrainCar car = current.cars[index];
                if (car == null || !originalCarIds.Contains(car.GetInstanceID()))
                {
                    return false;
                }
            }

            return true;
        }

        private int CountCabMissing()
        {
            if (!CabSetupInspected)
            {
                return 5;
            }

            CabSetupItem missing = CabSetupItem.All & ~CabPassedItems;
            int count = 0;
            CabSetupItem[] items =
            {
                CabSetupItem.MainReservoir,
                CabSetupItem.ControllingValve,
                CabSetupItem.TrailingValvesCutOut,
                CabSetupItem.IndependentBrakeReleased,
                CabSetupItem.CompressorAvailable
            };
            for (int index = 0; index < items.Length; index++)
            {
                if ((missing & items[index]) != 0)
                {
                    count++;
                }
            }

            return count;
        }

        private void SubscribeToConsist()
        {
            if (subscribed)
            {
                return;
            }

            for (int index = 0; index < cars.Count; index++)
            {
                TrainCar car = cars[index].Car;
                if (car != null)
                {
                    car.TrainsetChanged += trainsetChangedHandler;
                }
            }

            subscribed = true;
        }

        private void UnsubscribeFromConsist()
        {
            if (!subscribed)
            {
                return;
            }

            for (int index = 0; index < cars.Count; index++)
            {
                TrainCar car = cars[index].Car;
                if (car != null)
                {
                    car.TrainsetChanged -= trainsetChangedHandler;
                }
            }

            subscribed = false;
        }

        private void OnSelectedCarTrainsetChanged(Trainset ignored)
        {
            if (Phase == InspectionPhase.Charging)
            {
                SyncConsist();
            }
            else if (Phase == InspectionPhase.Complete)
            {
                SyncConsist();
                Phase = InspectionPhase.Charging;
                Report = null;
                Certificate = null;
                Main.ModEntry.Logger.Log("Consist changed after inspection: new cars attached. Conduct brake test for updated consist.");
            }
            else
            {
                Invalidate("The consist changed. Couple/uncouple activity invalidates this active brake test.");
            }
        }

        internal bool SyncConsist()
        {
            Trainset current = GetCurrentTrainset();
            if (current == null || current.cars == null || current.cars.Count == 0)
            {
                Invalidate("Consist is no longer available.");
                return false;
            }

            originalTrainset = current;
            List<TrainCar> ordered = BuildAutomaticOrder(current, preferredCar);
            if (IsReversed)
            {
                ordered.Reverse();
            }

            WalkaroundItem required = Main.Settings.RequirePhysicalWalkaround
                ? InspectionTypeRules.RequiredWalkaroundItems(Type)
                : WalkaroundItem.None;

            Dictionary<int, CarInspectionRecord> existingByInstance = new Dictionary<int, CarInspectionRecord>();
            for (int i = 0; i < cars.Count; i++)
            {
                if (cars[i].Car != null)
                {
                    existingByInstance[cars[i].InstanceId] = cars[i];
                }
            }

            UnsubscribeFromConsist();
            cars.Clear();
            originalCarIds.Clear();

            int newlyAddedCount = 0;
            for (int index = 0; index < ordered.Count; index++)
            {
                TrainCar car = ordered[index];
                int instanceId = car.GetInstanceID();
                CarInspectionRecord record;
                if (existingByInstance.TryGetValue(instanceId, out record))
                {
                    record.Number = index + 1;
                    record.Car = car;
                    record.RequiredItems = required;
                }
                else
                {
                    newlyAddedCount++;
                    WalkaroundItem previouslyChecked = CarInspectionHistory.GetValidCheckedItems(car);
                    record = new CarInspectionRecord
                    {
                        Car = car,
                        Number = index + 1,
                        InstanceId = instanceId,
                        Id = TrainIdentity.SafeCarId(car),
                        Guid = TrainIdentity.SafeCarGuid(car),
                        RequiredItems = required,
                        CheckedItems = previouslyChecked & required
                    };
                }

                cars.Add(record);
                originalCarIds.Add(instanceId);
            }

            ControlIndex = FindControlIndex(preferredCar);
            SubscribeToConsist();
            RefreshObservations();
            UpdatePlayerProximity();

            if (newlyAddedCount > 0)
            {
                Main.ModEntry.Logger.Log("Train Inspector: Consist updated on couple/uncouple (" + newlyAddedCount + " new cars, total " + cars.Count + ").");
            }

            return true;
        }

        private bool ConsistContainsCarGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return false;
            }

            for (int i = 0; i < cars.Count; i++)
            {
                if (cars[i].Guid == guid)
                {
                    return true;
                }
            }

            return false;
        }

        private void Invalidate(string reason)
        {
            if (Phase == InspectionPhase.Invalidated || disposed)
            {
                return;
            }

            InvalidReason = reason;
            Phase = InspectionPhase.Invalidated;
            Report = null;
            Certificate = null;
            UnsubscribeFromConsist();
            ClearActiveState();
        }

        private static string FormatRecordList(IList<CarInspectionRecord> records)
        {
            List<string> labels = new List<string>();
            for (int index = 0; index < records.Count; index++)
            {
                labels.Add(records[index].Label);
            }

            return string.Join(", ", labels.ToArray());
        }

        private static bool SafeIsLoco(TrainCar car)
        {
            if (car == null)
            {
                return false;
            }

            try
            {
                return car.IsLoco;
            }
            catch
            {
                return false;
            }
        }

        private static float SafeSpeed(TrainCar car)
        {
            try
            {
                return Mathf.Abs(car.GetAbsSpeed());
            }
            catch
            {
                return car == null || car.rb == null ? 0f : car.rb.velocity.magnitude;
            }
        }
    }
}
