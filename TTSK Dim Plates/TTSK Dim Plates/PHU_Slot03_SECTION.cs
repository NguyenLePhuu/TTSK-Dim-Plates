#pragma warning disable 1633

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Tekla.Structures;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using Tekla.Structures.Drawing;

using TSM = Tekla.Structures.Model;
using TSD = Tekla.Structures.Drawing;
using ModelPart = Tekla.Structures.Model.Part;
using ModelObject = Tekla.Structures.Model.ModelObject;
using ModelBoltGroup = Tekla.Structures.Model.BoltGroup;
using DrawingPart = Tekla.Structures.Drawing.Part;

namespace Tekla.Technology.Akit.UserScript
{
    // Slot 03 cho MainForm:
    // Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot03.Run()
    public class PHU_AutoDimSlot03
    {
        public static bool LastRunSucceeded
        {
            get { return PHU_Slot03_NeighborPlateSectionDim.LastRunSucceeded; }
        }

        public static void Run()
        {
            PHU_Slot03_NeighborPlateSectionDim.Run();
        }
    }

    public class PHU_Slot03_NeighborPlateSectionDim
    {
        private const double TOL = 1.0;
        public static bool LastRunSucceeded { get; private set; }

        // Theo dump mẫu:
        // Chain ngang: lỗ plate trái -> ref main -> lỗ plate phải.
        private const double HORIZONTAL_REF_TO_HOLE_TIER = 150.0;

        // Dim đứng ngoài: mép dầm main trái/phải -> mép dưới neighbor.
        private const double MAIN_TO_NEIGHBOR_SIDE_TIER = 360.0;

        // Dim đứng trong: mép trên neighbor -> lỗ plate.
        private const double NEIGHBOR_TO_PLATE_HOLE_SIDE_TIER = 220.0;

        private const double POINT_DUP_TOL = 0.5;

        // SLOT03 - QUY TẮC TẦNG DIM CHUNG
        // Tầng 0 = 150, các tầng sau +150.
        // Dim dọc Left/Right: offset từ điểm xa nhất của Plate theo hướng trái/phải.
        // Dim ngang Top/Bottom: offset từ mép dầm chính theo hướng trên/dưới.
        private const double SLOT03_DIM_TIER_BASE = 150.0;
        private const double SLOT03_DIM_TIER_STEP = 150.0;

        public static void Run()
        {
            LastRunSucceeded = false;

            TSD.DrawingHandler dh = new TSD.DrawingHandler();
            if (!dh.GetConnectionStatus())
            {
                Msg("DrawingHandler chưa kết nối.");
                return;
            }

            TSD.Drawing drawing = dh.GetActiveDrawing();
            if (drawing == null)
            {
                Msg("Không có active drawing.");
                return;
            }

            TSM.Model model = new TSM.Model();
            if (!model.GetConnectionStatus())
            {
                Msg("Model chưa kết nối.");
                return;
            }

            List<DrawingPart> selectedParts = GetSelectedDrawingParts(dh);
            if (selectedParts.Count < 2)
            {
                Msg("Slot03: Hãy chọn main shape + plate, hoặc main shape + neighbor + plate. Có thể chọn 1 bên hoặc 2 bên.");
                return;
            }

            Slot03SelectionContext manualContext;
            bool manualHasDummy;
            bool manualContextOk = TryBuildManualSelectionContext(
                model, drawing, selectedParts, out manualContext, out manualHasDummy);

            Slot03SelectionContext filteredContext;
            bool filteredContextOk = TryBuildGarbageFilteredContext(
                model, drawing, selectedParts, out filteredContext);

            bool hasGarbage =
                filteredContextOk &&
                SelectionContainsGarbage(model, selectedParts, filteredContext);

            Slot03SelectionContext context;
            bool useLegacyDummyLogic;

            if (!hasGarbage)
            {
                // Flow pick thu cong cu: giu nguyen cach nhan main/plate/neighbor/dummy.
                if (!manualContextOk)
                {
                    Msg("Slot03: Khong nhan dien du main + plate trong selection thu cong.");
                    return;
                }

                context = manualContext;
                useLegacyDummyLogic = manualHasDummy;
            }
            else
            {
                // Flow quet co rac: loc sach xong moi check dummy/Joycon lai mot lan nua.
                // Uu tien dummy nam trong selection nguoi dung vua quet; neu khong co thi moi fallback quet view.
                context = filteredContext;
                useLegacyDummyLogic = CheckDummyAfterGarbageFiltering(
                    model,
                    selectedParts,
                    context);
            }

            int created = ExecuteSlot03SelectionContext(
                model, dh, context, useLegacyDummyLogic);

            try { drawing.CommitChanges(); } catch { }

            LastRunSucceeded = created > 0;

            // Không popup hoàn thành để chạy gọn.
        }

        private class Slot03SelectionContext
        {
            public TSD.View View;
            public ModelPart MainBeam;
            public DrawingPart MainDrawingPart;
            public List<ModelPart> Plates = new List<ModelPart>();
            public List<DrawingPart> PlateDrawingParts = new List<DrawingPart>();
            public List<ModelPart> NeighborBeams = new List<ModelPart>();
            public List<DrawingPart> NeighborDrawingParts = new List<DrawingPart>();
        }

        private static bool TryBuildManualSelectionContext(
            TSM.Model model,
            TSD.Drawing drawing,
            List<DrawingPart> selectedParts,
            out Slot03SelectionContext context,
            out bool hasSelectedDummy)
        {
            context = null;
            hasSelectedDummy = false;

            if (model == null || drawing == null || selectedParts == null)
                return false;

            List<ModelPart> plates = new List<ModelPart>();
            List<DrawingPart> plateDrawingParts = new List<DrawingPart>();
            List<ModelPart> beams = new List<ModelPart>();
            List<DrawingPart> beamDrawingParts = new List<DrawingPart>();

            for (int i = 0; i < selectedParts.Count; i++)
            {
                DrawingPart drawingPart = selectedParts[i];
                ModelPart modelPart = SelectModelPart(model, drawingPart);
                if (modelPart == null)
                    continue;

                if (IsDummyReferencePart(modelPart))
                {
                    hasSelectedDummy = true;
                    continue;
                }

                if (IsRealPlatePart(modelPart))
                {
                    plates.Add(modelPart);
                    plateDrawingParts.Add(drawingPart);
                }
                else
                {
                    beams.Add(modelPart);
                    beamDrawingParts.Add(drawingPart);
                }
            }

            if (plates.Count == 0 || beams.Count < 1)
                return false;

            ModelPart mainBeam;
            DrawingPart mainDrawingPart;
            PickMainBeamByPlateAssemblies(
                plates, beams, beamDrawingParts, out mainBeam, out mainDrawingPart);

            if (mainBeam == null || mainDrawingPart == null)
                return false;

            Slot03SelectionContext result = new Slot03SelectionContext();
            result.MainBeam = mainBeam;
            result.MainDrawingPart = mainDrawingPart;
            result.Plates.AddRange(plates);
            result.PlateDrawingParts.AddRange(plateDrawingParts);

            for (int i = 0; i < beams.Count; i++)
            {
                if (SameIdentifier(beams[i].Identifier, mainBeam.Identifier))
                    continue;

                result.NeighborBeams.Add(beams[i]);
                result.NeighborDrawingParts.Add(beamDrawingParts[i]);
            }

            DrawingPart firstPlate = result.PlateDrawingParts.Count > 0
                ? result.PlateDrawingParts[0] : null;
            DrawingPart firstNeighbor = result.NeighborDrawingParts.Count > 0
                ? result.NeighborDrawingParts[0] : null;

            result.View = TryGetSelectedPartsView(
                firstPlate, result.MainDrawingPart, firstNeighbor);

            if (result.View == null && result.NeighborBeams.Count > 0)
                result.View = FindViewContainingParts(
                    drawing,
                    result.Plates[0].Identifier,
                    result.MainBeam.Identifier,
                    result.NeighborBeams[0].Identifier);

            if (result.View == null && result.NeighborBeams.Count == 0)
                result.View = FindViewContainingParts(
                    drawing,
                    result.Plates[0].Identifier,
                    result.MainBeam.Identifier);

            if (result.View == null)
                return false;

            context = result;
            return true;
        }

        private static bool TryBuildGarbageFilteredContext(
            TSM.Model model,
            TSD.Drawing drawing,
            List<DrawingPart> selectedParts,
            out Slot03SelectionContext context)
        {
            context = null;
            if (model == null || drawing == null || selectedParts == null)
                return false;

            ModelPart mainBeam = GetActiveAssemblyDrawingMainPart(model, drawing);
            if (mainBeam == null)
                return false;

            DrawingPart mainDrawingPart = null;
            List<ModelPart> plateCandidates = new List<ModelPart>();
            List<DrawingPart> plateDrawingCandidates = new List<DrawingPart>();
            List<ModelPart> neighborCandidates = new List<ModelPart>();
            List<DrawingPart> neighborDrawingCandidates = new List<DrawingPart>();

            for (int i = 0; i < selectedParts.Count; i++)
            {
                DrawingPart drawingPart = selectedParts[i];
                ModelPart modelPart = SelectModelPart(model, drawingPart);
                if (modelPart == null || IsDummyReferencePart(modelPart))
                    continue;

                if (SameIdentifier(modelPart.Identifier, mainBeam.Identifier))
                {
                    mainDrawingPart = drawingPart;
                    continue;
                }

                bool sameAssembly = IsPartInSameAssembly(modelPart, mainBeam);
                if (sameAssembly && IsRealPlatePart(modelPart))
                {
                    plateCandidates.Add(modelPart);
                    plateDrawingCandidates.Add(drawingPart);
                }
                else if (!sameAssembly && !IsRealPlatePart(modelPart))
                {
                    neighborCandidates.Add(modelPart);
                    neighborDrawingCandidates.Add(drawingPart);
                }
            }

            if (mainDrawingPart == null)
                return false;

            Slot03SelectionContext result = new Slot03SelectionContext();
            result.MainBeam = mainBeam;
            result.MainDrawingPart = mainDrawingPart;

            for (int i = 0; i < plateCandidates.Count; i++)
            {
                int linkedNeighborIndex = -1;
                int linkedNeighborCount = 0;

                for (int j = 0; j < neighborCandidates.Count; j++)
                {
                    if (!ArePartsDirectlyBoltConnected(plateCandidates[i], neighborCandidates[j]))
                        continue;

                    linkedNeighborIndex = j;
                    linkedNeighborCount++;
                }

                if (linkedNeighborCount != 1)
                    continue;

                result.Plates.Add(plateCandidates[i]);
                result.PlateDrawingParts.Add(plateDrawingCandidates[i]);
                AddUniquePartAndDrawing(
                    neighborCandidates[linkedNeighborIndex],
                    neighborDrawingCandidates[linkedNeighborIndex],
                    result.NeighborBeams,
                    result.NeighborDrawingParts);
            }

            if (result.Plates.Count < 1 || result.Plates.Count > 2 ||
                result.NeighborBeams.Count != result.Plates.Count)
                return false;

            DrawingPart firstPlate = result.PlateDrawingParts[0];
            DrawingPart firstNeighbor = result.NeighborDrawingParts[0];
            result.View = TryGetSelectedPartsView(
                firstPlate, result.MainDrawingPart, firstNeighbor);

            if (result.View == null)
                result.View = FindViewContainingParts(
                    drawing,
                    result.Plates[0].Identifier,
                    result.MainBeam.Identifier,
                    result.NeighborBeams[0].Identifier);

            if (result.View == null)
                return false;

            context = result;
            return true;
        }

        private static bool SelectionContainsGarbage(
            TSM.Model model,
            List<DrawingPart> selectedParts,
            Slot03SelectionContext cleanContext)
        {
            if (model == null || selectedParts == null || cleanContext == null)
                return false;

            for (int i = 0; i < selectedParts.Count; i++)
            {
                ModelPart part = SelectModelPart(model, selectedParts[i]);
                if (part == null || IsDummyReferencePart(part))
                    continue;

                if (SameIdentifier(part.Identifier, cleanContext.MainBeam.Identifier) ||
                    ContainsPartIdentifier(cleanContext.Plates, part.Identifier) ||
                    ContainsPartIdentifier(cleanContext.NeighborBeams, part.Identifier))
                    continue;

                return true;
            }

            return false;
        }

