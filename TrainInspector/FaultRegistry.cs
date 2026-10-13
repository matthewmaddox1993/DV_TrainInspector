using System;
using System.Collections.Generic;
using DV.Simulation.Brake;
using HarmonyLib;
using UnityEngine;

namespace TrainInspector
{
    internal static class FaultRegistry
    {
        private const float FaultUpdateIntervalSeconds = 0.2f;

        internal static string LastEvent { get; private set; }
        internal static float LastEventAt { get; private set; }

        private static readonly HashSet<string> reusableGuids = new HashSet<string>();
        private static readonly Dictionary<string, TrainCar> reusableCarsByGuid = new Dictionary<string, TrainCar>();
        private static readonly Dictionary<string, List<MechanicalFaultData>> activeBrakeFaultsByCarGuid =
            new Dictionary<string, List<MechanicalFaultData>>();
        private static readonly Dictionary<BrakeSystem, List<MechanicalFaultData>> brakeFaultCache =
            new Dictionary<BrakeSystem, List<MechanicalFaultData>>();
        private static bool hasActiveBrakeFaults;
        private static bool activeStateDirty = true;
        private static Trainset lastTickTrainset;
        private static float faultUpdateElapsed;
        private static float pendingTravelMetres;

        internal static void InvalidateActiveFaultState()
        {
            activeStateDirty = true;
        }

        private static bool CheckHasActiveBrakeFaults()
        {
            if (!activeStateDirty)
            {
                return hasActiveBrakeFaults;
            }

            activeStateDirty = false;
            hasActiveBrakeFaults = false;
            activeBrakeFaultsByCarGuid.Clear();
            brakeFaultCache.Clear();
            List<MechanicalFaultData> faults = InspectionPersistence.Data.Faults;
            if (faults == null || faults.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < faults.Count; i++)
            {
                MechanicalFaultData fault = faults[i];
                if (fault == null || !fault.Active
                    || (MechanicalFaultType)fault.FaultTypeValue == MechanicalFaultType.CouplerSeparation)
                {
                    continue;
                }

                if (!activeBrakeFaultsByCarGuid.TryGetValue(fault.CarGuid,
                        out List<MechanicalFaultData> carFaults))
                {
                    carFaults = new List<MechanicalFaultData>();
                    activeBrakeFaultsByCarGuid[fault.CarGuid] = carFaults;
                }

                carFaults.Add(fault);
                hasActiveBrakeFaults = true;
            }

            return hasActiveBrakeFaults;
        }

        internal static List<MechanicalFaultData> RollForMissedItems(
            IList<MissedInspectionItem> missedItems,
            InspectorSettings settings)
        {
            List<MechanicalFaultData> created = new List<MechanicalFaultData>();
            if (!settings.EnableMechanicalFailures || settings.MaximumFaultsPerDeparture <= 0
                                                   || settings.UncheckedItemFailureChancePercent <= 0f)
            {
                return created;
            }

            List<MissedInspectionItem> candidates = new List<MissedInspectionItem>();
            for (int index = 0; index < missedItems.Count; index++)
            {
                MissedInspectionItem item = missedItems[index];
                if (item == null || item.Car == null || item.Item == WalkaroundItem.LeftSide
                                                  || item.Item == WalkaroundItem.RightSide)
                {
                    continue;
                }

                if (item.Item == WalkaroundItem.Coupler && !settings.AllowCouplerSeparationFaults)
                {
                    continue;
                }

                candidates.Add(item);
            }

            Shuffle(candidates);
            float chance = settings.UncheckedItemFailureChancePercent / 100f;
            for (int index = 0; index < candidates.Count && created.Count < settings.MaximumFaultsPerDeparture; index++)
            {
                if (UnityEngine.Random.value > chance)
                {
                    continue;
                }

                MissedInspectionItem missed = candidates[index];
                MechanicalFaultType type = FaultForItem(missed.Item);
                if (HasFault(missed.CarGuid, type))
                {
                    continue;
                }

                MechanicalFaultData fault = new MechanicalFaultData
                {
                    Id = Guid.NewGuid().ToString("N"),
                    CarGuid = missed.CarGuid,
                    CarId = missed.CarId,
                    FaultTypeValue = (int)type,
                    Severity = UnityEngine.Random.Range(0.35f, 0.85f),
                    TravelUntilActivationMetres = UnityEngine.Random.Range(100f, 1000f),
                    TravelMetres = 0f,
                    Active = false,
                    Revealed = false,
                    ActionTriggered = false,
                    CreatedUtcTicks = DateTime.UtcNow.Ticks
                };

                InspectionPersistence.Data.Faults.Add(fault);
                created.Add(fault);
            }

            if (created.Count > 0)
            {
                InvalidateActiveFaultState();
                InspectionPersistence.Save();
            }

            return created;
        }

