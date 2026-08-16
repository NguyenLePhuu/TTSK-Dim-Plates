using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

using TSD = Tekla.Structures.Drawing;

namespace Tekla.Technology.Akit.UserScript
{
    internal static partial class PHU_Slot08DiagonalBraceDimensionEngine
    {
        private const int Type2ExpectedPlanCount = 27;
        private const int Type2PlanViewPlanCount = 26;
        private const int Type2SectionViewPlanCount = 1;

        private sealed class Type2Topology
        {
            public PartData Main;
            public PartData Cross;
            public PartData Plate;
            public P2 A;
            public P2 B;
            public P2 C;
            public P2 D;
            public P2 Joint;
            public P2 MainAxis;
            public P2 MainNormal;
            public P2 CrossAxis;
            public P2 CrossNormal;
        }

        private static bool TryResolvePlanTopologyType2(
            ViewData view,
            out Type2Topology topology)
        {
            topology = null;
            if (view == null || view.Main == null)
                return false;

            P2 main0;
            P2 main1;
            if (!TryFarthestReferencePair(view.Main, out main0, out main1))
                return false;
            CanonicalizeAxisPair(ref main0, ref main1);
            P2 mainDirection = Normalize(Subtract(main1, main0));
            double mainLength = Distance(main0, main1);
            if (mainDirection == null || mainLength <= PointTolerance)
                return false;

            List<PartData> longMembers = new List<PartData>();
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (part.AssemblyId != view.Main.AssemblyId)
                    continue;
                P2 first;
                P2 second;
                if (!TryFarthestReferencePair(part, out first, out second) ||
                    Distance(first, second) < mainLength * 0.05)
                    continue;
                longMembers.Add(part);
            }
            if (longMembers.Count != 2)
                return false;

            PartData cross = longMembers[0].IsMain
                ? longMembers[1]
                : longMembers[0];
            if (cross == null || cross.IsMain)
                return false;

            P2 cross0;
            P2 cross1;
            if (!TryFarthestReferencePair(cross, out cross0, out cross1))
                return false;
            P2 crossDirection = Normalize(Subtract(cross1, cross0));
            if (crossDirection == null ||
                Math.Abs(Dot(mainDirection, crossDirection)) >= DirectionTolerance)
                return false;

            P2 joint;
            if (!TryInfiniteLineIntersection(
                main0, main1, cross0, cross1, out joint) ||
                !IsStrictlyInsideReference(main0, main1, joint) ||
                !IsStrictlyInsideReference(cross0, cross1, joint))
                return false;

            PartData plate = ResolveConnectionPlateType2(
                view, view.Main, cross, mainLength);
            if (plate == null || !SharesBoltGroup(view.Main, cross))
                return false;

            P2 a = main0;
            P2 d = main1;
            P2 b;
            P2 c;
            if (Cross(mainDirection, crossDirection) > 0.0)
            {
                b = cross0;
                c = cross1;
            }
            else
            {
                b = cross1;
                c = cross0;
            }

            Type2Topology item = new Type2Topology();
            item.Main = view.Main;
            item.Cross = cross;
            item.Plate = plate;
            item.A = a;
            item.B = b;
            item.C = c;
            item.D = d;
            item.Joint = joint;
            item.MainAxis = Normalize(Subtract(item.D, item.A));
            item.CrossAxis = Normalize(Subtract(item.C, item.B));
            if (item.MainAxis == null || item.CrossAxis == null)
                return false;

            item.MainNormal = PerpendicularLeft(item.MainAxis);
            if (Dot(Subtract(item.C, item.Joint), item.MainNormal) < 0.0)
                item.MainNormal = Scale(item.MainNormal, -1.0);
            item.CrossNormal = PerpendicularLeft(item.CrossAxis);
            if (Dot(Subtract(item.D, item.Joint), item.CrossNormal) < 0.0)
                item.CrossNormal = Scale(item.CrossNormal, -1.0);

            topology = item;
            return true;
        }

