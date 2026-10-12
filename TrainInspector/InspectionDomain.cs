using System;
using System.Collections.Generic;

namespace TrainInspector
{
    internal enum InspectionType
    {
        FullDeparture,
        Continuity,
        CabChange,
        QuickSetAndRelease,
        Rolling
    }

    [Flags]
    internal enum WalkaroundItem
    {
        None = 0,
        LeftSide = 1 << 0,
        RightSide = 1 << 1,
        HoseAndAngleCocks = 1 << 2,
        Coupler = 1 << 3,
        Handbrake = 1 << 4,
        BrakeRigging = 1 << 5,
        WheelsAndBearings = 1 << 6
    }

    [Flags]
    internal enum CabSetupItem
    {
        None = 0,
        MainReservoir = 1 << 0,
        ControllingValve = 1 << 1,
        TrailingValvesCutOut = 1 << 2,
        IndependentBrakeReleased = 1 << 3,
        CompressorAvailable = 1 << 4,
        All = MainReservoir | ControllingValve | TrailingValvesCutOut | IndependentBrakeReleased
              | CompressorAvailable
    }

    internal enum MechanicalFaultType
    {
        BrakePipeLeak,
        DraggingHandbrake,
        WeakBrakeRigging,
        StuckBrakeCylinder,
        HotBearing,
        CouplerSeparation
    }

    internal sealed class MissedInspectionItem
    {
        internal TrainCar Car;
        internal int CarNumber;
        internal string CarId;
        internal string CarGuid;
        internal WalkaroundItem Item;

        internal string Description
        {
            get { return "car " + CarNumber + " (" + CarId + "): " + InspectionTypeRules.ItemLabel(Item); }
        }
    }

    internal static class InspectionTypeRules
    {
        internal static readonly WalkaroundItem[] EquipmentItems =
        {
            WalkaroundItem.HoseAndAngleCocks,
            WalkaroundItem.Coupler,
            WalkaroundItem.Handbrake,
            WalkaroundItem.BrakeRigging,
            WalkaroundItem.WheelsAndBearings
        };

        internal static string DisplayName(InspectionType type)
        {
            switch (type)
            {
                case InspectionType.FullDeparture:
                    return "Full departure test";
                case InspectionType.Continuity:
                    return "Continuity test";
                case InspectionType.CabChange:
                    return "Cab-change test";
                case InspectionType.QuickSetAndRelease:
                    return "Quick set-and-release";
                case InspectionType.Rolling:
                    return "Post-departure rolling inspection";
                default:
                    return type.ToString();
            }
        }

        internal static string ShortDescription(InspectionType type)
        {
            switch (type)
            {
                case InspectionType.FullDeparture:
                    return "Complete walk-around, leakage hold, application, release, and rear-car verification.";
                case InspectionType.Continuity:
                    return "Use after cars are added or removed; verifies connections and rear-car response.";
                case InspectionType.CabChange:
                    return "Use after changing the controlling locomotive or cab.";
                case InspectionType.QuickSetAndRelease:
                    return "A short functional brake response check after minor work.";
                case InspectionType.Rolling:
                    return "Inspect wheels, bearings, and brake rigging after the first controlled stop.";
                default:
                    return string.Empty;
            }
        }

        internal static WalkaroundItem RequiredWalkaroundItems(InspectionType type)
        {
            switch (type)
            {
                case InspectionType.FullDeparture:
                    return WalkaroundItem.LeftSide | WalkaroundItem.RightSide
                           | WalkaroundItem.HoseAndAngleCocks | WalkaroundItem.Coupler
                           | WalkaroundItem.Handbrake | WalkaroundItem.BrakeRigging
                           | WalkaroundItem.WheelsAndBearings;
                case InspectionType.Continuity:
                    return WalkaroundItem.HoseAndAngleCocks | WalkaroundItem.Coupler;
                case InspectionType.Rolling:
                    return WalkaroundItem.LeftSide | WalkaroundItem.RightSide
                           | WalkaroundItem.BrakeRigging | WalkaroundItem.WheelsAndBearings;
                default:
                    return WalkaroundItem.None;
            }
        }

        internal static bool RequiresCabSetup(InspectionType type)
        {
            return type != InspectionType.Rolling;
        }

        internal static bool RequiresBrakeCycle(InspectionType type)
        {
            return type != InspectionType.Rolling;
        }

        internal static bool RequiresLeakageTest(InspectionType type)
        {
            return type == InspectionType.FullDeparture;
        }

        internal static bool RequiresRearVerification(InspectionType type)
        {
            return type == InspectionType.FullDeparture || type == InspectionType.Continuity
                                                        || type == InspectionType.CabChange;
        }

        internal static string ItemLabel(WalkaroundItem item)
        {
            switch (item)
            {
                case WalkaroundItem.LeftSide:
                    return "left side visited";
                case WalkaroundItem.RightSide:
                    return "right side visited";
                case WalkaroundItem.HoseAndAngleCocks:
                    return "brake hose and angle cocks";
                case WalkaroundItem.Coupler:
                    return "coupler and cut lever";
                case WalkaroundItem.Handbrake:
                    return "handbrake";
                case WalkaroundItem.BrakeRigging:
                    return "brake rigging and cylinder";
                case WalkaroundItem.WheelsAndBearings:
                    return "wheels and bearings";
                default:
                    return item.ToString();
            }
        }

        internal static List<WalkaroundItem> Expand(WalkaroundItem mask)
        {
            List<WalkaroundItem> result = new List<WalkaroundItem>();
            WalkaroundItem[] values =
            {
                WalkaroundItem.LeftSide,
                WalkaroundItem.RightSide,
                WalkaroundItem.HoseAndAngleCocks,
                WalkaroundItem.Coupler,
                WalkaroundItem.Handbrake,
                WalkaroundItem.BrakeRigging,
                WalkaroundItem.WheelsAndBearings
            };

            for (int index = 0; index < values.Length; index++)
            {
                if ((mask & values[index]) != 0)
                {
                    result.Add(values[index]);
                }
            }

            return result;
        }
    }
}
