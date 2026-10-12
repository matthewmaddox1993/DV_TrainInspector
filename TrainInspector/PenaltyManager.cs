using System;
using System.Collections.Generic;
using System.Reflection;
using DV.InventorySystem;
using DV.Utils;
using HarmonyLib;
using UnityEngine;

namespace TrainInspector
{
    internal sealed class PenaltyEvent
    {
        internal DateTime OccurredAt;
        internal string TrainSignature;
        internal int MissedRequirements;
        internal int FineAssessed;
        internal double FineCollected;
        internal int FaultsCreated;
        internal string Summary;
    }

    internal static class PenaltyManager
    {
        private const float RollingInspectionMinimumDistance = 200f;
        private const float StoppedSpeedMetresPerSecond = 0.25f;
        private const float ConsistCheckInterval = 0.5f;
        private const float SpawnGracePeriodSeconds = 8.0f;
        private const float ConsistStabilizationSeconds = 5.0f;
        private const float DepartureSustainedDuration = 2.0f;
        private const float DepartureMinimumDistance = 12.0f;
        private const float MaxTeleportVelocityThreshold = 45.0f;

        private static string currentSignature;
        private static bool departureHandled;
        private static bool certifiedDeparture;
        private static float distanceSinceDeparture;
        private static bool rollingInspectionDue;
        private static long departureUtcTicks;
        private static float consistCheckTimer;
        private static float consistStabilizationTimer;
        private static float lastSpawnOrTeleportTime = -100f;
        private static float departureSpeedTimer;
        private static float accumulatedDepartureDistance;
        private static bool trainHasSettledStopped;
        private static float continuousRunningTimer;

        private static FieldInfo locoOwnershipTeleportingField;
        private static float lastLocoOwnershipLookupTime = -10f;
        private static bool locoOwnershipNotFound;

        internal static PenaltyEvent LastPenalty { get; private set; }

        internal static bool RollingInspectionDue
        {
            get { return rollingInspectionDue; }
        }

        internal static float DistanceSinceDeparture
        {
            get { return distanceSinceDeparture; }
        }

        internal static void ResetRuntime()
        {
            currentSignature = null;
            departureHandled = false;
            certifiedDeparture = false;
            distanceSinceDeparture = 0f;
            rollingInspectionDue = false;
            departureUtcTicks = 0L;
            consistCheckTimer = 0f;
            consistStabilizationTimer = 0f;
            lastSpawnOrTeleportTime = -100f;
            departureSpeedTimer = 0f;
            accumulatedDepartureDistance = 0f;
            trainHasSettledStopped = false;
            continuousRunningTimer = 0f;
            LastPenalty = null;
        }

        internal static void NotifyCarSpawnedOrTeleported(TrainCar car)
        {
            TrainCar occupied = SafePlayerCar();
            if (occupied == null || car == null || car.trainset == null || car.trainset == occupied.trainset || car == occupied)
            {
                lastSpawnOrTeleportTime = Time.time;
                trainHasSettledStopped = false;
                departureSpeedTimer = 0f;
                accumulatedDepartureDistance = 0f;
                continuousRunningTimer = 0f;
            }
        }