        private static PartData ResolveConnectionPlateType2(
            ViewData view,
            PartData main,
            PartData cross,
            double mainLength)
        {
            List<PartData> matches = new List<PartData>();
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData candidate = view.Parts[i];
                if (candidate.ModelId == main.ModelId ||
                    candidate.ModelId == cross.ModelId ||
                    candidate.AssemblyId != main.AssemblyId)
                    continue;
                if (!SharesBoltGroup(candidate, main) ||
                    !SharesBoltGroup(candidate, cross))
                    continue;

                int connectedLongMembers = 0;
                for (int p = 0; p < view.Parts.Count; p++)
                {
                    PartData member = view.Parts[p];
                    P2 first;
                    P2 second;
                    if (member.AssemblyId == main.AssemblyId &&
                        TryFarthestReferencePair(member, out first, out second) &&
                        Distance(first, second) >= mainLength * 0.05 &&
                        SharesBoltGroup(candidate, member))
                        connectedLongMembers++;
                }
                if (connectedLongMembers == 2)
                    matches.Add(candidate);
            }
            return matches.Count == 1 ? matches[0] : null;
        }

        private static bool IsStrictlyInsideReference(P2 first, P2 second, P2 point)
        {
            P2 span = Subtract(second, first);
            double denominator = Dot(span, span);
            if (denominator <= PointTolerance * PointTolerance)
                return false;
            double parameter = Dot(Subtract(point, first), span) / denominator;
            double clearance = Math.Max(
                PointTolerance / Math.Sqrt(denominator), 0.02);
            return parameter > clearance && parameter < 1.0 - clearance;
        }

        private static void CanonicalizeAxisPair(ref P2 first, ref P2 second)
        {
            P2 direction = Subtract(second, first);
            if (direction.X < -PointTolerance ||
                (Math.Abs(direction.X) <= PointTolerance && direction.Y < 0.0))
            {
                P2 swap = first;
                first = second;
                second = swap;
            }
        }

        private static ViewData ResolveType2SectionView(Context context)
        {
            List<ViewData> preferred = new List<ViewData>();
            for (int i = 0; i < context.Views.Count; i++)
            {
                ViewData candidate = context.Views[i];
                if (Object.ReferenceEquals(candidate, context.PlanView))
                    continue;
                PartData main = FindPart(
                    candidate, context.Type2Topology.Main.ModelId);
                PartData cross = FindPart(
                    candidate, context.Type2Topology.Cross.ModelId);
                PartData plate = FindPart(
                    candidate, context.Type2Topology.Plate.ModelId);
                if (main == null || cross == null || plate == null)
                    continue;

                P2 main0;
                P2 main1;
                P2 cross0;
                P2 cross1;
                if (!TryFarthestReferencePair(main, out main0, out main1) ||
                    !TryFarthestReferencePair(cross, out cross0, out cross1))
                    continue;
                CanonicalizeAxisPair(ref main0, ref main1);
                P2 mainAxis = Normalize(Subtract(main1, main0));
                P2 crossAxis = Normalize(Subtract(cross1, cross0));
                if (mainAxis == null || crossAxis == null ||
                    Math.Abs(Dot(mainAxis, crossAxis)) < DirectionTolerance)
                    continue;

                P2 normal = PerpendicularLeft(mainAxis);
                double mainLevel = Dot(Centroid(main), normal);
                double plateLevel = Dot(Centroid(plate), normal);
                double crossLevel = Dot(Centroid(cross), normal);
                if (mainLevel > plateLevel + PointTolerance &&
                    plateLevel > crossLevel + PointTolerance)
                    preferred.Add(candidate);
            }
            if (preferred.Count != 1)
                throw new InvalidOperationException(
                    "Khong xac dinh duy nhat view tiet dien Type2 co thu tu main-plate-cross. So view=" +
                    preferred.Count + ".");
            return preferred[0];
        }

        private static P2 Centroid(PartData part)
        {
            if (part == null || part.Vertices.Count == 0)
                return null;
            double x = 0.0;
            double y = 0.0;
            for (int i = 0; i < part.Vertices.Count; i++)
            {
                x += part.Vertices[i].X;
                y += part.Vertices[i].Y;
            }
            return new P2(x / part.Vertices.Count, y / part.Vertices.Count);
        }

        private static List<DimPlan> BuildType2Plans(Context context)
        {
            List<DimPlan> plans = new List<DimPlan>();
            BuildType2PlanViewPlans(context, plans);
            BuildType2SectionViewPlan(context, plans);
            return plans;
        }

        private static void BuildType2PlanViewPlans(
            Context context,
            List<DimPlan> plans)
        {
            Type2Topology t = context.Type2Topology;
            ViewData view = context.PlanView;
            TerminalFeature mainA = ResolveTerminal(t.Main, t.A, t.MainNormal);
            TerminalFeature mainD = ResolveTerminal(t.Main, t.D, t.MainNormal);
            TerminalFeature crossB = ResolveTerminal(t.Cross, t.B, t.CrossNormal);
            TerminalFeature crossC = ResolveTerminal(t.Cross, t.C, t.CrossNormal);
            P2 mainABolt = ResolveTerminalBolt(t.Main, t.A);
            P2 mainDBolt = ResolveTerminalBolt(t.Main, t.D);
            P2 crossBBolt = ResolveTerminalBolt(t.Cross, t.B);
            P2 crossCBolt = ResolveTerminalBolt(t.Cross, t.C);
            P2 jointBolt = ResolveTerminalBolt(t.Main, t.Joint);

            AddWidthPlan(plans, view, "T2-P-01",
                "cross B terminal profile width",
                t.CrossNormal, Scale(t.CrossAxis, -1.0),
                TierBand.CrossStartWidth,
                crossB, null, false);
            AddWidthPlan(plans, view, "T2-P-02",
                "cross B terminal profile width with nearest bolt",
                t.CrossNormal, Scale(t.CrossAxis, -1.0),
                TierBand.CrossStartWidthWithBolt,
                crossB, crossBBolt, true);
            AddWidthPlan(plans, view, "T2-P-03",
                "main D terminal profile width",
                Scale(t.MainNormal, -1.0), t.MainAxis,
                TierBand.MainEndWidth,
                mainD, null, false);
            AddWidthPlan(plans, view, "T2-P-04",
                "main D terminal profile width with nearest bolt",
                Scale(t.MainNormal, -1.0), t.MainAxis,
                TierBand.MainEndWidthWithBolt,
                mainD, mainDBolt, true);
            AddWidthPlan(plans, view, "T2-P-05",
                "cross C terminal profile width",
                t.CrossNormal, t.CrossAxis,
                TierBand.CrossEndWidth,
                crossC, null, false);
            AddWidthPlan(plans, view, "T2-P-06",
                "cross C terminal profile width with nearest bolt",
                t.CrossNormal, t.CrossAxis,
                TierBand.CrossEndWidthWithBolt,
                crossC, crossCBolt, true);
            AddWidthPlan(plans, view, "T2-P-07",
                "main A terminal profile width",
                Scale(t.MainNormal, -1.0), Scale(t.MainAxis, -1.0),
                TierBand.MainStartWidth,
                mainA, null, false);
            AddWidthPlan(plans, view, "T2-P-08",
                "main A terminal profile width with nearest bolt",
                Scale(t.MainNormal, -1.0), Scale(t.MainAxis, -1.0),
                TierBand.MainStartWidthWithBolt,
                mainA, mainABolt, true);

            AddPlan(plans, view, "T2-P-09",
                "main D exact reference/solid edge -> nearest terminal bolt",
                t.MainAxis, t.MainNormal,
                TierBand.MainLocalEndEdge,
                mainD.ReferenceIntersection, mainDBolt);
            AddPlan(plans, view, "T2-P-10",
                "main A exact reference/solid edge -> nearest terminal bolt",
                t.MainAxis, t.MainNormal,
                TierBand.MainLocalStartEdge,
                mainA.ReferenceIntersection, mainABolt);
            AddPlan(plans, view, "T2-P-11",
                "main outer flange terminal vertex -> joint bolt -> outer flange terminal vertex",
                t.MainAxis, t.MainNormal,
                TierBand.MainMajorInner,
                mainA.High, jointBolt,
                mainD.High);
            AddPlan(plans, view, "T2-P-12",
                "main REF-outer flange vertices-REF semantic chain",
                t.MainAxis, t.MainNormal,
                TierBand.MainMajorMiddle,
                t.A, mainA.High,
                mainD.High, t.D);
            AddPlan(plans, view, "T2-P-13", "main REF A -> REF D",
                t.MainAxis, t.MainNormal,
                TierBand.MainMajorOuter,
                t.A, t.D);

            AddPlan(plans, view, "T2-P-14",
                "cross B exact reference/solid edge -> nearest terminal bolt",
                Scale(t.CrossAxis, -1.0), t.CrossNormal,
                TierBand.CrossLocalBolt,
                crossB.ReferenceIntersection, crossBBolt);
            AddPlan(plans, view, "T2-P-15",
                "cross C exact reference/solid edge -> nearest terminal bolt",
                Scale(t.CrossAxis, -1.0), t.CrossNormal,
                TierBand.CrossLocalEdge,
                crossC.ReferenceIntersection, crossCBolt);
            AddPlan(plans, view, "T2-P-16",
                "cross outer flange terminal vertex -> joint bolt -> outer flange terminal vertex",
                Scale(t.CrossAxis, -1.0), t.CrossNormal,
                TierBand.MainMajorInner,
                crossC.High, jointBolt,
                crossB.High);
            AddPlan(plans, view, "T2-P-17",
                "cross REF-outer flange vertices-REF semantic chain",
                Scale(t.CrossAxis, -1.0), t.CrossNormal,
                TierBand.CrossMajorInner,
                t.C, crossC.High,
                crossB.High, t.B);
            AddPlan(plans, view, "T2-P-18", "cross REF C -> REF B",
                Scale(t.CrossAxis, -1.0), t.CrossNormal,
                TierBand.CrossMajorOuter,
                t.C, t.B);

            AddViewProjectedBoundaryPlan(plans, view, "T2-P-19",
                "right REF -> exact-edge -> REF",
                BoundaryNormal(t.B, t.D, t.Joint),
                TierBand.BoundaryRightInner,
                t.B, crossB.ReferenceIntersection,
                mainD.ReferenceIntersection, t.D);
            AddViewProjectedBoundaryPlan(plans, view, "T2-P-20", "right REF B -> REF D",
                BoundaryNormal(t.B, t.D, t.Joint),
                TierBand.BoundaryRightOuter,
                t.B, t.D);
            AddViewProjectedBoundaryPlan(plans, view, "T2-P-21",
                "left REF -> exact-edge -> REF",
                BoundaryNormal(t.A, t.C, t.Joint),
                TierBand.BoundaryLeftInner,
                t.A, mainA.ReferenceIntersection,
                crossC.ReferenceIntersection, t.C);
            AddViewProjectedBoundaryPlan(plans, view, "T2-P-22", "left REF A -> REF C",
                BoundaryNormal(t.A, t.C, t.Joint),
                TierBand.BoundaryLeftOuter,
                t.A, t.C);
            AddViewProjectedBoundaryPlan(plans, view, "T2-P-23",
                "bottom REF -> exact-edge -> REF",
                BoundaryNormal(t.A, t.B, t.Joint),
                TierBand.BoundaryBottomInner,
                t.A, mainA.ReferenceIntersection,
                crossB.ReferenceIntersection, t.B);
            AddViewProjectedBoundaryPlan(plans, view, "T2-P-24", "bottom REF A -> REF B",
                BoundaryNormal(t.A, t.B, t.Joint),
                TierBand.BoundaryBottomOuter,
                t.A, t.B);
            AddViewProjectedBoundaryPlan(plans, view, "T2-P-25",
                "top REF -> exact-edge -> REF",
                BoundaryNormal(t.C, t.D, t.Joint),
                TierBand.BoundaryTopInner,
                t.C, crossC.ReferenceIntersection,
                mainD.ReferenceIntersection, t.D);
            AddViewProjectedBoundaryPlan(plans, view, "T2-P-26", "top REF C -> REF D",
                BoundaryNormal(t.C, t.D, t.Joint),
                TierBand.BoundaryTopOuter,
                t.C, t.D);
        }

        private static void BuildType2SectionViewPlan(
            Context context,
            List<DimPlan> plans)
        {
            ViewData view = context.SectionView;
            PartData main = FindPart(view, context.Type2Topology.Main.ModelId);
            PartData cross = FindPart(view, context.Type2Topology.Cross.ModelId);
            if (main == null || cross == null)
                throw new InvalidOperationException(
                    "View tiet dien Type2 khong chua du hai thanh giang.");

            P2 main0;
            P2 main1;
            if (!TryFarthestReferencePair(main, out main0, out main1))
                throw new InvalidOperationException(
                    "View tiet dien Type2 khong co main reference line.");
            CanonicalizeAxisPair(ref main0, ref main1);
            P2 axis = Normalize(Subtract(main1, main0));
            if (axis == null)
                throw new InvalidOperationException(
                    "Main reference line trong view tiet dien Type2 khong hop le.");
            P2 normal = PerpendicularLeft(axis);
            P2 targetReference = Dot(main0, axis) <= Dot(main1, axis)
                ? main0
                : main1;
            TerminalFeature mainTerminal = ResolveSectionProfileAtReference(
                main, targetReference, normal);

            P2 cross0;
            P2 cross1;
            if (!TryFarthestReferencePair(cross, out cross0, out cross1))
                throw new InvalidOperationException(
                    "View tiet dien Type2 khong co cross reference line.");
            P2 crossTarget = Dot(cross0, axis) <= Dot(cross1, axis)
                ? cross0
                : cross1;
            TerminalFeature crossTerminal = ResolveSectionProfileAtReference(
                cross, crossTarget, normal);

            AddPlan(plans, view, "T2-S-01",
                "two L profile layers in section view, top -> bottom",
                normal, Scale(axis, -1.0),
                TierBand.SectionProfile,
                mainTerminal.High, mainTerminal.Low,
                crossTerminal.High, crossTerminal.Low);
        }

        private static TerminalFeature ResolveSectionProfileAtReference(
            PartData part,
            P2 referenceTarget,
            P2 semanticNormal)
        {
            double min = Double.PositiveInfinity;
            double max = Double.NegativeInfinity;
            for (int i = 0; i < part.Vertices.Count; i++)
            {
                double value = Dot(part.Vertices[i], semanticNormal);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
            List<P2> lowCandidates = new List<P2>();
            List<P2> highCandidates = new List<P2>();
            for (int i = 0; i < part.Vertices.Count; i++)
            {
                double value = Dot(part.Vertices[i], semanticNormal);
                if (Math.Abs(value - min) <= PointTolerance)
                    AddUnique(lowCandidates, part.Vertices[i]);
                if (Math.Abs(value - max) <= PointTolerance)
                    AddUnique(highCandidates, part.Vertices[i]);
            }
            if (lowCandidates.Count == 0 || highCandidates.Count == 0)
                throw new InvalidOperationException(
                    "Khong tim duoc section profile cua part " + part.ModelId + ".");

            TerminalFeature result = new TerminalFeature();
            result.Low = NearestPoint(lowCandidates, referenceTarget);
            result.High = NearestPoint(highCandidates, referenceTarget);
            if (result.Low == null || result.High == null)
                throw new InvalidOperationException(
                    "Khong resolve duoc section profile cua part " + part.ModelId + ".");
            return result;
        }

        private static P2 NearestPoint(List<P2> points, P2 target)
        {
            P2 best = null;
            double bestDistance = Double.PositiveInfinity;
            for (int i = 0; points != null && i < points.Count; i++)
            {
                double distance = Distance(points[i], target);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = points[i];
                }
            }
            return best;
        }

        private static int ExpectedPlanCountFor(Context context)
        {
            return context != null && context.Variant == TopologyVariant.Type2
                ? Type2ExpectedPlanCount
                : ExpectedPlanCount;
        }

        private static bool TrySnapshotType2FootMigration(
            Context context,
            List<DimPlan> plans,
            List<TSD.StraightDimensionSet> all,
            ReplacementSnapshot result)
        {
            if (context == null || context.Variant != TopologyVariant.Type2 ||
                plans == null || plans.Count != Type2ExpectedPlanCount ||
                all == null || result == null)
                return false;

            Type2Topology t = context.Type2Topology;
            TerminalFeature mainA = ResolveTerminal(t.Main, t.A, t.MainNormal);
            TerminalFeature mainD = ResolveTerminal(t.Main, t.D, t.MainNormal);
            TerminalFeature crossB = ResolveTerminal(t.Cross, t.B, t.CrossNormal);
            TerminalFeature crossC = ResolveTerminal(t.Cross, t.C, t.CrossNormal);
            P2 jointBolt = ResolveTerminalBolt(t.Main, t.Joint);

            Dictionary<string, List<P2>> legacyFeet =
                new Dictionary<string, List<P2>>(StringComparer.Ordinal);
            legacyFeet.Add("T2-P-11", new List<P2>
            {
                mainA.ReferenceIntersection, jointBolt,
                mainD.ReferenceIntersection
            });
            legacyFeet.Add("T2-P-12", new List<P2>
            {
                t.A, mainA.ReferenceIntersection,
                mainD.ReferenceIntersection, t.D
            });
            legacyFeet.Add("T2-P-16", new List<P2>
            {
                crossC.ReferenceIntersection, jointBolt,
                crossB.ReferenceIntersection
            });
            legacyFeet.Add("T2-P-17", new List<P2>
            {
                t.C, crossC.ReferenceIntersection,
                crossB.ReferenceIntersection, t.B
            });

            List<TSD.StraightDimensionSet> matched =
                new List<TSD.StraightDimensionSet>();
            HashSet<int> used = new HashSet<int>();
            for (int p = 0; p < plans.Count; p++)
            {
                DimPlan plan = plans[p];
                List<P2> legacy;
                legacyFeet.TryGetValue(plan.Name, out legacy);
                TSD.StraightDimensionSet found = null;
                for (int i = 0; i < all.Count; i++)
                {
                    TSD.StraightDimensionSet set = all[i];
                    int key = RuntimeHelpers.GetHashCode(set);
                    if (used.Contains(key) ||
                        !DimensionBelongsToView(set, plan.View.View))
                        continue;
                    List<P2> existing = ReadDimensionPoints(set);
                    bool currentMatch = PointChainsMatch(
                        existing, plan.Points, MatchTolerance);
                    bool legacyMatch = legacy != null && PointChainsMatch(
                        existing, legacy, MatchTolerance);
                    if ((!currentMatch && !legacyMatch) ||
                        !Type2MigrationDirectionMatches(set, plan))
                        continue;
                    found = set;
                    used.Add(key);
                    break;
                }
                if (found == null)
                    return false;
                matched.Add(found);
            }

            if (matched.Count != Type2ExpectedPlanCount)
                return false;
            result.Matched.Clear();
            result.Matched.AddRange(matched);
            result.ProtectedCount = all.Count - matched.Count;
            return true;
        }

        private static bool Type2MigrationDirectionMatches(
            TSD.StraightDimensionSet set,
            DimPlan plan)
        {
            if (DimensionDirectionMatches(set, plan.PlacementNormal))
                return true;
            if (!String.Equals(
                plan.Name, "T2-P-19", StringComparison.Ordinal))
                return false;

            P2 actual = ReadPointLikeMember(set, "UpDirection");
            if (actual == null)
                actual = ReadPointLikeMember(set, "OffsetDirection");
            actual = Normalize(actual);
            P2 expected = Normalize(plan.PlacementNormal);
            return actual != null && expected != null &&
                Math.Abs(Dot(actual, expected)) >= 0.95;
        }

        private static void ValidateType2Plans(
            Context context,
            List<DimPlan> plans)
        {
            if (plans == null || plans.Count != Type2ExpectedPlanCount)
                throw new InvalidOperationException(
                    "Slot 08 Type2 phai co dung " + Type2ExpectedPlanCount +
                    " dimension plans; thuc te=" +
                    (plans == null ? 0 : plans.Count) + ".");

            int planCount = 0;
            int sectionCount = 0;
            HashSet<string> signatures =
                new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < plans.Count; i++)
            {
                DimPlan plan = plans[i];
                if (plan == null || plan.View == null ||
                    plan.MeasurementAxis == null || plan.PlacementNormal == null)
                    throw new InvalidOperationException("Plan Slot 08 Type2 bi null.");
                if (plan.Points.Count < 2)
                    throw new InvalidOperationException(
                        plan.Name + " co it hon hai chan dim.");
                if (!IsFinite(plan.Distance) || plan.Distance <= PointTolerance)
                    throw new InvalidOperationException(
                        plan.Name + " co khoang dat dim khong hop le.");

                double min = Double.PositiveInfinity;
                double max = Double.NegativeInfinity;
                for (int p = 0; p < plan.Points.Count; p++)
                {
                    P2 point = plan.Points[p];
                    if (point == null || !IsFinite(point.X) || !IsFinite(point.Y))
                        throw new InvalidOperationException(
                            plan.Name + " co chan dim khong huu han.");
                    double projection = Dot(point, plan.MeasurementAxis);
                    min = Math.Min(min, projection);
                    max = Math.Max(max, projection);
                }
                if (max - min <= MinimumMeasuredSpan)
                    throw new InvalidOperationException(
                        plan.Name + " co measured span bang 0.");

                string signature = RuntimeHelpers.GetHashCode(plan.View).ToString(
                    CultureInfo.InvariantCulture) + "|" + plan.Name;
                if (!signatures.Add(signature))
                    throw new InvalidOperationException(
                        "Trung semantic signature " + plan.Name + ".");
                if (Object.ReferenceEquals(plan.View, context.PlanView))
                    planCount++;
                else if (Object.ReferenceEquals(plan.View, context.SectionView))
                    sectionCount++;
                else
                    throw new InvalidOperationException(
                        plan.Name + " thuoc view khong duoc phep.");
            }
            if (planCount != Type2PlanViewPlanCount ||
                sectionCount != Type2SectionViewPlanCount)
                throw new InvalidOperationException(
                    "Sai so plan Type2 theo view: main=" + planCount +
                    ", section=" + sectionCount + ".");
        }
    }
}