        internal static List<MechanicalFaultData> RollForPreExistingDefects(
            Trainset trainset,
            InspectorSettings settings)
        {
            List<MechanicalFaultData> created = new List<MechanicalFaultData>();
            if (trainset == null || trainset.cars == null || !settings.EnableMechanicalFailures
                || !settings.EnablePreExistingDefects || settings.PreExistingDefectChancePercent <= 0f)
            {
                return created;
            }

            float chance = settings.PreExistingDefectChancePercent / 100f;
            for (int index = 0; index < trainset.cars.Count; index++)
            {
                TrainCar car = trainset.cars[index];
                if (car == null)
                {
                    continue;
                }

                string guid = TrainIdentity.SafeCarGuid(car);
                if (HasAnyFault(guid))
                {
                    continue;
                }

                if (UnityEngine.Random.value > chance)
                {
                    continue;
                }

                List<MechanicalFaultType> candidateTypes = new List<MechanicalFaultType>
                {
                    MechanicalFaultType.BrakePipeLeak,
                    MechanicalFaultType.DraggingHandbrake,
                    MechanicalFaultType.WeakBrakeRigging,
                    MechanicalFaultType.StuckBrakeCylinder,
                    MechanicalFaultType.HotBearing
                };

                if (settings.AllowCouplerSeparationFaults)
                {
                    candidateTypes.Add(MechanicalFaultType.CouplerSeparation);
                }

                MechanicalFaultType type = candidateTypes[UnityEngine.Random.Range(0, candidateTypes.Count)];
                MechanicalFaultData fault = new MechanicalFaultData
                {
                    Id = Guid.NewGuid().ToString("N"),
                    CarGuid = guid,
                    CarId = TrainIdentity.SafeCarId(car),
                    FaultTypeValue = (int)type,
                    Severity = UnityEngine.Random.Range(0.40f, 0.90f),
                    TravelUntilActivationMetres = UnityEngine.Random.Range(100f, 1000f),
                    TravelMetres = 0f,
                    Active = false,
                    Revealed = false,
                    ActionTriggered = false,
                    CreatedUtcTicks = DateTime.UtcNow.Ticks
                };

                InspectionPersistence.Data.Faults.Add(fault);
                created.Add(fault);
            }

            if (created.Count > 0)
            {
                InvalidateActiveFaultState();
                InspectionPersistence.Save();
                Main.ModEntry.Logger.Log("Train Inspector: Generated " + created.Count
                    + " pre-existing equipment defect(s) on consist.");
            }

            return created;
        }

        internal static int CountKnownBadOrders(Trainset trainset)
        {
            if (trainset == null || trainset.cars == null)
            {
                return 0;
            }

            int count = 0;
            reusableGuids.Clear();
            for (int index = 0; index < trainset.cars.Count; index++)
            {
                TrainCar car = trainset.cars[index];
                if (car != null)
                {
                    reusableGuids.Add(TrainIdentity.SafeCarGuid(car));
                }
            }

            List<MechanicalFaultData> faults = InspectionPersistence.Data.Faults;
            for (int index = 0; index < faults.Count; index++)
            {
                MechanicalFaultData fault = faults[index];
                if (fault != null && reusableGuids.Contains(fault.CarGuid) && fault.Revealed)
                {
                    count++;
                }
            }

            return count;
        }

