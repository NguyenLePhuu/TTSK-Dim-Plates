// Sắp lại khoảng cách DIM có sẵn theo view, hướng nhìn và tầng; giữ chuỗi và side mark từng đoạn.
// LUỒNG: đọc/snapshot → giải vị trí tầng → sửa Distance trên StraightDimension con → kiểm lại.
// THAM SỐ: khoảng cách giấy quy đổi theo scale; SHARED_TIER_PAPER_* là mm giấy, INTERNAL_DIM_EDGE_TOL là mm view.
// LƯU Ý: không tạo/xóa/tách DIM; set.Modify() có thể làm mất side mark nên không thay thế bộ ghi segment.
// TRA CỨU: PROJECT_MAP.md ở gốc repo; chỉ mục kiểu/hàm và điểm chỉnh thông số.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.IO;
using System.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
using Model = Tekla.Structures.Model;
using Revision = Tekla.Technology.Akit.UserScript.PHU_DimensionRevision;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;

namespace TTSK_AutoDim_Plates
{
    // GIỮ NGUYÊN CHUỖI: chỉ sửa Distance trên DIM con; không tạo/xóa/tách set.
    // Tách tầng theo từng view và phía hiển thị = OffsetDirection × dấu Distance.
    // Tekla đo Distance từ DimensionPoint đầu; đổi gốc sang tâm/điểm ngoài có thể làm lệch tầng.
    // Dùng chung bộ giải khoảng cách và bộ ghi giữ side mark cho nhóm một/nhiều set.
    public static class PHU_DimSpacingNormalize
    {
        // TEST PORT TỪ FILE DIM PLATE:
        // Anchor A/B/C/D chỉ dùng làm GỐC TẦNG cho từng group.
        // Nếu dựng được anchor: tier1 = anchor + spacing, tier2 = tier1 + spacing, tier3 = tier2 + spacing...
        // Nếu không dựng được anchor an toàn thì tự fallback về logic DimSpacing cũ.
        private const bool USE_ANCHOR_ABCD_AS_TIER_BASE = true;

        // INTERNAL DIM: DIM nằm trong lòng thanh không được tham gia hệ tầng ngoài.
        // Geometry classifies internal; overlapping internal spans are planned together.
        private const bool HANDLE_INTERNAL_DIMS_SEPARATELY = true;
        private const double INTERNAL_DIM_EDGE_TOL = 1.0;
        private const double SHARED_TIER_PAPER_TOLERANCE = 1.0;
        private const double SHARED_TIER_PAPER_CLEARANCE = 2.0;

        private class ViewAnchorInfo
        {
            public bool IsValid;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
        }

        private static Dictionary<int, ViewAnchorInfo> ViewAnchorMap =
            new Dictionary<int, ViewAnchorInfo>();

        public class Result
        {
            public int FoundCount;
            public int GroupCount;
            public int ChangedCount;
            public int FailedCount;
            public int SkippedCount;
            public double Spacing;
            public string Scope;
            public bool IsPreview;
            public string FailureDetail;

            public string ToDisplayText(bool apply)
            {
                return (IsPreview ? "PREVIEW\r\n" : "APPLY\r\n")
                    + "Scope: "
                    + Scope
                    + "\r\n"
                    + "Spacing: "
                    + Spacing.ToString("0.###")
                    + " mm\r\n\r\n"
                    + "Found dim sets: "
                    + FoundCount
                    + "\r\n"
                    + "Groups: "
                    + GroupCount
                    + "\r\n"
                    + "Changed: "
                    + ChangedCount
                    + "\r\n"
                    + "Skipped: "
                    + SkippedCount
                    + "\r\n"
                    + "Failed: "
                    + FailedCount
                    + (string.IsNullOrEmpty(FailureDetail) ? "" : "\r\n" + FailureDetail);
            }
        }

        private class DimSetItem
        {
            public object DimSet;
            public Tekla.Structures.Drawing.View View;
            public double Distance;
            public double OriginalDistance;
            public double OriginalVisualLineLevel;
            public int OriginalTierIndex;
            public int LayoutTierIndex;
            public int CollectionIndex;
            public string StableId;
            public double AbsDistance;
            public double Sign;
            public SimpleVector Offset;
            public SimpleVector VisualOffset;
            public double OffsetAngle;
            public double LineAngle;
            public string GroupKey;
            public string SideKey;
            public string KindKey;
            public bool IsInternal;
            public bool IsLocal;
            public List<Point> InternalFeet;
            public PHU_InternalDimLayout.Body InternalBody;
        }

        private class DimDistanceChange
        {
            public DimSetItem Item;
            public double OriginalDistance;
            public double NewDistance;
            public int OriginalTierIndex;
            public string OriginalStableId;
            public List<ChildDistanceSnapshot> Children;
        }

        private class ChildDistanceSnapshot
        {
            public StraightDimension Child;
            public Point Start;
            public Point End;
            public Vector Direction;
            public double Distance;
            public double TargetDistance;
            public StraightDimension.StraightDimensionAttributes Attributes;
            public object SetAttributes;
        }

        public static Result Run(double spacing, string scope)
        {
            return Run(spacing, scope, true);
        }

        public static Result Run(double spacing, string scope, bool apply)
        {
            // Public Open API returns inherited set attributes for some individually
            // formatted children. Protect the real native properties around the whole
            // distance operation; never infer a child's colour from its parent.
            return apply ? RunWithNativeAttributes(spacing, scope) : RunCore(spacing, scope, false);
        }

        private sealed class NativeAttributeSnapshot
        {
            public int Id;
            public Dictionary<string, object> Properties;
        }

