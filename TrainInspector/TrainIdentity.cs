using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using DV.Simulation.Brake;

namespace TrainInspector
{
    internal static class TrainIdentity
    {
        internal static string SafeCarId(TrainCar car)
        {
            if (car == null)
            {
                return "unknown";
            }

            try
            {
                if (!string.IsNullOrEmpty(car.ID))
                {
                    return car.ID;
                }
            }
            catch
            {
                // A freshly spawned logic car can initialize one frame after the physical car.
            }

            return string.IsNullOrEmpty(car.name)
                ? "unknown"
                : car.name.Replace("(Clone)", string.Empty).Trim();
        }

        internal static string SafeCarGuid(TrainCar car)
        {
            if (car == null)
            {
                return "missing";
            }

            try
            {
                if (!string.IsNullOrEmpty(car.CarGUID))
                {
                    return car.CarGUID;
                }
            }
            catch
            {
                // Use a session-only fallback while a car is still initializing.
            }

            string id = SafeCarId(car);
            return id != "unknown" ? "id-" + id : "instance-" + car.GetInstanceID();
        }

        private sealed class ConsistCacheEntry
        {
            internal int CarCount;
            internal TrainCar FirstCar;
            internal TrainCar LastCar;
            internal string Signature;
        }

        private static readonly Dictionary<Trainset, ConsistCacheEntry> signatureCache =
            new Dictionary<Trainset, ConsistCacheEntry>();
        private static readonly SHA256 sharedSha256 = SHA256.Create();
        private static readonly object shaLock = new object();

        internal static string BuildConsistSignature(Trainset trainset)
        {
            if (trainset == null || trainset.cars == null || trainset.cars.Count == 0)
            {
                return string.Empty;
            }

            int count = trainset.cars.Count;
            TrainCar first = trainset.cars[0];
            TrainCar last = trainset.cars[count - 1];

            if (signatureCache.TryGetValue(trainset, out ConsistCacheEntry cached)
                && cached != null
                && cached.CarCount == count
                && cached.FirstCar == first
                && cached.LastCar == last)
            {
                return cached.Signature;
            }

            List<string> ids = new List<string>(count);
            for (int index = 0; index < count; index++)
            {
                TrainCar car = trainset.cars[index];
                if (car == null)
                {
                    return string.Empty;
                }

                ids.Add(SafeCarGuid(car));
            }

            string forward = string.Join("|", ids.ToArray());
            ids.Reverse();
            string reverse = string.Join("|", ids.ToArray());
            string canonical = string.CompareOrdinal(forward, reverse) <= 0 ? forward : reverse;

            string signature;
            byte[] bytesToHash = Encoding.UTF8.GetBytes(canonical);
            lock (shaLock)
            {
                byte[] bytes = sharedSha256.ComputeHash(bytesToHash);
                StringBuilder result = new StringBuilder(bytes.Length * 2);
                for (int index = 0; index < bytes.Length; index++)
                {
                    result.Append(bytes[index].ToString("x2"));
                }

                signature = result.ToString();
            }

            signatureCache[trainset] = new ConsistCacheEntry
            {
                CarCount = count,
                FirstCar = first,
                LastCar = last,
                Signature = signature
            };

            return signature;
        }

        internal static TrainCar FindControllingCar(Trainset trainset, TrainCar preferred = null)
        {
            if (trainset == null || trainset.cars == null)
            {
                return null;
            }

            TrainCar playerCar = null;
            try
            {
                playerCar = PlayerManager.PlayerTransform != null ? PlayerManager.Car : null;
            }
            catch
            {
            }

            if (preferred == null && playerCar != null && trainset.cars.Contains(playerCar) && HasTrainBrake(playerCar))
            {
                preferred = playerCar;
            }

            // If a preferred car is specified in this trainset and has a train brake, ALWAYS prioritize it.
            // Cab setup is often performed before the cutout valve is engaged, so do not bypass the player's
            // chosen car in favor of trailing units that happen to have their cutouts open.
            if (preferred != null && trainset.cars.Contains(preferred) && HasTrainBrake(preferred))
            {
                return preferred;
            }

            for (int index = 0; index < trainset.cars.Count; index++)
            {
                TrainCar car = trainset.cars[index];
                if (HasEnabledTrainBrake(car))
                {
                    return car;
                }
            }

            for (int index = 0; index < trainset.cars.Count; index++)
            {
                TrainCar car = trainset.cars[index];
                if (HasTrainBrake(car))
                {
                    return car;
                }
            }

            return null;
        }

        internal static int CountEnabledTrainBrakeValves(Trainset trainset, Brakeset brakeSet = null)
        {
            if (trainset == null || trainset.cars == null)
            {
                return 0;
            }

            int result = 0;
            for (int index = 0; index < trainset.cars.Count; index++)
            {
                TrainCar car = trainset.cars[index];
                BrakeSystem brake = car == null ? null : car.brakeSystem;
                if (brake != null && brake.hasTrainBrake && brake.trainBrakeCutout
                    && (brakeSet == null || ReferenceEquals(brake.brakeset, brakeSet)))
                {
                    result++;
                }
            }

            return result;
        }

        internal static bool HasTrainBrake(TrainCar car)
        {
            BrakeSystem brake = car == null ? null : car.brakeSystem;
            return brake != null && brake.hasTrainBrake;
        }

        internal static bool HasEnabledTrainBrake(TrainCar car)
        {
            BrakeSystem brake = car == null ? null : car.brakeSystem;
            return brake != null && brake.hasTrainBrake && brake.trainBrakeCutout;
        }
    }
}