        internal static List<MechanicalFaultData> TriggerImmediateDepartureFailures(
            Trainset trainset,
            out int failureCount)
        {
            List<MechanicalFaultData> triggered = new List<MechanicalFaultData>();
            failureCount = 0;
            if (trainset == null || trainset.cars == null || InspectionPersistence.Data.Faults.Count == 0)
            {
                return triggered;
            }

            reusableGuids.Clear();
            for (int index = 0; index < trainset.cars.Count; index++)
            {
                TrainCar car = trainset.cars[index];
                if (car != null)
                {
                    reusableGuids.Add(TrainIdentity.SafeCarGuid(car));
                }
            }

            bool changed = false;
            List<MechanicalFaultData> faults = InspectionPersistence.Data.Faults;
            for (int index = 0; index < faults.Count; index++)
            {
                MechanicalFaultData fault = faults[index];
                if (fault == null || !reusableGuids.Contains(fault.CarGuid))
                {
                    continue;
                }

                fault.Active = true;
                fault.TravelUntilActivationMetres = 0f;
                fault.TravelMetres = 0f;
                changed = true;
                triggered.Add(fault);

                TrainCar car = FindCar(trainset, fault.CarGuid);
                if (car == null)
                {
                    continue;
                }

                MechanicalFaultType type = (MechanicalFaultType)fault.FaultTypeValue;
                switch (type)
                {
                    case MechanicalFaultType.BrakePipeLeak:
                        if (car.brakeSystem != null && car.brakeSystem.brakeset != null)
                        {
                            car.brakeSystem.brakeset.pipePressure = 1f;
                        }
                        Announce("EMERGENCY: Catastrophic brake pipe venting on " + fault.CarId + "! Train line vented.");
                        Main.ModEntry.Logger.Warning(LastEvent);
                        break;

                    case MechanicalFaultType.DraggingHandbrake:
                        if (car.brakeSystem != null && car.brakeSystem.hasHandbrake)
                        {
                            car.brakeSystem.SetHandbrakePosition(1.0f);
                            fault.ActionTriggered = true;
                        }
                        Announce("EMERGENCY: Handbrake locked on " + fault.CarId + " upon departure!");
                        Main.ModEntry.Logger.Warning(LastEvent);
                        break;

                    case MechanicalFaultType.StuckBrakeCylinder:
                        if (car.brakeSystem != null)
                        {
                            car.brakeSystem.brakeCylinderPressureUnsmoothed = 4.0f;
                            car.brakeSystem.brakingFactor = 0.85f;
                        }
                        Announce("EMERGENCY: Brake cylinder locked on " + fault.CarId + "!");
                        Main.ModEntry.Logger.Warning(LastEvent);
                        break;

                    case MechanicalFaultType.HotBearing:
                        if (car.brakeSystem != null && car.brakeSystem.heatController != null)
                        {
                            car.brakeSystem.heatController.temperature = 950f;
                            car.brakeSystem.brakingFactor = 0.25f;
                        }
                        Announce("EMERGENCY: Wheel bearing severely overheated on " + fault.CarId + "!");
                        Main.ModEntry.Logger.Warning(LastEvent);
                        break;

                    case MechanicalFaultType.CouplerSeparation:
                        if (Main.Settings.AllowCouplerSeparationFaults && !fault.ActionTriggered)
                        {
                            Coupler coupler = ChooseConnectedCoupler(car);
                            if (coupler != null)
                            {
                                fault.ActionTriggered = true;
                                coupler.Uncouple(true, false, true);
                                Announce("EMERGENCY: Broken coupler caused train separation on " + fault.CarId + "!");
                                Main.ModEntry.Logger.Warning(LastEvent);
                            }
                        }
                        break;
                }
            }

            if (changed)
            {
                InvalidateActiveFaultState();
                InspectionPersistence.Save();
            }

            failureCount = triggered.Count;
            return triggered;
        }