        private static string EncodeNative(string value)
        { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? "")); }
        private static string DecodeNative(string value)
        { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
        private static string NativeDrawingId(Drawing drawing)
        { return ((Tekla.Structures.Identifier)GetPropertyOrFieldValue(drawing, "Identifier")).GUID.ToString(); }

        private static Result RunWithNativeAttributes(double spacing, string scope)
        {
            if (!IsFinite(spacing) || spacing <= 0) throw new ArgumentOutOfRangeException("spacing");
            var model = new Model.Model();
            var drawing = new DrawingHandler().GetActiveDrawing();
            if (!model.GetConnectionStatus() || drawing == null) throw new InvalidOperationException("Chưa mở bản vẽ Tekla.");
            // Share the native-properties lock with Revision so neither command
            // overwrites an attribute being changed by the other command.
            using (var mutex = new Mutex(false, "Local\\TTSK_RevisionNativeColor"))
            {
                bool locked;
                try { locked = mutex.WaitOne(0); } catch (AbandonedMutexException) { locked = true; }
                if (!locked) throw new InvalidOperationException("Tekla đang xử lý thuộc tính DIM; hãy chờ thao tác hiện tại hoàn tất.");
                try
                {
                    string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "TTSK Dim Plates", "DimSpacingJobs");
                    Directory.CreateDirectory(root);
                    string pending = Path.Combine(root, "pending.txt");
                    if (File.Exists(pending))
                    {
                        string previous = File.ReadAllText(pending);
                        if (!File.Exists(Path.Combine(previous, "result.txt")))
                            throw new InvalidOperationException("Lần Dimspacing trước chưa kết thúc. Log: " + previous);
                    }
                    string folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(folder);
                    string request = Path.Combine(folder, "request.txt"), response = Path.Combine(folder, "result.txt");
                    File.WriteAllLines(request, new[] { EncodeNative(model.GetInfo().ModelPath), NativeDrawingId(drawing),
                        spacing.ToString("R", CultureInfo.InvariantCulture), EncodeNative(scope),
                        DateTime.UtcNow.AddSeconds(60).Ticks.ToString(CultureInfo.InvariantCulture) }, Encoding.UTF8);
                    string assembly = typeof(PHU_DimSpacingNormalize).Assembly.Location;
                    string hash;
                    using (var stream = File.OpenRead(assembly)) using (var sha = SHA256.Create())
                        hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                    string writer = Path.Combine(root, "writer-" + hash + ".dll");
                    if (!File.Exists(writer)) File.Copy(assembly, writer);
                    string source = Path.Combine(folder, "Spacing.cs");
                    File.WriteAllText(source,
                        "#pragma warning disable 1633\n#pragma reference \"Tekla.Macros.Runtime\"\n" +
                        "using System;using System.IO;using System.Text;using System.Reflection;namespace UserMacros {public sealed class Macro {" +
                        "[Tekla.Macros.Runtime.MacroEntryPointAttribute()]public static void Run(Tekla.Macros.Runtime.IMacroRuntime runtime){" +
                        "string request=Encoding.UTF8.GetString(Convert.FromBase64String(\"" + EncodeNative(request) + "\"));try{" +
                        "Assembly.LoadFile(Encoding.UTF8.GetString(Convert.FromBase64String(\"" + EncodeNative(writer) + "\")))" +
                        ".GetType(\"TTSK_AutoDim_Plates.PHU_DimSpacingNormalize\").GetMethod(\"RunNativeSpacingRequest\").Invoke(null,new object[]{request});" +
                        "}catch(Exception ex){File.WriteAllText(Path.Combine(Path.GetDirectoryName(request),\"result.txt\"),\"ERROR: \"+ex.GetBaseException().Message);}}}}",
                        Encoding.UTF8);
                    File.WriteAllText(pending, folder);
                    try { Model.Operations.Operation.RunMacro(source); }
                    catch (Exception ex)
                    {
                        File.WriteAllText(Path.Combine(folder, "cancel"), "");
                        throw new InvalidOperationException("Không khởi chạy được bộ bảo toàn thuộc tính DIM. Log: " + folder, ex);
                    }
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    while (!File.Exists(response) && timer.ElapsedMilliseconds < 65000) Thread.Sleep(50);
                    if (!File.Exists(response))
                    {
                        File.WriteAllText(Path.Combine(folder, "cancel"), "");
                        throw new TimeoutException("Tekla chưa hoàn tất Dimspacing; yêu cầu đã hủy. Log: " + folder);
                    }
                    string[] rows = File.ReadAllLines(response, Encoding.UTF8);
                    if (rows.Length != 7 || rows[0] != "DONE") throw new InvalidOperationException(String.Join("\n", rows));
                    return new Result { FoundCount = Int32.Parse(rows[1]), GroupCount = Int32.Parse(rows[2]),
                        ChangedCount = Int32.Parse(rows[3]), FailedCount = Int32.Parse(rows[4]), SkippedCount = Int32.Parse(rows[5]),
                        FailureDetail = DecodeNative(rows[6]), Spacing = spacing, Scope = string.IsNullOrWhiteSpace(scope) ? "Toàn bộ bản vẽ" : scope };
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        // Runs on Tekla's macro worker. Only native property access is dispatched
        // to the main thread by the existing Revision bridge; Open API stays here.
        public static void RunNativeSpacingRequest(string request)
        {
            string folder = Path.GetDirectoryName(request), response = Path.Combine(folder, "result.txt");
            var distances = new List<DimDistanceChange>();
            var native = new List<NativeAttributeSnapshot>();
            Drawing drawing = null;
            bool writesStarted = false;
            string[] output;
            try
            {
                if (!String.Equals(System.Diagnostics.Process.GetCurrentProcess().ProcessName, "TeklaStructures", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Native spacing must run inside Tekla.");
                var rows = File.ReadAllLines(request, Encoding.UTF8);
                long deadline = Int64.Parse(rows[4], CultureInfo.InvariantCulture);
                Action check = () => { if (File.Exists(Path.Combine(folder, "cancel")) || DateTime.UtcNow.Ticks > deadline)
                    throw new OperationCanceledException("Yêu cầu Dimspacing đã hủy trước khi hoàn tất."); };
                check();
                var model = new Model.Model(); drawing = new DrawingHandler().GetActiveDrawing();
                if (!model.GetConnectionStatus() || drawing == null || NativeDrawingId(drawing) != rows[1]
                    || !String.Equals(model.GetInfo().ModelPath, DecodeNative(rows[0]), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Model hoặc bản vẽ đã thay đổi; chưa ghi DIM.");
                double spacing = Double.Parse(rows[2], CultureInfo.InvariantCulture);
                string scope = DecodeNative(rows[3]);
                var items = CollectStraightDimensionSets(drawing.GetSheet(), scope);
                foreach (var item in items)
                {
                    var change = new DimDistanceChange { Item = item, OriginalDistance = item.Distance, NewDistance = item.Distance };
                    if (!TryCaptureChildDistances(change)) throw new InvalidOperationException("Không đọc được toàn bộ đoạn DIM trước khi ghi.");
                    foreach (var child in change.Children)
                    {
                        child.Attributes = Revision.CopyValue(child.Attributes);
                        child.SetAttributes = Revision.CopyValue((AttributesBase)child.SetAttributes);
                        native.Add(new NativeAttributeSnapshot { Id = ((Tekla.Structures.Identifier)GetPropertyOrFieldValue(child.Child, "Identifier")).ID });
                    }
                    distances.Add(change);
                }
                NativeProperties(native, true, false);
                check(); writesStarted = true;
                Result result = RunCore(spacing, scope, true);
                if (result.FailedCount != 0) throw new InvalidOperationException(result.ToDisplayText(true));
                foreach (var change in distances)
                    foreach (var child in change.Children)
                    {
                        if (!child.Child.Select() || !IsFinite(child.Child.Distance))
                            throw new InvalidOperationException("Không đọc lại được khoảng cách DIM đã bố trí.");
                        child.TargetDistance = child.Child.Distance;
                    }
                check();
                // Restore only properties actually changed by Open API round-tripping.
                // Values come from each exact child, never from a parent, colour rule,
                // mark name, or a hard-coded red/black value.
                NativeProperties(native, false, true);
                if (!TryCommitDrawing(drawing)) throw new InvalidOperationException("Không commit được thuộc tính DIM đã bảo toàn.");
                NativeProperties(native, false, false);
                foreach (var change in distances)
                {
                    var set = (StraightDimensionSet)change.Item.DimSet;
                    if (!set.Select()) throw new InvalidOperationException("Không đọc lại được chuỗi DIM.");
                    var children = set.GetObjects(); int count = 0;
                    while (children.MoveNext())
                    {
                        var actual = children.Current as StraightDimension;
                        var saved = actual == null ? null : change.Children.Find(c => c.Child.IsSameDatabaseObject(actual));
                        if (saved == null || !SamePoint(actual.StartPoint, saved.Start) || !SamePoint(actual.EndPoint, saved.End)
                            || !SamePoint(actual.UpDirection, saved.Direction) || !saved.Attributes.IsEqual(actual.Attributes)
                            || !IsFinite(actual.Distance) || Math.Abs(actual.Distance - saved.TargetDistance) > .01)
                            throw new InvalidOperationException("Chân đo hoặc nhãn DIM đã thay đổi ngoài khoảng cách.");
                        count++;
                    }
                    if (count != change.Children.Count) throw new InvalidOperationException("Số đoạn DIM đã thay đổi.");
                }
                check();
                output = new[] { "DONE", result.FoundCount.ToString(), result.GroupCount.ToString(), result.ChangedCount.ToString(),
                    result.FailedCount.ToString(), result.SkippedCount.ToString(), EncodeNative(result.FailureDetail) };
            }
            catch (Exception ex)
            {
                bool restored = true;
                if (writesStarted)
                {
                    try
                    {
                        foreach (var change in distances) restored &= WriteChildDistances(change, true);
                        NativeProperties(native, false, true);
                        restored &= TryCommitDrawing(drawing);
                        NativeProperties(native, false, false);
                        foreach (var change in distances) restored &= VerifyChildDistances(change, true);
                    }
                    catch { restored = false; }
                }
                output = new[] { "ERROR: " + ex.GetBaseException().Message + (!writesStarted ? " Chưa ghi DIM." : restored
                    ? " Đã phục hồi khoảng cách và thuộc tính gốc." : " Chưa xác minh được phục hồi; kiểm tra bản vẽ trước khi lưu.") };
            }
            File.WriteAllLines(response + ".tmp", output, Encoding.UTF8);
            File.Move(response + ".tmp", response);
        }

        private static void NativeProperties(List<NativeAttributeSnapshot> snapshots, bool capture, bool restore)
        {
            if (snapshots.Count == 0) return;
            Revision.WithNativeColorFactory((factory, create, update) =>
            {
                foreach (var snapshot in snapshots)
                {
                    object proxy = create.Invoke(factory, new object[] { snapshot.Id, String.Empty });
                    if (proxy == null) throw new InvalidOperationException("Không đọc được thuộc tính riêng của đoạn DIM " + snapshot.Id);
                    try
                    {
                        var type = proxy.GetType(); var get = type.GetMethod("GetProperty");
                        if (capture)
                        {
                            snapshot.Properties = new Dictionary<string, object>(StringComparer.Ordinal);
                            foreach (string name in (IEnumerable)type.GetMethod("GetPropertyNames").Invoke(proxy, null))
                            {
                                object value = get.Invoke(proxy, new object[] { name });
                                if (value != null && !(value is string) && !value.GetType().IsPrimitive && !value.GetType().IsEnum)
                                    throw new InvalidOperationException("Unsupported native DIM property: " + name);
                                snapshot.Properties.Add(name, value);
                            }
                            if (!snapshot.Properties.ContainsKey("proFontColor")) throw new InvalidOperationException("Không đọc được thuộc tính chữ riêng của DIM.");
                        }
                        else
                        {
                            bool changed = false;
                            foreach (var pair in snapshot.Properties)
                            {
                                object actual = get.Invoke(proxy, new object[] { pair.Key });
                                if (Object.Equals(actual, pair.Value)) continue;
                                if (!restore) throw new InvalidOperationException("Thuộc tính riêng chưa được bảo toàn: DIM " + snapshot.Id + "/" + pair.Key);
                                if (!(bool)type.GetMethod("SetProperty").Invoke(proxy, new[] { (object)pair.Key, pair.Value }))
                                    throw new InvalidOperationException("Không phục hồi được thuộc tính DIM " + pair.Key);
                                changed = true;
                            }
                            if (changed && !(bool)update.Invoke(factory, new[] { proxy })) throw new InvalidOperationException("Không cập nhật được thuộc tính DIM gốc.");
                        }
                    }
                    finally { ((IDisposable)proxy).Dispose(); }
                }
            });
        }

        private static Result RunCore(double spacing, string scope, bool apply)
        {
            Result result = new Result();
            result.Spacing = spacing;
            result.IsPreview = !apply;
            result.Scope = string.IsNullOrWhiteSpace(scope) ? "Toàn bộ bản vẽ" : scope;

            if (!IsFinite(spacing) || spacing <= 0.0)
                throw new Exception("Khoảng cách dim phải lớn hơn 0.");

            DrawingHandler dh = new DrawingHandler();
            if (!dh.GetConnectionStatus())
                throw new Exception("Không kết nối được Tekla DrawingHandler.");

            Drawing drawing = dh.GetActiveDrawing();
            if (drawing == null)
                throw new Exception("Chưa mở bản vẽ Tekla active.");

            ContainerView sheet = drawing.GetSheet();
            if (sheet == null)
                throw new Exception("Không lấy được sheet của bản vẽ.");

            List<DimSetItem> items = CollectStraightDimensionSets(sheet, result.Scope);
            result.FoundCount = items.Count;

            // Dựng anchor A/B/C/D theo từng view từ toàn bộ chân DIM hiện có.
            // Chỉ dùng làm gốc tầng cho group; nếu fail thì fallback về logic cũ.
            BuildViewAnchorMap(items);

            // Tách DIM nằm trong lòng thanh ra khỏi hệ tầng ngoài.
            // Internal geometry/planning does not redefine the external anchor map.
            MarkInternalDimItems(items, spacing, drawing);

            Dictionary<string, List<DimSetItem>> groups =
                new Dictionary<string, List<DimSetItem>>();
            foreach (DimSetItem item in items)
            {
                if (item == null || item.DimSet == null)
                {
                    result.SkippedCount++;
                    continue;
                }

                if (!groups.ContainsKey(item.GroupKey))
                    groups[item.GroupKey] = new List<DimSetItem>();

                groups[item.GroupKey].Add(item);
            }

            result.GroupCount = groups.Count;
            var internalPlans = PrepareInternalPlans(groups, items, spacing, result);

            foreach (KeyValuePair<string, List<DimSetItem>> pair in groups)
            {
                List<DimSetItem> list = pair.Value;
                if (list == null || list.Count == 0)
                    continue;

                if (IsInternalOnlyGroup(list))
                    continue;

                if (list.Count == 1)
                {
                    // External single DIM remains on the established tier-1 path.
                    ArrangeSingleExternalDimItemByAnchor(list[0], spacing, result);
                    continue;
                }

                ArrangeExistingSetGroup(list, spacing, result, drawing);
            }

            // Internal collision checks use the predicted external targets. Only
            // apply them after those targets have been written and verified.
            if (result.FailedCount == 0)
                ApplyInternalPlans(internalPlans, groups, spacing, result, drawing);
            else
                result.SkippedCount += groups.Where(g => IsInternalOnlyGroup(g.Value)).Sum(g => g.Value.Count);

            try
            {
                if (apply)
                    drawing.CommitChanges();
            }
            catch { }
            return result;
        }

        private static void ArrangeExistingSetGroup(
            List<DimSetItem> list,
            double spacing,
            Result result,
            Drawing drawing
        )
        {
            if (list == null || list.Count <= 0)
                return;

            if (IsInternalOnlyGroup(list))
            {
                if (result != null) result.SkippedCount += list.Count;
                return;
            }

            // PHU V21 RULE:
            // - Existing layout/order is already correct.
            // - Use existing DimensionPoints only to read the current tier order.
            // - Do NOT create/delete/split any chain.
            // - Keep child endpoints and direction; only their Distance may change.
            // - User spacing is now the absolute tier distance:
            //      tier1 = spacing, tier2 = spacing*2, tier3 = spacing*3...
            //   Example: input 150 => first tier becomes 150, not keep old 123.

            SimpleVector groupAxis = GetGroupAxisFromFirstValidOffset(list);
            if (!TryCaptureOriginalTierOrder(list, groupAxis))
            {
                if (result != null)
                    result.SkippedCount += list.Count;
                return;
            }

            if (list.Count <= 1)
            {
                if (list.Count == 1)
                {
                    if (list[0] != null && list[0].IsInternal)
                        result.SkippedCount++;
                    else
                        ArrangeSingleExternalDimItemByAnchor(list[0], spacing, result);
                }
                return;
            }

            // Existing line levels determine order, not the new first-tier distance.
            double targetBaseVisualLevel;
            if (!TryGetAnchorFirstTierVisualLevel(list, groupAxis, spacing, out targetBaseVisualLevel))
            {
                if (result != null)
                    result.SkippedCount += list.Count;
                return;
            }

            AssignSharedLayoutTiers(list, groupAxis, spacing);

            // TEST PORT PLATE - BẢN 2:
            // Anchor A/B/C/D không chỉ áp cho tier 1 nữa.
            // Anchor là BASE của cả group:
            //   tier1 = anchor + spacing
            //   tier2 = tier1 + spacing
            //   tier3 = tier2 + spacing...
            // Nếu không giải được toàn bộ group thì bỏ qua trước khi ghi.
            List<DimDistanceChange> changes = new List<DimDistanceChange>();
            for (int i = 0; i < list.Count; i++)
            {
                double newDistance;
                bool solved = TrySolveDistanceForTargetVisualLevel(
                    list[i],
                    groupAxis,
                    targetBaseVisualLevel + spacing * list[i].LayoutTierIndex,
                    out newDistance
                );

                if (!solved || !IsFinite(newDistance))
                {
                    if (result != null)
                        result.SkippedCount += list.Count;
                    return;
                }

                if (
                    Math.Abs(list[i].OriginalDistance) > 0.0001
                    && Math.Abs(newDistance) > 0.0001
                    && Math.Sign(list[i].OriginalDistance) != Math.Sign(newDistance)
                )
                {
                    if (result != null)
                        result.SkippedCount += list.Count;
                    return;
                }

                changes.Add(
                    new DimDistanceChange
                    {
                        Item = list[i],
                        OriginalDistance = list[i].OriginalDistance,
                        NewDistance = newDistance,
                        OriginalTierIndex = list[i].OriginalTierIndex,
                        OriginalStableId = list[i].StableId
                    }
                );
            }

            if (
                changes.Count != list.Count
                || !TryApplyGroupAtomically(changes, groupAxis, spacing, result, drawing)
            )
                return;
        }

        private static void MarkInternalDimItems(List<DimSetItem> items, double spacing, Drawing drawing)
        {
            if (!HANDLE_INTERNAL_DIMS_SEPARATELY || items == null) return;
            var bodies = PHU_InternalDimLayout.ReadBodies(drawing,
                items.Where(i => i != null && i.View != null).Select(i => i.View));
            foreach (DimSetItem item in items)
            {
                if (item == null || item.View == null || item.DimSet == null) continue;
                item.InternalFeet = GetDimensionPoints(item.DimSet);
                if (item.KindKey != "H" && item.KindKey != "V") continue;
                PHU_InternalDimLayout.Body body;
                bodies.TryGetValue(item.View, out body);
                SimpleVector normal = item.VisualOffset;
                normal.Normalize();
                double level;
                var location = PHU_InternalDimLayout.Location.Uncertain;
                if (Math.Max(Math.Abs(normal.X), Math.Abs(normal.Y)) >= .999999
                    && TryGetCurrentVisualLineLevel(item, normal, out level))
                    location = PHU_InternalDimLayout.Classify(body, item.InternalFeet,
                        normal.X, normal.Y, level);
                if (location == PHU_InternalDimLayout.Location.Internal)
                {
                    item.IsInternal = true;
                    item.InternalBody = body;
                    item.GroupKey += "_INTERNAL";
                }
                else if (location == PHU_InternalDimLayout.Location.Uncertain)
                {
                    // Unproven local geometry uses the approved existing first tier.
                    // Never force a secondary connection into the global outer tiers.
                    var feet = item.InternalFeet;
                    bool needsLocal = IsInternalDimItem(item, spacing)
                        || (body != null && feet != null && feet.Count >= 2 && feet.All(body.Contains));
                    if (needsLocal)
                    {
                        item.IsInternal = true;
                        item.IsLocal = true;
                        item.GroupKey += "_LOCAL";
                    }
                }
            }
            // Partition local clusters by foot geometry, never by dimension-line
            // distance: moving a line must not change its cluster on the next run.
            foreach (var bucket in items.Where(i => i.IsLocal).GroupBy(i => i.GroupKey).ToList())
            {
                var pending = bucket.OrderBy(i => i.StableId, StringComparer.Ordinal).ToList();
                int component = 0;
                while (pending.Count > 0)
                {
                    var connected = new List<DimSetItem> { pending[0] }; pending.RemoveAt(0);
                    for (int c = 0; c < connected.Count; c++)
                        for (int j = pending.Count - 1; j >= 0; j--)
                            if (LocalFeetConnected(connected[c], pending[j]))
                            { connected.Add(pending[j]); pending.RemoveAt(j); }
                    foreach (var item in connected) item.GroupKey = bucket.Key + "_" + component;
                    component++;
                }
            }
        }

        private static bool LocalFeetConnected(DimSetItem a, DimSetItem b)
        {
            var normal = a.VisualOffset; normal.Normalize();
            var pa = a.InternalFeet; var pb = b.InternalFeet;
            double clearance = SHARED_TIER_PAPER_CLEARANCE * a.View.Attributes.Scale;
            return PHU_InternalDimLayout.FeetConnected(pa, pb, normal.X, normal.Y, clearance);
        }

        private static bool IsInternalOnlyGroup(List<DimSetItem> list)
        {
            if (list == null || list.Count == 0)
                return false;

            foreach (DimSetItem item in list)
            {
                if (item == null || !item.IsInternal)
                    return false;
            }

            return true;
        }

        // Legacy candidate detector ONLY for routing uncertain geometry to a local
        // reference-tier plan. It no longer authorizes independent Distance=spacing.
        private static bool IsInternalDimItem(DimSetItem item, double spacing)
        {
            try
            {
                if (item == null || item.View == null || item.DimSet == null)
                    return false;

                ViewAnchorInfo info;
                if (
                    ViewAnchorMap == null
                    || !ViewAnchorMap.TryGetValue(RuntimeHelpersHash(item.View), out info)
                )
                    return false;

                if (info == null || !info.IsValid)
                    return false;

                List<Point> pts = GetDimensionPoints(item.DimSet);
                if (pts == null || pts.Count < 2)
                    return false;

                // A dimension may have all of its extension-foot points inside the
                // part while its actual dimension line is outside. Only an actual
                // line inside the content bounds may be classified as INTERNAL.
                double dimensionLineX;
                double dimensionLineY;
                if (
                    !TryGetDimensionLineReferencePoint(item, out dimensionLineX, out dimensionLineY)
                )
                    return false;

                // Ngưỡng mới theo yêu cầu:
                // - DIM nằm trong lòng thật sự mới xem là INTERNAL.
                // - Nếu chân DIM nằm gần mép ngoài trong khoảng spacing người dùng nhập,
                //   tối đa 200mm, thì KHÔNG xem là internal để nó được chạy anchor/tier ngoài.
                // Ví dụ dim rãnh 7 nằm sát mép trên: không còn bị ArrangeInternalDimItem()
                // kéo offset từ chính chân nhỏ đó nữa.
                double nearEdgeLimit = spacing;
                if (nearEdgeLimit <= 0.0)
                    nearEdgeLimit = 200.0;
                if (nearEdgeLimit > 200.0)
                    nearEdgeLimit = 200.0;

                // H = dim đo ngang, offset lên/xuống: internal nếu chân dim nằm trong chiều cao vật thể
                // và KHÔNG nằm gần mép trên/dưới theo nearEdgeLimit.
                if (item.KindKey == "H")
                {
                    if (
                        dimensionLineY <= info.MinY + INTERNAL_DIM_EDGE_TOL
                        || dimensionLineY >= info.MaxY - INTERNAL_DIM_EDGE_TOL
                    )
                        return false;

                    foreach (Point p in pts)
                    {
                        if (p == null)
                            return false;

                        if (
                            p.Y <= info.MinY + INTERNAL_DIM_EDGE_TOL
                            || p.Y >= info.MaxY - INTERNAL_DIM_EDGE_TOL
                        )
                            return false;

                        if (p.Y <= info.MinY + nearEdgeLimit || p.Y >= info.MaxY - nearEdgeLimit)
                            return false;
                    }

                    return true;
                }

                // V = dim đo dọc, offset trái/phải: internal nếu chân dim nằm trong chiều rộng vật thể
                // và KHÔNG nằm gần mép trái/phải theo nearEdgeLimit.
                if (item.KindKey == "V")
                {
                    if (
                        dimensionLineX <= info.MinX + INTERNAL_DIM_EDGE_TOL
                        || dimensionLineX >= info.MaxX - INTERNAL_DIM_EDGE_TOL
                    )
                        return false;

                    foreach (Point p in pts)
                    {
                        if (p == null)
                            return false;

                        if (
                            p.X <= info.MinX + INTERNAL_DIM_EDGE_TOL
                            || p.X >= info.MaxX - INTERNAL_DIM_EDGE_TOL
                        )
                            return false;

                        if (p.X <= info.MinX + nearEdgeLimit || p.X >= info.MaxX - nearEdgeLimit)
                            return false;
                    }

                    return true;
                }
            }
            catch { }

            return false;
        }

        private static bool TryGetDimensionLineReferencePoint(
            DimSetItem item,
            out double dimensionLineX,
            out double dimensionLineY
        )
        {
            dimensionLineX = Double.NaN;
            dimensionLineY = Double.NaN;

            try
            {
                if (item == null || item.DimSet == null || !IsFinite(item.Distance))
                    return false;

                Point firstDimensionPoint = GetFirstDimensionPoint(item.DimSet);
                if (firstDimensionPoint == null)
                    return false;

                SimpleVector rawOffset = item.Offset;
                if (rawOffset.Length < 0.0001)
                    return false;

                rawOffset.Normalize();
                dimensionLineX = firstDimensionPoint.X + item.Distance * rawOffset.X;
                dimensionLineY = firstDimensionPoint.Y + item.Distance * rawOffset.Y;
                return IsFinite(dimensionLineX) && IsFinite(dimensionLineY);
            }
            catch
            {
                dimensionLineX = Double.NaN;
                dimensionLineY = Double.NaN;
                return false;
            }
        }

        private static Dictionary<string, List<DimDistanceChange>> PrepareInternalPlans(
            Dictionary<string, List<DimSetItem>> groups, List<DimSetItem> allItems,
            double spacing, Result result)
        {
            var plans = new Dictionary<string, List<DimDistanceChange>>();
            var spansByGroup = new Dictionary<string, List<PHU_InternalDimLayout.Span>>();
            var targets = allItems.ToDictionary(i => i, i => i.OriginalDistance);
            // Predict the unchanged external solver's final lines for collision checks.
            foreach (var pair in groups)
            {
                var list = pair.Value;
                if (IsInternalOnlyGroup(list)) continue;
                var axis = GetGroupAxisFromFirstValidOffset(list);
                double first;
                if (!TryCaptureOriginalTierOrder(list, axis)
                    || !TryGetAnchorFirstTierVisualLevel(list, axis, spacing, out first)) continue;
                AssignSharedLayoutTiers(list, axis, spacing);
                var candidate = new Dictionary<DimSetItem, double>();
                foreach (var item in list)
                {
                    double distance;
                    if (!TrySolveDistanceForTargetVisualLevel(item, axis, first + spacing * item.LayoutTierIndex, out distance)
                        || !IsFinite(distance) || (Math.Abs(distance) > .0001 && Math.Abs(item.OriginalDistance) > .0001
                        && Math.Sign(distance) != Math.Sign(item.OriginalDistance))) break;
                    candidate[item] = distance;
                }
                if (candidate.Count == list.Count) foreach (var c in candidate) targets[c.Key] = c.Value;
            }
            foreach (var pair in groups)
            {
                var list = pair.Value;
                if (!IsInternalOnlyGroup(list)) continue;
                plans[pair.Key] = null;
                var axis = GetGroupAxisFromFirstValidOffset(list);
                if (!TryCaptureOriginalTierOrder(list, axis)) continue;
                var spans = new List<PHU_InternalDimLayout.Span>();
                foreach (var item in list)
                {
                    double min, max;
                    if (!TryGetMeasuredSpan(item, axis, out min, out max)) break;
                    spans.Add(new PHU_InternalDimLayout.Span { Min = min, Max = max,
                        Anchor = GetDimensionLineAnchorProjection(item, axis), OriginalLevel = item.OriginalVisualLineLevel });
                }
                if (spans.Count != list.Count) continue;
                spansByGroup[pair.Key] = spans;
                double clearance = SHARED_TIER_PAPER_CLEARANCE * list[0].View.Attributes.Scale;
                if (list[0].IsLocal || !PHU_InternalDimLayout.Plan(list[0].InternalBody, spans,
                    axis.X, axis.Y, spacing, clearance))
                {
                    foreach (var item in list) item.IsLocal = true;
                    continue;
                }
                var changes = MakeInternalChanges(list, spans, axis);
                if (changes == null) continue;
                plans[pair.Key] = changes;
                foreach (var c in changes) targets[c.Item] = c.NewDistance;
            }
            // A proved internal plan that would hit another group switches to local
            // reference-tier placement. Repeat as old reference lines become fixed.
            bool converted;
            do
            {
                converted = false;
                foreach (var pair in plans.ToList())
                {
                    var list = groups[pair.Key];
                    if (pair.Value == null || list[0].IsLocal) continue;
                    var axis = GetGroupAxisFromFirstValidOffset(list);
                    double clearance = SHARED_TIER_PAPER_CLEARANCE * list[0].View.Attributes.Scale;
                    var obstacles = InternalObstacles(list, allItems, targets, axis);
                    if (spansByGroup[pair.Key].Any(span => obstacles.Any(o =>
                        PHU_InternalDimLayout.Overlaps(span, o, clearance)
                        && Math.Abs(span.TargetLevel - o.TargetLevel) < Math.Max(spacing, clearance) - .01)))
                    {
                        foreach (var item in list) { item.IsLocal = true; targets[item] = item.OriginalDistance; }
                        plans[pair.Key] = null; converted = true;
                    }
                }
            } while (converted);
            foreach (var pair in plans.OrderBy(p => groups[p.Key][0].StableId, StringComparer.Ordinal).ToList())
            {
                var list = groups[pair.Key];
                if (!list[0].IsLocal || !spansByGroup.ContainsKey(pair.Key)) continue;
                var axis = GetGroupAxisFromFirstValidOffset(list);
                double clearance = SHARED_TIER_PAPER_CLEARANCE * list[0].View.Attributes.Scale;
                var spans = spansByGroup[pair.Key];
                if (!PHU_InternalDimLayout.PlanLocal(spans, spacing, clearance,
                    InternalObstacles(list, allItems, targets, axis))) continue;
                plans[pair.Key] = MakeInternalChanges(list, spans, axis);
                if (plans[pair.Key] != null) foreach (var c in plans[pair.Key]) targets[c.Item] = c.NewDistance;
            }
            int invalid = plans.Where(p => p.Value == null).Sum(p => groups[p.Key].Count);
            if (invalid > 0 && result != null)
                result.FailureDetail = "Cannot resolve finite dimension geometry for " + invalid + " DIM.";
            return plans;
        }

        private static List<PHU_InternalDimLayout.Span> InternalObstacles(List<DimSetItem> group,
            List<DimSetItem> all, Dictionary<DimSetItem, double> targets, SimpleVector axis)
        {
            var obstacles = new List<PHU_InternalDimLayout.Span>();
            foreach (var item in all)
            {
                if (item.View != group[0].View || group.Contains(item)) continue;
                var other = item.VisualOffset; other.Normalize();
                if (Math.Abs(axis.X * other.X + axis.Y * other.Y) < .999999) continue;
                double min, max, level;
                if (TryGetMeasuredSpan(item, axis, out min, out max)
                    && TryGetVisualLineLevelForDistance(item, axis, targets[item], out level))
                    obstacles.Add(new PHU_InternalDimLayout.Span { Min = min, Max = max, TargetLevel = level });
            }
            return obstacles;
        }

        private static List<DimDistanceChange> MakeInternalChanges(List<DimSetItem> list,
            List<PHU_InternalDimLayout.Span> spans, SimpleVector axis)
        {
            var changes = new List<DimDistanceChange>();
            for (int i = 0; i < list.Count; i++)
            {
                double distance;
                if (!TrySolveDistanceForTargetVisualLevel(list[i], axis, spans[i].TargetLevel, out distance)
                    || !IsFinite(distance) || (Math.Abs(distance) > .0001 && Math.Abs(list[i].OriginalDistance) > .0001
                    && Math.Sign(distance) != Math.Sign(list[i].OriginalDistance))) return null;
                changes.Add(new DimDistanceChange { Item = list[i], OriginalDistance = list[i].OriginalDistance,
                    NewDistance = distance, OriginalTierIndex = i, OriginalStableId = list[i].StableId });
            }
            return changes;
        }

        private static void ApplyInternalPlans(Dictionary<string, List<DimDistanceChange>> plans,
            Dictionary<string, List<DimSetItem>> groups, double spacing, Result result, Drawing drawing)
        {
            var applied = new List<List<DimDistanceChange>>();
            int changedBefore = result.ChangedCount;
            foreach (var pair in plans)
            {
                if (pair.Value == null) { result.SkippedCount += groups[pair.Key].Count; continue; }
                if (TryApplyGroupAtomically(pair.Value, GetGroupAxisFromFirstValidOffset(groups[pair.Key]),
                    spacing, result, drawing))
                {
                    applied.Add(pair.Value);
                    continue;
                }
                // Collision preflight considered all future internal targets. If one
                // group fails, none of those targets may remain partially applied.
                bool restored = true;
                if (!result.IsPreview)
                    foreach (var previous in applied) restored &= RestoreGroupDistances(previous, drawing);
                result.ChangedCount = changedBefore;
                if (!restored) result.FailureDetail = "Không xác minh được phục hồi nhóm internal. Hãy kiểm tra bản vẽ trước khi chạy lại.";
                return;
            }
        }

        private static void ArrangeSingleExternalDimItemByAnchor(
            DimSetItem item,
            double spacing,
            Result result
        )
        {
            try
            {
                if (item == null || item.DimSet == null)
                {
                    if (result != null)
                        result.SkippedCount++;
                    return;
                }

                SimpleVector axis = item.VisualOffset;
                if (axis.Length < 0.0001)
                {
                    if (result != null)
                        result.SkippedCount++;
                    return;
                }
                axis.Normalize();

                List<DimSetItem> one = new List<DimSetItem>();
                one.Add(item);

                double targetLevel;
                if (
                    !USE_ANCHOR_ABCD_AS_TIER_BASE
                    || !TryGetAnchorFirstTierVisualLevel(one, axis, spacing, out targetLevel)
                    || Double.IsNaN(targetLevel)
                )
                {
                    if (result != null)
                        result.SkippedCount++;
                    return;
                }

                double solvedDistance;
                if (
                    TrySolveDistanceForTargetVisualLevel(
                        item,
                        axis,
                        targetLevel,
                        out solvedDistance
                    )
                )
                {
                    TryApplyDistance(item, solvedDistance, result);
                    return;
                }

                if (result != null)
                    result.SkippedCount++;
            }
            catch
            {
                if (result != null)
                    result.FailedCount++;
            }
        }

        private static void BuildViewAnchorMap(List<DimSetItem> items)
        {
            ViewAnchorMap = new Dictionary<int, ViewAnchorInfo>();

            try
            {
                if (items == null)
                    return;

                foreach (DimSetItem item in items)
                {
                    if (item == null || item.View == null || item.DimSet == null)
                        continue;

                    List<Point> pts = GetDimensionPoints(item.DimSet);
                    if (pts == null || pts.Count == 0)
                        continue;

                    int key = RuntimeHelpersHash(item.View);
                    ViewAnchorInfo info;
                    if (!ViewAnchorMap.TryGetValue(key, out info) || info == null)
                    {
                        info = new ViewAnchorInfo();
                        info.MinX = 999999999.0;
                        info.MaxX = -999999999.0;
                        info.MinY = 999999999.0;
                        info.MaxY = -999999999.0;
                        ViewAnchorMap[key] = info;
                    }

                    foreach (Point p in pts)
                    {
                        if (p == null)
                            continue;

                        if (p.X < info.MinX)
                            info.MinX = p.X;
                        if (p.X > info.MaxX)
                            info.MaxX = p.X;
                        if (p.Y < info.MinY)
                            info.MinY = p.Y;
                        if (p.Y > info.MaxY)
                            info.MaxY = p.Y;
                    }
                }

                foreach (KeyValuePair<int, ViewAnchorInfo> pair in ViewAnchorMap)
                {
                    ViewAnchorInfo info = pair.Value;
                    if (info == null)
                        continue;

                    info.IsValid =
                        info.MaxX > info.MinX + 0.001
                        && info.MaxY > info.MinY + 0.001
                        && !Double.IsNaN(info.MinX)
                        && !Double.IsNaN(info.MaxX)
                        && !Double.IsNaN(info.MinY)
                        && !Double.IsNaN(info.MaxY)
                        && !Double.IsInfinity(info.MinX)
                        && !Double.IsInfinity(info.MaxX)
                        && !Double.IsInfinity(info.MinY)
                        && !Double.IsInfinity(info.MaxY);
                }
            }
            catch
            {
                ViewAnchorMap = new Dictionary<int, ViewAnchorInfo>();
            }
        }

        private static bool TryGetAnchorFirstTierVisualLevel(
            List<DimSetItem> list,
            SimpleVector visualAxis,
            double spacing,
            out double targetVisualLevel
        )
        {
            targetVisualLevel = Double.NaN;

            try
            {
                if (list == null || list.Count == 0 || visualAxis.Length < 0.0001 || spacing <= 0.0)
                    return false;

                DimSetItem seed = list[0];
                if (seed == null || seed.View == null)
                    return false;

                ViewAnchorInfo info;
                if (
                    ViewAnchorMap == null
                    || !ViewAnchorMap.TryGetValue(RuntimeHelpersHash(seed.View), out info)
                )
                    return false;

                visualAxis.Normalize();

                // A sloped group or a view with only collinear feet has no usable
                // A/B/C/D rectangle. Use the actual feet projected on its normal.
                if (info == null || !info.IsValid
                    || Math.Max(Math.Abs(visualAxis.X), Math.Abs(visualAxis.Y)) < 0.999999)
                {
                    double outer = Double.NegativeInfinity;
                    foreach (DimSetItem item in list)
                    {
                        List<Point> points = GetDimensionPoints(item.DimSet);
                        if (points == null || points.Count < 2)
                            return false;
                        foreach (Point point in points)
                        {
                            double projection = point.X * visualAxis.X + point.Y * visualAxis.Y;
                            if (!IsFinite(projection))
                                return false;
                            outer = Math.Max(outer, projection);
                        }
                    }
                    targetVisualLevel = outer + spacing;
                    return IsFinite(targetVisualLevel);
                }

                // Cùng ý tưởng PA9 plate:
                // Top    = Y cao nhất + spacing
                // Bottom = Y thấp nhất - spacing
                // Left   = X nhỏ nhất - spacing
                // Right  = X lớn nhất + spacing
                // Viết dưới dạng projection theo visualAxis để dùng chung cho mọi hướng.
                if (Math.Abs(visualAxis.Y) >= Math.Abs(visualAxis.X))
                {
                    if (visualAxis.Y >= 0.0)
                        targetVisualLevel = info.MaxY + spacing;
                    else
                        targetVisualLevel = -info.MinY + spacing;
                }
                else
                {
                    if (visualAxis.X >= 0.0)
                        targetVisualLevel = info.MaxX + spacing;
                    else
                        targetVisualLevel = -info.MinX + spacing;
                }

                return !Double.IsNaN(targetVisualLevel) && !Double.IsInfinity(targetVisualLevel);
            }
            catch
            {
                targetVisualLevel = Double.NaN;
                return false;
            }
        }

        private static SimpleVector GetGroupAxisFromFirstValidOffset(List<DimSetItem> list)
        {
            SimpleVector v = new SimpleVector();
            if (list == null)
                return v;

            foreach (DimSetItem item in list)
            {
                if (item == null)
                    continue;

                // Use VISUAL side, not raw UpDirection.
                // Raw UpDirection + negative Distance can visually place the dim on the opposite side.
                v = item.VisualOffset;
                if (v.Length > 0.0001)
                {
                    v.Normalize();
                    return v;
                }
            }

            return v;
        }

        private static bool TryGetCurrentVisualLineLevel(
            DimSetItem item,
            SimpleVector groupAxis,
            out double level
        )
        {
            level = Double.NaN;

            try
            {
                if (item == null || item.DimSet == null || groupAxis.Length < 0.0001)
                    return false;

                groupAxis.Normalize();
                if (groupAxis.Length < 0.0001)
                    return false;

                double anchor = GetDimensionLineAnchorProjection(item, groupAxis);
                if (!IsFinite(anchor))
                    return false;

                SimpleVector itemOffset = item.Offset;
                if (itemOffset.Length < 0.0001)
                    return false;
                itemOffset.Normalize();

                double dot = itemOffset.X * groupAxis.X + itemOffset.Y * groupAxis.Y;
                if (!IsFinite(dot) || Math.Abs(dot) < 0.0001)
                    return false;

                // Capture is performed before any group writes, so Distance is the live original value here.
                level = anchor + item.Distance * dot;
                return IsFinite(level);
            }
            catch
            {
                level = Double.NaN;
                return false;
            }
        }

        private static bool TryGetVisualLineLevelForDistance(
            DimSetItem item,
            SimpleVector groupAxis,
            double distance,
            out double level
        )
        {
            level = Double.NaN;

            try
            {
                if (
                    item == null
                    || item.DimSet == null
                    || groupAxis.Length < 0.0001
                    || !IsFinite(distance)
                )
                    return false;

                groupAxis.Normalize();
                if (groupAxis.Length < 0.0001)
                    return false;

                double anchor = GetDimensionLineAnchorProjection(item, groupAxis);
                if (!IsFinite(anchor))
                    return false;

                SimpleVector itemOffset = item.Offset;
                if (itemOffset.Length < 0.0001)
                    return false;
                itemOffset.Normalize();

                double dot = itemOffset.X * groupAxis.X + itemOffset.Y * groupAxis.Y;
                if (!IsFinite(dot) || Math.Abs(dot) < 0.0001)
                    return false;

                level = anchor + distance * dot;
                return IsFinite(level);
            }
            catch
            {
                level = Double.NaN;
                return false;
            }
        }

        private static bool TryCaptureOriginalTierOrder(
            List<DimSetItem> list,
            SimpleVector groupAxis
        )
        {
            if (list == null || list.Count == 0 || groupAxis.Length < 0.0001)
                return false;

            SimpleVector axis = groupAxis;
            axis.Normalize();
            if (axis.Length < 0.0001)
                return false;

            foreach (DimSetItem item in list)
            {
                double level;
                if (!TryGetCurrentVisualLineLevel(item, axis, out level))
                    return false;

                item.OriginalVisualLineLevel = level;
            }

            list.Sort(
                delegate(DimSetItem a, DimSetItem b)
                {
                    double delta = a.OriginalVisualLineLevel - b.OriginalVisualLineLevel;
                    if (Math.Abs(delta) > 0.01)
                        return delta < 0.0 ? -1 : 1;

                    int c = StringComparer.Ordinal.Compare(
                        a.StableId ?? string.Empty,
                        b.StableId ?? string.Empty
                    );
                    if (c != 0)
                        return c;

                    return a.CollectionIndex.CompareTo(b.CollectionIndex);
                }
            );

            for (int i = 0; i < list.Count; i++)
                list[i].OriginalTierIndex = i;

            return true;
        }

        // Preserve the captured order. Only consecutive, already aligned, disjoint
        // sets may share a tier; never pack a distant original tier into an inner one.
        private static void AssignSharedLayoutTiers(
            List<DimSetItem> list, SimpleVector axis, double spacing)
        {
            int tier = 0;
            int tierStart = 0;
            for (int i = 0; i < list.Count; i++)
            {
                bool shares = i > 0;
                for (int j = tierStart; shares && j < i; j++)
                    shares = CanShareOriginalTier(list[j], list[i], axis, spacing);
                if (i > 0 && !shares)
                {
                    tier++;
                    tierStart = i;
                }
                list[i].LayoutTierIndex = tier;
            }
        }

        private static bool CanShareOriginalTier(
            DimSetItem a, DimSetItem b, SimpleVector axis, double spacing)
        {
            if (a == null || b == null || a.IsInternal || b.IsInternal
                || a.View == null || !Object.ReferenceEquals(a.View, b.View)
                || a.GroupKey != b.GroupKey || a.SideKey != b.SideKey
                || a.KindKey != b.KindKey || (a.KindKey != "H" && a.KindKey != "V")
                || !IsFinite(spacing) || spacing <= 0.0)
                return false;

            double scale;
            try { scale = a.View.Attributes.Scale; }
            catch { return false; }
            if (!IsFinite(scale) || scale <= 0.0)
                return false;

            // Complete-link comparison prevents a sequence of small offsets from
            // merging several intentionally distinct levels into one tier.
            double tolerance = Math.Min(SHARED_TIER_PAPER_TOLERANCE * scale, spacing * 0.20);
            double delta = Math.Abs(a.OriginalVisualLineLevel - b.OriginalVisualLineLevel);
            if (!IsFinite(delta) || delta > tolerance)
                return false;

            axis.Normalize();
            SimpleVector av = a.VisualOffset;
            SimpleVector bv = b.VisualOffset;
            av.Normalize();
            bv.Normalize();
            // Rounded angle groups can contain slightly different directions.
            // Sharing is restricted to parallel, orthogonal dimension lines.
            if (Math.Max(Math.Abs(axis.X), Math.Abs(axis.Y)) < 0.999999
                || av.X * axis.X + av.Y * axis.Y < 0.999999
                || bv.X * axis.X + bv.Y * axis.Y < 0.999999)
                return false;

            double aMin, aMax, bMin, bMax;
            if (!TryGetMeasuredSpan(a, axis, out aMin, out aMax)
                || !TryGetMeasuredSpan(b, axis, out bMin, out bMax))
                return false;
            double gap = Math.Max(bMin - aMax, aMin - bMax);
            return gap > SHARED_TIER_PAPER_CLEARANCE * scale;
        }

        private static bool TryGetMeasuredSpan(
            DimSetItem item, SimpleVector normal, out double min, out double max)
        {
            min = Double.PositiveInfinity;
            max = Double.NegativeInfinity;
            List<Point> points = GetDimensionPoints(item.DimSet);
            if (points == null || points.Count < 2)
                return false;
            foreach (Point point in points)
            {
                double projection = -normal.Y * point.X + normal.X * point.Y;
                if (!IsFinite(projection))
                    return false;
                min = Math.Min(min, projection);
                max = Math.Max(max, projection);
            }
            return max - min > 0.01;
        }

        private static void TryApplyDistance(DimSetItem item, double newDistance, Result result)
        {
            StraightDimensionSet set = item == null ? null : item.DimSet as StraightDimensionSet;
            if (set == null || !IsFinite(newDistance))
            {
                result.FailedCount++;
                return;
            }
            item.OriginalTierIndex = 0;
            item.LayoutTierIndex = 0;
            var changes = new List<DimDistanceChange> {
                new DimDistanceChange {
                    Item = item, OriginalDistance = item.OriginalDistance,
                    NewDistance = newDistance, OriginalTierIndex = 0,
                    OriginalStableId = item.StableId
                }
            };
            TryApplyGroupAtomically(changes, item.VisualOffset, result.Spacing, result, set.GetDrawing());
        }

        // Snapshot every child before writing any member of the group. Child distances
        // use their own start points; add the parent's signed displacement, not its
        // absolute Distance (the two API anchors need not be the same).
        private static bool TryCaptureChildDistances(DimDistanceChange change)
        {
            StraightDimensionSet set = change.Item.DimSet as StraightDimensionSet;
            if (set == null)
                return false;
            var snapshots = new List<ChildDistanceSnapshot>();
            SimpleVector parentNormal = change.Item.Offset;
            parentNormal.Normalize();
            double displacement = change.NewDistance - change.OriginalDistance;
            DrawingObjectEnumerator children = set.GetObjects();
            while (children.MoveNext())
            {
                StraightDimension child = children.Current as StraightDimension;
                if (child == null || child.StartPoint == null || child.EndPoint == null
                    || child.UpDirection == null || !IsFinite(child.Distance)
                    || !SamePoint(child.StartPoint, child.StartPoint)
                    || !SamePoint(child.EndPoint, child.EndPoint)
                    || !SamePoint(child.UpDirection, child.UpDirection))
                    return false;
                var normal = new SimpleVector(child.UpDirection.X, child.UpDirection.Y);
                normal.Normalize();
                double dot = normal.X * parentNormal.X + normal.Y * parentNormal.Y;
                if (!IsFinite(dot) || Math.Abs(dot) < 0.999999)
                    return false;
                double target = child.Distance + displacement / dot;
                if (!IsFinite(target))
                    return false;
                snapshots.Add(new ChildDistanceSnapshot {
                    Child = child,
                    Start = new Point(child.StartPoint), End = new Point(child.EndPoint),
                    Direction = new Vector(child.UpDirection),
                    Distance = child.Distance, TargetDistance = target,
                    Attributes = child.Attributes,
                    SetAttributes = GetPropertyOrFieldValue(child, "DimensionSetAttributes")
                });
            }
            if (snapshots.Count == 0)
                return false;
            change.Children = snapshots;
            return true;
        }

        private static bool WriteChildDistances(DimDistanceChange change, bool restore)
        {
            if (change.Children == null || change.Children.Count == 0)
                return false;
            bool success = true;
            foreach (ChildDistanceSnapshot snapshot in change.Children)
            {
                try
                {
                    double target = restore ? snapshot.Distance : snapshot.TargetDistance;
                    if (restore && !snapshot.Child.Select())
                    {
                        success = false;
                        continue;
                    }
                    if (Math.Abs(snapshot.Child.Distance - target) <= 0.000001)
                        continue;
                    if (!SetDistanceValue(snapshot.Child, target) || !snapshot.Child.Modify())
                        success = false;
                }
                catch { success = false; }
                if (!success && !restore)
                    return false;
            }
            return success;
        }

        private static bool SamePoint(Point a, Point b)
        {
            return a != null && b != null && IsFinite(a.X) && IsFinite(a.Y) && IsFinite(a.Z)
                && Math.Abs(a.X - b.X) <= 0.000001
                && Math.Abs(a.Y - b.Y) <= 0.000001
                && Math.Abs(a.Z - b.Z) <= 0.000001;
        }

        private static bool VerifyChildDistances(DimDistanceChange change, bool restore)
        {
            StraightDimensionSet set = change.Item.DimSet as StraightDimensionSet;
            if (set == null || !set.Select())
                return false;
            var current = new List<StraightDimension>();
            var enumerator = set.GetObjects();
            while (enumerator.MoveNext())
            {
                var child = enumerator.Current as StraightDimension;
                if (child == null)
                    return false;
                current.Add(child);
            }
            if (current.Count != change.Children.Count)
                return false;
            foreach (ChildDistanceSnapshot snapshot in change.Children)
            {
                StraightDimension actual = current.Find(c => c.IsSameDatabaseObject(snapshot.Child));
                if (actual == null || !SamePoint(actual.StartPoint, snapshot.Start)
                    || !SamePoint(actual.EndPoint, snapshot.End)
                    || !SamePoint(actual.UpDirection, snapshot.Direction)
                    || !snapshot.Attributes.IsEqual(actual.Attributes)
                    || !IsFinite(actual.Distance)
                    || Math.Abs(actual.Distance - (restore ? snapshot.Distance : snapshot.TargetDistance)) > 0.01)
                    return false;
                var attributes = snapshot.SetAttributes as AttributesBase;
                if (attributes == null || !attributes.IsEqual(GetPropertyOrFieldValue(actual, "DimensionSetAttributes")))
                    return false;
            }
            return true;
        }
        private static bool TryApplyGroupAtomically(
            List<DimDistanceChange> changes,
            SimpleVector groupAxis,
            double spacing,
            Result result,
            Drawing drawing
        )
        {
            if (changes == null || changes.Count == 0 || drawing == null)
            {
                if (result != null)
                    result.FailedCount++;
                return false;
            }

            bool writesStarted = false;
            try
            {
                foreach (DimDistanceChange change in changes)
                {
                    if (
                        change == null
                        || change.Item == null
                        || change.Item.DimSet == null
                        || !IsFinite(change.OriginalDistance)
                        || !IsFinite(change.NewDistance)
                    )
                        throw new Exception("Invalid dimension change.");
                    if (!TryCaptureChildDistances(change))
                        throw new Exception("Cannot snapshot existing dimension segments.");
                    if (Math.Abs(change.OriginalDistance) > 0.0001
                        && Math.Abs(change.NewDistance) > 0.0001
                        && Math.Sign(change.OriginalDistance) != Math.Sign(change.NewDistance))
                        throw new Exception("Dimension would change visual side.");
                }

                if (result != null && result.IsPreview)
                {
                    result.ChangedCount += changes.Count;
                    return true;
                }

                // Never call set.Modify(): it rewrites segment attributes and can
                // remove native plate side marks. Move each existing segment only.
                writesStarted = true;
                foreach (DimDistanceChange change in changes)
                {
                    if (!WriteChildDistances(change, false))
                        throw new Exception("Dimension Modify failed.");
                }

                if (!TryCommitDrawing(drawing))
                    throw new Exception("Dimension commit failed.");

                if (!VerifyGroupApply(changes, groupAxis, spacing))
                    throw new Exception("Dimension group verification failed.");

                foreach (DimDistanceChange change in changes)
                {
                    double actualDistance;
                    if (
                        TryGetDistanceValue(change.Item.DimSet, out actualDistance)
                        && IsFinite(actualDistance)
                    )
                    {
                        change.Item.Distance = actualDistance;
                        change.Item.AbsDistance = Math.Abs(actualDistance);
                        change.Item.Sign = actualDistance < 0.0 ? -1.0 : 1.0;

                        SimpleVector currentVisualOffset;
                        if (
                            TryGetVisualOffsetForDistance(
                                change.Item,
                                actualDistance,
                                out currentVisualOffset
                            )
                        )
                            change.Item.VisualOffset = currentVisualOffset;
                    }
                }

                if (result != null)
                    result.ChangedCount += changes.Count;
                return true;
            }
            catch
            {
                if (writesStarted && !RestoreGroupDistances(changes, drawing) && result != null)
                    result.FailureDetail = "Không xác minh được phục hồi khoảng cách DIM. Hãy kiểm tra bản vẽ trước khi chạy lại.";
                if (result != null)
                    result.FailedCount++;
                return false;
            }
        }

        private static bool VerifyGroupApply(
            List<DimDistanceChange> changes,
            SimpleVector groupAxis,
            double spacing
        )
        {
            if (changes == null || changes.Count == 0 || spacing <= 0.0)
                return false;

            double previousLevel = Double.NaN;
            for (int i = 0; i < changes.Count; i++)
            {
                DimDistanceChange change = changes[i];
                if (change == null || change.Item == null || change.Item.DimSet == null)
                    return false;
                if (change.Children != null && !VerifyChildDistances(change, false))
                    return false;

                if (
                    change.OriginalTierIndex != i
                    || !String.Equals(
                        change.Item.StableId,
                        change.OriginalStableId,
                        StringComparison.Ordinal
                    )
                )
                    return false;

                double currentDistance;
                if (
                    !TryGetDistanceValue(change.Item.DimSet, out currentDistance)
                    || !IsFinite(currentDistance)
                )
                    return false;

                if (Math.Abs(currentDistance - change.NewDistance) > 0.01)
                    return false;

                if (
                    Math.Abs(change.OriginalDistance) > 0.0001
                    && Math.Abs(currentDistance) > 0.0001
                    && Math.Sign(change.OriginalDistance) != Math.Sign(currentDistance)
                )
                    return false;

                SimpleVector currentVisualOffset;
                if (
                    !TryGetVisualOffsetForDistance(
                        change.Item,
                        currentDistance,
                        out currentVisualOffset
                    )
                )
                    return false;

                SimpleVector originalVisualOffset = change.Item.VisualOffset;
                if (originalVisualOffset.Length < 0.0001)
                    return false;
                originalVisualOffset.Normalize();
                double sideDot =
                    originalVisualOffset.X * currentVisualOffset.X
                    + originalVisualOffset.Y * currentVisualOffset.Y;
                if (!IsFinite(sideDot) || sideDot < 0.999)
                    return false;

                double currentLevel;
                if (
                    !TryGetVisualLineLevelForDistance(
                        change.Item,
                        groupAxis,
                        currentDistance,
                        out currentLevel
                    )
                )
                    return false;

                if (!change.Item.IsInternal && !Double.IsNaN(previousLevel))
                {
                    double delta = currentLevel - previousLevel;
                    int tierStep = change.Item.LayoutTierIndex - changes[i - 1].Item.LayoutTierIndex;
                    if (tierStep < 0 || tierStep > 1
                        || (tierStep == 1 && delta <= 0.0)
                        || !IsFinite(delta) || Math.Abs(delta - spacing * tierStep) > 0.5)
                        return false;
                }

                previousLevel = currentLevel;
            }

            return true;
        }

        private static bool RestoreGroupDistances(List<DimDistanceChange> changes, Drawing drawing)
        {
            if (changes == null)
                return false;

            bool anyWritten = false;
            bool restored = true;
            foreach (DimDistanceChange change in changes)
            {
                if (change == null || change.Children == null)
                    continue;
                restored &= WriteChildDistances(change, true);
                anyWritten = true;
            }
            if (anyWritten)
                restored &= TryCommitDrawing(drawing);

            foreach (DimDistanceChange change in changes)
            {
                if (change == null || change.Children == null)
                    continue;
                try { restored &= VerifyChildDistances(change, true); }
                catch { restored = false; }
            }
            foreach (DimDistanceChange change in changes)
            {
                if (change == null || change.Item == null)
                    continue;

                change.Item.Distance = change.OriginalDistance;
                change.Item.AbsDistance = Math.Abs(change.OriginalDistance);
                change.Item.Sign = change.OriginalDistance < 0.0 ? -1.0 : 1.0;
            }
            return restored;
        }

        private static bool TryGetVisualOffsetForDistance(
            DimSetItem item,
            double distance,
            out SimpleVector visualOffset
        )
        {
            visualOffset = new SimpleVector();
            if (item == null || item.Offset.Length < 0.0001 || !IsFinite(distance))
                return false;

            visualOffset = item.Offset;
            visualOffset.Normalize();
            double sign = distance < 0.0 ? -1.0 : 1.0;
            visualOffset.X *= sign;
            visualOffset.Y *= sign;
            visualOffset.Normalize();
            return visualOffset.Length > 0.0001;
        }

        private static bool TryInvokeNoArgSuccessful(object obj, string methodName)
        {
            if (obj == null || string.IsNullOrWhiteSpace(methodName))
                return false;

            try
            {
                MethodInfo method = obj.GetType()
                    .GetMethod(
                        methodName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );
                if (method == null || method.GetParameters().Length != 0)
                    return false;

                object value = method.Invoke(obj, null);
                if (value is bool)
                    return (bool)value;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryCommitDrawing(Drawing drawing)
        {
            try
            {
                return drawing != null && drawing.CommitChanges();
            }
            catch
            {
                return false;
            }
        }

        private static bool CanUseUnifiedGeometrySpacing(
            List<DimSetItem> list,
            SimpleVector groupAxis
        )
        {
            if (list == null || list.Count <= 1 || groupAxis.Length < 0.0001)
                return false;

            int ok = 0;
            foreach (DimSetItem item in list)
            {
                double level = GetVisualLineLevel(item, groupAxis);
                if (!Double.IsNaN(level))
                    ok++;
            }

            return ok >= 2;
        }

        private static bool TrySolveDistanceForTargetVisualLevel(
            DimSetItem item,
            SimpleVector visualAxis,
            double targetVisualLevel,
            out double distance
        )
        {
            distance = 0.0;

            if (item == null || item.DimSet == null || visualAxis.Length < 0.0001)
                return false;

            visualAxis.Normalize();
            if (visualAxis.Length < 0.0001 || !IsFinite(targetVisualLevel))
                return false;

            double anchor = GetDimensionLineAnchorProjection(item, visualAxis);
            if (!IsFinite(anchor))
                return false;

            SimpleVector rawOffset = item.Offset;
            if (rawOffset.Length < 0.0001)
                return false;
            rawOffset.Normalize();

            double dot = rawOffset.X * visualAxis.X + rawOffset.Y * visualAxis.Y;
            if (!IsFinite(dot) || Math.Abs(dot) < 0.0001)
                return false;

            distance = (targetVisualLevel - anchor) / dot;
            return IsFinite(distance);
        }

        private static SimpleVector GetGroupOutDirection(List<DimSetItem> list)
        {
            SimpleVector v = new SimpleVector();
            if (list == null || list.Count == 0)
                return v;

            // Use tier-1 candidate by AbsDistance only to define stable outward side.
            // This does not move tier 1; it only chooses a common axis for measuring existing levels.
            DimSetItem seed = list[0];
            foreach (DimSetItem item in list)
            {
                if (item != null && item.AbsDistance < seed.AbsDistance)
                    seed = item;
            }

            v = seed.Offset;
            if (v.Length < 0.0001)
                return v;
            v.Normalize();

            // Tekla Distance may be negative. Actual visual line can lie opposite to OffsetDirection.
            double sign = Math.Abs(seed.Sign) < 0.0001 ? 1.0 : seed.Sign;
            v.X *= sign;
            v.Y *= sign;
            v.Normalize();
            return v;
        }

        private static bool HasGeometryLevel(List<DimSetItem> list, SimpleVector groupOut)
        {
            if (list == null || list.Count == 0 || groupOut.Length < 0.0001)
                return false;

            int okCount = 0;
            foreach (DimSetItem item in list)
            {
                double level = GetVisualLineLevel(item, groupOut);
                if (!Double.IsNaN(level))
                    okCount++;
            }
            return okCount >= 2;
        }

        private static double GetVisualLineLevelForDistance(
            DimSetItem item,
            SimpleVector outUnit,
            double distance
        )
        {
            if (item == null || item.DimSet == null || outUnit.Length < 0.0001)
                return Double.NaN;

            double anchor = GetDimensionLineAnchorProjection(item, outUnit);
            if (Double.IsNaN(anchor))
                return Double.NaN;

            SimpleVector itemOffset = item.Offset;
            if (itemOffset.Length < 0.0001)
                return Double.NaN;
            itemOffset.Normalize();

            double dotOffsetToOut = itemOffset.X * outUnit.X + itemOffset.Y * outUnit.Y;
            if (Math.Abs(dotOffsetToOut) < 0.0001)
                return Double.NaN;

            return anchor + distance * dotOffsetToOut;
        }

        private static double GetVisualLineLevel(DimSetItem item, SimpleVector outUnit)
        {
            if (item == null || item.DimSet == null || outUnit.Length < 0.0001)
                return Double.NaN;

            double anchor = GetDimensionLineAnchorProjection(item, outUnit);
            if (Double.IsNaN(anchor))
                return Double.NaN;

            SimpleVector itemOffset = item.Offset;
            if (itemOffset.Length < 0.0001)
                return Double.NaN;
            itemOffset.Normalize();

            double dotOffsetToOut = itemOffset.X * outUnit.X + itemOffset.Y * outUnit.Y;
            if (Math.Abs(dotOffsetToOut) < 0.0001)
                return Double.NaN;

            return anchor + item.Distance * dotOffsetToOut;
        }

        private static double GetDimensionLineAnchorProjection(
            DimSetItem item,
            SimpleVector outUnit
        )
        {
            if (item == null || item.DimSet == null || outUnit.Length < 0.0001)
                return Double.NaN;

            Point firstDimensionPoint = GetFirstDimensionPoint(item.DimSet);
            if (firstDimensionPoint == null)
                return Double.NaN;

            outUnit.Normalize();

            // Tekla defines StraightDimensionSet.Distance from DimensionPoints[0].
            // This indexed point is the set's actual distance reference, whereas the
            // outer-most point may be a protruding foot and must not determine tier order.
            double anchor = firstDimensionPoint.X * outUnit.X + firstDimensionPoint.Y * outUnit.Y;
            return IsFinite(anchor) ? anchor : Double.NaN;
        }

        private static Point GetFirstDimensionPoint(object dimSet)
        {
            object raw = GetPropertyOrFieldValue(dimSet, "DimensionPoints");
            if (raw == null)
                return null;

            try
            {
                IList indexedPoints = raw as IList;
                if (indexedPoints != null && indexedPoints.Count > 0)
                    return indexedPoints[0] as Point;
            }
            catch { }

            try
            {
                Type type = raw.GetType();
                foreach (
                    PropertyInfo property in type.GetProperties(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    )
                )
                {
                    ParameterInfo[] parameters = property.GetIndexParameters();
                    if (parameters.Length != 1)
                        continue;

                    object index = Convert.ChangeType(0, parameters[0].ParameterType);
                    Point point = property.GetValue(raw, new object[] { index }) as Point;
                    if (point != null)
                        return point;
                }
            }
            catch { }

            try
            {
                IEnumerable enumerable = raw as IEnumerable;
                if (enumerable != null)
                {
                    IEnumerator enumerator = enumerable.GetEnumerator();
                    if (enumerator != null && enumerator.MoveNext())
                        return enumerator.Current as Point;
                }
            }
            catch { }

            return null;
        }

        private static double GetAnchorProjection(DimSetItem item, SimpleVector offUnit)
        {
            if (item == null || item.DimSet == null)
                return Double.NaN;

            List<Point> pts = GetDimensionPoints(item.DimSet);
            if (pts == null || pts.Count == 0)
                return Double.NaN;

            return GetPointsCenterProjection(pts, offUnit);
        }

        private static double GetPointsCenterProjection(List<Point> pts, SimpleVector offUnit)
        {
            if (pts == null || pts.Count == 0 || offUnit.Length < 0.0001)
                return Double.NaN;

            double cx = 0.0;
            double cy = 0.0;
            foreach (Point p in pts)
            {
                cx += p.X;
                cy += p.Y;
            }
            cx /= pts.Count;
            cy /= pts.Count;

            return cx * offUnit.X + cy * offUnit.Y;
        }

        private static List<Point> GetDimensionPoints(object dimSet)
        {
            object raw = GetPropertyOrFieldValue(dimSet, "DimensionPoints");
            if (raw == null)
                return null;

            List<Point> points = new List<Point>();

            IEnumerable enumerable = raw as IEnumerable;
            if (enumerable != null)
            {
                foreach (object obj in enumerable)
                {
                    Point p = obj as Point;
                    if (p != null)
                        points.Add(p);
                }
            }

            if (points.Count > 0)
                return RemoveDuplicatePoints(points);

            // Fallback: Count + indexer / Item property.
            int count = 0;
            object countObj = GetPropertyOrFieldValue(raw, "Count");
            try
            {
                if (countObj != null)
                    count = Convert.ToInt32(countObj);
            }
            catch
            {
                count = 0;
            }

            if (count <= 0)
                return null;

            Type t = raw.GetType();
            PropertyInfo indexer = null;
            foreach (
                PropertyInfo prop in t.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                )
            )
            {
                if (prop.GetIndexParameters().Length == 1)
                {
                    indexer = prop;
                    break;
                }
            }

            if (indexer != null)
            {
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        object obj = indexer.GetValue(raw, new object[] { i });
                        Point p = obj as Point;
                        if (p != null)
                            points.Add(p);
                    }
                    catch { }
                }
            }

            return points.Count > 0 ? RemoveDuplicatePoints(points) : null;
        }

        private static List<Point> RemoveDuplicatePoints(List<Point> points)
        {
            List<Point> clean = new List<Point>();
            foreach (Point p in points)
            {
                bool exists = false;
                foreach (Point q in clean)
                {
                    if (Distance2(p, q) < 0.01)
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                    clean.Add(p);
            }
            return clean;
        }

        private static double Distance2(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        private static List<Point> SortPointsAlongDimension(List<Point> points, string kindKey)
        {
            List<Point> sorted = new List<Point>(points);

            if (kindKey == "H")
            {
                sorted.Sort(
                    delegate(Point a, Point b)
                    {
                        int c = a.X.CompareTo(b.X);
                        if (c != 0)
                            return c;
                        return a.Y.CompareTo(b.Y);
                    }
                );
                return sorted;
            }

            if (kindKey == "V")
            {
                sorted.Sort(
                    delegate(Point a, Point b)
                    {
                        int c = a.Y.CompareTo(b.Y);
                        if (c != 0)
                            return c;
                        return a.X.CompareTo(b.X);
                    }
                );
                return sorted;
            }

            // Sloped: sort along the longest axis. This keeps point order stable without moving anchors.
            double minX = sorted[0].X,
                maxX = sorted[0].X;
            double minY = sorted[0].Y,
                maxY = sorted[0].Y;
            foreach (Point p in sorted)
            {
                if (p.X < minX)
                    minX = p.X;
                if (p.X > maxX)
                    maxX = p.X;
                if (p.Y < minY)
                    minY = p.Y;
                if (p.Y > maxY)
                    maxY = p.Y;
            }

            if ((maxX - minX) >= (maxY - minY))
            {
                sorted.Sort(
                    delegate(Point a, Point b)
                    {
                        int c = a.X.CompareTo(b.X);
                        if (c != 0)
                            return c;
                        return a.Y.CompareTo(b.Y);
                    }
                );
            }
            else
            {
                sorted.Sort(
                    delegate(Point a, Point b)
                    {
                        int c = a.Y.CompareTo(b.Y);
                        if (c != 0)
                            return c;
                        return a.X.CompareTo(b.X);
                    }
                );
            }

            return sorted;
        }

        private static List<DimSetItem> CollectStraightDimensionSets(
            ContainerView sheet,
            string scope
        )
        {
            List<DimSetItem> items = new List<DimSetItem>();

            Type straightSetType = typeof(StraightDimensionSet);
            if (straightSetType == null)
                return items;

            List<Tekla.Structures.Drawing.View> allViews =
                new List<Tekla.Structures.Drawing.View>();
            DrawingObjectEnumerator views = sheet.GetAllViews();
            while (views.MoveNext())
            {
                Tekla.Structures.Drawing.View view = views.Current as Tekla.Structures.Drawing.View;
                if (view != null)
                    allViews.Add(view);
            }

            Tekla.Structures.Drawing.View frontView = FindViewByViewTypeForDimSpacing(
                allViews,
                "FrontView",
                "Front"
            );

            foreach (Tekla.Structures.Drawing.View view in allViews)
            {
                if (view == null)
                    continue;
                if (!ScopeAcceptsView(view, scope, frontView))
                    continue;

                try
                {
                    DrawingObjectEnumerator e = view.GetAllObjects(straightSetType);
                    int collectionIndex = 0;
                    while (e.MoveNext())
                    {
                        object dimSet = e.Current;
                        int currentCollectionIndex = collectionIndex++;
                        if (dimSet == null)
                            continue;

                        DimSetItem item = BuildDimSetItem(dimSet, view, currentCollectionIndex);
                        if (item != null)
                            items.Add(item);
                    }
                }
                catch { }
            }

            return items;
        }

        private static DimSetItem BuildDimSetItem(
            object dimSet,
            Tekla.Structures.Drawing.View view,
            int collectionIndex
        )
        {
            double distance;
            if (!TryGetDistanceValue(dimSet, out distance))
                return null;

            SimpleVector offset;
            if (!TryGetOffsetDirection(dimSet, out offset))
                return null;

            offset.Normalize();

            string viewKey = GetViewKey(view);
            double sign = distance < 0.0 ? -1.0 : 1.0;

            // VISUAL side = UpDirection/OffsetDirection * sign(Distance).
            // This is the key fix for: upper dims running downward / lower dims running upward.
            SimpleVector visualOffset = offset;
            visualOffset.X *= sign;
            visualOffset.Y *= sign;
            visualOffset.Normalize();

            double offsetAngle = Normalize360(
                Math.Atan2(visualOffset.Y, visualOffset.X) * 180.0 / Math.PI
            );
            double roundedOffset = RoundAngle(offsetAngle, 5.0, 360.0);

            string kindKey = GetKindKey(visualOffset);
            string sideKey = GetSideKey(visualOffset, kindKey);

            DimSetItem item = new DimSetItem();
            item.DimSet = dimSet;
            item.View = view;
            item.Distance = distance;
            item.OriginalDistance = distance;
            item.OriginalVisualLineLevel = Double.NaN;
            item.OriginalTierIndex = -1;
            item.CollectionIndex = collectionIndex;
            item.StableId = GetStableDimSetId(dimSet, view, collectionIndex);
            item.AbsDistance = Math.Abs(distance);
            item.Sign = sign;
            item.Offset = offset; // raw Tekla direction, used only when setting Distance
            item.VisualOffset = visualOffset; // actual drawing side, used for grouping/sorting
            item.OffsetAngle = roundedOffset;
            item.LineAngle = 0.0;
            item.SideKey = sideKey;
            item.KindKey = kindKey;

            if (kindKey == "V")
                item.GroupKey =
                    viewKey + "_VERT_OFF_" + roundedOffset.ToString("0") + "_SIDE_" + sideKey;
            else if (kindKey == "H")
                item.GroupKey =
                    viewKey + "_HORIZ_OFF_" + roundedOffset.ToString("0") + "_SIDE_" + sideKey;
            else
                item.GroupKey =
                    viewKey + "_SLOPE_OFF_" + roundedOffset.ToString("0") + "_SIDE_" + sideKey;

            return item;
        }

        private static string GetKindKey(SimpleVector offset)
        {
            double ax = Math.Abs(offset.X);
            double ay = Math.Abs(offset.Y);

            if (ay >= ax * 2.0)
                return "H";
            if (ax >= ay * 2.0)
                return "V";
            return "S";
        }

        private static string GetSideKey(SimpleVector offset, string kindKey)
        {
            if (kindKey == "H")
                return offset.Y >= 0.0 ? "TOP" : "BOTTOM";

            if (kindKey == "V")
                return offset.X >= 0.0 ? "RIGHT" : "LEFT";

            double angle = Normalize360(Math.Atan2(offset.Y, offset.X) * 180.0 / Math.PI);
            if (angle >= 45.0 && angle < 135.0)
                return "SLOPE_TOP";
            if (angle >= 135.0 && angle < 225.0)
                return "SLOPE_LEFT";
            if (angle >= 225.0 && angle < 315.0)
                return "SLOPE_BOTTOM";
            return "SLOPE_RIGHT";
        }

        private static bool ScopeAcceptsView(
            Tekla.Structures.Drawing.View view,
            string scope,
            Tekla.Structures.Drawing.View frontView
        )
        {
            if (view == null)
                return false;

            if (
                string.IsNullOrWhiteSpace(scope)
                || scope.IndexOf("Toàn", StringComparison.OrdinalIgnoreCase) >= 0
                || scope.IndexOf("All", StringComparison.OrdinalIgnoreCase) >= 0
            )
                return true;

            string target = scope.Trim().ToLowerInvariant();

            if (target == "front")
                return ViewTypeMatchesForDimSpacing(view, "FrontView", "Front");

            if (target == "top")
            {
                if (ViewTypeMatchesForDimSpacing(view, "TopView", "Top"))
                    return true;

                return IsSpecialTopSectionForDimSpacing(view, frontView);
            }

            if (target == "bottom")
            {
                if (ViewTypeMatchesForDimSpacing(view, "BottomView", "Bottom"))
                    return true;

                return IsSpecialBottomSectionForDimSpacing(view, frontView);
            }

            return true;
        }

        private static Tekla.Structures.Drawing.View FindViewByViewTypeForDimSpacing(
            List<Tekla.Structures.Drawing.View> views,
            string exactViewTypeName,
            string fallbackText
        )
        {
            try
            {
                if (views == null)
                    return null;

                foreach (Tekla.Structures.Drawing.View view in views)
                {
                    if (ViewTypeMatchesForDimSpacing(view, exactViewTypeName, fallbackText))
                        return view;
                }
            }
            catch { }

            return null;
        }

        private static bool ViewTypeMatchesForDimSpacing(
            Tekla.Structures.Drawing.View view,
            string exactViewTypeName,
            string fallbackText
        )
        {
            try
            {
                if (view == null)
                    return false;

                string text = "";
                try
                {
                    text = view.ViewType.ToString();
                }
                catch
                {
                    text = "";
                }

                if (
                    !string.IsNullOrEmpty(exactViewTypeName)
                    && string.Equals(text, exactViewTypeName, StringComparison.OrdinalIgnoreCase)
                )
                    return true;

                if (
                    !string.IsNullOrEmpty(fallbackText)
                    && text.IndexOf(fallbackText, StringComparison.OrdinalIgnoreCase) >= 0
                )
                    return true;
            }
            catch { }

            return false;
        }

        private static bool IsSpecialTopSectionForDimSpacing(
            Tekla.Structures.Drawing.View view,
            Tekla.Structures.Drawing.View frontView
        )
        {
            try
            {
                if (view == null || frontView == null)
                    return false;

                if (!ViewTypeMatchesForDimSpacing(view, "SectionView", "Section"))
                    return false;

                if (!IsSectionWidthCloseToFrontForDimSpacing(view, frontView))
                    return false;

                return view.Origin.Y > frontView.Origin.Y;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSpecialBottomSectionForDimSpacing(
            Tekla.Structures.Drawing.View view,
            Tekla.Structures.Drawing.View frontView
        )
        {
            try
            {
                if (view == null || frontView == null)
                    return false;

                if (!ViewTypeMatchesForDimSpacing(view, "SectionView", "Section"))
                    return false;

                if (!IsSectionWidthCloseToFrontForDimSpacing(view, frontView))
                    return false;

                return view.Origin.Y < frontView.Origin.Y;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSectionWidthCloseToFrontForDimSpacing(
            Tekla.Structures.Drawing.View sectionView,
            Tekla.Structures.Drawing.View frontView
        )
        {
            try
            {
                double sectionWidth = GetViewRestrictionBoxWidthForDimSpacing(sectionView);
                double frontWidth = GetViewRestrictionBoxWidthForDimSpacing(frontView);

                if (sectionWidth <= 0.0 || frontWidth <= 0.0)
                    return false;

                double tolerance = Math.Max(10.0, frontWidth * 0.10);
                return Math.Abs(sectionWidth - frontWidth) <= tolerance;
            }
            catch
            {
                return false;
            }
        }

        private static double GetViewRestrictionBoxWidthForDimSpacing(
            Tekla.Structures.Drawing.View view
        )
        {
            try
            {
                if (view == null || view.RestrictionBox == null)
                    return 0.0;

                AABB box = view.RestrictionBox;
                if (box.MinPoint == null || box.MaxPoint == null)
                    return 0.0;

                return Math.Abs(box.MaxPoint.X - box.MinPoint.X);
            }
            catch
            {
                return 0.0;
            }
        }

        private static string GetViewKey(Tekla.Structures.Drawing.View view)
        {
            // IMPORTANT: view.Name can be empty for Front/Top/Bottom in some drawings.
            // If the key is empty, dimensions from different views are mixed together.
            // Always append runtime hash so each Tekla view has independent tiers.
            string viewKey = "VIEW";
            try
            {
                object typeObj = view.GetType().GetProperty("ViewType")?.GetValue(view, null);
                if (typeObj != null)
                    viewKey = typeObj.ToString();
            }
            catch { }

            try
            {
                object nameObj = view.GetType().GetProperty("Name")?.GetValue(view, null);
                if (nameObj != null && !string.IsNullOrWhiteSpace(nameObj.ToString()))
                    viewKey = viewKey + "_" + nameObj.ToString();
            }
            catch { }

            return viewKey + "_VH_" + RuntimeHelpersHash(view).ToString();
        }

        private static string GetStableDimSetId(
            object dimSet,
            Tekla.Structures.Drawing.View view,
            int collectionIndex
        )
        {
            try
            {
                object identifier = GetPropertyOrFieldValue(dimSet, "Identifier");
                object id = GetPropertyOrFieldValue(identifier, "ID");
                if (id != null)
                {
                    string idText = Convert.ToString(
                        id,
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                    if (!string.IsNullOrWhiteSpace(idText))
                        return "ID:" + idText;
                }
            }
            catch { }

            string stableViewKey = GetStableTierViewKey(view);
            return stableViewKey
                + "_COL_"
                + collectionIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string GetStableTierViewKey(Tekla.Structures.Drawing.View view)
        {
            string viewKey = "VIEW";

            try
            {
                object typeObj = view == null ? null : GetPropertyOrFieldValue(view, "ViewType");
                if (typeObj != null && !string.IsNullOrWhiteSpace(typeObj.ToString()))
                    viewKey = typeObj.ToString();
            }
            catch { }

            try
            {
                object nameObj = view == null ? null : GetPropertyOrFieldValue(view, "Name");
                if (nameObj != null && !string.IsNullOrWhiteSpace(nameObj.ToString()))
                    viewKey += "_" + nameObj.ToString();
            }
            catch { }

            return viewKey;
        }

        private static double Normalize360(double angle)
        {
            while (angle < 0.0)
                angle += 360.0;
            while (angle >= 360.0)
                angle -= 360.0;
            return angle;
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        private static double RoundAngle(double angle, double step, double max)
        {
            double a = Math.Round(angle / step) * step;
            while (a < 0.0)
                a += max;
            while (a >= max)
                a -= max;
            return a;
        }

        private static bool TryGetDistanceValue(object dim, out double distance)
        {
            distance = 0.0;
            if (TryReadDoublePropertyOrField(dim, "Distance", out distance))
                return true;

            object attr = GetPropertyOrFieldValue(dim, "Attributes");
            if (TryReadDoublePropertyOrField(attr, "Distance", out distance))
                return true;

            object setAttr = GetPropertyOrFieldValue(dim, "DimensionSetAttributes");
            if (TryReadDoublePropertyOrField(setAttr, "Distance", out distance))
                return true;

            return false;
        }

        private static bool SetDistanceValue(object dim, double distance)
        {
            if (TrySetDoublePropertyOrField(dim, "Distance", distance))
                return true;

            object attr = GetPropertyOrFieldValue(dim, "Attributes");
            if (TrySetDoublePropertyOrField(attr, "Distance", distance))
                return true;

            object setAttr = GetPropertyOrFieldValue(dim, "DimensionSetAttributes");
            if (TrySetDoublePropertyOrField(setAttr, "Distance", distance))
                return true;

            return false;
        }

        private static bool TryGetOffsetDirection(object dim, out SimpleVector up)
        {
            up = new SimpleVector();
            string[] names = new string[]
            {
                "UpDirection",
                "OffsetDirection",
                "Normal",
                "Direction"
            };

            foreach (string name in names)
            {
                object value = GetPropertyOrFieldValue(dim, name);
                if (TryConvertToVector(value, out up))
                    return true;
            }

            object attr = GetPropertyOrFieldValue(dim, "Attributes");
            foreach (string name in names)
            {
                object value = GetPropertyOrFieldValue(attr, name);
                if (TryConvertToVector(value, out up))
                    return true;
            }

            object setAttr = GetPropertyOrFieldValue(dim, "DimensionSetAttributes");
            foreach (string name in names)
            {
                object value = GetPropertyOrFieldValue(setAttr, name);
                if (TryConvertToVector(value, out up))
                    return true;
            }

            return false;
        }

        private static bool TryConvertToVector(object value, out SimpleVector v)
        {
            v = new SimpleVector();
            if (value == null)
                return false;

            try
            {
                object xObj = GetPropertyOrFieldValue(value, "X");
                object yObj = GetPropertyOrFieldValue(value, "Y");
                if (xObj == null || yObj == null)
                    return false;

                v.X = Convert.ToDouble(xObj);
                v.Y = Convert.ToDouble(yObj);
                return v.Length > 0.0001;
            }
            catch
            {
                return false;
            }
        }

        private struct SimpleVector
        {
            public double X;
            public double Y;

            public SimpleVector(double x, double y)
            {
                X = x;
                Y = y;
            }

            public double Length
            {
                get { return Math.Sqrt(X * X + Y * Y); }
            }

            public void Normalize()
            {
                double len = Length;
                if (len < 0.0001)
                    return;
                X /= len;
                Y /= len;
            }
        }

        private static bool TryReadDoublePropertyOrField(object obj, string name, out double value)
        {
            value = 0.0;
            object raw = GetPropertyOrFieldValue(obj, name);
            if (raw == null)
                return false;

            try
            {
                value = Convert.ToDouble(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySetDoublePropertyOrField(object obj, string name, double value)
        {
            if (obj == null)
                return false;
            Type type = obj.GetType();

            try
            {
                PropertyInfo prop = type.GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (prop != null && prop.CanWrite && prop.GetIndexParameters().Length == 0)
                {
                    object converted = Convert.ChangeType(value, prop.PropertyType);
                    prop.SetValue(obj, converted, null);
                    return true;
                }
            }
            catch { }

            try
            {
                FieldInfo field = type.GetField(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (field != null)
                {
                    object converted = Convert.ChangeType(value, field.FieldType);
                    field.SetValue(obj, converted);
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static object GetPropertyOrFieldValue(object obj, string name)
        {
            if (obj == null || string.IsNullOrWhiteSpace(name))
                return null;
            Type type = obj.GetType();

            try
            {
                PropertyInfo prop = type.GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (prop != null && prop.CanRead && prop.GetIndexParameters().Length == 0)
                    return prop.GetValue(obj, null);
            }
            catch { }

            try
            {
                FieldInfo field = type.GetField(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (field != null)
                    return field.GetValue(obj);
            }
            catch { }

            return null;
        }

        private static object InvokeNoArg(object obj, string methodName)
        {
            if (obj == null || string.IsNullOrWhiteSpace(methodName))
                return null;
            try
            {
                MethodInfo method = obj.GetType()
                    .GetMethod(
                        methodName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );
                if (method == null)
                    return null;
                if (method.GetParameters().Length != 0)
                    return null;
                return method.Invoke(obj, null);
            }
            catch
            {
                return null;
            }
        }

        private static int RuntimeHelpersHash(object obj)
        {
            if (obj == null)
                return 0;
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}

namespace TTSK_AutoDim_Plates
{
    // Independent of the external tier solver. Geometry is read once per run;
    // no work-plane change, drawing mutation, or static geometry cache.
    internal static class PHU_InternalDimLayout
    {
        internal enum Location { External, Internal, Uncertain }
        internal const double EdgeTolerance = 1.0;
        private const double Epsilon = 0.000001;

        internal sealed class Body
        {
            // Union of projected outer face loops. Closed bolt holes are deliberately
            // filled: internal means inside the part envelope, not inside steel material.
            // Open notches/concave outer contours remain open.
            internal readonly List<Point[]> Faces = new List<Point[]>();

            internal bool Contains(Point p)
            {
                if (!Finite(p)) return false;
                foreach (Point[] face in Faces)
                {
                    bool inside = false;
                    for (int i = 0, j = face.Length - 1; i < face.Length; j = i++)
                    {
                        Point a = face[j], b = face[i];
                        double dx = b.X - a.X, dy = b.Y - a.Y;
                        double length2 = dx * dx + dy * dy;
                        double t = length2 < Epsilon * Epsilon ? 0 :
                            Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length2));
                        double ex = p.X - a.X - t * dx, ey = p.Y - a.Y - t * dy;
                        if (ex * ex + ey * ey <= Epsilon * Epsilon) return true;
                        if ((a.Y > p.Y) != (b.Y > p.Y)
                            && p.X < a.X + (p.Y - a.Y) * dx / dy) inside = !inside;
                    }
                    if (inside) return true;
                }
                return false;
            }

            internal bool Covers(Point a, Point b)
            {
                if (!Contains(a) || !Contains(b)) return false;
                // Test every interval between contour crossings, not a sampling grid.
                // Thus even a narrow notch between two valid endpoints is detected.
                var cuts = new List<double> { 0, 1 };
                double dx = b.X - a.X, dy = b.Y - a.Y;
                foreach (Point[] face in Faces)
                    for (int i = 0; i < face.Length; i++)
                    {
                        Point c = face[i], d = face[(i + 1) % face.Length];
                        double ex = d.X - c.X, ey = d.Y - c.Y;
                        double cross = dx * ey - dy * ex;
                        if (Math.Abs(cross) < Epsilon) continue;
                        double t = ((c.X - a.X) * ey - (c.Y - a.Y) * ex) / cross;
                        double u = ((c.X - a.X) * dy - (c.Y - a.Y) * dx) / cross;
                        if (t > 0 && t < 1 && u >= -Epsilon && u <= 1 + Epsilon) cuts.Add(t);
                    }
                cuts.Sort();
                for (int i = 1; i < cuts.Count; i++)
                {
                    double t = (cuts[i - 1] + cuts[i]) / 2;
                    if (!Contains(new Point(a.X + t * dx, a.Y + t * dy))) return false;
                }
                return true;
            }
        }

        internal sealed class Span
        {
            internal double Min, Max, Anchor, OriginalLevel, TargetLevel;
        }

        internal static bool Finite(double x) { return !Double.IsNaN(x) && !Double.IsInfinity(x); }
        private static bool Finite(Point p) { return p != null && Finite(p.X) && Finite(p.Y) && Finite(p.Z); }
        internal static Point At(double level, double along, double nx, double ny)
        { return new Point(nx * level - ny * along, ny * level + nx * along); }

        internal static bool CoversLine(Body body, double level, double min, double max, double nx, double ny)
        {
            return body != null && body.Covers(At(level, min, nx, ny), At(level, max, nx, ny))
                && body.Covers(At(level - EdgeTolerance, min, nx, ny), At(level - EdgeTolerance, max, nx, ny))
                && body.Covers(At(level + EdgeTolerance, min, nx, ny), At(level + EdgeTolerance, max, nx, ny));
        }

        internal static Location Classify(Body body, IList<Point> feet, double nx, double ny, double level)
        {
            if (body == null || body.Faces.Count == 0 || feet == null || feet.Count < 2
                || !Finite(level) || !Finite(nx) || !Finite(ny)
                || Math.Abs(nx * nx + ny * ny - 1) > Epsilon) return Location.Uncertain;
            double min = Double.PositiveInfinity, max = Double.NegativeInfinity;
            bool edgeFoot = false;
            foreach (Point p in feet)
            {
                // No guessed ownership for dimensions referring to another part/grid.
                if (!Finite(p) || !body.Contains(p)) return Location.Uncertain;
                double t = -ny * p.X + nx * p.Y;
                min = Math.Min(min, t); max = Math.Max(max, t);
                if (!body.Contains(new Point(p.X - nx * EdgeTolerance, p.Y - ny * EdgeTolerance))
                    || !body.Contains(new Point(p.X + nx * EdgeTolerance, p.Y + ny * EdgeTolerance))) edgeFoot = true;
            }
            if (max - min <= Epsilon) return Location.Uncertain;
            // An edge chain belongs to the external solver, even after a previous
            // internal run collapsed its line into the part.
            if (edgeFoot) return Location.External;
            if (CoversLine(body, level, min, max, nx, ny)) return Location.Internal;
            Point a = At(level, min, nx, ny), b = At(level, max, nx, ny);
            // Entirely outside is external; a straddling line is left untouched.
            if (!body.Contains(a) && !body.Contains(b))
            {
                // Covers is insufficient for intersection; include every edge crossing.
                foreach (Point[] face in body.Faces)
                    for (int i = 0; i < face.Length; i++)
                    {
                        Point c = face[i], d = face[(i + 1) % face.Length];
                        double nc = c.X * nx + c.Y * ny, nd = d.X * nx + d.Y * ny;
                        if (Math.Abs(nd - nc) < Epsilon)
                        {
                            if (Math.Abs(level - nc) <= Epsilon)
                            {
                                double tc = -ny * c.X + nx * c.Y, td = -ny * d.X + nx * d.Y;
                                if (Math.Max(tc, td) >= min && Math.Min(tc, td) <= max) return Location.Uncertain;
                            }
                            continue;
                        }
                        double ratio = (level - nc) / (nd - nc);
                        double t = -ny * (c.X + ratio * (d.X - c.X)) + nx * (c.Y + ratio * (d.Y - c.Y));
                        if (ratio >= 0 && ratio <= 1 && t >= min && t <= max) return Location.Uncertain;
                    }
                return Location.External;
            }
            return Location.Uncertain;
        }

        internal static bool Overlaps(Span a, Span b, double clearance)
        { return Math.Max(a.Min, b.Min) <= Math.Min(a.Max, b.Max) + clearance; }

        internal static bool FeetConnected(IList<Point> a, IList<Point> b, double nx, double ny, double clearance)
        {
            if (a == null || b == null || a.Count < 2 || b.Count < 2
                || a.Any(p => !Finite(p)) || b.Any(p => !Finite(p))) return false;
            // Both projections must overlap; equal measured Y rows alone must not
            // join the two ends of a beam or separate plate connections.
            return Math.Max(a.Min(p => p.X * nx + p.Y * ny), b.Min(p => p.X * nx + p.Y * ny))
                    <= Math.Min(a.Max(p => p.X * nx + p.Y * ny), b.Max(p => p.X * nx + p.Y * ny)) + clearance
                && Math.Max(a.Min(p => -p.X * ny + p.Y * nx), b.Min(p => -p.X * ny + p.Y * nx))
                    <= Math.Min(a.Max(p => -p.X * ny + p.Y * nx), b.Max(p => -p.X * ny + p.Y * nx)) + clearance;
        }

        internal static bool PlanLocal(IList<Span> spans, double spacing, double clearance, IList<Span> obstacles)
        {
            if (spans == null || spans.Count == 0 || !Finite(spacing) || spacing <= 0
                || !Finite(clearance) || clearance < 0) return false;
            double step = Math.Max(spacing, clearance);
            for (int i = 0; i < spans.Count; i++)
            {
                var s = spans[i];
                if (s == null || !Finite(s.Anchor) || !Finite(s.OriginalLevel)
                    || !Finite(s.Min) || !Finite(s.Max) || s.Max <= s.Min) return false;
                s.TargetLevel = i == 0 ? s.OriginalLevel : Math.Max(spans[0].OriginalLevel, s.Anchor + .01);
                if (i == 0) continue; // User-approved local reference tier stays fixed.
                for (int j = 0; j < i; j++)
                    if (Overlaps(s, spans[j], clearance))
                        s.TargetLevel = Math.Max(s.TargetLevel, spans[j].TargetLevel + step);
                // Each collision moves beyond an obstacle, so at most N+1 passes.
                for (int pass = 0; obstacles != null && pass <= obstacles.Count; pass++)
                {
                    double before = s.TargetLevel;
                    foreach (var obstacle in obstacles)
                        if (Overlaps(s, obstacle, clearance) && Math.Abs(s.TargetLevel - obstacle.TargetLevel) < step - .01)
                            s.TargetLevel = obstacle.TargetLevel + step;
                    if (s.TargetLevel == before) break;
                }
                if (!Finite(s.TargetLevel)) return false;
            }
            return true;
        }

        // Caller supplies original visual order. Disjoint local dimensions retain
        // their own anchors. Overlapping spans receive outward tiers as one plan.
        internal static bool Plan(Body body, IList<Span> spans, double nx, double ny,
            double spacing, double clearance)
        {
            if (body == null || spans == null || !Finite(spacing) || spacing <= 0
                || !Finite(clearance) || clearance < 0) return false;
            for (int i = 0; i < spans.Count; i++)
            {
                Span s = spans[i];
                if (s == null || !Finite(s.Anchor) || !Finite(s.Min) || !Finite(s.Max)
                    || !Finite(s.OriginalLevel) || s.Max <= s.Min
                    || (i > 0 && s.OriginalLevel < spans[i - 1].OriginalLevel - .01)) return false;
                s.TargetLevel = s.Anchor + spacing;
                for (int j = 0; j < i; j++)
                    if (Overlaps(s, spans[j], clearance))
                    {
                        if (spacing < clearance) return false;
                        s.TargetLevel = Math.Max(s.TargetLevel, spans[j].TargetLevel + spacing);
                    }
                if (!Finite(s.TargetLevel) || !CoversLine(body, s.TargetLevel, s.Min, s.Max, nx, ny)) return false;
            }
            return true;
        }

        internal static Dictionary<View, Body> ReadBodies(Drawing drawing, IEnumerable<View> views)
        {
            var result = new Dictionary<View, Body>();
            try
            {
                var model = new Model.Model();
                if (!model.GetConnectionStatus()) return result;
                Model.Part main = null;
                var single = drawing as SinglePartDrawing;
                var assembly = drawing as AssemblyDrawing;
                if (single != null) main = model.SelectModelObject(single.PartIdentifier) as Model.Part;
                if (assembly != null)
                {
                    var obj = model.SelectModelObject(assembly.AssemblyIdentifier);
                    var ass = obj as Model.Assembly;
                    main = ass == null ? obj as Model.Part : ass.GetMainPart() as Model.Part;
                }
                if (main == null) return result;
                var toGlobal = model.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal;
                var globalFaces = ReadGlobalFaces(main, toGlobal);
                foreach (View view in views.Distinct())
                {
                    try
                    {
                        bool visible = false;
                        var parts = view.GetAllObjects(typeof(Tekla.Structures.Drawing.Part));
                        while (parts.MoveNext())
                        {
                            var part = parts.Current as Tekla.Structures.Drawing.Part;
                            if (part != null && part.ModelIdentifier.ID == main.Identifier.ID) visible = true;
                        }
                        if (!visible) continue;
                        var body = ProjectBody(globalFaces, view);
                        if (body.Faces.Count > 0) result.Add(view, body);
                    }
                    catch { /* Missing geometry is uncertain, never a fabricated box. */ }
                }
            }
            catch { result.Clear(); }
            return result;
        }
        private static List<List<Point[]>> ReadGlobalFaces(Model.Part part, Matrix toGlobal)
        {
                var globalFaces = new List<List<Point[]>>();
                var faces = part.GetSolid().GetFaceEnumerator();
                while (faces.MoveNext())
                {
                    var face = faces.Current as Tekla.Structures.Solid.Face;
                    if (face == null) throw new InvalidOperationException("Unreadable solid face");
                    var loops = face.GetLoopEnumerator();
                    var faceLoops = new List<Point[]>();
                    while (loops.MoveNext())
                    {
                        var loop = loops.Current as Tekla.Structures.Solid.Loop;
                        if (loop == null) throw new InvalidOperationException("Unreadable solid loop");
                        var points = new List<Point>();
                        var vertices = loop.GetVertexEnumerator();
                        while (vertices.MoveNext())
                        {
                            var p = toGlobal.Transform(vertices.Current);
                            if (!Finite(p)) throw new InvalidOperationException("Invalid solid vertex");
                            points.Add(p);
                        }
                        if (points.Count < 3) throw new InvalidOperationException("Incomplete solid loop");
                        faceLoops.Add(points.ToArray());
                    }
                    globalFaces.Add(faceLoops);
                }
            return globalFaces;
        }

        private static Body ProjectBody(List<List<Point[]>> globalFaces, View view)
        {
                        var matrix = MatrixFactory.ToCoordinateSystem(view.DisplayCoordinateSystem);
                        var body = new Body();
                        foreach (var face in globalFaces)
                        {
                            Point[] outer = null;
                            double largest = Epsilon;
                            foreach (Point[] loop in face)
                            {
                                Point[] projected = loop.Select(matrix.Transform).ToArray();
                                if (projected.Any(p => !Finite(p))) throw new InvalidOperationException("Invalid view projection");
                                double area = 0;
                                for (int i = 0; i < projected.Length; i++)
                                {
                                    Point a = projected[i], b = projected[(i + 1) % projected.Length];
                                    area += (a.X - projected[0].X) * (b.Y - projected[0].Y)
                                        - (b.X - projected[0].X) * (a.Y - projected[0].Y);
                                }
                                if (Math.Abs(area) > largest) { largest = Math.Abs(area); outer = projected; }
                            }
                            if (outer != null) body.Faces.Add(outer);
                        }
            return body;
        }


    }
}