        private static bool ContainsPartIdentifier(
            List<ModelPart> parts,
            Identifier identifier)
        {
            if (parts == null || identifier == null)
                return false;

            for (int i = 0; i < parts.Count; i++)
                if (parts[i] != null && SameIdentifier(parts[i].Identifier, identifier))
                    return true;

            return false;
        }

        private static bool CheckDummyAfterGarbageFiltering(
            TSM.Model model,
            List<DrawingPart> selectedParts,
            Slot03SelectionContext context)
        {
            if (model == null || context == null)
                return false;

            // 1) Nguon dang tin cay nhat: chinh selection nguoi dung vua quet.
            // Trong flow co rac, TryBuildGarbageFilteredContext bo qua dummy de loc main/plate/neighbor,
            // nen can check lai dummy truc tiep tren selectedParts sau khi da co context sach.
            if (HasDummyInSelectedDrawingParts(model, selectedParts))
                return true;

            // 2) Fallback: quet lai dummy trong dung view cua context sach.
            // Co gioi han theo vung main/plate/neighbor de tranh bat nham dummy cua cum khac cung view.
            if (HasDummyInContextViewNearCleanParts(model, context))
                return true;

            return false;
        }

        private static bool HasDummyInSelectedDrawingParts(
            TSM.Model model,
            List<DrawingPart> selectedParts)
        {
            try
            {
                if (model == null || selectedParts == null)
                    return false;

                for (int i = 0; i < selectedParts.Count; i++)
                {
                    ModelPart modelPart = SelectModelPart(model, selectedParts[i]);
                    if (modelPart != null && IsDummyReferencePart(modelPart))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool HasDummyInContextViewNearCleanParts(
            TSM.Model model,
            Slot03SelectionContext context)
        {
            if (model == null || context == null || context.View == null)
                return false;

            TSM.TransformationPlane oldPlane = null;
            bool planeChanged = false;

            try
            {
                oldPlane = model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TSM.TransformationPlane(context.View.DisplayCoordinateSystem));
                planeChanged = true;

                Bounds2D cleanBounds = GetSlot03ContextBounds2D(context);
                bool hasCleanBounds = cleanBounds.Valid;

                TSD.DrawingObjectEnumerator parts =
                    context.View.GetAllObjects(typeof(DrawingPart));

                while (parts != null && parts.MoveNext())
                {
                    DrawingPart drawingPart = parts.Current as DrawingPart;
                    if (drawingPart == null || drawingPart.ModelIdentifier == null)
                        continue;

                    ModelPart modelPart =
                        model.SelectModelObject(drawingPart.ModelIdentifier) as ModelPart;

                    if (modelPart == null || !IsDummyReferencePart(modelPart))
                        continue;

                    // Neu khong lay duoc bounds cua context thi giu fallback cu: co dummy trong view la dung.
                    if (!hasCleanBounds)
                        return true;

                    // Chi nhan dummy nam gan cum main/plate/neighbor da loc sach.
                    if (IsPartNearBounds2D(modelPart, cleanBounds, 300.0))
                        return true;
                }
            }
            catch
            {
            }
            finally
            {
                if (planeChanged && oldPlane != null)
                {
                    try { model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane); } catch { }
                }
            }

            return false;
        }

        private static Bounds2D GetSlot03ContextBounds2D(Slot03SelectionContext context)
        {
            Bounds2D result = new Bounds2D();
            result.Valid = false;

            if (context == null)
                return result;

            AddPartBoundsToUnion(context.MainBeam, ref result);

            if (context.Plates != null)
            {
                for (int i = 0; i < context.Plates.Count; i++)
                    AddPartBoundsToUnion(context.Plates[i], ref result);
            }

            if (context.NeighborBeams != null)
            {
                for (int i = 0; i < context.NeighborBeams.Count; i++)
                    AddPartBoundsToUnion(context.NeighborBeams[i], ref result);
            }

            return result;
        }

        private static void AddPartBoundsToUnion(ModelPart part, ref Bounds2D unionBounds)
        {
            try
            {
                if (part == null)
                    return;

                Bounds2D b = GetPartBounds2D(part);
                if (!b.Valid)
                    return;

                if (!unionBounds.Valid)
                {
                    unionBounds = b;
                    return;
                }

                unionBounds.MinX = Math.Min(unionBounds.MinX, b.MinX);
                unionBounds.MaxX = Math.Max(unionBounds.MaxX, b.MaxX);
                unionBounds.MinY = Math.Min(unionBounds.MinY, b.MinY);
                unionBounds.MaxY = Math.Max(unionBounds.MaxY, b.MaxY);
                unionBounds.Valid = true;
            }
            catch
            {
            }
        }

        private static bool IsPartNearBounds2D(
            ModelPart part,
            Bounds2D referenceBounds,
            double tolerance)
        {
            try
            {
                if (part == null || !referenceBounds.Valid)
                    return false;

                Bounds2D partBounds = GetPartBounds2D(part);
                if (!partBounds.Valid)
                    return false;

                Bounds2D expanded = referenceBounds;
                expanded.MinX -= tolerance;
                expanded.MaxX += tolerance;
                expanded.MinY -= tolerance;
                expanded.MaxY += tolerance;

                return BoundsOverlap2D(partBounds, expanded);
            }
            catch
            {
                return false;
            }
        }

        private static bool BoundsOverlap2D(Bounds2D a, Bounds2D b)
        {
            if (!a.Valid || !b.Valid)
                return false;

            if (a.MaxX < b.MinX || a.MinX > b.MaxX)
                return false;

            if (a.MaxY < b.MinY || a.MinY > b.MaxY)
                return false;

            return true;
        }

        private static int ExecuteSlot03SelectionContext(
            TSM.Model model,
            TSD.DrawingHandler drawingHandler,
            Slot03SelectionContext context,
            bool useLegacyDummyLogic)
        {
            if (model == null || drawingHandler == null || context == null ||
                context.View == null || context.MainBeam == null ||
                context.Plates == null || context.Plates.Count == 0)
                return 0;

            if (context.NeighborBeams == null || context.NeighborBeams.Count == 0)
                return CreateSectionMainPlateOnlyDims(
                    model,
                    drawingHandler,
                    context.View,
                    context.MainBeam,
                    context.Plates);

            return CreateSectionNeighborPlateDimsRouter(
                model,
                drawingHandler,
                context.View,
                context.MainBeam,
                context.NeighborBeams,
                context.Plates,
                useLegacyDummyLogic);
        }

        private static int CreateSectionNeighborPlateDims(
            TSM.Model model,
            TSD.DrawingHandler dh,
            TSD.View view,
            ModelPart mainBeam,
            List<ModelPart> neighborBeams,
            List<ModelPart> plates,
            bool useLegacyDummyLogic)
        {
            int count = 0;

            if (model == null || dh == null || view == null || mainBeam == null ||
                neighborBeams == null || neighborBeams.Count == 0 ||
                plates == null || plates.Count == 0)
                return count;

            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TSM.TransformationPlane(view.DisplayCoordinateSystem));

                Bounds2D mainBox = GetPartBounds2D(mainBeam);
                if (!mainBox.Valid)
                    return count;

                Point mainRef = new Point(
                    (mainBox.MinX + mainBox.MaxX) / 2.0,
                    mainBox.MaxY,
                    0);

                SideGroup left = BuildSideGroup(
                    model,
                    view,
                    true,
                    mainBox,
                    mainRef,
                    neighborBeams,
                    plates);

                SideGroup right = BuildSideGroup(
                    model,
                    view,
                    false,
                    mainBox,
                    mainRef,
                    neighborBeams,
                    plates);

                TSD.StraightDimensionSetHandler handler =
                    new TSD.StraightDimensionSetHandler();

                Slot03TierManager tierManager = new Slot03TierManager();

                // Dim ngang khong doi o ca hai che do:
                // chi lay mot lo cao nhat cua moi cum lam dai dien.
                count += CreateMainAndRepresentativeHoleChain(
                    handler, view, tierManager, mainBox, mainRef, left, right);

                // 2 + 3. Cụm trái.
                if (left != null && left.Valid)
                {
                    // Mép trên neighbor trái -> lỗ Plate. Tầng 1.
                    if (!useLegacyDummyLogic)
                    {
                        count += CreateNeighborAndHoleChain(
                            handler, view, tierManager, left, new Vector(-1, 0, 0));
                    }
                    else if (left.HolePoint != null && left.NeighborTopPoint != null)
                    {
                        if (CreateDimChain(
                            handler,
                            view,
                            new Point[] { left.NeighborTopPoint, GetHolePointWithPhiGap(left.Plate, left.Neighbor, left.HolePoint, new Vector(-1, 0, 0)) },
                            new Vector(-1, 0, 0),
                            tierManager.TakeLeftDistance(left.NeighborTopPoint, left.PlateBox),
                            "GEO_DIMENSION"))
                        {
                            count++;
                        }
                    }

                    // Mép dầm main trái -> mép dưới neighbor. Tầng 2.
                    if (left.MainToNeighborP1 != null && left.MainToNeighborP2 != null)
                    {
                        Point[] leftMainToNeighborChain =
                            left.NeighborTopPoint != null && Math.Abs(left.MainToNeighborP2.Y - left.NeighborTopPoint.Y) > TOL
                            ? new Point[] { left.MainToNeighborP2, left.NeighborTopPoint, left.MainToNeighborP1 }
                            : new Point[] { left.MainToNeighborP2, left.MainToNeighborP1 };

                        if (CreateDimChain(
                            handler,
                            view,
                            leftMainToNeighborChain,
                            new Vector(-1, 0, 0),
                            tierManager.TakeLeftDistance(left.MainToNeighborP2, left.PlateBox),
                            "GEO_DIMENSION"))
                        {
                            count++;
                        }
                    }
                }

                // 2 + 3. Cụm phải.
                if (right != null && right.Valid)
                {
                    // Mép trên neighbor phải -> lỗ Plate. Tầng 1.
                    if (!useLegacyDummyLogic)
                    {
                        count += CreateNeighborAndHoleChain(
                            handler, view, tierManager, right, new Vector(1, 0, 0));
                    }
                    else if (right.HolePoint != null && right.NeighborTopPoint != null)
                    {
                        if (CreateDimChain(
                            handler,
                            view,
                            new Point[] { right.NeighborTopPoint, GetHolePointWithPhiGap(right.Plate, right.Neighbor, right.HolePoint, new Vector(1, 0, 0)) },
                            new Vector(1, 0, 0),
                            tierManager.TakeRightDistance(right.NeighborTopPoint, right.PlateBox),
                            "GEO_DIMENSION"))
                        {
                            count++;
                        }
                    }

                    // Mép dầm main phải -> mép dưới neighbor. Tầng 2.
                    if (right.MainToNeighborP1 != null && right.MainToNeighborP2 != null)
                    {
                        Point[] rightMainToNeighborChain =
                            right.NeighborTopPoint != null && Math.Abs(right.MainToNeighborP2.Y - right.NeighborTopPoint.Y) > TOL
                            ? new Point[] { right.MainToNeighborP2, right.NeighborTopPoint, right.MainToNeighborP1 }
                            : new Point[] { right.MainToNeighborP2, right.MainToNeighborP1 };

                        if (CreateDimChain(
                            handler,
                            view,
                            rightMainToNeighborChain,
                            new Vector(1, 0, 0),
                            tierManager.TakeRightDistance(right.MainToNeighborP2, right.PlateBox),
                            "GEO_DIMENSION"))
                        {
                            count++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Msg("Slot03 ERROR:\n" + ex.Message);
            }
            finally
            {
                try { model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane); } catch { }
            }


            return count;
        }

        // Chain ngang: chi dung lo cao nhat cua moi cum lam diem dai dien,
        // dung nhu logic cu o ca hai che do.
        private static int CreateMainAndRepresentativeHoleChain(
            TSD.StraightDimensionSetHandler handler,
            TSD.View view,
            Slot03TierManager tierManager,
            Bounds2D mainBox,
            Point mainRef,
            SideGroup left,
            SideGroup right)
        {
            if (handler == null || view == null || tierManager == null || mainRef == null)
                return 0;

            List<Point> chain = new List<Point>();

            if (left != null && left.Valid && left.HolePoint != null)
                chain.Add(GetHolePointWithPhiGap(left.Plate, left.Neighbor, left.HolePoint, new Vector(0, 1, 0)));

            chain.Add(mainRef);

            if (right != null && right.Valid && right.HolePoint != null)
                chain.Add(GetHolePointWithPhiGap(right.Plate, right.Neighbor, right.HolePoint, new Vector(0, 1, 0)));

            if (chain.Count < 2)
                return 0;

            return CreateDimChain(
                handler,
                view,
                chain.ToArray(),
                new Vector(0, 1, 0),
                tierManager.TakeTopDistance(chain[0], mainBox),
                "GEO_DIMENSION") ? 1 : 0;
        }

        // Che do khong dummy: mep tren neighbor -> tat ca lo -> mep duoi neighbor.
        private static int CreateNeighborAndHoleChain(
            TSD.StraightDimensionSetHandler handler,
            TSD.View view,
            Slot03TierManager tierManager,
            SideGroup group,
            Vector direction)
        {
            if (handler == null || view == null || tierManager == null ||
                group == null || !group.Valid || direction == null ||
                group.HolePoints == null || group.HolePoints.Count == 0)
                return 0;

            List<Point> holes = new List<Point>();
            for (int i = 0; i < group.HolePoints.Count; i++)
            {
                Point p = group.HolePoints[i];
                if (p != null)
                    holes.Add(new Point(p.X, p.Y, 0));
            }

            holes.Sort(delegate (Point a, Point b)
            {
                return b.Y.CompareTo(a.Y);
            });

            double innerX = group.IsLeft ? group.NeighborBox.MaxX : group.NeighborBox.MinX;
            Point neighborTop = new Point(innerX, group.NeighborBox.MaxY, 0);
            Point neighborBottom = new Point(innerX, group.NeighborBox.MinY, 0);

            List<Point> chain = new List<Point>();
            chain.Add(neighborTop);
            for (int i = 0; i < holes.Count; i++)
            {
                chain.Add(GetHolePointWithPhiGap(
                    group.Plate, group.Neighbor, holes[i], direction));
            }
            chain.Add(neighborBottom);

            double distance = direction.X < 0.0
                ? tierManager.TakeLeftDistance(neighborTop, group.PlateBox)
                : tierManager.TakeRightDistance(neighborTop, group.PlateBox);

            return CreateDimChain(
                handler,
                view,
                chain.ToArray(),
                direction,
                distance,
                "GEO_DIMENSION") ? 1 : 0;
        }

        // SLOT03 ADD-ON: route neighbor ngang/dọc riêng để không cho neighbor dọc lọt vào thuật toán ngang cũ.
        private static int CreateSectionNeighborPlateDimsRouter(
            TSM.Model model,
            TSD.DrawingHandler dh,
            TSD.View view,
            ModelPart mainBeam,
            List<ModelPart> neighborBeams,
            List<ModelPart> plates,
            bool useLegacyDummyLogic)
        {
            int count = 0;

            if (model == null || dh == null || view == null || mainBeam == null ||
                neighborBeams == null || neighborBeams.Count == 0 ||
                plates == null || plates.Count == 0)
                return count;

            List<ModelPart> horizontalNeighbors = new List<ModelPart>();
            List<ModelPart> verticalNeighbors = new List<ModelPart>();

            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TSM.TransformationPlane(view.DisplayCoordinateSystem));

                Bounds2D mainBox = GetPartBounds2D(mainBeam);
                if (!mainBox.Valid)
                    return count;

                SplitNeighborBeamsByOrientation(mainBox, neighborBeams, horizontalNeighbors, verticalNeighbors);
            }
            catch (Exception ex)
            {
                Msg("Slot03 route ERROR:\n" + ex.Message);
                return count;
            }
            finally
            {
                try { model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane); } catch { }
            }