        internal static void TickTrain(Trainset trainset, float distanceMetres, float deltaTime)
        {
            if (!Main.Settings.EnableMechanicalFailures || trainset == null || trainset.cars == null
                || distanceMetres <= 0f || InspectionPersistence.Data.Faults.Count == 0)
            {
                if (!ReferenceEquals(lastTickTrainset, trainset))
                {
                    lastTickTrainset = trainset;
                    faultUpdateElapsed = 0f;
                    pendingTravelMetres = 0f;
                }

                return;
            }

            if (!ReferenceEquals(lastTickTrainset, trainset))
            {
                lastTickTrainset = trainset;
                faultUpdateElapsed = 0f;
                pendingTravelMetres = 0f;
            }

            faultUpdateElapsed += Mathf.Max(0f, deltaTime);
            pendingTravelMetres += distanceMetres;
            if (faultUpdateElapsed < FaultUpdateIntervalSeconds)
            {
                return;
            }

            distanceMetres = pendingTravelMetres;
            faultUpdateElapsed = 0f;
            pendingTravelMetres = 0f;

            reusableCarsByGuid.Clear();
            for (int index = 0; index < trainset.cars.Count; index++)
            {
                TrainCar car = trainset.cars[index];
                if (car != null)
                {
                    reusableCarsByGuid[TrainIdentity.SafeCarGuid(car)] = car;
                }
            }

            bool changed = false;
            List<MechanicalFaultData> faults = InspectionPersistence.Data.Faults;
            for (int index = 0; index < faults.Count; index++)
            {
                MechanicalFaultData fault = faults[index];
                if (fault == null || !reusableCarsByGuid.TryGetValue(fault.CarGuid, out TrainCar car))
                {
                    continue;
                }

                MechanicalFaultType type = (MechanicalFaultType)fault.FaultTypeValue;
                if (type == MechanicalFaultType.CouplerSeparation
                    && !Main.Settings.AllowCouplerSeparationFaults)
                {
                    continue;
                }

                if (!fault.Active)
                {
                    fault.TravelMetres += distanceMetres;
                    if (fault.TravelMetres >= fault.TravelUntilActivationMetres)
                    {
                        fault.Active = true;
                        changed = true;
                        activeStateDirty = true;
                        Announce("Mechanical failure on " + fault.CarId + ": " + Describe(fault) + ".");
                        Main.ModEntry.Logger.Warning(LastEvent);
                    }
                }

                if (!fault.Active)
                {
                    continue;
                }

                if (type == MechanicalFaultType.DraggingHandbrake && !fault.ActionTriggered
                    && car.brakeSystem != null && car.brakeSystem.hasHandbrake)
                {
                    car.brakeSystem.SetHandbrakePosition(Mathf.Max(car.brakeSystem.handbrakePosition,
                        Mathf.Lerp(0.25f, 0.75f, fault.Severity)));
                    fault.ActionTriggered = true;
                    changed = true;
                }
                else if (type == MechanicalFaultType.CouplerSeparation && !fault.ActionTriggered)
                {
                    Coupler coupler = ChooseConnectedCoupler(car);
                    if (coupler != null)
                    {
                        fault.ActionTriggered = true;
                        changed = true;
                        coupler.Uncouple(true, false, true);
                        Announce("Coupler failure on " + fault.CarId + " caused a train separation.");
                        Main.ModEntry.Logger.Warning(LastEvent);
                    }
                }
            }

            if (changed)
            {
                InvalidateActiveFaultState();
                InspectionPersistence.Save();
            }
        }

        internal static void ApplyBrakeEffects(BrakeSystem brake, float deltaTime)
        {
            if (brake == null || !Main.IsEnabled || !Main.Settings.EnableMechanicalFailures || !CheckHasActiveBrakeFaults())
            {
                return;
            }

            if (!brakeFaultCache.TryGetValue(brake, out List<MechanicalFaultData> faults))
            {
                TrainCar car = brake.GetComponent<TrainCar>() ?? brake.GetComponentInParent<TrainCar>();
                string guid = car == null ? null : TrainIdentity.SafeCarGuid(car);
                activeBrakeFaultsByCarGuid.TryGetValue(guid ?? string.Empty, out faults);
                brakeFaultCache[brake] = faults;
            }

            if (faults == null)
            {
                return;
            }

            for (int index = 0; index < faults.Count; index++)
            {
                MechanicalFaultData fault = faults[index];
                if (fault == null || !fault.Active)
                {
                    continue;
                }

                float severity = Mathf.Clamp01(fault.Severity);
                switch ((MechanicalFaultType)fault.FaultTypeValue)
                {
                    case MechanicalFaultType.BrakePipeLeak:
                        if (brake.brakeset != null)
                        {
                            float lossPerSecond = Mathf.Lerp(0.015f, 0.08f, severity);
                            brake.brakeset.pipePressure = Mathf.Max(1f,
                                brake.brakeset.pipePressure - lossPerSecond * Mathf.Max(0f, deltaTime));
                        }

                        break;
                    case MechanicalFaultType.DraggingHandbrake:
                        brake.brakeCylinderPressureUnsmoothed = Mathf.Max(brake.brakeCylinderPressureUnsmoothed,
                            Mathf.Lerp(1.8f, 3.2f, severity));
                        brake.brakingFactor = Mathf.Max(brake.brakingFactor, Mathf.Lerp(0.18f, 0.55f, severity));
                        break;
                    case MechanicalFaultType.WeakBrakeRigging:
                        brake.brakingFactor *= Mathf.Lerp(0.65f, 0.15f, severity);
                        break;
                    case MechanicalFaultType.StuckBrakeCylinder:
                        brake.brakeCylinderPressureUnsmoothed = Mathf.Max(brake.brakeCylinderPressureUnsmoothed,
                            Mathf.Lerp(2f, 4f, severity));
                        brake.brakingFactor = Mathf.Max(brake.brakingFactor, Mathf.Lerp(0.2f, 0.75f, severity));
                        break;
                    case MechanicalFaultType.HotBearing:
                        if (brake.heatController != null)
                        {
                            brake.heatController.temperature = Mathf.Min(1000f,
                                brake.heatController.temperature + Mathf.Lerp(8f, 30f, severity)
                                * Mathf.Max(0f, deltaTime));
                        }

                        brake.brakingFactor = Mathf.Max(brake.brakingFactor, Mathf.Lerp(0.04f, 0.14f, severity));
                        break;
                }
            }
        }

