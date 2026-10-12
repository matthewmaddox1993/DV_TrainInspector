using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace TrainInspector
{
    [Serializable]
    public sealed class InspectionCertificateData
    {
        public string Id;
        public string ConsistSignature;
        public string ControlCarGuid;
        public int InspectionTypeValue;
        public int CarCount;
        public long IssuedUtcTicks;
        public long ExpiresUtcTicks;
        public float RemainingHours;
        public string Summary;
    }

    [Serializable]
    public sealed class MechanicalFaultData
    {
        public string Id;
        public string CarGuid;
        public string CarId;
        public int FaultTypeValue;
        public float Severity;
        public float TravelUntilActivationMetres;
        public float TravelMetres;
        public bool Active;
        public bool Revealed;
        public bool ActionTriggered;
        public long CreatedUtcTicks;
    }

    [Serializable]
    public sealed class CarInspectionHistoryData
    {
        public string CarGuid;
        public string CarId;
        public int CheckedWalkaroundMask;
        public long InspectedUtcTicks;
        public float RemainingHours;
    }

    [Serializable]
    public sealed class ActiveSessionData
    {
        public string ConsistSignature;
        public string PreferredCarGuid;
        public int TypeValue;
        public int PhaseValue;
        public bool IsReversed;
        public bool CabSetupInspected;
        public int CabPassedItemsValue;
        public bool LeakageCompleted;
        public float RecordedLeakage;
        public float ServiceReduction;
        public bool RearApplicationConfirmed;
        public bool RearReleaseConfirmed;
        public float PhaseElapsed;
    }

    [Serializable]
    public sealed class InspectorRuntimeData
    {
        public int Version = 1;
        public double TotalFinesAssessed;
        public double TotalFinesCollected;
        public ActiveSessionData ActiveSession;
        public List<InspectionCertificateData> Certificates = new List<InspectionCertificateData>();
        public List<MechanicalFaultData> Faults = new List<MechanicalFaultData>();
        public List<CarInspectionHistoryData> InspectedCars = new List<CarInspectionHistoryData>();
    }

    internal static class InspectionPersistence
    {
        private static string statePath;

        internal static InspectorRuntimeData Data { get; private set; } = new InspectorRuntimeData();

        internal static void Initialize(string modDirectory)
        {
            statePath = Path.Combine(modDirectory, "TrainInspectorState.xml");
            Data = Load();
            Normalize();
        }

        internal static void Save()
        {
            if (string.IsNullOrEmpty(statePath))
            {
                return;
            }

            try
            {
                string temporaryPath = statePath + ".tmp";
                XmlSerializer serializer = new XmlSerializer(typeof(InspectorRuntimeData));
                using (FileStream stream = File.Create(temporaryPath))
                {
                    serializer.Serialize(stream, Data);
                }

                File.Copy(temporaryPath, statePath, true);
                File.Delete(temporaryPath);
            }
            catch (Exception exception)
            {
                if (Main.ModEntry != null)
                {
                    Main.ModEntry.Logger.Warning("Could not save Train Inspector state: " + exception.Message);
                }
            }
        }

        private static InspectorRuntimeData Load()
        {
            if (string.IsNullOrEmpty(statePath) || !File.Exists(statePath))
            {
                return new InspectorRuntimeData();
            }

            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(InspectorRuntimeData));
                using (FileStream stream = File.OpenRead(statePath))
                {
                    InspectorRuntimeData loaded = serializer.Deserialize(stream) as InspectorRuntimeData;
                    return loaded ?? new InspectorRuntimeData();
                }
            }
            catch (Exception exception)
            {
                if (Main.ModEntry != null)
                {
                    Main.ModEntry.Logger.Warning("Could not load Train Inspector state; starting clean: "
                                                 + exception.Message);
                }

                return new InspectorRuntimeData();
            }
        }

        private static void Normalize()
        {
            if (Data.Certificates == null)
            {
                Data.Certificates = new List<InspectionCertificateData>();
            }

            if (Data.Faults == null)
            {
                Data.Faults = new List<MechanicalFaultData>();
            }

            if (Data.InspectedCars == null)
            {
                Data.InspectedCars = new List<CarInspectionHistoryData>();
            }
        }

        internal static void AdvanceTime(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            float deltaHours = deltaTime / 3600f;
            List<InspectionCertificateData> certs = Data.Certificates;
            if (certs != null)
            {
                for (int i = 0; i < certs.Count; i++)
                {
                    InspectionCertificateData cert = certs[i];
                    if (cert != null && cert.RemainingHours > 0f)
                    {
                        cert.RemainingHours -= deltaHours;
                        if (cert.RemainingHours < 0f)
                        {
                            cert.RemainingHours = 0f;
                        }
                    }
                }
            }

            List<CarInspectionHistoryData> cars = Data.InspectedCars;
            if (cars != null)
            {
                for (int i = 0; i < cars.Count; i++)
                {
                    CarInspectionHistoryData car = cars[i];
                    if (car != null && car.RemainingHours > 0f)
                    {
                        car.RemainingHours -= deltaHours;
                        if (car.RemainingHours < 0f)
                        {
                            car.RemainingHours = 0f;
                        }
                    }
                }
            }
        }
    }

    internal static class InspectionCertificateRegistry
    {
        internal static bool IsCertificateValid(InspectionCertificateData certificate)
        {
            if (certificate == null)
            {
                return false;
            }

            if (certificate.RemainingHours > 0f)
            {
                return true;
            }

            // Fallback for legacy state files where RemainingHours was uninitialized (0):
            long now = DateTime.UtcNow.Ticks;
            if (certificate.ExpiresUtcTicks > now)
            {
                certificate.RemainingHours = (float)TimeSpan.FromTicks(certificate.ExpiresUtcTicks - now).TotalHours;
                return certificate.RemainingHours > 0f;
            }

            return false;
        }

        internal static InspectionCertificateData Issue(InspectionSession session)
        {
            if (session == null || session.Report == null || !session.Report.Passed)
            {
                return null;
            }

            Trainset trainset = session.GetCurrentTrainset();
            string signature = TrainIdentity.BuildConsistSignature(trainset);
            if (string.IsNullOrEmpty(signature))
            {
                return null;
            }

            DateTime issued = DateTime.UtcNow;
            float validityHours = Main.Settings != null ? Main.Settings.CertificateValidityHours : 8f;
            InspectionCertificateData certificate = new InspectionCertificateData
            {
                Id = Guid.NewGuid().ToString("N"),
                ConsistSignature = signature,
                ControlCarGuid = TrainIdentity.SafeCarGuid(session.ControlCar),
                InspectionTypeValue = (int)session.Type,
                CarCount = session.CarCount,
                IssuedUtcTicks = issued.Ticks,
                ExpiresUtcTicks = issued.AddHours(validityHours).Ticks,
                RemainingHours = validityHours,
                Summary = session.Report.Summary
            };

            List<InspectionCertificateData> certificates = InspectionPersistence.Data.Certificates;
            for (int index = certificates.Count - 1; index >= 0; index--)
            {
                InspectionCertificateData existing = certificates[index];
                if (existing == null || existing.ConsistSignature == signature
                                     && existing.InspectionTypeValue == certificate.InspectionTypeValue)
                {
                    certificates.RemoveAt(index);
                }
            }

            certificates.Add(certificate);
            PruneExpired();
            InspectionPersistence.Save();
            return certificate;
        }

        internal static InspectionCertificateData GetValidDepartureCertificate(Trainset trainset)
        {
            string signature = TrainIdentity.BuildConsistSignature(trainset);
            if (string.IsNullOrEmpty(signature))
            {
                return null;
            }

            TrainCar control = TrainIdentity.FindControllingCar(trainset);
            string controlGuid = TrainIdentity.SafeCarGuid(control);
            List<InspectionCertificateData> certificates = InspectionPersistence.Data.Certificates;
            for (int index = certificates.Count - 1; index >= 0; index--)
            {
                InspectionCertificateData certificate = certificates[index];
                if (!IsCertificateValid(certificate))
                {
                    continue;
                }

                if (certificate.ConsistSignature == signature
                    && certificate.InspectionTypeValue != (int)InspectionType.Rolling)
                {
                    if (certificate.ControlCarGuid == controlGuid || TrainsetContainsCarGuid(trainset, certificate.ControlCarGuid))
                    {
                        return certificate;
                    }
                }
            }

            return null;
        }

        private static bool TrainsetContainsCarGuid(Trainset trainset, string guid)
        {
            if (trainset == null || trainset.cars == null || string.IsNullOrEmpty(guid))
            {
                return false;
            }

            for (int i = 0; i < trainset.cars.Count; i++)
            {
                if (TrainIdentity.SafeCarGuid(trainset.cars[i]) == guid)
                {
                    return true;
                }
            }

            return false;
        }

        internal static void Revoke(InspectionCertificateData certificate)
        {
            if (certificate == null || string.IsNullOrEmpty(certificate.Id))
            {
                return;
            }

            List<InspectionCertificateData> certificates = InspectionPersistence.Data.Certificates;
            for (int index = certificates.Count - 1; index >= 0; index--)
            {
                InspectionCertificateData existing = certificates[index];
                if (existing != null && existing.Id == certificate.Id)
                {
                    certificates.RemoveAt(index);
                    InspectionPersistence.Save();
                    return;
                }
            }
        }

        internal static void RevokeForConsist(string consistSignature)
        {
            if (string.IsNullOrEmpty(consistSignature))
            {
                return;
            }

            bool changed = false;
            List<InspectionCertificateData> certificates = InspectionPersistence.Data.Certificates;
            for (int index = certificates.Count - 1; index >= 0; index--)
            {
                InspectionCertificateData existing = certificates[index];
                if (existing != null && existing.ConsistSignature == consistSignature)
                {
                    certificates.RemoveAt(index);
                    changed = true;
                }
            }

            if (changed)
            {
                InspectionPersistence.Save();
            }
        }

        internal static InspectionCertificateData GetLatestRollingCertificate(Trainset trainset)
        {
            string signature = TrainIdentity.BuildConsistSignature(trainset);
            InspectionCertificateData latest = null;
            List<InspectionCertificateData> certificates = InspectionPersistence.Data.Certificates;
            for (int index = 0; index < certificates.Count; index++)
            {
                InspectionCertificateData candidate = certificates[index];
                if (candidate != null && candidate.ConsistSignature == signature
                                      && candidate.InspectionTypeValue == (int)InspectionType.Rolling
                                      && IsCertificateValid(candidate)
                                      && (latest == null || candidate.IssuedUtcTicks > latest.IssuedUtcTicks))
                {
                    latest = candidate;
                }
            }

            return latest;
        }

        internal static void PruneExpired()
        {
            List<InspectionCertificateData> certificates = InspectionPersistence.Data.Certificates;
            for (int index = certificates.Count - 1; index >= 0; index--)
            {
                InspectionCertificateData certificate = certificates[index];
                if (certificate == null || !IsCertificateValid(certificate))
                {
                    certificates.RemoveAt(index);
                }
            }
        }
    }

    internal static class CarInspectionHistory
    {
        internal static void RecordCarChecked(TrainCar car, WalkaroundItem checkedItems)
        {
            if (car == null)
            {
                return;
            }

            string guid = TrainIdentity.SafeCarGuid(car);
            string id = TrainIdentity.SafeCarId(car);
            RecordCarChecked(guid, id, checkedItems);
        }

        internal static void RecordCarChecked(string guid, string id, WalkaroundItem checkedItems)
        {
            if (string.IsNullOrEmpty(guid) || guid == "missing")
            {
                return;
            }

            long now = DateTime.UtcNow.Ticks;
            float validityHours = Main.Settings != null ? Main.Settings.CertificateValidityHours : 8f;
            List<CarInspectionHistoryData> list = InspectionPersistence.Data.InspectedCars;
            for (int i = 0; i < list.Count; i++)
            {
                CarInspectionHistoryData entry = list[i];
                if (entry != null && entry.CarGuid == guid)
                {
                    entry.CheckedWalkaroundMask = (int)checkedItems;
                    entry.InspectedUtcTicks = now;
                    entry.RemainingHours = validityHours;
                    entry.CarId = id;
                    InspectionPersistence.Save();
                    return;
                }
            }

            list.Add(new CarInspectionHistoryData
            {
                CarGuid = guid,
                CarId = id,
                CheckedWalkaroundMask = (int)checkedItems,
                InspectedUtcTicks = now,
                RemainingHours = validityHours
            });
            PruneExpired();
            InspectionPersistence.Save();
        }

        internal static WalkaroundItem GetValidCheckedItems(TrainCar car)
        {
            if (car == null)
            {
                return WalkaroundItem.None;
            }

            string guid = TrainIdentity.SafeCarGuid(car);
            return GetValidCheckedItems(guid);
        }

        internal static WalkaroundItem GetValidCheckedItems(string guid)
        {
            if (string.IsNullOrEmpty(guid) || guid == "missing")
            {
                return WalkaroundItem.None;
            }

            float hours = Main.Settings != null ? Main.Settings.CertificateValidityHours : 8f;
            long maxAgeTicks = TimeSpan.FromHours(hours).Ticks;
            long now = DateTime.UtcNow.Ticks;
            List<CarInspectionHistoryData> list = InspectionPersistence.Data.InspectedCars;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                CarInspectionHistoryData item = list[i];
                if (item != null && item.CarGuid == guid)
                {
                    if (item.RemainingHours > 0f)
                    {
                        return (WalkaroundItem)item.CheckedWalkaroundMask;
                    }

                    if (now - item.InspectedUtcTicks <= maxAgeTicks)
                    {
                        item.RemainingHours = (float)TimeSpan.FromTicks(maxAgeTicks - (now - item.InspectedUtcTicks)).TotalHours;
                        return (WalkaroundItem)item.CheckedWalkaroundMask;
                    }

                    return WalkaroundItem.None;
                }
            }

            return WalkaroundItem.None;
        }

        internal static void PruneExpired()
        {
            float hours = Main.Settings != null ? Main.Settings.CertificateValidityHours : 8f;
            long maxAgeTicks = TimeSpan.FromHours(hours).Ticks;
            long now = DateTime.UtcNow.Ticks;
            List<CarInspectionHistoryData> list = InspectionPersistence.Data.InspectedCars;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                CarInspectionHistoryData item = list[i];
                if (item == null || (item.RemainingHours <= 0f && (now - item.InspectedUtcTicks > maxAgeTicks)))
                {
                    list.RemoveAt(i);
                }
            }
        }
    }
}