            if (horizontalNeighbors.Count > 0)
                count += CreateSectionNeighborPlateDims(model, dh, view, mainBeam, horizontalNeighbors, plates, useLegacyDummyLogic);

            if (verticalNeighbors.Count > 0)
                count += CreateSectionVerticalNeighborPlateDims(model, dh, view, mainBeam, verticalNeighbors, plates);

            return count;
        }

        private static void SplitNeighborBeamsByOrientation(
            Bounds2D mainBox,
            List<ModelPart> neighborBeams,
            List<ModelPart> horizontalNeighbors,
            List<ModelPart> verticalNeighbors)
        {
            if (neighborBeams == null || horizontalNeighbors == null || verticalNeighbors == null || !mainBox.Valid)
                return;

            double mainCenterX = (mainBox.MinX + mainBox.MaxX) / 2.0;
            double mainCenterY = (mainBox.MinY + mainBox.MaxY) / 2.0;

            for (int i = 0; i < neighborBeams.Count; i++)
            {
                ModelPart n = neighborBeams[i];
                Bounds2D b = GetPartBounds2D(n);
                if (!b.Valid)
                    continue;

                double cx = (b.MinX + b.MaxX) / 2.0;
                double cy = (b.MinY + b.MaxY) / 2.0;
                double w = Math.Abs(b.MaxX - b.MinX);
                double h = Math.Abs(b.MaxY - b.MinY);
                double dx = cx - mainCenterX;
                double dy = cy - mainCenterY;

                bool aboveOrBelowMain = cy > mainBox.MaxY + TOL || cy < mainBox.MinY - TOL;
                bool leftOrRightMain = cx < mainBox.MinX - TOL || cx > mainBox.MaxX + TOL;

                // Neighbor dọc phải được nhận theo hình dạng trước.
                // Trường hợp trong hình/dump: POST dọc có thể nằm lệch X sang phải,
                // nên nếu chỉ xét Math.Abs(dy) so với Math.Abs(dx) thì cụm dưới dễ bị rơi qua flow ngang.
                bool verticalByShape = h > w + TOL;
                bool verticalByPosition = aboveOrBelowMain && Math.Abs(dy) >= Math.Abs(dx) * 0.35;

                if (verticalByShape || verticalByPosition)
                {
                    verticalNeighbors.Add(n);
                    continue;
                }

                if (leftOrRightMain || Math.Abs(dx) > Math.Abs(dy))
                    horizontalNeighbors.Add(n);
                else
                    verticalNeighbors.Add(n);
            }
        }

        private static int CreateSectionVerticalNeighborPlateDims(
            TSM.Model model,
            TSD.DrawingHandler dh,
            TSD.View view,
            ModelPart mainBeam,
            List<ModelPart> neighborBeams,
            List<ModelPart> plates)
        {
            int count = 0;

            if (model == null || dh == null || view == null || mainBeam == null ||
                neighborBeams == null || neighborBeams.Count == 0 ||
                plates == null || plates.Count == 0)
                return count;

            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TSM.TransformationPlane(view.DisplayCoordinateSystem));

                Bounds2D mainBox = GetPartBounds2D(mainBeam);
                if (!mainBox.Valid)
                    return count;

                VerticalGroup top = BuildVerticalGroup(model, view, true, mainBox, neighborBeams, plates);
                VerticalGroup bottom = BuildVerticalGroup(model, view, false, mainBox, neighborBeams, plates);

                TSD.StraightDimensionSetHandler handler =
                    new TSD.StraightDimensionSetHandler();

                Slot03TierManager tierManager = new Slot03TierManager();

                if (top != null && top.Valid)
                    count += CreateOneVerticalNeighborGroupDims(handler, view, tierManager, mainBox, top);

                if (bottom != null && bottom.Valid)
                    count += CreateOneVerticalNeighborGroupDims(handler, view, tierManager, mainBox, bottom);
            }
            catch (Exception ex)
            {
                Msg("Slot03 vertical ERROR:\n" + ex.Message);
            }
            finally
            {
                try { model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane); } catch { }
            }

            return count;
        }

        private class VerticalGroup
        {
            public bool Valid;
            public bool HoleIsBelowMainBottom;
            public bool HoleIsOnRightOfMain;
            public ModelPart Neighbor;
            public ModelPart Plate;
            public Bounds2D NeighborBox;
            public Bounds2D PlateBox;
            public List<Point> HorizontalHolePoints;
            public List<Point> VerticalHolePoints;
            public Point MainEdgeToHolePoint;
            public Point MainCenterPoint;
            public Point NeighborEdge1Point;
            public Point NeighborEdge2Point;
        }

        private static VerticalGroup BuildVerticalGroup(
            TSM.Model model,
            TSD.View view,
            bool isTop,
            Bounds2D mainBox,
            List<ModelPart> neighborBeams,
            List<ModelPart> plates)
        {
            VerticalGroup g = new VerticalGroup();
            g.Valid = false;

            double mainCenterX = (mainBox.MinX + mainBox.MaxX) / 2.0;
            double mainCenterY = (mainBox.MinY + mainBox.MaxY) / 2.0;

            ModelPart neighbor = null;
            Bounds2D neighborBox = new Bounds2D();
            neighborBox.Valid = false;
            double bestNeighborDist = 999999999.0;

            for (int i = 0; i < neighborBeams.Count; i++)
            {
                Bounds2D b = GetPartBounds2D(neighborBeams[i]);
                if (!b.Valid)
                    continue;

                double cy = (b.MinY + b.MaxY) / 2.0;
                bool sideOK = isTop ? cy > mainCenterY : cy < mainCenterY;
                if (!sideOK)
                    continue;

                double innerY = isTop ? b.MinY : b.MaxY;
                double candidateMainEdgeY = isTop ? mainBox.MaxY : mainBox.MinY;
                double d = Math.Abs(innerY - candidateMainEdgeY);

                if (d < bestNeighborDist)
                {
                    bestNeighborDist = d;
                    neighbor = neighborBeams[i];
                    neighborBox = b;
                }
            }

            if (neighbor == null || !neighborBox.Valid)
                return g;

            ModelPart plate = null;
            Bounds2D plateBox = new Bounds2D();
            plateBox.Valid = false;
            double bestPlateDist = 999999999.0;

            for (int i = 0; i < plates.Count; i++)
            {
                Bounds2D b = GetPartBounds2D(plates[i]);
                if (!b.Valid)
                    continue;

                double cy = (b.MinY + b.MaxY) / 2.0;
                bool sideOK = isTop ? cy > mainCenterY : cy < mainCenterY;
                if (!sideOK)
                    continue;

                double d = Distance2D(
                    new Point((b.MinX + b.MaxX) / 2.0, (b.MinY + b.MaxY) / 2.0, 0),
                    new Point((neighborBox.MinX + neighborBox.MaxX) / 2.0, (neighborBox.MinY + neighborBox.MaxY) / 2.0, 0));

                if (d < bestPlateDist)
                {
                    bestPlateDist = d;
                    plate = plates[i];
                    plateBox = b;
                }
            }

            // Fallback cho case 1 plate + 2 neighbor hoặc plate center không nằm hẳn trên/dưới main:
            // nếu không tìm được plate cùng phía, lấy plate gần neighbor nhất, không xét side.
            if (plate == null || !plateBox.Valid)
            {
                bestPlateDist = 999999999.0;

                for (int i = 0; i < plates.Count; i++)
                {
                    Bounds2D b = GetPartBounds2D(plates[i]);
                    if (!b.Valid)
                        continue;

                    double d = Distance2D(
                        new Point((b.MinX + b.MaxX) / 2.0, (b.MinY + b.MaxY) / 2.0, 0),
                        new Point((neighborBox.MinX + neighborBox.MaxX) / 2.0, (neighborBox.MinY + neighborBox.MaxY) / 2.0, 0));

                    if (d < bestPlateDist)
                    {
                        bestPlateDist = d;
                        plate = plates[i];
                        plateBox = b;
                    }
                }
            }

            if (plate == null || !plateBox.Valid)
                return g;

            List<Point> linkedBoltCenters =
                GetLinkedPlateNeighborBoltCenters(model, view, plate, neighbor, plateBox, neighborBox);

            List<Point> holePoints = GetPlateHolesForVerticalGroup(plateBox, linkedBoltCenters);
            if (holePoints.Count == 0)
            {
                linkedBoltCenters = GetViewBoltCentersInPlateNeighborOverlap(model, view, plateBox, neighborBox);
                holePoints = GetPlateHolesForVerticalGroup(plateBox, linkedBoltCenters);
            }

            Point verticalHole = PickHighestHoleForVerticalGroup(holePoints);
            if (verticalHole == null)
                return g;

            bool holeIsBelowMainBottom =
                IsSlot03HoleBelowMainBottom(verticalHole.Y, mainBox.MinY);
            bool holeIsOnRightOfMain =
                IsSlot03HoleOnRightOfMain(verticalHole.X, mainCenterX);
            bool horizontalDimOnTop = !holeIsBelowMainBottom;

            // One horizontal foot per X column. For a top DIM, each column uses
            // its lowest hole; for a bottom DIM, each column uses its highest hole.
            List<Point> horizontalHoles = BuildHorizontalHoleChainForVerticalGroup(
                holePoints,
                horizontalDimOnTop);
            List<Point> verticalHoles = BuildVerticalHoleChainForVerticalGroup(
                holePoints,
                holeIsOnRightOfMain);
            if (horizontalHoles.Count == 0 || verticalHoles.Count == 0)
                return g;

            double horizontalPartEdgeY = horizontalDimOnTop
                ? mainBox.MaxY
                : mainBox.MinY;
            double horizontalNeighborEdgeY = horizontalDimOnTop
                ? neighborBox.MaxY
                : neighborBox.MinY;

            // Chân DIM dọc trên dầm chính:
            // - lỗ trái/phải chọn đúng mép trái/phải của dầm;
            // - lỗ dưới đáy đo từ đáy, còn lại đo từ đỉnh dầm.
            Point verticalMainEdgePoint = new Point(
                holeIsOnRightOfMain ? mainBox.MaxX : mainBox.MinX,
                holeIsBelowMainBottom ? mainBox.MinY : mainBox.MaxY,
                0);

            // The main-beam foot follows the visual side of the horizontal DIM:
            // top DIM -> main top edge; bottom DIM -> main bottom edge.
            Point horizontalMainCenterEdgePoint =
                new Point(mainCenterX, horizontalPartEdgeY, 0);

            g.Neighbor = neighbor;
            g.Plate = plate;
            g.NeighborBox = neighborBox;
            g.PlateBox = plateBox;
            g.HorizontalHolePoints = horizontalHoles;
            g.VerticalHolePoints = verticalHoles;
            g.HoleIsBelowMainBottom = holeIsBelowMainBottom;
            g.HoleIsOnRightOfMain = holeIsOnRightOfMain;

            // DIM dọc: mép bên + đỉnh/đáy thật của main -> tâm lỗ.
            g.MainEdgeToHolePoint = verticalMainEdgePoint;

            // Dim ngang tầng 2: tâm main thật trên mép main -> tâm lỗ liên kết.
            g.MainCenterPoint = horizontalMainCenterEdgePoint;

            // Dim ngang tầng 1: mép neighbor -> tâm lỗ -> mép neighbor.
            g.NeighborEdge1Point = new Point(neighborBox.MinX, horizontalNeighborEdgeY, 0);
            g.NeighborEdge2Point = new Point(neighborBox.MaxX, horizontalNeighborEdgeY, 0);

            g.Valid = true;
            return g;
        }

        private static bool IsSlot03HoleBelowMainBottom(
            double holeY,
            double mainBottomY)
        {
            // Lỗ bằng hoặc chỉ lệch trong tolerance tại đáy vẫn được xem là
            // nằm trên đáy, do đó DIM ngang phải đặt phía trên.
            return holeY < mainBottomY - TOL;
        }

        private static bool IsSlot03HoleOnRightOfMain(
            double holeX,
            double mainCenterX)
        {
            // Chỉ điểm đúng tâm mới dùng phía trái làm tie-break ổn định.
            return holeX > mainCenterX;
        }

        private static int CreateOneVerticalNeighborGroupDims(
            TSD.StraightDimensionSetHandler handler,
            TSD.View view,
            Slot03TierManager tierManager,
            Bounds2D mainBox,
            VerticalGroup g)
        {
            int count = 0;
            if (handler == null || view == null || tierManager == null || g == null || !g.Valid)
                return count;

            Vector verticalDimDirection = g.HoleIsOnRightOfMain
                ? new Vector(1, 0, 0)
                : new Vector(-1, 0, 0);
            bool horizontalDimOnTop = !g.HoleIsBelowMainBottom;
            Vector horizontalDimDirection = horizontalDimOnTop
                ? new Vector(0, 1, 0)
                : new Vector(0, -1, 0);

            List<Point> verticalChain = new List<Point>();
            AddUniquePointByAxis(verticalChain, g.MainEdgeToHolePoint, false, TOL);
            if (g.VerticalHolePoints != null)
            {
                for (int i = 0; i < g.VerticalHolePoints.Count; i++)
                {
                    Point foot = GetHolePointWithPhiGap(
                        g.Plate,
                        g.Neighbor,
                        g.VerticalHolePoints[i],
                        verticalDimDirection);
                    AddUniquePointByAxis(verticalChain, foot, false, TOL);
                }
            }
            verticalChain.Sort(delegate (Point a, Point b)
            {
                int byY = b.Y.CompareTo(a.Y);
                return byY != 0 ? byY : a.X.CompareTo(b.X);
            });

            List<Point> horizontalHoleFeet = new List<Point>();
            if (g.HorizontalHolePoints != null)
            {
                for (int i = 0; i < g.HorizontalHolePoints.Count; i++)
                {
                    Point foot = GetHolePointWithPhiGap(
                        g.Plate,
                        g.Neighbor,
                        g.HorizontalHolePoints[i],
                        horizontalDimDirection);
                    AddUniquePointByAxis(horizontalHoleFeet, foot, true, TOL);
                }
            }
            horizontalHoleFeet.Sort(delegate (Point a, Point b)
            {
                int byX = a.X.CompareTo(b.X);
                return byX != 0 ? byX : a.Y.CompareTo(b.Y);
            });

            // 1. Vertical chain: main top/bottom -> every distinct Y row, top to bottom.
            if (verticalChain.Count >= 2)
            {
                if (CreateDimChain(
                    handler,
                    view,
                    verticalChain.ToArray(),
                    verticalDimDirection,
                    g.HoleIsOnRightOfMain
                        ? tierManager.TakeRightDistance(g.MainEdgeToHolePoint, g.PlateBox)
                        : tierManager.TakeLeftDistance(g.MainEdgeToHolePoint, g.PlateBox),
                    "GEO_DIMENSION"))
                {
                    count++;
                }
            }

            // 2. Horizontal chain tier 1: neighbor edge -> every distinct X column -> edge.
            List<Point> neighborHorizontalChain = new List<Point>();
            AddUniquePointByAxis(neighborHorizontalChain, g.NeighborEdge1Point, true, TOL);
            for (int i = 0; i < horizontalHoleFeet.Count; i++)
                AddUniquePointByAxis(neighborHorizontalChain, horizontalHoleFeet[i], true, TOL);
            AddUniquePointByAxis(neighborHorizontalChain, g.NeighborEdge2Point, true, TOL);
            neighborHorizontalChain.Sort(delegate (Point a, Point b)
            {
                int byX = a.X.CompareTo(b.X);
                return byX != 0 ? byX : a.Y.CompareTo(b.Y);
            });

            if (neighborHorizontalChain.Count >= 2)
            {
                if (CreateDimChain(
                    handler,
                    view,
                    neighborHorizontalChain.ToArray(),
                    horizontalDimDirection,
                    horizontalDimOnTop
                        ? tierManager.TakeTopDistance(g.NeighborEdge1Point, mainBox)
                        : tierManager.TakeBottomDistance(g.NeighborEdge1Point, mainBox),
                    "GEO_DIMENSION"))
                {
                    count++;
                }
            }

            // 3. Horizontal chain tier 2: main foot -> every distinct X column.
            List<Point> mainHorizontalChain = new List<Point>();
            AddUniquePointByAxis(mainHorizontalChain, g.MainCenterPoint, true, TOL);
            for (int i = 0; i < horizontalHoleFeet.Count; i++)
                AddUniquePointByAxis(mainHorizontalChain, horizontalHoleFeet[i], true, TOL);
            mainHorizontalChain.Sort(delegate (Point a, Point b)
            {
                int byX = a.X.CompareTo(b.X);
                return byX != 0 ? byX : a.Y.CompareTo(b.Y);
            });

            if (mainHorizontalChain.Count >= 2)
            {
                if (CreateDimChain(
                    handler,
                    view,
                    mainHorizontalChain.ToArray(),
                    horizontalDimDirection,
                    horizontalDimOnTop
                        ? tierManager.TakeTopDistance(g.MainCenterPoint, mainBox)
                        : tierManager.TakeBottomDistance(g.MainCenterPoint, mainBox),
                    "GEO_DIMENSION"))
                {
                    count++;
                }
            }

            return count;
        }

        private static List<Point> GetPlateHolesForVerticalGroup(
            Bounds2D plateBox,
            List<Point> boltCenters)
        {
            List<Point> result = new List<Point>();
            if (boltCenters == null || boltCenters.Count == 0)
                return result;

            for (int i = 0; i < boltCenters.Count; i++)
            {
                Point p = boltCenters[i];
                if (p == null)
                    continue;

                if (p.X < plateBox.MinX - 20.0 || p.X > plateBox.MaxX + 20.0 ||
                    p.Y < plateBox.MinY - 20.0 || p.Y > plateBox.MaxY + 20.0)
                    continue;

                AddUniquePoint2D(result, new Point(p.X, p.Y, 0), POINT_DUP_TOL);
            }

            return result;
        }

        private static Point PickHighestHoleForVerticalGroup(List<Point> holePoints)
        {
            if (holePoints == null || holePoints.Count == 0)
                return null;

            Point best = null;
            for (int i = 0; i < holePoints.Count; i++)
            {
                Point p = holePoints[i];
                if (p == null)
                    continue;

                if (best == null || p.Y > best.Y + TOL ||
                    (Math.Abs(p.Y - best.Y) <= TOL && p.X < best.X))
                {
                    best = p;
                }
            }

            return best == null ? null : new Point(best.X, best.Y, 0);
        }

        private static List<Point> BuildHorizontalHoleChainForVerticalGroup(
            List<Point> holePoints,
            bool dimOnTop)
        {
            List<Point> result = new List<Point>();
            if (holePoints == null || holePoints.Count == 0)
                return result;

            List<Point> sorted = new List<Point>();
            for (int i = 0; i < holePoints.Count; i++)
            {
                Point p = holePoints[i];
                if (p != null)
                    sorted.Add(new Point(p.X, p.Y, 0));
            }
            sorted.Sort(delegate (Point a, Point b)
            {
                int byX = a.X.CompareTo(b.X);
                return byX != 0 ? byX : a.Y.CompareTo(b.Y);
            });

            int index = 0;
            while (index < sorted.Count)
            {
                double columnX = sorted[index].X;
                Point selected = sorted[index];
                int next = index + 1;

                while (next < sorted.Count && Math.Abs(sorted[next].X - columnX) <= TOL)
                {
                    Point candidate = sorted[next];
                    bool better = dimOnTop
                        ? candidate.Y < selected.Y - TOL
                        : candidate.Y > selected.Y + TOL;
                    if (better)
                        selected = candidate;
                    next++;
                }

                result.Add(new Point(selected.X, selected.Y, 0));
                index = next;
            }

            return result;
        }

        private static List<Point> BuildVerticalHoleChainForVerticalGroup(
            List<Point> holePoints,
            bool dimOnRight)
        {
            List<Point> result = new List<Point>();
            if (holePoints == null || holePoints.Count == 0)
                return result;

            List<Point> sorted = new List<Point>();
            for (int i = 0; i < holePoints.Count; i++)
            {
                Point p = holePoints[i];
                if (p != null)
                    sorted.Add(new Point(p.X, p.Y, 0));
            }
            sorted.Sort(delegate (Point a, Point b)
            {
                int byY = b.Y.CompareTo(a.Y);
                return byY != 0 ? byY : a.X.CompareTo(b.X);
            });

            int index = 0;
            while (index < sorted.Count)
            {
                double rowY = sorted[index].Y;
                Point selected = sorted[index];
                int next = index + 1;

                while (next < sorted.Count && Math.Abs(sorted[next].Y - rowY) <= TOL)
                {
                    Point candidate = sorted[next];
                    // Put the foot on the far side of the row so the extension
                    // line crosses all holes before reaching the DIM line.
                    bool better = dimOnRight
                        ? candidate.X < selected.X - TOL
                        : candidate.X > selected.X + TOL;
                    if (better)
                        selected = candidate;
                    next++;
                }

                result.Add(new Point(selected.X, selected.Y, 0));
                index = next;
            }

            return result;
        }

        private static void AddUniquePointByAxis(
            List<Point> points,
            Point point,
            bool compareX,
            double tolerance)
        {
            if (points == null || point == null)
                return;

            for (int i = 0; i < points.Count; i++)
            {
                double delta = compareX
                    ? Math.Abs(points[i].X - point.X)
                    : Math.Abs(points[i].Y - point.Y);
                if (delta <= tolerance)
                    return;
            }

            points.Add(new Point(point.X, point.Y, 0));
        }

        private class SideGroup
        {
            public bool Valid;
            public bool IsLeft;
            public ModelPart Neighbor;
            public ModelPart Plate;
            public Bounds2D NeighborBox;
            public Bounds2D PlateBox;
            public Point HolePoint;
            public List<Point> HolePoints;
            public Point NeighborTopPoint;
            public Point MainToNeighborP1;
            public Point MainToNeighborP2;
        }

        private class Slot03TierManager
        {
            private int _topTier = 0;
            private int _bottomTier = 0;
            private int _leftTier = 0;
            private int _rightTier = 0;

            private static double TierOffset(int tier)
            {
                if (tier < 0)
                    tier = 0;

                return SLOT03_DIM_TIER_BASE + SLOT03_DIM_TIER_STEP * tier;
            }

            public double TakeTopDistance(Point firstPoint, Bounds2D mainBox)
            {
                double offset = TierOffset(_topTier);
                _topTier++;

                if (firstPoint == null || !mainBox.Valid)
                    return offset;

                double targetY = mainBox.MaxY + offset;
                return Math.Abs(targetY - firstPoint.Y);
            }

            public double TakeBottomDistance(Point firstPoint, Bounds2D mainBox)
            {
                double offset = TierOffset(_bottomTier);
                _bottomTier++;

                if (firstPoint == null || !mainBox.Valid)
                    return offset;

                double targetY = mainBox.MinY - offset;
                return Math.Abs(firstPoint.Y - targetY);
            }

            public double TakeLeftDistance(Point firstPoint, Bounds2D plateBox)
            {
                double offset = TierOffset(_leftTier);
                _leftTier++;

                if (firstPoint == null || !plateBox.Valid)
                    return offset;

                double targetX = plateBox.MinX - offset;
                return Math.Abs(firstPoint.X - targetX);
            }

            public double TakeRightDistance(Point firstPoint, Bounds2D plateBox)
            {
                double offset = TierOffset(_rightTier);
                _rightTier++;

                if (firstPoint == null || !plateBox.Valid)
                    return offset;

                double targetX = plateBox.MaxX + offset;
                return Math.Abs(targetX - firstPoint.X);
            }
        }

        private static SideGroup BuildSideGroup(
            TSM.Model model,
            TSD.View view,
            bool isLeft,
            Bounds2D mainBox,
            Point mainRef,
            List<ModelPart> neighborBeams,
            List<ModelPart> plates)
        {
            SideGroup g = new SideGroup();
            g.IsLeft = isLeft;
            g.Valid = false;
            g.HolePoints = new List<Point>();

            double mainCenterX = (mainBox.MinX + mainBox.MaxX) / 2.0;

            ModelPart neighbor = null;
            Bounds2D neighborBox = new Bounds2D();
            neighborBox.Valid = false;

            double bestNeighborDist = 999999999.0;

            for (int i = 0; i < neighborBeams.Count; i++)
            {
                Bounds2D b = GetPartBounds2D(neighborBeams[i]);
                if (!b.Valid)
                    continue;

                double cx = (b.MinX + b.MaxX) / 2.0;
                bool sideOK = isLeft ? cx < mainCenterX : cx > mainCenterX;
                if (!sideOK)
                    continue;

                double innerX = isLeft ? b.MaxX : b.MinX;
                double d = Math.Abs(innerX - (isLeft ? mainBox.MinX : mainBox.MaxX));
                if (d < bestNeighborDist)
                {
                    bestNeighborDist = d;
                    neighbor = neighborBeams[i];
                    neighborBox = b;
                }
            }

            if (neighbor == null || !neighborBox.Valid)
                return g;

            ModelPart plate = null;
            Bounds2D plateBox = new Bounds2D();
            plateBox.Valid = false;

            double bestPlateDist = 999999999.0;

            for (int i = 0; i < plates.Count; i++)
            {
                Bounds2D b = GetPartBounds2D(plates[i]);
                if (!b.Valid)
                    continue;

                double cx = (b.MinX + b.MaxX) / 2.0;
                bool sideOK = isLeft ? cx < mainCenterX : cx > mainCenterX;
                if (!sideOK)
                    continue;

                double d = Distance2D(
                    new Point((b.MinX + b.MaxX) / 2.0, (b.MinY + b.MaxY) / 2.0, 0),
                    new Point((neighborBox.MinX + neighborBox.MaxX) / 2.0, (neighborBox.MinY + neighborBox.MaxY) / 2.0, 0));

                if (d < bestPlateDist)
                {
                    bestPlateDist = d;
                    plate = plates[i];
                    plateBox = b;
                }
            }

            if (plate == null || !plateBox.Valid)
                return g;

            // Chỉ lấy lỗ liên kết thật giữa neighbor và plate.
            // Không lấy toàn bộ lỗ trong view nữa, vì sẽ bắt nhầm lỗ/mark chữ nhật không liên kết.
            List<Point> linkedBoltCenters =
                GetLinkedPlateNeighborBoltCenters(model, view, plate, neighbor, plateBox, neighborBox);

            Point hole = PickPlateHoleForSide(plateBox, linkedBoltCenters, isLeft, mainCenterX);
            if (hole == null)
            {
                // fallback cuối: chỉ tìm lỗ nằm trong vùng giao hình học plate + neighbor.
                linkedBoltCenters =
                    GetViewBoltCentersInPlateNeighborOverlap(model, view, plateBox, neighborBox);

                hole = PickPlateHoleForSide(plateBox, linkedBoltCenters, isLeft, mainCenterX);
            }

            if (hole == null)
            {
                // Không có lỗ liên kết thật thì không tạo cụm dim này.
                return g;
            }

            double mainEdgeX = isLeft ? mainBox.MinX : mainBox.MaxX;
            double neighborInnerX = isLeft ? neighborBox.MaxX : neighborBox.MinX;

            g.Neighbor = neighbor;
            g.Plate = plate;
            g.NeighborBox = neighborBox;
            g.PlateBox = plateBox;
            g.HolePoint = hole;
            for (int i = 0; i < linkedBoltCenters.Count; i++)
            {
                Point p = linkedBoltCenters[i];
                if (p == null)
                    continue;

                bool sideOK = isLeft ? p.X < mainCenterX : p.X > mainCenterX;
                if (!sideOK || !PointInsideBounds(p, plateBox, 20.0))
                    continue;

                AddUniquePoint2D(g.HolePoints, new Point(p.X, p.Y, 0), POINT_DUP_TOL);
            }

            // Dam bao lo dai dien hien tai luon nam trong chain moi.
            AddUniquePoint2D(g.HolePoints, new Point(hole.X, hole.Y, 0), POINT_DUP_TOL);

            // Mép trên neighbor trái/phải -> lỗ Plate.
            g.NeighborTopPoint = new Point(neighborInnerX, neighborBox.MaxY, 0);

            // Mép dầm main trái/phải -> mép dưới neighbor.
            // Theo dump: đo chiều đứng từ bottom neighbor lên top main, x theo mép trong neighbor/main.
            g.MainToNeighborP1 = new Point(neighborInnerX, neighborBox.MinY, 0);
            g.MainToNeighborP2 = new Point(mainEdgeX, mainBox.MaxY, 0);

            g.Valid = true;
            return g;
        }


        private static List<Point> GetLinkedPlateNeighborBoltCenters(
            TSM.Model model,
            TSD.View view,
            ModelPart plate,
            ModelPart neighbor,
            Bounds2D plateBox,
            Bounds2D neighborBox)
        {
            List<Point> result = new List<Point>();

            try
            {
                AddLinkedBoltCentersFromPart(model, plate, neighbor, plateBox, neighborBox, result);
                AddLinkedBoltCentersFromPart(model, neighbor, plate, plateBox, neighborBox, result);

                if (result.Count > 0)
                    return result;
            }
            catch
            {
            }

            // Fallback hình học: chỉ nhận bolt nằm trong vùng giao giữa plate và neighbor.
            // Đây là đúng ý "lỗ liên kết giữa neighbor và plate", và sẽ bỏ qua lỗ/mark chữ nhật nằm riêng trong plate.
            return GetViewBoltCentersInPlateNeighborOverlap(model, view, plateBox, neighborBox);
        }

        private static void AddLinkedBoltCentersFromPart(
            TSM.Model model,
            ModelPart ownerPart,
            ModelPart otherPart,
            Bounds2D plateBox,
            Bounds2D neighborBox,
            List<Point> result)
        {
            try
            {
                if (model == null || ownerPart == null || otherPart == null || result == null)
                    return;

                ModelObjectEnumerator bolts = ownerPart.GetBolts();
                while (bolts != null && bolts.MoveNext())
                {
                    ModelBoltGroup bg = bolts.Current as ModelBoltGroup;
                    if (bg == null || bg.BoltPositions == null)
                        continue;

                    if (!IsAllowedDimensionBoltGroup(bg))
                        continue;

                    if (!BoltGroupReferencesPart(bg, ownerPart) && !BoltGroupReferencesPart(bg, otherPart))
                    {
                        // Một số Tekla không expose PartToBoltTo/PartToBeBolted rõ ràng.
                        // Khi không đọc được quan hệ part, vẫn kiểm tra bằng vùng giao hình học phía dưới.
                    }

                    bool relationOk =
                        BoltGroupReferencesPart(bg, ownerPart) &&
                        BoltGroupReferencesPart(bg, otherPart);

                    foreach (object obj in bg.BoltPositions)
                    {
                        Point p = obj as Point;
                        if (p == null)
                            continue;

                        if (!PointInsideBounds(p, plateBox, 25.0))
                            continue;

                        if (!PointInsideBounds(p, neighborBox, 25.0))
                            continue;

                        // Chỉ nhận bolt group thực sự liên kết giữa plate và neighbor.
                        // Không dùng điều kiện hình học ở đây vì lỗ/mark chữ nhật có thể nằm trong vùng plate nhưng không phải lỗ liên kết.
                        if (relationOk)
                            AddUniquePoint2D(result, new Point(p.X, p.Y, 0), POINT_DUP_TOL);
                    }
                }
            }
            catch
            {
            }
        }

        private static List<Point> GetViewBoltCentersInPlateNeighborOverlap(
            TSM.Model model,
            TSD.View view,
            Bounds2D plateBox,
            Bounds2D neighborBox)
        {
            List<Point> result = new List<Point>();

            try
            {
                if (model == null || view == null)
                    return result;

                TSD.DrawingObjectEnumerator e = view.GetAllObjects(typeof(Tekla.Structures.Drawing.Bolt));
                while (e != null && e.MoveNext())
                {
                    TSD.DrawingObject dobj = e.Current as TSD.DrawingObject;
                    if (dobj == null)
                        continue;

                    Identifier id = TryGetModelIdentifier(dobj);
                    if (id == null)
                        continue;

                    ModelObject mo = model.SelectModelObject(id);
                    ModelBoltGroup bg = mo as ModelBoltGroup;
                    if (bg == null || bg.BoltPositions == null)
                        continue;

                    if (!IsAllowedDimensionBoltGroup(bg))
                        continue;

                    foreach (object obj in bg.BoltPositions)
                    {
                        Point p = obj as Point;
                        if (p == null)
                            continue;

                        if (!PointInsideBounds(p, plateBox, 25.0))
                            continue;

                        if (!PointInsideBounds(p, neighborBox, 25.0))
                            continue;

                        AddUniquePoint2D(result, new Point(p.X, p.Y, 0), POINT_DUP_TOL);
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private static bool PointInsideBounds(Point p, Bounds2D b, double tol)
        {
            if (p == null || !b.Valid)
                return false;

            return p.X >= b.MinX - tol &&
                   p.X <= b.MaxX + tol &&
                   p.Y >= b.MinY - tol &&
                   p.Y <= b.MaxY + tol;
        }

        private static bool BoltGroupReferencesPart(ModelBoltGroup bg, ModelPart part)
        {
            try
            {
                if (bg == null || part == null || part.Identifier == null)
                    return false;

                string[] propNames = new string[]
                {
                    "PartToBoltTo",
                    "PartToBeBolted",
                    "Father",
                    "FatherObject"
                };

                for (int i = 0; i < propNames.Length; i++)
                {
                    object value = GetPropertyValue(bg, propNames[i]);
                    if (ObjectOrEnumerableContainsIdentifier(value, part.Identifier))
                        return true;
                }

                object others = GetPropertyValue(bg, "OtherPartsToBolt");
                if (ObjectOrEnumerableContainsIdentifier(others, part.Identifier))
                    return true;
            }
            catch
            {
            }

            return false;
        }

        private static bool ObjectOrEnumerableContainsIdentifier(object value, Identifier id)
        {
            try
            {
                if (value == null || id == null)
                    return false;

                ModelObject mo = value as ModelObject;
                if (mo != null && SameIdentifier(mo.Identifier, id))
                    return true;

                Identifier directId = value as Identifier;
                if (directId != null && SameIdentifier(directId, id))
                    return true;

                IEnumerable enumerable = value as IEnumerable;
                if (enumerable != null && !(value is string))
                {
                    foreach (object item in enumerable)
                    {
                        if (ObjectOrEnumerableContainsIdentifier(item, id))
                            return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static Point PickPlateHoleForSide(
            Bounds2D plateBox,
            List<Point> boltCenters,
            bool isLeft,
            double mainCenterX)
        {
            if (boltCenters == null || boltCenters.Count == 0)
                return null;

            Point best = null;
            double bestY = -999999999.0;

            for (int i = 0; i < boltCenters.Count; i++)
            {
                Point p = boltCenters[i];
                if (p == null)
                    continue;

                if (p.X < plateBox.MinX - 20.0 || p.X > plateBox.MaxX + 20.0 ||
                    p.Y < plateBox.MinY - 20.0 || p.Y > plateBox.MaxY + 20.0)
                    continue;

                bool sideOK = isLeft ? p.X < mainCenterX : p.X > mainCenterX;
                if (!sideOK)
                    continue;

                // Dump chọn lỗ phía trên của plate.
                if (best == null || p.Y > bestY)
                {
                    best = new Point(p.X, p.Y, 0);
                    bestY = p.Y;
                }
            }

            return best;
        }

        private static List<Point> GetSelectedBoltCentersOrViewBolts(
            TSM.Model model,
            TSD.DrawingHandler dh,
            TSD.View view)
        {
            List<Point> result = new List<Point>();

            try
            {
                TSD.DrawingObjectEnumerator selected =
                    dh.GetDrawingObjectSelector().GetSelected();

                while (selected != null && selected.MoveNext())
                {
                    TSD.DrawingObject dobj = selected.Current as TSD.DrawingObject;
                    if (dobj == null)
                        continue;

                    if (dobj.GetType().FullName != "Tekla.Structures.Drawing.Bolt")
                        continue;

                    Identifier id = TryGetModelIdentifier(dobj);
                    AddBoltGroupPoints(model, id, result);
                }
            }
            catch
            {
            }

            if (result.Count > 0)
                return result;

            try
            {
                TSD.DrawingObjectEnumerator e = view.GetAllObjects(typeof(Tekla.Structures.Drawing.Bolt));
                while (e != null && e.MoveNext())
                {
                    TSD.DrawingObject dobj = e.Current as TSD.DrawingObject;
                    if (dobj == null)
                        continue;

                    Identifier id = TryGetModelIdentifier(dobj);
                    AddBoltGroupPoints(model, id, result);
                }
            }
            catch
            {
            }

            return result;
        }

        private static void AddBoltGroupPoints(
            TSM.Model model,
            Identifier id,
            List<Point> result)
        {
            try
            {
                if (model == null || id == null || result == null)
                    return;

                ModelObject mo = model.SelectModelObject(id);
                ModelBoltGroup bg = mo as ModelBoltGroup;
                if (bg == null || bg.BoltPositions == null)
                    return;

                foreach (object obj in bg.BoltPositions)
                {
                    Point p = obj as Point;
                    if (p == null)
                        continue;

                    Point q = new Point(p.X, p.Y, 0);

                    bool exists = false;
                    for (int i = 0; i < result.Count; i++)
                    {
                        if (Distance2D(result[i], q) <= POINT_DUP_TOL)
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                        result.Add(q);
                }
            }
            catch
            {
            }
        }

        private static Identifier TryGetModelIdentifier(object drawingObject)
        {
            try
            {
                if (drawingObject == null)
                    return null;

                object value = GetPropertyValue(drawingObject, "ModelIdentifier");
                return value as Identifier;
            }
            catch
            {
                return null;
            }
        }

        private struct Bounds2D
        {
            public bool Valid;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
        }

        private static Bounds2D GetPartBounds2D(ModelPart part)
        {
            Bounds2D b = new Bounds2D();
            b.Valid = false;

            try
            {
                Solid s = part.GetSolid();
                Point min = s.MinimumPoint;
                Point max = s.MaximumPoint;

                b.MinX = Math.Min(min.X, max.X);
                b.MaxX = Math.Max(min.X, max.X);
                b.MinY = Math.Min(min.Y, max.Y);
                b.MaxY = Math.Max(min.Y, max.Y);
                b.Valid = Math.Abs(b.MaxX - b.MinX) > TOL && Math.Abs(b.MaxY - b.MinY) > TOL;
            }
            catch
            {
            }

            return b;
        }

        private static void PickMainBeamByPlateAssemblies(
            List<ModelPart> plates,
            List<ModelPart> beams,
            List<DrawingPart> beamDrawingParts,
            out ModelPart mainBeam,
            out DrawingPart mainDrawingPart)
        {
            mainBeam = null;
            mainDrawingPart = null;

            int bestScore = -1;
            int bestIndex = -1;

            for (int i = 0; i < beams.Count; i++)
            {
                ModelPart beam = beams[i];
                if (beam == null)
                    continue;

                string beamAssembly = GetReportString(beam, "ASSEMBLY_POS");
                int score = 0;

                for (int p = 0; p < plates.Count; p++)
                {
                    string plateAssembly = GetReportString(plates[p], "ASSEMBLY_POS");
                    if (!string.IsNullOrEmpty(plateAssembly) &&
                        !string.IsNullOrEmpty(beamAssembly) &&
                        string.Equals(plateAssembly, beamAssembly, StringComparison.OrdinalIgnoreCase))
                    {
                        score++;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0 || bestScore <= 0)
            {
                double bestArea = -1.0;
                bestIndex = 0;

                for (int i = 0; i < beams.Count; i++)
                {
                    Bounds2D box = GetPartBounds2D(beams[i]);
                    double area = box.Valid ? Math.Abs(box.MaxX - box.MinX) * Math.Abs(box.MaxY - box.MinY) : 0.0;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        bestIndex = i;
                    }
                }
            }

            if (bestIndex >= 0 && bestIndex < beams.Count)
            {
                mainBeam = beams[bestIndex];
                if (bestIndex < beamDrawingParts.Count)
                    mainDrawingPart = beamDrawingParts[bestIndex];
            }
        }

        private static List<DrawingPart> GetSelectedDrawingParts(TSD.DrawingHandler dh)
        {
            List<DrawingPart> result = new List<DrawingPart>();

            try
            {
                TSD.DrawingObjectEnumerator e =
                    dh.GetDrawingObjectSelector().GetSelected();

                while (e != null && e.MoveNext())
                {
                    DrawingPart dp = e.Current as DrawingPart;
                    if (dp != null)
                        result.Add(dp);
                }
            }
            catch
            {
            }

            return result;
        }

        private static ModelPart SelectModelPart(TSM.Model model, DrawingPart dp)
        {
            try
            {
                if (model == null || dp == null || dp.ModelIdentifier == null)
                    return null;

                return model.SelectModelObject(dp.ModelIdentifier) as ModelPart;
            }
            catch
            {
                return null;
            }
        }

        private static TSD.View TryGetSelectedPartsView(params DrawingPart[] parts)
        {
            TSD.View result = null;

            if (parts == null)
                return null;

            for (int i = 0; i < parts.Length; i++)
            {
                TSD.View v = TryGetDrawingObjectView(parts[i]);
                if (v == null)
                    continue;

                if (result == null)
                    result = v;
                else if (!object.ReferenceEquals(result, v))
                    return result;
            }

            return result;
        }

        private static TSD.View TryGetDrawingObjectView(object drawingObject)
        {
            if (drawingObject == null)
                return null;

            string[] methodNames = new string[]
            {
                "GetView",
                "GetFatherView",
                "GetParentView"
            };

            for (int i = 0; i < methodNames.Length; i++)
            {
                try
                {
                    MethodInfo m = drawingObject.GetType().GetMethod(
                        methodNames[i],
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        Type.EmptyTypes,
                        null);

                    if (m == null)
                        continue;

                    object value = m.Invoke(drawingObject, null);
                    TSD.View view = value as TSD.View;
                    if (view != null)
                        return view;
                }
                catch
                {
                }
            }

            string[] propertyNames = new string[]
            {
                "View",
                "FatherView",
                "ParentView"
            };

            for (int i = 0; i < propertyNames.Length; i++)
            {
                try
                {
                    object value = GetPropertyValue(drawingObject, propertyNames[i]);
                    TSD.View view = value as TSD.View;
                    if (view != null)
                        return view;
                }
                catch
                {
                }
            }

            return null;
        }

        private static TSD.View FindViewContainingParts(
            TSD.Drawing drawing,
            Identifier id1,
            Identifier id2,
            Identifier id3)
        {
            try
            {
                if (drawing == null || id1 == null || id2 == null || id3 == null)
                    return null;

                TSD.ContainerView sheet = drawing.GetSheet();
                if (sheet == null)
                    return null;

                TSD.DrawingObjectEnumerator views = sheet.GetAllViews();
                while (views != null && views.MoveNext())
                {
                    TSD.View view = views.Current as TSD.View;
                    if (view == null)
                        continue;

                    bool has1 = false;
                    bool has2 = false;
                    bool has3 = false;

                    TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(DrawingPart));
                    while (parts != null && parts.MoveNext())
                    {
                        DrawingPart dp = parts.Current as DrawingPart;
                        if (dp == null || dp.ModelIdentifier == null)
                            continue;

                        if (SameIdentifier(dp.ModelIdentifier, id1)) has1 = true;
                        if (SameIdentifier(dp.ModelIdentifier, id2)) has2 = true;
                        if (SameIdentifier(dp.ModelIdentifier, id3)) has3 = true;

                        if (has1 && has2 && has3)
                            return view;
                    }
                }
            }
            catch
            {
            }

            return null;
        }


        private static TSD.View FindViewContainingParts(
            TSD.Drawing drawing,
            Identifier id1,
            Identifier id2)
        {
            try
            {
                if (drawing == null || id1 == null || id2 == null)
                    return null;

                TSD.ContainerView sheet = drawing.GetSheet();
                if (sheet == null)
                    return null;

                TSD.DrawingObjectEnumerator views = sheet.GetAllViews();
                while (views != null && views.MoveNext())
                {
                    TSD.View view = views.Current as TSD.View;
                    if (view == null)
                        continue;

                    bool has1 = false;
                    bool has2 = false;

                    TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(DrawingPart));
                    while (parts != null && parts.MoveNext())
                    {
                        DrawingPart dp = parts.Current as DrawingPart;
                        if (dp == null || dp.ModelIdentifier == null)
                            continue;

                        if (SameIdentifier(dp.ModelIdentifier, id1)) has1 = true;
                        if (SameIdentifier(dp.ModelIdentifier, id2)) has2 = true;

                        if (has1 && has2)
                            return view;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsDummyReferencePart(ModelPart part)
        {
            string name = GetReportString(part, "NAME").Trim().ToUpperInvariant();
            string material = GetReportString(part, "MATERIAL").Trim().ToUpperInvariant();
            string partPos = GetReportString(part, "PART_POS").Trim().ToUpperInvariant();

            if (name.IndexOf("BJ") >= 0)
                return true;

            if (name.IndexOf("JOINT") >= 0 || name.IndexOf("JOYCON") >= 0)
                return true;

            if (material.IndexOf("JOINT") >= 0 || material.IndexOf("JOYCON") >= 0)
                return true;

            if (partPos.IndexOf("DUMMY") >= 0 || partPos.IndexOf("BJ") >= 0 ||
                partPos.IndexOf("JOINT") >= 0 || partPos.IndexOf("JOYCON") >= 0)
                return true;

            return false;
        }

        private static ModelPart GetActiveAssemblyDrawingMainPart(TSM.Model model, TSD.Drawing drawing)
        {
            try
            {
                if (model == null || drawing == null)
                    return null;

                TSD.AssemblyDrawing assemblyDrawing = drawing as TSD.AssemblyDrawing;
                if (assemblyDrawing == null)
                    return null;

                Identifier assemblyId = GetIdentifierProperty(assemblyDrawing, "AssemblyIdentifier");
                if (assemblyId == null)
                    assemblyId = GetIdentifierProperty(assemblyDrawing, "ModelIdentifier");
                if (assemblyId == null)
                    return null;

                ModelObject modelObject = model.SelectModelObject(assemblyId);
                ModelPart directPart = modelObject as ModelPart;
                if (directPart != null)
                    return directPart;

                TSM.Assembly assembly = modelObject as TSM.Assembly;
                if (assembly == null)
                    return null;

                return assembly.GetMainPart() as ModelPart;
            }
            catch
            {
                return null;
            }
        }

        private static Identifier GetIdentifierProperty(object obj, string propertyName)
        {
            try
            {
                object value = GetPropertyValue(obj, propertyName);
                Identifier id = value as Identifier;
                if (id != null)
                    return id;

                ModelObject modelObject = value as ModelObject;
                return modelObject == null ? null : modelObject.Identifier;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsPartInSameAssembly(ModelPart part, ModelPart mainPart)
        {
            try
            {
                if (part == null || mainPart == null)
                    return false;

                TSM.Assembly partAssembly = part.GetAssembly();
                TSM.Assembly mainAssembly = mainPart.GetAssembly();
                if (partAssembly == null || mainAssembly == null)
                    return false;

                return SameIdentifier(partAssembly.Identifier, mainAssembly.Identifier);
            }
            catch
            {
                return false;
            }
        }

        private static bool ArePartsDirectlyBoltConnected(ModelPart first, ModelPart second)
        {
            if (first == null || second == null)
                return false;

            return PartBoltCollectionContainsDirectConnection(first, second) ||
                   PartBoltCollectionContainsDirectConnection(second, first);
        }

        private static bool PartBoltCollectionContainsDirectConnection(ModelPart owner, ModelPart other)
        {
            try
            {
                ModelObjectEnumerator bolts = owner.GetBolts();
                while (bolts != null && bolts.MoveNext())
                {
                    ModelBoltGroup boltGroup = bolts.Current as ModelBoltGroup;
                    if (boltGroup == null)
                        continue;

                    // boltGroup den tu owner.GetBolts(), nen owner da duoc Tekla xac nhan.
                    // Chi can group tham chieu truc tiep den part con lai.
                    if (BoltGroupReferencesPart(boltGroup, other))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static void AddUniquePartAndDrawing(
            ModelPart modelPart,
            DrawingPart drawingPart,
            List<ModelPart> modelParts,
            List<DrawingPart> drawingParts)
        {
            if (modelPart == null || drawingPart == null || modelParts == null || drawingParts == null)
                return;

            for (int i = 0; i < modelParts.Count; i++)
                if (SameIdentifier(modelParts[i].Identifier, modelPart.Identifier))
                    return;

            modelParts.Add(modelPart);
            drawingParts.Add(drawingPart);
        }

        private static bool IsRealPlatePart(ModelPart part)
        {
            if (IsDummyReferencePart(part))
                return false;

            string typeName = part.GetType().FullName;
            string profile = GetProfileString(part).Trim().ToUpperInvariant();
            string name = GetReportString(part, "NAME").Trim().ToUpperInvariant();

            if (typeName.IndexOf("ContourPlate") >= 0)
                return true;

            if (name.IndexOf("PLATE") >= 0)
                return true;

            if (profile.StartsWith("PL") ||
                profile.StartsWith("PLT") ||
                profile.StartsWith("FB") ||
                profile.StartsWith("FL") ||
                profile.IndexOf("PLATE") >= 0)
                return true;

            return false;
        }

        private static string GetProfileString(ModelPart part)
        {
            if (part == null)
                return "";

            try
            {
                object profileObj = GetPropertyValue(part, "Profile");
                object profileString = GetPropertyValue(profileObj, "ProfileString");
                if (profileString != null)
                    return profileString.ToString();
            }
            catch
            {
            }

            string value = "";
            try
            {
                if (part.GetReportProperty("PROFILE", ref value) && !string.IsNullOrEmpty(value))
                    return value;
            }
            catch
            {
            }

            return "";
        }

        private static string GetReportString(ModelObject obj, string reportName)
        {
            if (obj == null)
                return "";

            try
            {
                string s = "";
                obj.GetReportProperty(reportName, ref s);
                if (s == null)
                    return "";
                return s.Trim();
            }
            catch
            {
                return "";
            }
        }

        private static object GetPropertyValue(object obj, string name)
        {
            try
            {
                if (obj == null || string.IsNullOrEmpty(name))
                    return null;

                PropertyInfo p = obj.GetType().GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (p == null || !p.CanRead || p.GetIndexParameters().Length > 0)
                    return null;

                return p.GetValue(obj, null);
            }
            catch
            {
                return null;
            }
        }

        private static bool SameIdentifier(Identifier a, Identifier b)
        {
            if (a == null || b == null)
                return false;

            try
            {
                return a.ID == b.ID;
            }
            catch
            {
                return a.ToString() == b.ToString();
            }
        }


        private static Point GetHolePointWithPhiGap(ModelPart plate, ModelPart neighbor, Point holeCenter, Vector direction)
        {
            try
            {
                if (holeCenter == null || direction == null)
                    return holeCenter;

                double gap = GetHoleCenterDimGapByPhi(plate, holeCenter);
                if (gap <= 0.0)
                    gap = GetHoleCenterDimGapByPhi(neighbor, holeCenter);

                if (gap <= 0.0)
                    return new Point(holeCenter.X, holeCenter.Y, 0);

                double x = holeCenter.X;
                double y = holeCenter.Y;

                if (Math.Abs(direction.X) >= Math.Abs(direction.Y))
                {
                    if (direction.X < 0.0)
                        x -= gap;
                    else if (direction.X > 0.0)
                        x += gap;
                }
                else
                {
                    if (direction.Y < 0.0)
                        y -= gap;
                    else if (direction.Y > 0.0)
                        y += gap;
                }

                return new Point(x, y, 0);
            }
            catch
            {
                return holeCenter;
            }
        }

        private static double GetHoleCenterDimGapByPhi(ModelPart part, Point holeCenter)
        {
            try
            {
                if (part == null || holeCenter == null)
                    return 0.0;

                ModelObjectEnumerator bolts = part.GetBolts();

                while (bolts != null && bolts.MoveNext())
                {
                    ModelBoltGroup bg = bolts.Current as ModelBoltGroup;
                    if (bg == null || bg.BoltPositions == null)
                        continue;

                    if (!IsAllowedDimensionBoltGroup(bg))
                        continue;

                    foreach (object obj in bg.BoltPositions)
                    {
                        Point p = obj as Point;
                        if (p == null)
                            continue;

                        if (Math.Abs(p.X - holeCenter.X) <= 1.0 &&
                            Math.Abs(p.Y - holeCenter.Y) <= 1.0)
                        {
                            double d = GetBoltGroupPhiForDimGap(bg);
                            if (d > 0.0)
                                return d;
                        }
                    }
                }
            }
            catch
            {
            }

            return 0.0;
        }

        private static double GetBoltGroupPhiForDimGap(ModelBoltGroup bg)
        {
            if (bg == null)
                return 0.0;

            // ƯU TIÊN PHI LỖ THỰC trước. M/BoltSize chỉ là fallback cuối cùng.
            double v = GetReportDouble(bg, "HOLE_DIAMETER");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "BOLT_HOLE_DIAMETER");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "HOLE_SIZE");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "HOLE_DIAM");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "BOLT_HOLE_SIZE");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "HoleDiameter");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "HoleSize");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "BoltHoleDiameter");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "BoltHoleSize");
            if (v > 0.0 && v < 500.0) return v;

            // Nếu Tekla không trả trực tiếp phi lỗ, thử tính phi lỗ = M + tolerance.
            double bolt = GetReportDouble(bg, "BOLT_DIAMETER");
            if (!(bolt > 0.0 && bolt < 500.0))
                bolt = GetDoublePropertyByReflection(bg, "BoltSize");
            if (!(bolt > 0.0 && bolt < 500.0))
                bolt = GetReportDouble(bg, "BOLT_SIZE");
            if (!(bolt > 0.0 && bolt < 500.0))
                bolt = GetReportDouble(bg, "DIAMETER");
            if (!(bolt > 0.0 && bolt < 500.0))
                bolt = GetDoublePropertyByReflection(bg, "Diameter");

            double tol = GetReportDouble(bg, "HOLE_TOLERANCE");
            if (!(tol > 0.0 && tol < 100.0))
                tol = GetReportDouble(bg, "BOLT_HOLE_TOLERANCE");
            if (!(tol > 0.0 && tol < 100.0))
                tol = GetReportDouble(bg, "TOLERANCE");
            if (!(tol > 0.0 && tol < 100.0))
                tol = GetDoublePropertyByReflection(bg, "Tolerance");
            if (!(tol > 0.0 && tol < 100.0))
                tol = GetDoublePropertyByReflection(bg, "HoleTolerance");

            if (bolt > 0.0 && bolt < 500.0 && tol > 0.0 && tol < 100.0)
                return bolt + tol;

            // Fallback cuối cùng mới dùng M/BoltSize nếu không đọc được phi lỗ thật.
            if (bolt > 0.0 && bolt < 500.0)
                return bolt;

            return 0.0;
        }

        private static double GetReportDouble(ModelBoltGroup bg, string propertyName)
        {
            try
            {
                if (bg == null || string.IsNullOrEmpty(propertyName))
                    return 0.0;

                double value = 0.0;
                bg.GetReportProperty(propertyName, ref value);
                return value;
            }
            catch
            {
                return 0.0;
            }
        }

        private static double GetDoublePropertyByReflection(object obj, string propertyName)
        {
            try
            {
                if (obj == null || string.IsNullOrEmpty(propertyName))
                    return 0.0;

                PropertyInfo prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (prop == null || !prop.CanRead)
                    return 0.0;

                object value = prop.GetValue(obj, null);
                if (value == null)
                    return 0.0;

                if (value is double)
                    return (double)value;

                if (value is int)
                    return Convert.ToDouble((int)value);

                if (value is float)
                    return Convert.ToDouble((float)value);

                double result;
                if (double.TryParse(
                    value.ToString().Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out result))
                    return result;
            }
            catch
            {
            }

            return 0.0;
        }

        private static void AddUniquePoint2D(List<Point> list, Point p, double tol)
        {
            if (list == null || p == null)
                return;

            for (int i = 0; i < list.Count; i++)
            {
                if (Distance2D(list[i], p) <= tol)
                    return;
            }

            list.Add(p);
        }

        private static double Distance2D(Point a, Point b)
        {
            if (a == null || b == null)
                return 999999999.0;

            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool CreateDimChain(
            TSD.StraightDimensionSetHandler handler,
            TSD.View view,
            Point[] points,
            Vector direction,
            double distance,
            string attributeName)
        {
            try
            {
                if (handler == null || view == null || points == null || points.Length < 2)
                    return false;

                PointList list = new PointList();

                for (int i = 0; i < points.Length; i++)
                {
                    Point p = points[i];
                    if (p == null)
                        continue;

                    bool duplicate = false;
                    foreach (Point old in list)
                    {
                        if (Distance2D(old, p) <= POINT_DUP_TOL)
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (!duplicate)
                        list.Add(new Point(p.X, p.Y, 0));
                }

                if (list.Count < 2)
                    return false;

                TSD.StraightDimensionSet dim =
                    handler.CreateDimensionSet(view, list, direction, distance);

                if (dim != null && !string.IsNullOrEmpty(attributeName))
                    TryApplyStraightDimAttributes(dim, attributeName);

                return dim != null;
            }
            catch
            {
                // Không để 1 dim lỗi làm dừng toàn bộ Slot03.
                return false;
            }
        }

        private static void TryApplyStraightDimAttributes(
            TSD.StraightDimensionSet dim,
            string attributeName)
        {
            try
            {
                if (dim == null || string.IsNullOrEmpty(attributeName))
                    return;

                object attr = dim.Attributes;
                if (attr == null)
                    return;

                MethodInfo loadMethod = attr.GetType().GetMethod(
                    "LoadAttributes",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(string) },
                    null);

                if (loadMethod == null)
                    return;

                loadMethod.Invoke(attr, new object[] { attributeName });
                dim.Modify();
            }
            catch
            {
            }
        }


        private const double MAIN_PLATE_HORIZONTAL_REF_TO_HOLE_TIER = 220.0;
        private const double MAIN_PLATE_HOLE_SIDE_TIER = 205.0;
        private const double MAIN_PLATE_TOTAL_SIDE_TIER = 287.0;

        private static int CreateSectionMainPlateOnlyDims(
            TSM.Model model,
            TSD.DrawingHandler dh,
            TSD.View view,
            ModelPart mainBeam,
            List<ModelPart> plates)
        {
            int count = 0;

            if (model == null || dh == null || view == null || mainBeam == null ||
                plates == null || plates.Count == 0)
                return count;

            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TSM.TransformationPlane(view.DisplayCoordinateSystem));

                Bounds2D mainBox = GetPartBounds2D(mainBeam);
                if (!mainBox.Valid)
                    return count;

                Point mainRef = new Point(
                    (mainBox.MinX + mainBox.MaxX) / 2.0,
                    mainBox.MaxY,
                    0);

                MainPlateSideGroup left = BuildMainPlateSideGroup(
                    model,
                    view,
                    true,
                    mainBox,
                    mainBeam,
                    plates);

                MainPlateSideGroup right = BuildMainPlateSideGroup(
                    model,
                    view,
                    false,
                    mainBox,
                    mainBeam,
                    plates);

                TSD.StraightDimensionSetHandler handler =
                    new TSD.StraightDimensionSetHandler();

                Slot03TierManager tierManager = new Slot03TierManager();

                // 1. DIM NGANG: Ref main -> lỗ thực Plate.
                List<Point> horizontalChain = new List<Point>();
                if (left != null && left.Valid && left.Holes != null && left.Holes.Count > 0)
                {
                    Point h = PickHorizontalHole(left.Holes, true);
                    if (h != null)
                        horizontalChain.Add(GetHolePointWithPhiGap(left.Plate, mainBeam, h, new Vector(0, 1, 0)));
                }

                horizontalChain.Add(mainRef);

                if (right != null && right.Valid && right.Holes != null && right.Holes.Count > 0)
                {
                    Point h = PickHorizontalHole(right.Holes, false);
                    if (h != null)
                        horizontalChain.Add(GetHolePointWithPhiGap(right.Plate, mainBeam, h, new Vector(0, 1, 0)));
                }

                if (horizontalChain.Count >= 2)
                {
                    if (CreateDimChain(
                        handler,
                        view,
                        horizontalChain.ToArray(),
                        new Vector(0, 1, 0),
                        tierManager.TakeTopDistance(horizontalChain[0], mainBox),
                        "GEO_DIMENSION"))
                    {
                        count++;
                    }
                }

                // 2 + 3. Cụm trái/phải: Mép trên dầm -> lỗ -> lỗ -> mép dưới dầm, và mép trên -> mép dưới.
                if (left != null && left.Valid)
                {
                    count += CreateMainPlateSideDims(handler, view, mainBeam, mainBox, left, new Vector(-1, 0, 0), tierManager);
                }

                if (right != null && right.Valid)
                {
                    count += CreateMainPlateSideDims(handler, view, mainBeam, mainBox, right, new Vector(1, 0, 0), tierManager);
                }
            }
            catch (Exception ex)
            {
                Msg("Slot03 Main+Plate ERROR:\n" + ex.Message);
            }
            finally
            {
                try { model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane); } catch { }
            }

            return count;
        }

        private class MainPlateSideGroup
        {
            public bool Valid;
            public bool IsLeft;
            public ModelPart Plate;
            public Bounds2D PlateBox;
            public List<Point> Holes;
            public double MainEdgeX;
        }

        private static MainPlateSideGroup BuildMainPlateSideGroup(
            TSM.Model model,
            TSD.View view,
            bool isLeft,
            Bounds2D mainBox,
            ModelPart mainBeam,
            List<ModelPart> plates)
        {
            MainPlateSideGroup g = new MainPlateSideGroup();
            g.Valid = false;
            g.IsLeft = isLeft;
            g.Holes = new List<Point>();

            double mainCenterX = (mainBox.MinX + mainBox.MaxX) / 2.0;
            double bestDist = 999999999.0;
            ModelPart bestPlate = null;
            Bounds2D bestBox = new Bounds2D();
            bestBox.Valid = false;

            for (int i = 0; i < plates.Count; i++)
            {
                Bounds2D b = GetPartBounds2D(plates[i]);
                if (!b.Valid)
                    continue;

                double cx = (b.MinX + b.MaxX) / 2.0;
                bool sideOK = isLeft ? cx < mainCenterX : cx > mainCenterX;
                if (!sideOK)
                    continue;

                double innerX = isLeft ? b.MaxX : b.MinX;
                double mainEdgeX = isLeft ? mainBox.MinX : mainBox.MaxX;
                double d = Math.Abs(innerX - mainEdgeX);

                if (d < bestDist)
                {
                    bestDist = d;
                    bestPlate = plates[i];
                    bestBox = b;
                }
            }

            if (bestPlate == null || !bestBox.Valid)
                return g;

            List<Point> holes = GetAllowedBoltCentersFromPlate(bestPlate, bestBox);
            holes = FilterHolesBySideAndMain(holes, bestBox, isLeft, mainCenterX);

            if (holes == null || holes.Count == 0)
                return g;

            g.Plate = bestPlate;
            g.PlateBox = bestBox;
            g.Holes = holes;
            g.MainEdgeX = isLeft ? mainBox.MinX : mainBox.MaxX;
            g.Valid = true;
            return g;
        }

        private static List<Point> GetAllowedBoltCentersFromPlate(ModelPart plate, Bounds2D plateBox)
        {
            List<Point> result = new List<Point>();

            try
            {
                if (plate == null || !plateBox.Valid)
                    return result;

                ModelObjectEnumerator bolts = plate.GetBolts();
                while (bolts != null && bolts.MoveNext())
                {
                    ModelBoltGroup bg = bolts.Current as ModelBoltGroup;
                    if (bg == null || bg.BoltPositions == null)
                        continue;

                    if (!IsAllowedDimensionBoltGroup(bg))
                        continue;

                    foreach (object obj in bg.BoltPositions)
                    {
                        Point p = obj as Point;
                        if (p == null)
                            continue;

                        if (!PointInsideBounds(p, plateBox, 25.0))
                            continue;

                        AddUniquePoint2D(result, new Point(p.X, p.Y, 0), POINT_DUP_TOL);
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private static List<Point> FilterHolesBySideAndMain(
            List<Point> holes,
            Bounds2D plateBox,
            bool isLeft,
            double mainCenterX)
        {
            List<Point> result = new List<Point>();

            if (holes == null)
                return result;

            for (int i = 0; i < holes.Count; i++)
            {
                Point p = holes[i];
                if (p == null)
                    continue;

                if (p.X < plateBox.MinX - 25.0 || p.X > plateBox.MaxX + 25.0 ||
                    p.Y < plateBox.MinY - 25.0 || p.Y > plateBox.MaxY + 25.0)
                    continue;

                bool sideOK = isLeft ? p.X < mainCenterX : p.X > mainCenterX;
                if (!sideOK)
                    continue;

                AddUniquePoint2D(result, p, POINT_DUP_TOL);
            }

            result.Sort(delegate (Point a, Point b)
            {
                int cx = a.X.CompareTo(b.X);
                if (cx != 0) return cx;
                return a.Y.CompareTo(b.Y);
            });

            return result;
        }

        private static Point PickHorizontalHole(List<Point> holes, bool isLeft)
        {
            if (holes == null || holes.Count == 0)
                return null;

            Point best = null;
            double bestY = -999999999.0;

            for (int i = 0; i < holes.Count; i++)
            {
                Point p = holes[i];
                if (p == null)
                    continue;

                // Theo dump: chain ngang bắt vào lỗ phía trên của cụm lỗ thực.
                if (best == null || p.Y > bestY)
                {
                    best = p;
                    bestY = p.Y;
                }
            }

            if (best == null)
                return null;

            return new Point(best.X, best.Y, 0);
        }

        private static int CreateMainPlateSideDims(
            TSD.StraightDimensionSetHandler handler,
            TSD.View view,
            ModelPart mainBeam,
            Bounds2D mainBox,
            MainPlateSideGroup g,
            Vector direction,
            Slot03TierManager tierManager)
        {
            int count = 0;

            if (handler == null || view == null || g == null || !g.Valid || g.Holes == null || g.Holes.Count == 0)
                return count;

            List<Point> sortedHoles = new List<Point>();
            for (int i = 0; i < g.Holes.Count; i++)
                if (g.Holes[i] != null)
                    sortedHoles.Add(g.Holes[i]);

            sortedHoles.Sort(delegate (Point a, Point b)
            {
                return a.Y.CompareTo(b.Y);
            });

            Point mainBottom = new Point(g.MainEdgeX, mainBox.MinY, 0);
            Point mainTop = new Point(g.MainEdgeX, mainBox.MaxY, 0);

            List<Point> holeChain = new List<Point>();
            // Thứ tự theo yêu cầu/dump: Mép trên dầm -> lỗ -> lỗ -> mép dưới dầm.
            holeChain.Add(mainTop);

            for (int i = sortedHoles.Count - 1; i >= 0; i--)
            {
                Point h = sortedHoles[i];
                holeChain.Add(GetHolePointWithPhiGap(g.Plate, mainBeam, h, direction));
            }

            holeChain.Add(mainBottom);

            if (CreateDimChain(
                handler,
                view,
                holeChain.ToArray(),
                direction,
                (direction.X < 0.0 ? tierManager.TakeLeftDistance(holeChain[0], g.PlateBox) : tierManager.TakeRightDistance(holeChain[0], g.PlateBox)),
                "GEO_DIMENSION"))
            {
                count++;
            }

            if (CreateDimChain(
                handler,
                view,
                new Point[] { mainTop, mainBottom },
                direction,
                (direction.X < 0.0 ? tierManager.TakeLeftDistance(mainTop, g.PlateBox) : tierManager.TakeRightDistance(mainTop, g.PlateBox)),
                "GEO_DIMENSION"))
            {
                count++;
            }

            return count;
        }

        private static bool IsAllowedDimensionBoltGroup(ModelBoltGroup bg)
        {
            try
            {
                if (bg == null)
                    return false;

                object boltValue = GetPropertyValue(bg, "Bolt");
                if (boltValue is bool)
                {
                    if (!(bool)boltValue)
                        return false;
                }

                string type = GetReportString(bg, "TYPE").ToUpperInvariant();
                string name = GetReportString(bg, "NAME").ToUpperInvariant();
                string std = "";
                try
                {
                    object v = GetPropertyValue(bg, "BoltStandard");
                    if (v != null)
                        std = v.ToString().ToUpperInvariant();
                }
                catch
                {
                    std = "";
                }

                // Theo dump: lỗ không dim là Galva_Bolt và Bolt = false.
                if (type.IndexOf("GALVA") >= 0 || name.IndexOf("GALVA") >= 0 || std.IndexOf("GALVA") >= 0)
                    return false;

                return true;
            }
            catch
            {
                return true;
            }
        }

        private static void Msg(string text)
        {
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    text,
                    "PHU Slot03 Neighbor Plate Section Dim",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
            }
            catch
            {
            }
        }
    }
}