        internal static List<MechanicalFaultData> GetFaults(TrainCar car, bool includeHidden)
        {
            List<MechanicalFaultData> result = new List<MechanicalFaultData>();
            if (car == null)
            {
                return result;
            }

            string guid = TrainIdentity.SafeCarGuid(car);
            List<MechanicalFaultData> faults = InspectionPersistence.Data.Faults;
            for (int index = 0; index < faults.Count; index++)
            {
                MechanicalFaultData fault = faults[index];
                if (fault != null && fault.CarGuid == guid && (includeHidden || fault.Revealed || fault.Active))
                {
                    result.Add(fault);
                }
            }

            return result;
        }

        internal static List<MechanicalFaultData> RevealForItem(TrainCar car, WalkaroundItem item)
        {
            List<MechanicalFaultData> revealed = new List<MechanicalFaultData>();
            if (car == null)
            {
                return revealed;
            }

            string guid = TrainIdentity.SafeCarGuid(car);
            List<MechanicalFaultData> faults = InspectionPersistence.Data.Faults;
            for (int index = 0; index < faults.Count; index++)
            {
                MechanicalFaultData fault = faults[index];
                if (fault == null || fault.CarGuid != guid || !ItemCanReveal(item,
                        (MechanicalFaultType)fault.FaultTypeValue))
                {
                    continue;
                }

                if (!fault.Revealed)
                {
                    fault.Revealed = true;
                }

                revealed.Add(fault);
            }

            if (revealed.Count > 0)
            {
                InspectionPersistence.Save();
            }

            return revealed;
        }

        internal static int GetRepairCost(MechanicalFaultData fault)
        {
            if (fault == null)
            {
                return 150;
            }

            return Mathf.RoundToInt(150f + 650f * Mathf.Clamp01(fault.Severity));
        }

        internal static bool Repair(string faultId, out int cost, out string reason)
        {
            cost = 0;
            reason = string.Empty;
            List<MechanicalFaultData> faults = InspectionPersistence.Data.Faults;
            for (int index = 0; index < faults.Count; index++)
            {
                MechanicalFaultData fault = faults[index];
                if (fault == null || fault.Id != faultId)
                {
                    continue;
                }

                if (!fault.Revealed && !fault.Active)
                {
                    reason = "The latent defect has not been found by an inspection.";
                    return false;
                }

                cost = GetRepairCost(fault);
                if (!PenaltyManager.TryCollectMoney(cost, out double collected, false) || collected < cost)
                {
                    reason = "Not enough money for the $" + cost + " repair.";
                    return false;
                }

                faults.RemoveAt(index);
                InvalidateActiveFaultState();
                InspectionPersistence.Save();
                reason = "Repaired " + Describe(fault) + " on " + fault.CarId + ".";
                Announce(reason);
                return true;
            }

            reason = "That fault no longer exists.";
            return false;
        }

