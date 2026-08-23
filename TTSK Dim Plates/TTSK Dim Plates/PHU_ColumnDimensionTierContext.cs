#pragma warning disable 1633

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tekla.Technology.Akit.UserScript
{
    public enum PHU_ColumnShapeFamily
    {
        Unknown = 0,
        ShapeIH = 1,
        ShapeC = 2,
        ShapeL = 3
    }

    /// <summary>
    /// Per-run handoff for project-specific column DIM tiers.  The spacing
    /// source remains the Shape implementation that actually ran; Neighbor
    /// publishes only its final semantic tier and Grid consumes it.
    /// </summary>
    public static class PHU_ColumnDimensionTierContext
    {
        private static bool _enabled;
        private static PHU_ColumnShapeFamily _family;
        private static readonly Dictionary<string, double> NeighborOutermostVerticalLines =
            new Dictionary<string, double>(StringComparer.Ordinal);

        public static void Configure(bool enabled, PHU_ColumnShapeFamily family)
        {
            Reset();
            _enabled = enabled;
            _family = family;
        }

        public static void Reset()
        {
            _enabled = false;
            _family = PHU_ColumnShapeFamily.Unknown;
            NeighborOutermostVerticalLines.Clear();
        }

        public static void ClearNeighborTiers()
        {
            NeighborOutermostVerticalLines.Clear();
        }

        public static bool TryGetShapeSpacing(
            out double tierBase,
            out double tierStep,
            out string message
        )
        {
            tierBase = 0.0;
            tierStep = 0.0;
            message = String.Empty;
            if (!_enabled)
            {
                message = "Column tier context is disabled.";
                return false;
            }

            bool resolved;
            switch (_family)
            {
                case PHU_ColumnShapeFamily.ShapeIH:
                    resolved = ShapeScript.TryGetCurrentColumnDimensionTierSpacing(
                        out tierBase,
                        out tierStep
                    );
                    break;
                case PHU_ColumnShapeFamily.ShapeC:
                    resolved = ShapeCScript.TryGetCurrentColumnDimensionTierSpacing(
                        out tierBase,
                        out tierStep
                    );
                    break;
                case PHU_ColumnShapeFamily.ShapeL:
                    resolved = ShapeLScript.TryGetCurrentColumnDimensionTierSpacing(
                        out tierBase,
                        out tierStep
                    );
                    break;
                default:
                    resolved = false;
                    break;
            }

            if (!resolved || !IsFinitePositive(tierBase) || !IsFinitePositive(tierStep))
            {
                tierBase = 0.0;
                tierStep = 0.0;
                message = "The active Shape did not publish a valid column tier contract.";
                return false;
            }

            message =
                "Column tier contract="
                + _family.ToString()
                + " base="
                + tierBase.ToString("0.###", CultureInfo.InvariantCulture)
                + " step="
                + tierStep.ToString("0.###", CultureInfo.InvariantCulture)
                + ".";
            return true;
        }

        public static void RegisterNeighborOutermostVerticalLine(
            int viewIdentifier,
            int side,
            double lineCoordinate
        )
        {
            if (
                !_enabled
                || viewIdentifier <= 0
                || (side != -1 && side != 1)
                || Double.IsNaN(lineCoordinate)
                || Double.IsInfinity(lineCoordinate)
            )
                return;

            string key = BuildKey(viewIdentifier, side);
            double current;
            if (
                !NeighborOutermostVerticalLines.TryGetValue(key, out current)
                || (lineCoordinate - current) * side > 0.20
            )
                NeighborOutermostVerticalLines[key] = lineCoordinate;
        }

        public static bool TryGetNeighborOutermostVerticalLine(
            int viewIdentifier,
            int side,
            out double lineCoordinate
        )
        {
            lineCoordinate = 0.0;
            if (!_enabled || viewIdentifier <= 0 || (side != -1 && side != 1))
                return false;
            return NeighborOutermostVerticalLines.TryGetValue(
                BuildKey(viewIdentifier, side),
                out lineCoordinate
            );
        }

        /// <summary>
        /// Resolves the two Grid vertical tiers without knowing any Neighbor
        /// geometry.  With Neighbor, Grid continues one Shape step outside its
        /// final tier.  Without Neighbor, Grid reuses the Shape total tier that
        /// it replaces, so no blank tier is introduced.
        /// </summary>
        public static bool TryResolveGridVerticalLines(
            int viewIdentifier,
            int side,
            double replaceableShapeTotalLine,
            double tierStep,
            out double columnLine,
            out double floorLine
        )
        {
            columnLine = 0.0;
            floorLine = 0.0;
            if (
                !_enabled
                || viewIdentifier <= 0
                || (side != -1 && side != 1)
                || !IsFinite(replaceableShapeTotalLine)
                || !IsFinitePositive(tierStep)
            )
                return false;

            double neighborLine;
            columnLine = TryGetNeighborOutermostVerticalLine(viewIdentifier, side, out neighborLine)
                ? neighborLine + (side * tierStep)
                : replaceableShapeTotalLine;
            floorLine = columnLine + (side * tierStep);
            return IsFinite(columnLine) && IsFinite(floorLine);
        }

        private static string BuildKey(int viewIdentifier, int side)
        {
            return viewIdentifier.ToString(CultureInfo.InvariantCulture)
                + ":"
                + side.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsFinitePositive(double value)
        {
            return IsFinite(value) && value > 0.0;
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }
    }
}
