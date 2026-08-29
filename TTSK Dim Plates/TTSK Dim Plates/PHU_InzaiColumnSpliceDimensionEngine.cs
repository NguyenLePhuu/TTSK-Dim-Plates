#pragma warning disable 1633

using System;
using System.Collections.Generic;

using TSM = Tekla.Structures.Model;

namespace Tekla.Technology.Akit.UserScript
{
    /// <summary>
    /// Optional Inzai-only adapter for the REF-node H-beam splice topology.
    /// The legacy Geo_11/Geo_13 Neighbor engine remains unchanged.  This
    /// adapter shares only project routing and the Shape-owned tier contract.
    /// </summary>
    public static class PHU_InzaiColumnSpliceDimensionEngine
    {
        private static bool _enabled;

        public static bool LastRunApplicable { get; private set; }
        public static bool LastRunSucceeded { get; private set; }
        public static int LastCreatedCount { get; private set; }
        public static int LastReusedCount { get; private set; }
        public static string LastRunMessage { get; private set; }

        public static void Configure(bool enabled)
        {
            ResetResult();
            PHU_InzaiColumnCompositeDimensionContext.Reset();
            _enabled = enabled;
        }

        public static void Reset()
        {
            _enabled = false;
            ResetResult();
            PHU_InzaiColumnCompositeDimensionContext.Reset();
        }

        /// <summary>
        /// Runs after Shape and the legacy Neighbor engine, before Column Grid.
        /// An unsupported or uncertain relation is skipped without blocking the
        /// already-completed Shape/Neighbor flow or the following Grid flow.
        /// </summary>
        public static bool ExecuteAfterShape()
        {
            ResetResult();
            if (!_enabled)
                return true;

            try
            {
                TSM.Model model = new TSM.Model();
                string projectKey = String.Empty;
                string routeMessage = String.Empty;
                if (
                    !model.GetConnectionStatus()
                    || !PHU_ColumnProjectRouter.TryResolve(
                        model,
                        out projectKey,
                        out routeMessage)
                    || !String.Equals(
                        projectKey,
                        PHU_ColumnProjectRouter.InzaiDataCenterKey,
                        StringComparison.Ordinal)
                )
                {
                    LastRunSucceeded = true;
                    LastRunMessage =
                        "Inzai Splice DIM skipped: project route is not Inzai Data Center. "
                        + routeMessage;
                    return true;
                }

                PHU_Slot10RefConnectionDimensionEngine.InzaiFlowResult result =
                    PHU_Slot10RefConnectionDimensionEngine.RunInzaiColumnFlow();
                LastRunApplicable = result != null && result.Applicable;
                LastCreatedCount = result == null ? 0 : result.CreatedCount;
                LastReusedCount = result == null ? 0 : result.ReplacedCount;
                LastRunSucceeded = result != null && result.Success;
                LastRunMessage =
                    result == null
                        ? "Inzai Splice DIM skipped: no result was returned."
                        : result.Message;
                return true;
            }
            catch (Exception ex)
            {
                LastRunApplicable = false;
                LastRunSucceeded = false;
                LastCreatedCount = 0;
                LastReusedCount = 0;
                LastRunMessage = "Inzai Splice DIM skipped: " + ex.Message;
                return true;
            }
        }

        /// <summary>Read-only; no drawing/model mutation or CommitChanges.</summary>
        public static string AuditPreparedPlans()
        {
            try
            {
                TSM.Model model = new TSM.Model();
                string projectKey = String.Empty;
                string routeMessage = String.Empty;
                if (
                    !model.GetConnectionStatus()
                    || !PHU_ColumnProjectRouter.TryResolve(
                        model,
                        out projectKey,
                        out routeMessage)
                    || !String.Equals(
                        projectKey,
                        PHU_ColumnProjectRouter.InzaiDataCenterKey,
                        StringComparison.Ordinal)
                )
                    return "INZAI SPLICE AUDIT not routed. " + routeMessage;
                return PHU_Slot10RefConnectionDimensionEngine.AuditInzaiColumnPlan();
            }
            catch (Exception ex)
            {
                return "INZAI SPLICE AUDIT failed: " + ex.Message;
            }
        }

        /// <summary>Pure geometry; no Tekla drawing/model mutation.</summary>
        public static string AuditGeometryRegression()
        {
            return PHU_Slot10RefConnectionDimensionEngine.AuditInzaiGeometryRegression();
        }

        private static void ResetResult()
        {
            LastRunApplicable = false;
            LastRunSucceeded = false;
            LastCreatedCount = 0;
            LastReusedCount = 0;
            LastRunMessage = String.Empty;
        }
    }

    internal sealed class PHU_InzaiCompositeViewGeometry
    {
        public int ViewIdentifier;
        public double RefX;
        public double BaseY;
        public double BaseLeftX;
        public double BaseRightX;
        public double SpliceLowY;
        public double SpliceLowLeftX;
        public double SpliceHighY;
        public double SpliceHighLeftX;
        public double TerminalY;
        public double TerminalLeftX;
        public double TerminalRightX;
        public double TerminalRefY;
        public double HorizontalClearanceY;
        public bool Type2TerminalFamily;
        public double Type2TerminalAxisLine = Double.NaN;
        public readonly List<Tekla.Structures.Geometry3d.Point> BottomBoltPoints =
            new List<Tekla.Structures.Geometry3d.Point>();
    }

    internal static class PHU_InzaiColumnCompositeDimensionContext
    {
        private static readonly Dictionary<int, PHU_InzaiCompositeViewGeometry> Views =
            new Dictionary<int, PHU_InzaiCompositeViewGeometry>();

        public static void Reset()
        {
            Views.Clear();
        }

        public static void Publish(PHU_InzaiCompositeViewGeometry geometry)
        {
            if (geometry == null || geometry.ViewIdentifier <= 0)
                return;
            Views[geometry.ViewIdentifier] = geometry;
        }

        public static bool TryGet(
            int viewIdentifier,
            out PHU_InzaiCompositeViewGeometry geometry)
        {
            return Views.TryGetValue(viewIdentifier, out geometry);
        }
    }
}