        internal static string Describe(MechanicalFaultData fault)
        {
            if (fault == null)
            {
                return "unknown defect";
            }

            switch ((MechanicalFaultType)fault.FaultTypeValue)
            {
                case MechanicalFaultType.BrakePipeLeak:
                    return "brake-pipe leakage";
                case MechanicalFaultType.DraggingHandbrake:
                    return "dragging handbrake";
                case MechanicalFaultType.WeakBrakeRigging:
                    return "weak brake rigging";
                case MechanicalFaultType.StuckBrakeCylinder:
                    return "sticking brake cylinder";
                case MechanicalFaultType.HotBearing:
                    return "overheating wheel bearing";
                case MechanicalFaultType.CouplerSeparation:
                    return "defective coupler";
                default:
                    return "unknown defect";
            }
        }

        private static MechanicalFaultType FaultForItem(WalkaroundItem item)
        {
            switch (item)
            {
                case WalkaroundItem.HoseAndAngleCocks:
                    return MechanicalFaultType.BrakePipeLeak;
                case WalkaroundItem.Coupler:
                    return MechanicalFaultType.CouplerSeparation;
                case WalkaroundItem.Handbrake:
                    return MechanicalFaultType.DraggingHandbrake;
                case WalkaroundItem.WheelsAndBearings:
                    return MechanicalFaultType.HotBearing;
                case WalkaroundItem.BrakeRigging:
                    return UnityEngine.Random.value < 0.5f
                        ? MechanicalFaultType.WeakBrakeRigging
                        : MechanicalFaultType.StuckBrakeCylinder;
                default:
                    return MechanicalFaultType.WeakBrakeRigging;
            }
        }

        private static bool ItemCanReveal(WalkaroundItem item, MechanicalFaultType type)
        {
            switch (item)
            {
                case WalkaroundItem.HoseAndAngleCocks:
                    return type == MechanicalFaultType.BrakePipeLeak;
                case WalkaroundItem.Coupler:
                    return type == MechanicalFaultType.CouplerSeparation;
                case WalkaroundItem.Handbrake:
                    return type == MechanicalFaultType.DraggingHandbrake;
                case WalkaroundItem.BrakeRigging:
                    return type == MechanicalFaultType.WeakBrakeRigging
                           || type == MechanicalFaultType.StuckBrakeCylinder;
                case WalkaroundItem.WheelsAndBearings:
                    return type == MechanicalFaultType.HotBearing;
                default:
                    return false;
            }
        }

        private static bool HasAnyFault(string carGuid)
        {
            List<MechanicalFaultData> faults = InspectionPersistence.Data.Faults;
            for (int index = 0; index < faults.Count; index++)
            {
                MechanicalFaultData fault = faults[index];
                if (fault != null && fault.CarGuid == carGuid)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasFault(string carGuid, MechanicalFaultType type)
        {
            List<MechanicalFaultData> faults = InspectionPersistence.Data.Faults;
            for (int index = 0; index < faults.Count; index++)
            {
                MechanicalFaultData fault = faults[index];
                if (fault != null && fault.CarGuid == carGuid && fault.FaultTypeValue == (int)type)
                {
                    return true;
                }
            }

            return false;
        }

        private static TrainCar FindCar(Trainset trainset, string guid)
        {
            for (int index = 0; index < trainset.cars.Count; index++)
            {
                TrainCar car = trainset.cars[index];
                if (car != null && TrainIdentity.SafeCarGuid(car) == guid)
                {
                    return car;
                }
            }

            return null;
        }

        private static Coupler ChooseConnectedCoupler(TrainCar car)
        {
            if (car.rearCoupler != null && car.rearCoupler.IsCoupled())
            {
                return car.rearCoupler;
            }

            if (car.frontCoupler != null && car.frontCoupler.IsCoupled())
            {
                return car.frontCoupler;
            }

            return null;
        }

        private static void Shuffle<T>(IList<T> values)
        {
            for (int index = values.Count - 1; index > 0; index--)
            {
                int other = UnityEngine.Random.Range(0, index + 1);
                T temporary = values[index];
                values[index] = values[other];
                values[other] = temporary;
            }
        }

        private static void Announce(string message)
        {
            LastEvent = message;
            LastEventAt = Time.unscaledTime;
        }
    }

    [HarmonyPatch(typeof(BrakeSystem), "Tick")]
    internal static class BrakeSystemTickPatch
    {
        [HarmonyPostfix]
        private static void AfterBrakeTick(BrakeSystem __instance, float dt)
        {
            FaultRegistry.ApplyBrakeEffects(__instance, dt);
        }
    }
}
