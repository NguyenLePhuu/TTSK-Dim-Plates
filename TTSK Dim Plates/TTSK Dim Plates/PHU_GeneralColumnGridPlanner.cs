#pragma warning disable 1633

using System;
using System.Collections.Generic;

namespace Tekla.Technology.Akit.UserScript
{
    internal enum PHU_GeneralColumnVerticalTopology
    {
        BaseBetweenGrids = 0,
        LowerGridCuts = 1,
        UpperGridCuts = 2,
        BothGridsCut = 3
    }

    internal enum PHU_GeneralColumnVerticalChainRole
    {
        ShapeRefEdges = 0,
        ReferenceEnvelope = 1,
        GridEnvelope = 2
    }

    internal enum PHU_GeneralColumnHorizontalRelation
    {
        GridLeftOfReference = 0,
        GridRightOfReference = 1,
        GridCoincidentWithReference = 2
    }

    internal sealed class PHU_GeneralColumnVerticalChain
    {
        public PHU_GeneralColumnVerticalChainRole Role;
        public double[] Stations;
    }

    internal sealed class PHU_GeneralColumnVerticalPlan
    {
        public PHU_GeneralColumnVerticalTopology Topology;
        public readonly List<PHU_GeneralColumnVerticalChain> LeftInnerToOuter =
            new List<PHU_GeneralColumnVerticalChain>();
        public readonly List<PHU_GeneralColumnVerticalChain> RightInnerToOuter =
            new List<PHU_GeneralColumnVerticalChain>();
    }

    internal sealed class PHU_GeneralColumnHorizontalPlan
    {
        public PHU_GeneralColumnHorizontalRelation Relation;
        public double[] Stations;
    }

    /// <summary>
    /// Tekla-independent geometry planner for the company-standard Column Grid
    /// DIM strategy. Inzai does not call this class and keeps its own topology.
    /// All vertical station arrays are ordered bottom-to-top. Horizontal
    /// non-coincident arrays deliberately retain semantic REF -> GRID order.
    /// </summary>
    internal static class PHU_GeneralColumnGridPlanner
    {
        internal static PHU_GeneralColumnVerticalPlan BuildVertical(
            double lowerGrid,
            double upperGrid,
            double refBottom,
            double refTop,
            double edgeBottom,
            double edgeTop,
            double tolerance
        )
        {
            ValidateTolerance(tolerance);
            RequireFinite(lowerGrid, upperGrid, refBottom, refTop, edgeBottom, edgeTop);

            if (upperGrid - lowerGrid <= tolerance)
                throw new InvalidOperationException(
                    "The lower and upper Grid levels are not distinct."
                );
            if (refTop - refBottom <= tolerance)
                throw new InvalidOperationException("The MainPart REF span is not valid.");
            if (edgeTop - edgeBottom <= tolerance)
                throw new InvalidOperationException("The Shape edge span is not valid.");
            if (edgeBottom < refBottom - tolerance || edgeTop > refTop + tolerance)
            {
                throw new InvalidOperationException(
                    "The Shape edge span is not contained by the REF span."
                );
            }
            if (lowerGrid > refTop + tolerance || upperGrid < refBottom - tolerance)
            {
                throw new InvalidOperationException(
                    "The selected Grid pair does not overlap or bracket the REF span."
                );
            }

            bool lowerCuts = lowerGrid > refBottom + tolerance;
            bool upperCuts = upperGrid < refTop - tolerance;

            PHU_GeneralColumnVerticalPlan result = new PHU_GeneralColumnVerticalPlan();
            if (lowerCuts && upperCuts)
                result.Topology = PHU_GeneralColumnVerticalTopology.BothGridsCut;
            else if (lowerCuts)
                result.Topology = PHU_GeneralColumnVerticalTopology.LowerGridCuts;
            else if (upperCuts)
                result.Topology = PHU_GeneralColumnVerticalTopology.UpperGridCuts;
            else
                result.Topology = PHU_GeneralColumnVerticalTopology.BaseBetweenGrids;

            double[] shape = SortedUnique(tolerance, refBottom, edgeBottom, edgeTop, refTop);

            List<double> referenceValues = new List<double>();
            referenceValues.Add(refBottom);
            referenceValues.Add(refTop);
            if (lowerGrid < refBottom - tolerance)
                referenceValues.Add(lowerGrid);
            if (upperGrid > refTop + tolerance)
                referenceValues.Add(upperGrid);
            double[] reference = SortedUnique(tolerance, referenceValues);

            List<double> outerValues = new List<double>();
            outerValues.Add(lowerGrid);
            outerValues.Add(upperGrid);
            if (lowerCuts)
                outerValues.Add(refBottom);
            if (upperCuts)
                outerValues.Add(refTop);
            double[] outer = SortedUnique(tolerance, outerValues);

            AddDistinctChain(
                result.LeftInnerToOuter,
                PHU_GeneralColumnVerticalChainRole.ShapeRefEdges,
                shape,
                tolerance
            );
            AddDistinctChain(
                result.LeftInnerToOuter,
                PHU_GeneralColumnVerticalChainRole.ReferenceEnvelope,
                reference,
                tolerance
            );
            AddDistinctChain(
                result.LeftInnerToOuter,
                PHU_GeneralColumnVerticalChainRole.GridEnvelope,
                outer,
                tolerance
            );

            AddDistinctChain(
                result.RightInnerToOuter,
                PHU_GeneralColumnVerticalChainRole.ShapeRefEdges,
                shape,
                tolerance
            );
            AddDistinctChain(
                result.RightInnerToOuter,
                PHU_GeneralColumnVerticalChainRole.ReferenceEnvelope,
                reference,
                tolerance
            );

            if (result.LeftInnerToOuter.Count == 0 || result.RightInnerToOuter.Count == 0)
            {
                throw new InvalidOperationException("The general Column vertical plan is empty.");
            }
            return result;
        }