        private static bool IsLocoOwnershipTeleporting()
        {
            if (locoOwnershipTeleportingField == null && !locoOwnershipNotFound)
            {
                if (Time.time - lastLocoOwnershipLookupTime > 2f)
                {
                    lastLocoOwnershipLookupTime = Time.time;
                    try
                    {
                        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                        for (int i = 0; i < assemblies.Length; i++)
                        {
                            Assembly asm = assemblies[i];
                            if (asm != null && string.Equals(asm.GetName().Name, "LocoOwnership", StringComparison.OrdinalIgnoreCase))
                            {
                                Type teleporterType = asm.GetType("LocoOwnership.Shared.CarTeleporter", false);
                                if (teleporterType != null)
                                {
                                    locoOwnershipTeleportingField = teleporterType.GetField("isTeleportingTrain",
                                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                                }

                                if (locoOwnershipTeleportingField == null)
                                {
                                    locoOwnershipNotFound = true;
                                }
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // Silently ignore reflection errors
                    }
                }
            }

            if (locoOwnershipTeleportingField != null)
            {
                try
                {
                    object val = locoOwnershipTeleportingField.GetValue(null);
                    return val is bool b && b;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        internal static void Tick(InspectionSession activeSession, float deltaTime)
        {
            InspectionPersistence.AdvanceTime(deltaTime);

            TrainCar occupied = SafePlayerCar();
            Trainset trainset = occupied == null ? null : occupied.trainset;
            if (trainset == null || trainset.cars == null || trainset.cars.Count == 0)
            {
                currentSignature = null;
                departureHandled = false;
                certifiedDeparture = false;
                distanceSinceDeparture = 0f;
                rollingInspectionDue = false;
                departureUtcTicks = 0L;
                consistCheckTimer = 0f;
                consistStabilizationTimer = 0f;
                departureSpeedTimer = 0f;
                accumulatedDepartureDistance = 0f;
                trainHasSettledStopped = false;
                continuousRunningTimer = 0f;
                return;
            }

            if (consistStabilizationTimer > 0f)
            {
                consistStabilizationTimer -= deltaTime;
            }

            consistCheckTimer += deltaTime;
            if (consistCheckTimer >= ConsistCheckInterval || currentSignature == null)
            {
                consistCheckTimer = 0f;
                string signature = TrainIdentity.BuildConsistSignature(trainset);
                if (signature != currentSignature)
                {
                    currentSignature = signature;
                    departureHandled = false;
                    certifiedDeparture = false;
                    distanceSinceDeparture = 0f;
                    rollingInspectionDue = false;
                    departureUtcTicks = 0L;
                    consistStabilizationTimer = ConsistStabilizationSeconds;
                    departureSpeedTimer = 0f;
                    accumulatedDepartureDistance = 0f;
                    trainHasSettledStopped = false;
                    continuousRunningTimer = 0f;
                }

                if (rollingInspectionDue)
                {
                    InspectionCertificateData rollingCertificate =
                        InspectionCertificateRegistry.GetLatestRollingCertificate(trainset);
                    if (rollingCertificate != null && rollingCertificate.IssuedUtcTicks >= departureUtcTicks)
                    {
                        rollingInspectionDue = false;
                    }
                }
            }

            float speed = SafeSpeed(occupied);

            // Filter out teleport / physics anomaly spikes (> 45 m/s)
            if (speed > MaxTeleportVelocityThreshold)
            {
                departureSpeedTimer = 0f;
                accumulatedDepartureDistance = 0f;
                trainHasSettledStopped = false;
                return;
            }

            bool isTeleporting = IsLocoOwnershipTeleporting();
            bool inSpawnGracePeriod = (Time.time - lastSpawnOrTeleportTime) < SpawnGracePeriodSeconds;
            bool isConsistStabilizing = consistStabilizationTimer > 0f;

            if (speed <= StoppedSpeedMetresPerSecond)
            {
                continuousRunningTimer = 0f;
                if (!isTeleporting && !inSpawnGracePeriod)
                {
                    trainHasSettledStopped = true;
                }
            }
            else
            {
                continuousRunningTimer += deltaTime;
            }

            float travelled = Mathf.Max(0f, speed * Mathf.Max(0f, deltaTime));
            if (departureHandled)
            {
                distanceSinceDeparture += travelled;
            }

            FaultRegistry.TickTrain(trainset, travelled);

            float departureSpeed = Main.Settings.DepartureSpeedKmh / 3.6f;
            if (!departureHandled)
            {
                if (isTeleporting || inSpawnGracePeriod || isConsistStabilizing)
                {
                    departureSpeedTimer = 0f;
                    accumulatedDepartureDistance = 0f;
                }
                else
                {
                    bool isArmed = trainHasSettledStopped || continuousRunningTimer >= 30.0f;
                    if (isArmed && speed >= departureSpeed)
                    {
                        departureSpeedTimer += deltaTime;
                        accumulatedDepartureDistance += travelled;

                        if (departureSpeedTimer >= DepartureSustainedDuration &&
                            accumulatedDepartureDistance >= DepartureMinimumDistance)
                        {
                            HandleDeparture(trainset, activeSession);
                        }
                    }
                    else
                    {
                        departureSpeedTimer = 0f;
                        accumulatedDepartureDistance = 0f;
                    }
                }
            }

            if (certifiedDeparture && !rollingInspectionDue
                                   && distanceSinceDeparture >= RollingInspectionMinimumDistance
                                   && speed <= StoppedSpeedMetresPerSecond)
            {
                rollingInspectionDue = true;
                Main.ModEntry.Logger.Log("Train Inspector: post-departure rolling inspection is now due.");
            }
        }

        internal static void NotifyCertificateIssued(InspectionCertificateData certificate, Trainset trainset)
        {
            if (certificate == null || trainset == null)
            {
                return;
            }

            string signature = TrainIdentity.BuildConsistSignature(trainset);
            if (certificate.InspectionTypeValue == (int)InspectionType.Rolling)
            {
                if (signature == currentSignature)
                {
                    rollingInspectionDue = false;
                }

                return;
            }

            if (signature == currentSignature)
            {
                departureHandled = false;
                certifiedDeparture = true;
                distanceSinceDeparture = 0f;
                departureSpeedTimer = 0f;
                accumulatedDepartureDistance = 0f;
            }
        }

        internal static bool TryCollectMoney(double requested, out double collected, bool allowPartial = true)
        {
            collected = 0.0;
            if (requested <= 0.0)
            {
                return true;
            }

            try
            {
                Inventory inventory = SingletonBehaviour<Inventory>.Instance;
                if (inventory == null)
                {
                    return false;
                }

                if (!allowPartial && inventory.PlayerMoney < requested)
                {
                    return false;
                }

                collected = Math.Min(requested, Math.Max(0.0, inventory.PlayerMoney));
                if (collected <= 0.0)
                {
                    return false;
                }

                if (!inventory.RemoveMoney(collected))
                {
                    collected = 0.0;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                Main.ModEntry.Logger.Warning("Could not collect an inspection charge: " + exception.Message);
                collected = 0.0;
                return false;
            }
        }

        private static void HandleDeparture(Trainset trainset, InspectionSession activeSession)
        {
            departureHandled = true;
            distanceSinceDeparture = 0f;
            departureUtcTicks = DateTime.UtcNow.Ticks;

            int knownBadOrders = FaultRegistry.CountKnownBadOrders(trainset);
            int immediateFailuresTriggered = 0;
            List<MechanicalFaultData> immediateFaults =
                FaultRegistry.TriggerImmediateDepartureFailures(trainset, out immediateFailuresTriggered);

            InspectionCertificateData certificate =
                InspectionCertificateRegistry.GetValidDepartureCertificate(trainset);
            if (certificate != null && immediateFailuresTriggered == 0 && knownBadOrders == 0)
            {
                certifiedDeparture = true;
                Main.ModEntry.Logger.Log("Certified departure: inspection certificate " + certificate.Id + " accepted.");
                return;
            }

            certifiedDeparture = false;
            List<MissedInspectionItem> missedItems;
            int missedRequirements;
            string activeSignature = activeSession == null
                ? string.Empty
                : TrainIdentity.BuildConsistSignature(activeSession.GetCurrentTrainset());
            if (activeSession != null && activeSignature == currentSignature)
            {
                missedItems = activeSession.GetMissedEquipmentItems();
                missedRequirements = activeSession.GetMissedRequirementCount();
            }
            else
            {
                missedItems = BuildDefaultMissedItems(trainset, out missedRequirements);
            }

            if (missedRequirements <= 0 && (missedItems == null || missedItems.Count == 0)
                && immediateFailuresTriggered == 0 && knownBadOrders == 0)
            {
                certifiedDeparture = true;
                if (activeSession != null && activeSignature == currentSignature)
                {
                    if (activeSession.Certificate == null && activeSession.CanFinish(out _))
                    {
                        activeSession.Finish();
                    }
                }
                Main.ModEntry.Logger.Log("Certified departure: consist satisfied all inspection requirements.");
                return;
            }

            int fine = 0;
            if (missedRequirements > 0)
            {
                fine += Main.Settings.BaseMissedInspectionFine
                        + Main.Settings.FinePerMissedItem * Math.Max(0, missedRequirements);
            }

            if (knownBadOrders > 0)
            {
                fine += Main.Settings.BadOrderDepartureFine * knownBadOrders;
            }

            double collected = 0.0;
            if (Main.Settings.EnableWalletPenalties)
            {
                TryCollectMoney(fine, out collected);
            }
            else
            {
                fine = 0;
            }

            List<MechanicalFaultData> faults = FaultRegistry.RollForMissedItems(missedItems, Main.Settings);
            InspectionPersistence.Data.TotalFinesAssessed += fine;
            InspectionPersistence.Data.TotalFinesCollected += collected;
            InspectionPersistence.Save();

            LastPenalty = new PenaltyEvent
            {
                OccurredAt = DateTime.Now,
                TrainSignature = currentSignature,
                MissedRequirements = missedRequirements,
                FineAssessed = fine,
                FineCollected = collected,
                FaultsCreated = faults.Count + immediateFailuresTriggered,
                Summary = BuildPenaltySummary(missedRequirements, fine, collected, faults.Count,
                    immediateFailuresTriggered, knownBadOrders)
            };

            Main.ModEntry.Logger.Warning(LastPenalty.Summary);
        }

        private static List<MissedInspectionItem> BuildDefaultMissedItems(Trainset trainset, out int missedRequirements)
        {
            List<MissedInspectionItem> result = new List<MissedInspectionItem>();
            missedRequirements = 0;
            if (trainset == null || trainset.cars == null)
            {
                return result;
            }

            int uninspectedCars = 0;
            for (int carIndex = 0; carIndex < trainset.cars.Count; carIndex++)
            {
                TrainCar car = trainset.cars[carIndex];
                if (car == null)
                {
                    continue;
                }

                WalkaroundItem checkedMask = CarInspectionHistory.GetValidCheckedItems(car);
                WalkaroundItem requiredMask = Main.Settings.RequirePhysicalWalkaround
                    ? InspectionTypeRules.RequiredWalkaroundItems(InspectionType.FullDeparture)
                    : WalkaroundItem.None;

                List<WalkaroundItem> missing = InspectionTypeRules.Expand(requiredMask & ~checkedMask);
                if (missing.Count > 0)
                {
                    uninspectedCars++;
                    missedRequirements += missing.Count;
                }

                for (int itemIndex = 0; itemIndex < InspectionTypeRules.EquipmentItems.Length; itemIndex++)
                {
                    WalkaroundItem item = InspectionTypeRules.EquipmentItems[itemIndex];
                    if ((requiredMask & item) != 0 && (checkedMask & item) == 0)
                    {
                        result.Add(new MissedInspectionItem
                        {
                            Car = car,
                            CarNumber = carIndex + 1,
                            CarId = TrainIdentity.SafeCarId(car),
                            CarGuid = TrainIdentity.SafeCarGuid(car),
                            Item = item
                        });
                    }
                }
            }

            if (uninspectedCars > 0 || InspectionCertificateRegistry.GetValidDepartureCertificate(trainset) == null)
            {
                missedRequirements += 9;
            }

            return result;
        }

        private static string BuildPenaltySummary(int missed, int assessed, double collected, int newFaults,
            int immediateFailures, int knownBadOrders)
        {
            string result;
            if (knownBadOrders > 0 && missed <= 0)
            {
                result = "Defective departure: departed with " + knownBadOrders + " known BAD ORDER defect"
                         + (knownBadOrders == 1 ? "" : "s");
            }
            else if (missed > 0)
            {
                result = "Uninspected departure: " + missed + " required check" + (missed == 1 ? " was" : "s were")
                         + " missed";
                if (knownBadOrders > 0)
                {
                    result += " and departed with " + knownBadOrders + " known BAD ORDER defect"
                             + (knownBadOrders == 1 ? "" : "s");
                }
            }
            else
            {
                result = "Safety violation on departure";
            }

            if (immediateFailures > 0)
            {
                result += "; " + immediateFailures + " defect" + (immediateFailures == 1 ? " failed immediately" : "s failed immediately")
                          + " on departure";
            }

            if (assessed > 0)
            {
                result += "; $" + assessed + " assessed";
                if (collected < assessed)
                {
                    result += " ($" + collected.ToString("0") + " collected from available cash)";
                }
            }

            if (newFaults > 0)
            {
                result += "; " + newFaults + " latent mechanical risk" + (newFaults == 1 ? " was" : "s were")
                          + " created";
            }

            return result + ".";
        }

        private static TrainCar SafePlayerCar()
        {
            try
            {
                return PlayerManager.Car;
            }
            catch
            {
                return null;
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
                return car.rb == null ? 0f : car.rb.velocity.magnitude;
            }
        }
    }

    [HarmonyPatch(typeof(TrainCar), "MoveToTrack")]
    internal static class TrainCarMoveToTrackPatch
    {
        [HarmonyPostfix]
        private static void AfterMoveToTrack(TrainCar __instance)
        {
            try
            {
                PenaltyManager.NotifyCarSpawnedOrTeleported(__instance);
            }
            catch (Exception ex)
            {
                Main.ModEntry?.Logger?.Warning("Train Inspector: error in TrainCarMoveToTrackPatch: " + ex.Message);
            }
        }
    }
}