        internal static PHU_GeneralColumnHorizontalPlan BuildHorizontal(
            double reference,
            double grid,
            double edgeLeft,
            double edgeRight,
            double tolerance
        )
        {
            ValidateTolerance(tolerance);
            RequireFinite(reference, grid, edgeLeft, edgeRight);
            if (edgeRight - edgeLeft <= tolerance)
                throw new InvalidOperationException(
                    "The MainPart horizontal edge span is not valid."
                );

            PHU_GeneralColumnHorizontalPlan result = new PHU_GeneralColumnHorizontalPlan();
            if (Math.Abs(grid - reference) <= tolerance)
            {
                if (reference <= edgeLeft + tolerance || reference >= edgeRight - tolerance)
                {
                    throw new InvalidOperationException(
                        "A coincident vertical Grid is not inside the MainPart edges."
                    );
                }
                result.Relation = PHU_GeneralColumnHorizontalRelation.GridCoincidentWithReference;
                result.Stations = SortedUnique(tolerance, edgeLeft, grid, edgeRight);
                if (result.Stations.Length != 3)
                    throw new InvalidOperationException(
                        "The edge -> coincident Grid -> edge chain did not retain three feet."
                    );
            }
            else
            {
                result.Relation =
                    grid < reference
                        ? PHU_GeneralColumnHorizontalRelation.GridLeftOfReference
                        : PHU_GeneralColumnHorizontalRelation.GridRightOfReference;
                // Semantic order is intentionally REF -> GRID on both sides.
                result.Stations = new double[] { reference, grid };
            }
            return result;
        }

        internal static double[] SelectFloorPair(IEnumerable<double> coordinates, double tolerance)
        {
            ValidateTolerance(tolerance);
            double[] ordered = SortedUnique(tolerance, coordinates);
            if (ordered.Length < 2)
                throw new InvalidOperationException(
                    "At least two distinct horizontal Grid levels are required."
                );
            return new double[] { ordered[0], ordered[ordered.Length - 1] };
        }

        internal static double SelectNearestCoordinate(
            double reference,
            IEnumerable<double> coordinates,
            double tolerance
        )
        {
            ValidateTolerance(tolerance);
            RequireFinite(reference);
            double[] ordered = SortedUnique(tolerance, coordinates);
            if (ordered.Length == 0)
                throw new InvalidOperationException(
                    "No overlapping vertical Grid candidate is available."
                );

            double selected = ordered[0];
            double best = Math.Abs(selected - reference);
            for (int i = 1; i < ordered.Length; i++)
            {
                double distance = Math.Abs(ordered[i] - reference);
                if (distance < best - tolerance)
                {
                    selected = ordered[i];
                    best = distance;
                }
                else if (
                    Math.Abs(distance - best) <= tolerance
                    && Math.Abs(ordered[i] - selected) > tolerance
                )
                {
                    throw new InvalidOperationException(
                        "Two vertical Grids are equally close to the MainPart REF."
                    );
                }
            }
            return selected;
        }

        private static void AddDistinctChain(
            List<PHU_GeneralColumnVerticalChain> target,
            PHU_GeneralColumnVerticalChainRole role,
            double[] stations,
            double tolerance
        )
        {
            if (stations == null || stations.Length < 2)
                return;
            for (int i = 0; i < target.Count; i++)
            {
                if (ChainsMatch(target[i].Stations, stations, tolerance))
                    return;
            }

            PHU_GeneralColumnVerticalChain chain = new PHU_GeneralColumnVerticalChain();
            chain.Role = role;
            chain.Stations = stations;
            target.Add(chain);
        }

        private static bool ChainsMatch(double[] first, double[] second, double tolerance)
        {
            if (first == null || second == null || first.Length != second.Length)
                return false;
            for (int i = 0; i < first.Length; i++)
            {
                if (Math.Abs(first[i] - second[i]) > tolerance)
                    return false;
            }
            return true;
        }

        private static double[] SortedUnique(double tolerance, params double[] values)
        {
            return SortedUnique(tolerance, (IEnumerable<double>)values);
        }

        private static double[] SortedUnique(double tolerance, IEnumerable<double> values)
        {
            List<double> ordered = new List<double>();
            if (values != null)
            {
                foreach (double value in values)
                {
                    RequireFinite(value);
                    ordered.Add(value);
                }
            }
            ordered.Sort();

            List<double> unique = new List<double>();
            for (int i = 0; i < ordered.Count; i++)
            {
                if (
                    unique.Count == 0
                    || Math.Abs(ordered[i] - unique[unique.Count - 1]) > tolerance
                )
                    unique.Add(ordered[i]);
            }
            return unique.ToArray();
        }

        private static void ValidateTolerance(double tolerance)
        {
            if (!IsFinite(tolerance) || tolerance <= 0.0)
                throw new ArgumentOutOfRangeException("tolerance");
        }

        private static void RequireFinite(params double[] values)
        {
            for (int i = 0; values != null && i < values.Length; i++)
            {
                if (!IsFinite(values[i]))
                    throw new InvalidOperationException(
                        "A general Column Grid coordinate is not finite."
                    );
            }
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }
    }
}
