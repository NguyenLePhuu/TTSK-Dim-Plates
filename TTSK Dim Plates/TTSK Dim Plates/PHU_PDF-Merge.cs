// BẢN BUILD CỐ ĐỊNH 2026-07-19 - CHẾ ĐỘ IN / GỘP PDF KHI HOVER
// YÊU CẦU NUGET: PDFsharp-GDI 6.2.4
// - Chế độ Print: chỉ xuất từng file PDF riêng lẻ. Không thực thi bất kỳ mã gộp PDF nào.
// - Chế độ Merge: xuất các file PDF riêng lẻ, chỉ gộp những file được tạo trong lần chạy hiện tại,
//   kiểm tra file PDF đã gộp, sau đó chỉ xóa các file PDF riêng lẻ của lần chạy hiện tại.
// - Toàn bộ file đầu ra được lưu tại Desktop\TTSK_PDF\DD-MM-YY.
// - Tên các file PDF riêng lẻ vẫn giữ nguyên quy tắc đặt tên MARK và REV hiện tại.
// - Chỉ tên file PDF đã gộp mới sử dụng UDA DR_DRAWN_BY của bản vẽ từ Document Manager.
// - Nếu trường Drawn By để trống, tên file PDF đã gộp mặc định là TTSK_Merge.
// - Các file PDF tồn tại từ những lần chạy trước sẽ không bao giờ bị gộp hoặc bị xóa.
// - Chế độ Merge File hoạt động độc lập với Tekla: người dùng chọn các file PDF bên ngoài,
//   các file này được gộp thành TTSK_Merge.pdf, được kiểm tra, sau đó chỉ các file nguồn đã chọn mới bị xóa.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Text;
using System.Windows.Forms;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;
using Tekla.Structures.DPMPrinter;

namespace TTSK_AutoDim_Plates
{
    public static class DrawingPdfPrinter
    {
        private const string OutputSubFolder = "TTSK_PDF";
        private const string DrawnByUdaName = "DR_DRAWN_BY";
        private const int OutputWaitTimeoutMilliseconds = 60000;
        private const int OutputWaitIntervalMilliseconds = 250;
        private const int DelayBetweenDrawingsMilliseconds = 150;

        static DrawingPdfPrinter()
        {
            // Đăng ký AssemblyResolve đảm bảo nạp đúng DPMPrinter.dll từ thư mục bin Tekla khi ứng dụng chạy ở môi trường khác
            try
            {
                AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
                {
                    try
                    {
                        string assemblySimpleName = new AssemblyName(args.Name).Name;
                        if (assemblySimpleName.StartsWith("Tekla", StringComparison.OrdinalIgnoreCase)
                            || assemblySimpleName.StartsWith("DPMPrinter", StringComparison.OrdinalIgnoreCase)
                            || assemblySimpleName.StartsWith("DotNetKit", StringComparison.OrdinalIgnoreCase)
                            || assemblySimpleName.StartsWith("Trimble", StringComparison.OrdinalIgnoreCase))
                        {
                            string binPath = @"C:\Program Files\Tekla Structures\2025.0\bin";
                            string targetPath = Path.Combine(binPath, assemblySimpleName + ".dll");
                            if (File.Exists(targetPath))
                            {
                                return System.Reflection.Assembly.LoadFrom(targetPath);
                            }
                        }
                    }
                    catch { }
                    return null;
                };
            }
            catch { }
        }

        public static DrawingPdfPrintResult PrintToSeparatePdfs(
            IList<DrawingPdfPrintJob> jobs,
            IWin32Window owner
        )
        {
            return ExecutePdfWorkflow(jobs, owner, false);
        }

        public static DrawingPdfPrintResult PrintAndMergePdfs(
            IList<DrawingPdfPrintJob> jobs,
            IWin32Window owner
        )
        {
            return ExecutePdfWorkflow(jobs, owner, true);
        }

        private static DrawingPdfPrintResult ExecutePdfWorkflow(
            IList<DrawingPdfPrintJob> jobs,
            IWin32Window owner,
            bool mergeAndDeleteChildren
        )
        {
            DrawingPdfPrintResult result = new DrawingPdfPrintResult();
            string modelPath = null;
            string outputDirectory = null;
            DateTime printRunDate = DateTime.Now;

            result.MergeRequested = mergeAndDeleteChildren;

            try
            {
                List<DrawingPdfPrintJob> validJobs =
                    jobs == null
                        ? new List<DrawingPdfPrintJob>()
                        : jobs.Where(job => job != null && job.Drawing != null).ToList();

                result.RequestedDrawingCount = validJobs.Count;

                if (validJobs.Count == 0)
                {
                    result.Message =
                        "Không có drawing hợp lệ để xuất PDF. "
                        + "Hãy chọn drawing trong Document Manager rồi thử lại.";
                    return result;
                }

                Model model = new Model();
                if (!model.GetConnectionStatus())
                {
                    result.Message = "Không kết nối được với model Tekla Structures đang mở.";
                    return result;
                }

                modelPath = ResolveModelPath(model);
                outputDirectory = ResolveDesktopDatedOutputFolder(printRunDate);

                if (string.IsNullOrWhiteSpace(outputDirectory))
                {
                    result.Message = "Không xác định được thư mục Desktop để lưu PDF.";
                    return result;
                }

                Directory.CreateDirectory(outputDirectory);

                result.OutputDirectory = outputDirectory;
                result.OutputFilePath = outputDirectory;

                DrawingHandler drawingHandler = new DrawingHandler();
                if (!drawingHandler.GetConnectionStatus())
                {
                    result.Message =
                        "Drawing API chưa kết nối đúng với Tekla Structures. "
                        + "Hãy kiểm tra Tekla đang mở model và ứng dụng đang chạy bằng x64.";
                    return result;
                }

                Drawing activeDrawing = drawingHandler.GetActiveDrawing();
                if (activeDrawing != null)
                {
                    // Đóng và lưu bản vẽ đang mở theo yêu cầu để giải phóng Document Manager và tiến hành in
                    try
                    {
                        drawingHandler.CloseActiveDrawing(true);
                    }
                    catch
                    {
                        try { drawingHandler.CloseActiveDrawing(); } catch { }
                    }
                }


                for (int index = 0; index < validJobs.Count; index++)
                {
                    DrawingPdfPrintJob job = validJobs[index];
                    DrawingPdfItemResult itemResult = new DrawingPdfItemResult();
                    itemResult.Index = index;
                    itemResult.Mark = NormalizeDisplayText(job.Mark, "Drawing " + (index + 1));
                    itemResult.Revision = NormalizeDisplayText(job.Revision, string.Empty);
                    itemResult.DrawnBy = NormalizeDisplayText(
                        job.DrawnBy,
                        GetDrawingDrawnBy(job.Drawing)
                    );
                    itemResult.DrawingName = string.IsNullOrWhiteSpace(job.DrawingName)
                        ? (job.Drawing != null ? job.Drawing.Name : string.Empty)
                        : job.DrawingName;

                    string baseFileName = BuildDrawingFileBaseName(
                        itemResult.Mark,
                        itemResult.Revision
                    );

                    string outputFilePath = BuildUniqueOutputPath(
                        outputDirectory,
                        baseFileName,
                        ".pdf"
                    );

                    itemResult.OutputFilePath = outputFilePath;
                    result.ItemResults.Add(itemResult);

                    try
                    {
                        Application.DoEvents();

                        DPMPrinterAttributes printAttributes = CreatePdfPrintAttributes(
                            outputFilePath,
                            job.Drawing
                        );

                        // Thực thi in PDF chuẩn Tekla API với đầy đủ màu sắc (Full Color)
                        bool printSucceeded = false;
                        string printErrorMessage = null;
                        try
                        {
                            printSucceeded = PrintDrawingWithTeklaApi(
                                drawingHandler,
                                job.Drawing,
                                printAttributes,
                                outputFilePath,
                                out printErrorMessage
                            );
                        }
                        catch (Exception printEx)
                        {
                            printSucceeded = false;
                            printErrorMessage = "Ngoại lệ khi in: " + GetDeepestExceptionMessage(printEx);
                        }

                        itemResult.TeklaPrintReturnedSuccess = printSucceeded;

                        bool outputCreated = WaitForCompletedPdf(
                            outputFilePath,
                            OutputWaitTimeoutMilliseconds
                        );

                        itemResult.OutputFileVerified = outputCreated;

                        if (printSucceeded && outputCreated)
                        {
                            itemResult.Success = true;
                            itemResult.Message = "Đã tạo PDF thành công.";
                            result.SuccessfulDrawingCount++;
                        }
                        else
                        {
                            itemResult.Success = false;
                            if (!string.IsNullOrWhiteSpace(printErrorMessage))
                            {
                                itemResult.Message = printErrorMessage;
                            }
                            else
                            {
                                itemResult.Message = BuildItemFailureMessage(
                                    printSucceeded,
                                    outputCreated
                                );
                            }
                            result.FailedDrawingCount++;
                        }
                    }
                    catch (Exception itemException)
                    {
                        itemResult.Success = false;
                        itemResult.Message =
                            "Print drawing lỗi: " + GetDeepestExceptionMessage(itemException);
                        itemResult.ExceptionDetails = itemException.ToString();
                        result.FailedDrawingCount++;
                    }

                    Application.DoEvents();
                    Thread.Sleep(DelayBetweenDrawingsMilliseconds);
                }

                result.DrawingCount = result.SuccessfulDrawingCount;

                List<DrawingPdfItemResult> successfulItems = result
                    .ItemResults.Where(item =>
                        item != null
                        && item.Success
                        && item.OutputFileVerified
                        && !string.IsNullOrWhiteSpace(item.OutputFilePath)
                        && File.Exists(item.OutputFilePath)
                    )
                    .OrderBy(item => item.Index)
                    .ToList();

                if (mergeAndDeleteChildren)
                {
                    RunMergeAndCleanup(successfulItems, outputDirectory, result);
                }

                bool allIndividualPdfsSucceeded =
                    result.SuccessfulDrawingCount == validJobs.Count
                    && result.FailedDrawingCount == 0;

                if (mergeAndDeleteChildren)
                {
                    result.Success =
                        allIndividualPdfsSucceeded
                        && result.MergeAttempted
                        && result.MergeSuccess
                        && result.CleanupAttempted
                        && result.CleanupSuccess;
                }
                else
                {
                    result.Success = allIndividualPdfsSucceeded;
                }

                result.PartialSuccess = result.SuccessfulDrawingCount > 0 && !result.Success;

                BuildFinalResult(validJobs.Count, mergeAndDeleteChildren, modelPath, result);

                if (result.SuccessfulDrawingCount > 0 || result.MergeSuccess)
                    TryOpenOutputFolder(outputDirectory);

                return result;
            }
            catch (Exception ex)
            {
                result.OutputDirectory = outputDirectory;
                result.OutputFilePath = outputDirectory;
                result.LogFilePath = FindLatestDpmPrinterLog(modelPath);
                result.DiagnosticDetails = ex.ToString() + AppendLogTail(result.LogFilePath, 20);
                result.Message =
                    (mergeAndDeleteChildren ? "Merge PDF lỗi: " : "Print PDF lỗi: ")
                    + GetDeepestExceptionMessage(ex);
                result.DiagnosticFilePath = WriteDiagnosticFile(outputDirectory, result);
                return result;
            }
        }

        private static void RunMergeAndCleanup(
            IList<DrawingPdfItemResult> successfulItems,
            string outputDirectory,
            DrawingPdfPrintResult result
        )
        {
            if (successfulItems == null || successfulItems.Count == 0)
            {
                result.MergeAttempted = false;
                result.MergeSuccess = false;
                result.MergeMessage = "Không có PDF con thành công trong phiên hiện tại để gộp.";
                return;
            }

            result.MergeAttempted = true;

            try
            {
                List<string> currentRunPdfFiles = successfulItems
                    .Select(item => item.OutputFilePath)
                    .ToList();

                string mergedBaseName = ResolveMergedFileBaseName(successfulItems);

                string mergedFilePath = BuildUniqueOutputPath(
                    outputDirectory,
                    mergedBaseName,
                    ".pdf"
                );

                MergePdfFiles(currentRunPdfFiles, mergedFilePath);

                bool mergedFileVerified = WaitForCompletedPdf(
                    mergedFilePath,
                    OutputWaitTimeoutMilliseconds
                );

                if (!mergedFileVerified)
                {
                    throw new IOException("PDF đã được gộp nhưng không xác minh được file đầu ra.");
                }

                result.MergeSuccess = true;
                result.MergedFilePath = mergedFilePath;
                result.MergeMessage =
                    "Đã gộp " + currentRunPdfFiles.Count + " PDF con thành một PDF nhiều trang.";

                DeleteCurrentRunChildFiles(successfulItems, result);
            }
            catch (Exception mergeException)
            {
                result.MergeSuccess = false;
                result.MergeMessage = "Gộp PDF lỗi: " + GetDeepestExceptionMessage(mergeException);
                result.MergeExceptionDetails = mergeException.ToString();
            }
        }

        private static void DeleteCurrentRunChildFiles(
            IList<DrawingPdfItemResult> successfulItems,
            DrawingPdfPrintResult result
        )
        {
            result.CleanupAttempted = true;
            List<string> cleanupErrors = new List<string>();

            foreach (DrawingPdfItemResult item in successfulItems)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.OutputFilePath))
                    continue;

                try
                {
                    if (File.Exists(item.OutputFilePath))
                        File.Delete(item.OutputFilePath);

                    if (File.Exists(item.OutputFilePath))
                    {
                        cleanupErrors.Add("Không xóa được: " + item.OutputFilePath);
                        continue;
                    }

                    item.ChildFileDeleted = true;
                    result.DeletedChildFileCount++;
                }
                catch (Exception deleteException)
                {
                    cleanupErrors.Add(
                        item.OutputFilePath + " | " + GetDeepestExceptionMessage(deleteException)
                    );
                }
            }

            result.CleanupSuccess = cleanupErrors.Count == 0;

            if (result.CleanupSuccess)
            {
                result.CleanupMessage =
                    "Đã xóa "
                    + result.DeletedChildFileCount
                    + " PDF con vừa tạo trong phiên Merge này.";
            }
            else
            {
                result.CleanupMessage =
                    "PDF tổng đã được tạo nhưng còn "
                    + cleanupErrors.Count
                    + " PDF con không xóa được."
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, cleanupErrors.ToArray());
            }
        }

        private static void BuildFinalResult(
            int requestedCount,
            bool mergeAndDeleteChildren,
            string modelPath,
            DrawingPdfPrintResult result
        )
        {
            if (result.Success)
            {
                if (mergeAndDeleteChildren)
                {
                    result.Message =
                        "Đã tạo PDF tổng và xóa "
                        + result.DeletedChildFileCount
                        + " PDF con của phiên này.";
                }
                else
                {
                    result.Message =
                        "Đã tạo " + result.SuccessfulDrawingCount + " file PDF riêng thành công.";
                }

                return;
            }

            if (result.FailedDrawingCount > 0)
                result.LogFilePath = FindLatestDpmPrinterLog(modelPath);

            string logTail = ReadLogTail(result.LogFilePath, 20);
            List<string> diagnostics = new List<string>();

            if (!string.IsNullOrWhiteSpace(result.MergeExceptionDetails))
            {
                diagnostics.Add(
                    "PDF MERGE ERROR" + Environment.NewLine + result.MergeExceptionDetails
                );
            }

            if (!string.IsNullOrWhiteSpace(logTail))
            {
                diagnostics.Add("DPMPRINTER LOG TAIL" + Environment.NewLine + logTail);
            }

            result.DiagnosticDetails = string.Join(
                Environment.NewLine + Environment.NewLine,
                diagnostics.ToArray()
            );

            if (!mergeAndDeleteChildren)
            {
                result.Message =
                    "Đã tạo "
                    + result.SuccessfulDrawingCount
                    + "/"
                    + requestedCount
                    + " file PDF riêng. Lỗi: "
                    + result.FailedDrawingCount
                    + " drawing.";
            }
            else if (!result.MergeSuccess)
            {
                result.Message =
                    "Đã tạo "
                    + result.SuccessfulDrawingCount
                    + "/"
                    + requestedCount
                    + " PDF con nhưng không tạo được PDF tổng. "
                    + result.MergeMessage;
            }
            else if (!result.CleanupSuccess)
            {
                result.Message =
                    "PDF tổng đã được tạo nhưng chưa xóa hết PDF con. " + result.CleanupMessage;
            }
            else
            {
                result.Message =
                    "Đã tạo PDF tổng từ "
                    + result.SuccessfulDrawingCount
                    + "/"
                    + requestedCount
                    + " drawing. Có "
                    + result.FailedDrawingCount
                    + " drawing xuất lỗi.";
            }

            result.DiagnosticFilePath = WriteDiagnosticFile(result.OutputDirectory, result);
        }

        public static string GetDrawingDrawnBy(Drawing drawing)
        {
            if (drawing == null)
                return string.Empty;

            string value;

            if (TryGetDrawingStringUserProperty(drawing, DrawnByUdaName, out value))
            {
                return value;
            }

            // Compatibility fallback for custom environments that expose the same
            // Document Manager value without the standard DR_ prefix.
            if (TryGetDrawingStringUserProperty(drawing, "DRAWN_BY", out value))
            {
                return value;
            }

            return string.Empty;
        }

        private static bool TryGetDrawingStringUserProperty(
            Drawing drawing,
            string propertyName,
            out string value
        )
        {
            value = string.Empty;

            if (drawing == null || string.IsNullOrWhiteSpace(propertyName))
                return false;

            try
            {
                string propertyValue = string.Empty;
                bool found = drawing.GetUserProperty(propertyName, ref propertyValue);

                if (found && !string.IsNullOrWhiteSpace(propertyValue))
                {
                    value = propertyValue.Trim();
                    return true;
                }
            }
            catch { }

            try
            {
                drawing.Select();

                string propertyValue = string.Empty;
                bool found = drawing.GetUserProperty(propertyName, ref propertyValue);

                if (found && !string.IsNullOrWhiteSpace(propertyValue))
                {
                    value = propertyValue.Trim();
                    return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Compatibility wrapper. The old single-PDF call now uses Merge mode.
        /// </summary>
        public static DrawingPdfPrintResult PrintToSinglePdf(
            IList<Drawing> drawings,
            IWin32Window owner
        )
        {
            List<DrawingPdfPrintJob> jobs = new List<DrawingPdfPrintJob>();

            if (drawings != null)
            {
                for (int index = 0; index < drawings.Count; index++)
                {
                    Drawing drawing = drawings[index];
                    if (drawing == null)
                        continue;

                    jobs.Add(
                        new DrawingPdfPrintJob
                        {
                            Drawing = drawing,
                            Mark = TryGetDrawingDisplayText(drawing, index),
                            Revision = string.Empty,
                            DrawnBy = GetDrawingDrawnBy(drawing)
                        }
                    );
                }
            }

            return PrintAndMergePdfs(jobs, owner);
        }

        private static DPMPrinterAttributes CreatePdfPrintAttributes(
            string outputFilePath,
            Drawing drawing
        )
        {
            DPMPrinterAttributes printAttributes = new DPMPrinterAttributes();
            printAttributes.OutputType = DotPrintOutputType.PDF;
            printAttributes.OutputFileName = outputFilePath;
            printAttributes.OpenFileWhenFinished = false;
            printAttributes.Orientation = DotPrintOrientationType.Auto;
            printAttributes.ColorMode = DotPrintColor.Color;
            printAttributes.PaperSize = GetPdfPaperSize(drawing);
            printAttributes.ScalingMethod = DotPrintScalingType.Auto;
            printAttributes.PrintToMultipleSheet = DotPrintToMultipleSheet.Off;
            return printAttributes;
        }

        /// <summary>
        /// Khóa định danh bản vẽ chính xác và duy nhất từ Document Manager / Drawing List (Source of Truth).
        /// Bao gồm cả định danh nội tại (Drawing Database Identifier), định danh liên kết Model Object,
        /// số sheet, loại bản vẽ (Assembly/SinglePart/CastUnit/GA), Mark và Name.
        /// </summary>
        internal sealed class DrawingSelectionKey
        {
            public string DrawingType { get; set; }
            public int DrawingId { get; set; }
            public int DrawingId2 { get; set; }
            public string DrawingGuid { get; set; }
            public int ModelObjectId { get; set; }
            public int ModelObjectId2 { get; set; }
            public string ModelObjectGuid { get; set; }
            public int SheetNumber { get; set; }
            public string Mark { get; set; }
            public string Name { get; set; }

            public override string ToString()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Type={0}|DrawId={1}|DrawGuid={2}|ModelId={3}|Sheet={4}|Mark={5}|Name={6}",
                    DrawingType ?? string.Empty,
                    DrawingId,
                    DrawingGuid ?? string.Empty,
                    ModelObjectId,
                    SheetNumber,
                    Mark ?? string.Empty,
                    Name ?? string.Empty
                );
            }
        }

        /// <summary>
        /// Lấy Identifier độc nhất của bản vẽ từ Drawing Database (kế thừa từ DatabaseObject).
        /// </summary>
        private static Identifier GetDatabaseObjectIdentifier(Drawing drawing)
        {
            if (drawing == null)
                return null;

            try
            {
                PropertyInfo prop = typeof(DatabaseObject).GetProperty(
                    "Identifier",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (prop != null)
                {
                    Identifier id = prop.GetValue(drawing, null) as Identifier;
                    if (id != null)
                        return id;
                }
            }
            catch { }

            try
            {
                PropertyInfo prop = drawing.GetType().GetProperty(
                    "Identifier",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (prop != null)
                {
                    Identifier id = prop.GetValue(drawing, null) as Identifier;
                    if (id != null)
                        return id;
                }
            }
            catch { }

            try
            {
                FieldInfo field = typeof(DatabaseObject).GetField(
                    "_Identifier",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (field != null)
                {
                    Identifier id = field.GetValue(drawing) as Identifier;
                    if (id != null)
                        return id;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Lấy Model Object Identifier tương ứng với bản vẽ (AssemblyIdentifier / PartIdentifier / CastUnitIdentifier).
        /// </summary>
        private static Identifier GetModelObjectIdentifier(Drawing drawing)
        {
            if (drawing == null)
                return null;

            try
            {
                if (drawing is AssemblyDrawing)
                    return ((AssemblyDrawing)drawing).AssemblyIdentifier;
                if (drawing is SinglePartDrawing)
                    return ((SinglePartDrawing)drawing).PartIdentifier;
                if (drawing is CastUnitDrawing)
                    return ((CastUnitDrawing)drawing).CastUnitIdentifier;
            }
            catch { }

            try
            {
                PropertyInfo prop = typeof(Drawing).GetProperty(
                    "ModelObjectIdentifier",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (prop != null)
                {
                    Identifier id = prop.GetValue(drawing, null) as Identifier;
                    if (id != null)
                        return id;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Lấy số SheetNumber của bản vẽ.
        /// </summary>
        private static int GetDrawingSheetNumber(Drawing drawing)
        {
            if (drawing == null)
                return 0;

            try
            {
                if (drawing is AssemblyDrawing)
                    return ((AssemblyDrawing)drawing).SheetNumber;
                if (drawing is SinglePartDrawing)
                    return ((SinglePartDrawing)drawing).SheetNumber;
                if (drawing is CastUnitDrawing)
                    return ((CastUnitDrawing)drawing).SheetNumber;
            }
            catch { }

            try
            {
                FieldInfo field = typeof(Drawing).GetField(
                    "_SheetNumber",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (field != null)
                {
                    object val = field.GetValue(drawing);
                    if (val is int)
                        return (int)val;
                }
            }
            catch { }

            return 0;
        }

        /// <summary>
        /// Xây dựng khóa định danh đầy đủ DrawingSelectionKey từ chính đối tượng drawing nguồn (Source of Truth).
        /// </summary>
        private static DrawingSelectionKey BuildDrawingSelectionKey(Drawing drawing)
        {
            if (drawing == null)
                return null;

            DrawingSelectionKey key = new DrawingSelectionKey();
            key.DrawingType = drawing.GetType().Name;
            key.Mark = drawing.Mark ?? string.Empty;
            key.Name = drawing.Name ?? string.Empty;

            Identifier drawId = GetDatabaseObjectIdentifier(drawing);
            if (drawId != null)
            {
                key.DrawingId = drawId.ID;
                key.DrawingId2 = drawId.ID2;
                key.DrawingGuid = drawId.GUID.ToString();
            }

            Identifier modelId = GetModelObjectIdentifier(drawing);
            if (modelId != null)
            {
                key.ModelObjectId = modelId.ID;
                key.ModelObjectId2 = modelId.ID2;
                key.ModelObjectGuid = modelId.GUID.ToString();
            }

            key.SheetNumber = GetDrawingSheetNumber(drawing);
            return key;
        }

        /// <summary>
        /// Mã hóa DrawingSelectionKey thành chuỗi Base64 an toàn để truyền qua command-line argument.
        /// </summary>
        private static string EncodeDrawingSelectionKey(DrawingSelectionKey key)
        {
            if (key == null)
                return string.Empty;

            StringBuilder sb = new StringBuilder();
            sb.Append("Type=").Append(Uri.EscapeDataString(key.DrawingType ?? string.Empty)).Append(";");
            sb.Append("DrawId=").Append(key.DrawingId.ToString(CultureInfo.InvariantCulture)).Append(";");
            sb.Append("DrawId2=").Append(key.DrawingId2.ToString(CultureInfo.InvariantCulture)).Append(";");
            sb.Append("DrawGuid=").Append(Uri.EscapeDataString(key.DrawingGuid ?? string.Empty)).Append(";");
            sb.Append("ModelId=").Append(key.ModelObjectId.ToString(CultureInfo.InvariantCulture)).Append(";");
            sb.Append("ModelId2=").Append(key.ModelObjectId2.ToString(CultureInfo.InvariantCulture)).Append(";");
            sb.Append("ModelGuid=").Append(Uri.EscapeDataString(key.ModelObjectGuid ?? string.Empty)).Append(";");
            sb.Append("Sheet=").Append(key.SheetNumber.ToString(CultureInfo.InvariantCulture)).Append(";");
            sb.Append("Mark=").Append(Uri.EscapeDataString(key.Mark ?? string.Empty)).Append(";");
            sb.Append("Name=").Append(Uri.EscapeDataString(key.Name ?? string.Empty));

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        /// <summary>
        /// Giải mã chuỗi Base64 thành đối tượng DrawingSelectionKey.
        /// </summary>
        private static DrawingSelectionKey DecodeDrawingSelectionKey(string base64Payload)
        {
            if (string.IsNullOrWhiteSpace(base64Payload))
                return null;

            string rawText = string.Empty;
            try
            {
                rawText = Encoding.UTF8.GetString(Convert.FromBase64String(base64Payload));
            }
            catch
            {
                rawText = base64Payload;
            }

            DrawingSelectionKey key = new DrawingSelectionKey();

            if (!string.IsNullOrEmpty(rawText) && rawText.Contains("=") && rawText.Contains(";"))
            {
                string[] pairs = rawText.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string pair in pairs)
                {
                    int eqIndex = pair.IndexOf('=');
                    if (eqIndex <= 0)
                        continue;

                    string name = pair.Substring(0, eqIndex).Trim();
                    string val = Uri.UnescapeDataString(pair.Substring(eqIndex + 1));

                    if (string.Equals(name, "Type", StringComparison.OrdinalIgnoreCase))
                        key.DrawingType = val;
                    else if (string.Equals(name, "DrawId", StringComparison.OrdinalIgnoreCase))
                    {
                        int id;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                            key.DrawingId = id;
                    }
                    else if (string.Equals(name, "DrawId2", StringComparison.OrdinalIgnoreCase))
                    {
                        int id2;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out id2))
                            key.DrawingId2 = id2;
                    }
                    else if (string.Equals(name, "DrawGuid", StringComparison.OrdinalIgnoreCase))
                        key.DrawingGuid = val;
                    else if (string.Equals(name, "ModelId", StringComparison.OrdinalIgnoreCase))
                    {
                        int mid;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out mid))
                            key.ModelObjectId = mid;
                    }
                    else if (string.Equals(name, "ModelId2", StringComparison.OrdinalIgnoreCase))
                    {
                        int mid2;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out mid2))
                            key.ModelObjectId2 = mid2;
                    }
                    else if (string.Equals(name, "ModelGuid", StringComparison.OrdinalIgnoreCase))
                        key.ModelObjectGuid = val;
                    else if (string.Equals(name, "Sheet", StringComparison.OrdinalIgnoreCase))
                    {
                        int sheet;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out sheet))
                            key.SheetNumber = sheet;
                    }
                    else if (string.Equals(name, "Mark", StringComparison.OrdinalIgnoreCase))
                        key.Mark = val;
                    else if (string.Equals(name, "Name", StringComparison.OrdinalIgnoreCase))
                        key.Name = val;
                }
            }
            else
            {
                // Fallback nếu truyền chuỗi Mark thuần túy từ phiên bản cũ
                key.Mark = rawText;
            }

            return key;
        }

        /// <summary>
        /// Kiểm tra 2 đối tượng Drawing có cùng một thực thể duy nhất trong Tekla Drawing Database hay không.
        /// </summary>
        private static bool IsSameDrawingIdentity(Drawing first, Drawing second)
        {
            if (first == null || second == null)
                return false;
            if (object.ReferenceEquals(first, second))
                return true;

            // 1. So khớp bằng Database Identifier ID nếu cả hai có ID > 0 (chính xác tuyệt đối)
            Identifier id1 = GetDatabaseObjectIdentifier(first);
            Identifier id2 = GetDatabaseObjectIdentifier(second);
            if (id1 != null && id2 != null && id1.ID > 0 && id2.ID > 0)
            {
                return id1.ID == id2.ID;
            }

            // 2. So khớp bằng ModelObjectId + SheetNumber + DrawingType + Mark
            Identifier mid1 = GetModelObjectIdentifier(first);
            Identifier mid2 = GetModelObjectIdentifier(second);
            if (mid1 != null && mid2 != null && mid1.ID > 0 && mid2.ID > 0)
            {
                if (mid1.ID == mid2.ID)
                {
                    int s1 = GetDrawingSheetNumber(first);
                    int s2 = GetDrawingSheetNumber(second);
                    if (s1 == s2 && string.Equals(first.GetType().Name, second.GetType().Name, StringComparison.OrdinalIgnoreCase))
                    {
                        return string.Equals(first.Mark, second.Mark, StringComparison.Ordinal);
                    }
                }
                return false;
            }

            // 3. So khớp chặt chẽ bằng DrawingType + Mark + Name chính xác
            return string.Equals(first.GetType().Name, second.GetType().Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.Mark, second.Mark, StringComparison.Ordinal)
                && string.Equals(first.Name, second.Name, StringComparison.Ordinal);
        }

        /// <summary>
        /// Giải quyết và tìm duy nhất một bản vẽ khớp chính xác với DrawingSelectionKey.
        /// Chỉ quét tập bản vẽ người dùng đang chọn trong Document Manager, tuyệt đối không break sớm.
        /// Chỉ chấp nhận khi số ứng viên tìm thấy chính xác là 1 (candidates.Count == 1).
        /// Nếu không tìm thấy (0) hoặc mơ hồ danh tính (> 1) thì từ chối in để bảo vệ người dùng:
        /// "Thà không in còn hơn in nhầm bản vẽ".
        /// </summary>
        private static Drawing ResolveDrawingBySelectionKey(
            DrawingHandler drawingHandler,
            DrawingSelectionKey key,
            out string resolveDiag
        )
        {
            resolveDiag = string.Empty;
            if (drawingHandler == null || key == null)
            {
                resolveDiag = "DrawingHandler hoặc SelectionKey là null.";
                return null;
            }

            DrawingEnumerator enumerator = drawingHandler.GetDrawingSelector().GetSelected();
            List<Drawing> candidates = new List<Drawing>();

            while (enumerator.MoveNext())
            {
                Drawing current = enumerator.Current;
                if (current == null)
                    continue;

                // BƯỚC 1: PRIMARY IDENTITY - Drawing Database Identifier ID (duy nhất trong toàn bộ Drawing DB)
                if (key.DrawingId > 0)
                {
                    Identifier curId = GetDatabaseObjectIdentifier(current);
                    if (curId != null && curId.ID == key.DrawingId)
                    {
                        bool typeMatches = string.IsNullOrEmpty(key.DrawingType) ||
                            string.Equals(current.GetType().Name, key.DrawingType, StringComparison.OrdinalIgnoreCase);
                        bool markMatches = string.IsNullOrEmpty(key.Mark) ||
                            string.Equals(current.Mark, key.Mark, StringComparison.Ordinal);

                        if (typeMatches && markMatches)
                        {
                            candidates.Add(current);
                            continue;
                        }
                    }
                    continue;
                }

                // BƯỚC 2: SECONDARY IDENTITY - ModelObjectId + SheetNumber
                if (key.ModelObjectId > 0)
                {
                    Identifier curModelId = GetModelObjectIdentifier(current);
                    if (curModelId != null && curModelId.ID == key.ModelObjectId)
                    {
                        int curSheet = GetDrawingSheetNumber(current);
                        bool typeMatches = string.IsNullOrEmpty(key.DrawingType) ||
                            string.Equals(current.GetType().Name, key.DrawingType, StringComparison.OrdinalIgnoreCase);
                        bool markMatches = string.IsNullOrEmpty(key.Mark) ||
                            string.Equals(current.Mark, key.Mark, StringComparison.Ordinal);

                        if (curSheet == key.SheetNumber && typeMatches && markMatches)
                        {
                            candidates.Add(current);
                            continue;
                        }
                    }
                    continue;
                }

                // BƯỚC 3: TERTIARY FALLBACK - So sánh chính xác DrawingType + Mark + Name (Ordinal)
                if (!string.IsNullOrEmpty(key.Mark) && string.Equals(current.Mark, key.Mark, StringComparison.Ordinal))
                {
                    bool typeMatches = string.IsNullOrEmpty(key.DrawingType) ||
                        string.Equals(current.GetType().Name, key.DrawingType, StringComparison.OrdinalIgnoreCase);
                    bool nameMatches = string.IsNullOrEmpty(key.Name) ||
                        string.Equals(current.Name, key.Name, StringComparison.Ordinal);

                    if (typeMatches && nameMatches)
                    {
                        candidates.Add(current);
                    }
                }
            }

            if (candidates.Count == 1)
            {
                resolveDiag = string.Format(
                    CultureInfo.InvariantCulture,
                    "Đã xác định duy nhất bản vẽ: Type={0}, Mark={1}, ID={2}",
                    candidates[0].GetType().Name,
                    candidates[0].Mark,
                    key.DrawingId
                );
                return candidates[0];
            }

            if (candidates.Count == 0)
            {
                resolveDiag = string.Format(
                    CultureInfo.InvariantCulture,
                    "Không xác định được duy nhất drawing mục tiêu trong tập drawing đã chọn của phiên hiện tại. Số ứng viên: 0. Khóa: {0}",
                    key
                );
                return null;
            }

            resolveDiag = string.Format(
                CultureInfo.InvariantCulture,
                "Không xác định được duy nhất drawing mục tiêu trong tập drawing đã chọn của phiên hiện tại. Số ứng viên: {0}. Khóa: {1}",
                candidates.Count,
                key
            );
            return null;
        }

        /// <summary>
        /// Ghi file chẩn đoán tạm thời từ Worker để chuyển tiếp nguyên nhân lỗi cụ thể về tiến trình chính.
        /// </summary>
        private static void WriteWorkerDiagFile(string diagFilePath, string message)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(diagFilePath) && !string.IsNullOrWhiteSpace(message))
                {
                    string parentDir = Path.GetDirectoryName(diagFilePath);
                    if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }
                    File.WriteAllText(diagFilePath, message.Trim(), Encoding.UTF8);
                }
            }
            catch { }
        }

        /// <summary>
        /// Điểm vào (Worker EntryPoint) cho tiến trình Worker in PDF màu độc lập (--print-color-worker).
        /// Chạy trong tiến trình con riêng biệt để toàn bộ việc nạp WPF/AkitUI và kích hoạt DPI awareness
        /// của Tekla DpmPrinter chỉ diễn ra trong Worker, giúp bảo vệ 100% kích thước giao diện chính (MainForm).
        /// </summary>
        public static int ExecuteColorPrintWorker(string drawingKeyBase64, string outputFilePathBase64)
        {
            string diagFilePath = null;
            try
            {
                if (string.IsNullOrWhiteSpace(drawingKeyBase64) || string.IsNullOrWhiteSpace(outputFilePathBase64))
                    return 1;

                string outputFilePath = Encoding.UTF8.GetString(Convert.FromBase64String(outputFilePathBase64));
                diagFilePath = outputFilePath + ".diag";

                DrawingSelectionKey key = DecodeDrawingSelectionKey(drawingKeyBase64);

                DrawingHandler drawingHandler = new DrawingHandler();
                if (!drawingHandler.GetConnectionStatus())
                {
                    WriteWorkerDiagFile(diagFilePath, "Mất kết nối Drawing API với Tekla Structures.");
                    return 2;
                }

                string resolveDiag;
                Drawing target = ResolveDrawingBySelectionKey(drawingHandler, key, out resolveDiag);

                if (target == null)
                {
                    // Không xác định được duy nhất bản vẽ nguồn (count == 0 hoặc count > 1)
                    WriteWorkerDiagFile(diagFilePath, resolveDiag);
                    return 3;
                }

                string directError;
                bool success = PrintDrawingWithTeklaApiDirect(drawingHandler, target, outputFilePath, out directError);
                if (success && File.Exists(outputFilePath) && new FileInfo(outputFilePath).Length > 0)
                {
                    try { if (File.Exists(diagFilePath)) File.Delete(diagFilePath); } catch { }
                    return 0;
                }

                WriteWorkerDiagFile(
                    diagFilePath,
                    !string.IsNullOrWhiteSpace(directError)
                        ? directError
                        : "Tekla DPM Printer không xuất được file PDF đầu ra."
                );
                return 4;
            }
            catch (Exception ex)
            {
                WriteWorkerDiagFile(diagFilePath, "Lỗi ngoại lệ trong Worker: " + GetDeepestExceptionMessage(ex));
                return -1;
            }
        }

        /// <summary>
        /// Thực thi in màu trực tiếp qua engine DpmPrinter của Tekla Structures.
        /// Tự động mở bản vẽ ngầm nếu chưa active, và có bước kiểm chứng bắt buộc:
        /// CHỈ nạp DpmData khi và chỉ khi Active Drawing thực tế đã được xác thực 100% trùng khớp với target drawing.
        /// </summary>
        private static bool PrintDrawingWithTeklaApiDirect(
            DrawingHandler drawingHandler,
            Drawing drawing,
            string outputFilePath
        )
        {
            string unusedError;
            return PrintDrawingWithTeklaApiDirect(drawingHandler, drawing, outputFilePath, out unusedError);
        }

        /// <summary>
        /// Thực thi in màu trực tiếp qua engine DpmPrinter của Tekla Structures (có thông điệp lỗi chi tiết).
        /// </summary>
        private static bool PrintDrawingWithTeklaApiDirect(
            DrawingHandler drawingHandler,
            Drawing drawing,
            string outputFilePath,
            out string errorDiag
        )
        {
            errorDiag = null;
            if (drawingHandler == null || drawing == null || string.IsNullOrWhiteSpace(outputFilePath))
            {
                errorDiag = "Tham số DrawingHandler hoặc Drawing đầu vào là null.";
                return false;
            }

            bool needCloseAfterPrint = false;
            try
            {
                // Kiểm tra xem bản vẽ đang mở (Active Drawing) có đúng là target drawing hay không
                Drawing currentActive = null;
                try { currentActive = drawingHandler.GetActiveDrawing(); } catch { }

                bool isAlreadyActive = IsSameDrawingIdentity(currentActive, drawing);
                if (!isAlreadyActive)
                {
                    // Mở bản vẽ ở chế độ ngầm (silent mode) để Tekla nạp đủ dữ liệu vector màu
                    bool opened = drawingHandler.SetActiveDrawing(drawing, false);
                    if (opened)
                    {
                        needCloseAfterPrint = true;
                    }
                }

                // BƯỚC XÁC THỰC BẢN VẼ ĐANG ACTIVE: Bảo vệ sinh tử chống in nhầm bản vẽ
                Drawing confirmedActive = null;
                try { confirmedActive = drawingHandler.GetActiveDrawing(); } catch { }

                if (confirmedActive == null || !IsSameDrawingIdentity(confirmedActive, drawing))
                {
                    // Nếu Active Drawing thực tế không khớp với target drawing, lập tức hủy lệnh in!
                    errorDiag = "Không thể kích hoạt đúng bản vẽ mục tiêu trong Tekla Structures.";
                    return false;
                }

                // Nạp DpmData từ Active Drawing đã được xác nhận 100% trùng khớp với target
                var dpm = new DpmData();
                bool dpmLoaded = false;
                try
                {
                    dpm.LoadDpmFromActiveDrawing();
                    dpmLoaded = true;
                }
                catch
                {
                    dpmLoaded = false;
                }

                if (!dpmLoaded)
                {
                    errorDiag = "Tekla Structures không nạp được dữ liệu vector màu (DpmData) từ bản vẽ.";
                    return false;
                }

                // Thiết lập các bộ xử lý cấu hình in của Tekla Structures
                var advHandler = new DefaultAdvancedOptionHandler();
                var fileHandler = new DefaultSettingsFileHandler(advHandler);
                var paperSettings = new DefaultPaperSettingsHandler(fileHandler);
                var printOptions = new PrintOptions(paperSettings);
                var paperSizeHandler = new DefaultPaperSizeHandler(printOptions);
                var colorHandler = new DefaultColorTableHandler();
                var dpmHandler = new DefaultDpmHandler(colorHandler, advHandler);
                var modelHandler = new DefaultModelHandler();
                var printer = new DpmPrinter(dpmHandler, modelHandler, true);

                // Thiết lập chế độ in màu đầy đủ (Full Color)
                printer.SetColorMode(PrintOptions.ColorModeEnum.Color);

                var dpmOptions = printOptions.GetDpmPrinterOptions(colorHandler);
                dpmOptions.ColorMode = PrintOptions.ColorModeEnum.Color;
                dpmOptions.EmbedFonts = false;

                // Đảm bảo thư mục lưu file tồn tại
                string parentDir = Path.GetDirectoryName(outputFilePath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                if (File.Exists(outputFilePath))
                {
                    try { File.Delete(outputFilePath); } catch { }
                }

                var uiHooks = new PrintUiHooks();
                printer.WritePdf(dpm, paperSizeHandler, dpmOptions, outputFilePath, uiHooks);

                if (File.Exists(outputFilePath) && new FileInfo(outputFilePath).Length > 0)
                {
                    return true;
                }

                errorDiag = "Tekla DPM Printer hoàn tất nhưng không tạo được file PDF.";
            }
            catch (Exception ex)
            {
                errorDiag = "Lỗi khi in DpmPrinter: " + GetDeepestExceptionMessage(ex);
            }
            finally
            {
                // Luôn đảm bảo đóng bản vẽ ngầm nếu được mở trong lượt in này
                if (needCloseAfterPrint)
                {
                    try { drawingHandler.CloseActiveDrawing(false); } catch { }
                }
            }

            return false;
        }

        /// <summary>
        /// Phương thức proxy an toàn thực hiện in màu bản vẽ bám sát Tekla Open API.
        /// Bản vẽ được truyền vào (job.Drawing) là SOURCE OF TRUTH tuyệt đối.
        /// </summary>
        private static bool PrintDrawingWithTeklaApi(
            DrawingHandler drawingHandler,
            Drawing drawing,
            DPMPrinterAttributes printAttributes,
            string outputFilePath
        )
        {
            string unusedError;
            return PrintDrawingWithTeklaApi(drawingHandler, drawing, printAttributes, outputFilePath, out unusedError);
        }

        /// <summary>
        /// Phương thức proxy an toàn thực hiện in màu bản vẽ bám sát Tekla Open API (có out errorReason).
        /// Ưu tiên 1: Chạy tiến trình Worker độc lập ngầm (--print-color-worker) mang theo
        /// DrawingSelectionKey để bảo toàn DPI và kích thước UI chính (MainForm) không bị co nhỏ.
        /// Ưu tiên 2 (Fallback): In trực tiếp in-process qua DpmPrinter với chính đối tượng drawing nguồn.
        /// Ưu tiên 3 (Fallback gốc): In qua drawingHandler.PrintDrawing tiêu chuẩn với chính đối tượng drawing nguồn.
        /// </summary>
        private static bool PrintDrawingWithTeklaApi(
            DrawingHandler drawingHandler,
            Drawing drawing,
            DPMPrinterAttributes printAttributes,
            string outputFilePath,
            out string errorReason
        )
        {
            errorReason = null;
            if (drawingHandler == null || drawing == null || string.IsNullOrWhiteSpace(outputFilePath))
            {
                errorReason = "Tham số drawing hoặc đường dẫn đầu ra không hợp lệ.";
                return false;
            }

            // Ưu tiên 1: Chạy Worker Sub-process độc lập để bảo toàn DPI và kích thước UI của MainForm
            try
            {
                string exePath = Application.ExecutablePath;
                if (File.Exists(exePath))
                {
                    DrawingSelectionKey key = BuildDrawingSelectionKey(drawing);
                    string keyBase64 = EncodeDrawingSelectionKey(key);
                    string pathBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(outputFilePath ?? string.Empty));
                    string diagFilePath = outputFilePath + ".diag";

                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = exePath;
                    psi.Arguments = string.Format("--print-color-worker \"{0}\" \"{1}\"", keyBase64, pathBase64);
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;

                    using (Process worker = Process.Start(psi))
                    {
                        if (worker != null)
                        {
                            bool exited = worker.WaitForExit(60000);
                            if (!exited)
                            {
                                try { worker.Kill(); } catch { }
                                errorReason = "Tiến trình in ngầm quá thời gian chờ (Timeout 60s).";
                            }
                            else
                            {
                                if (File.Exists(diagFilePath))
                                {
                                    try
                                    {
                                        errorReason = File.ReadAllText(diagFilePath, Encoding.UTF8);
                                        File.Delete(diagFilePath);
                                    }
                                    catch { }
                                }

                                if (worker.ExitCode == 0 && File.Exists(outputFilePath) && new FileInfo(outputFilePath).Length > 0)
                                {
                                    errorReason = null;
                                    return true;
                                }

                                if (string.IsNullOrWhiteSpace(errorReason))
                                {
                                    errorReason = "Tiến trình in Worker kết thúc với mã lỗi: " + worker.ExitCode;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                errorReason = "Lỗi khởi chạy Worker in màu: " + GetDeepestExceptionMessage(ex);
            }

            // Ưu tiên 2: In trực tiếp in-process qua DpmPrinter với chính đối tượng drawing nguồn
            try
            {
                string directError;
                bool directSuccess = PrintDrawingWithTeklaApiDirect(drawingHandler, drawing, outputFilePath, out directError);
                if (directSuccess && File.Exists(outputFilePath) && new FileInfo(outputFilePath).Length > 0)
                {
                    errorReason = null;
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(directError))
                {
                    errorReason = directError;
                }
            }
            catch (Exception ex)
            {
                errorReason = "Lỗi in trực tiếp DpmPrinter: " + GetDeepestExceptionMessage(ex);
            }

            // Ưu tiên 3 (Fallback an toàn): In thông qua drawingHandler.PrintDrawing tiêu chuẩn của Tekla với chính đối tượng drawing nguồn
            try
            {
                bool legacySuccess = drawingHandler.PrintDrawing(drawing, printAttributes, outputFilePath);
                if (legacySuccess)
                {
                    errorReason = null;
                    return true;
                }

                if (string.IsNullOrWhiteSpace(errorReason))
                {
                    errorReason = "Lệnh in tiêu chuẩn của Tekla trả về thất bại.";
                }
                return false;
            }
            catch (Exception ex)
            {
                errorReason = "Lỗi khi gọi drawingHandler.PrintDrawing: " + GetDeepestExceptionMessage(ex);
                return false;
            }
        }

        private static DotPrintPaperSize GetPdfPaperSize(Drawing drawing)
        {
            // Thay doi sixe A1/A3 của bản vẽ.
            DotPrintPaperSize assemblyDrawingPaperSize = DotPrintPaperSize.A1;
            DotPrintPaperSize singlePartDrawingPaperSize = DotPrintPaperSize.A3;

            if (drawing is AssemblyDrawing)
                return assemblyDrawingPaperSize;

            if (drawing is SinglePartDrawing)
                return singlePartDrawingPaperSize;

            // Other drawing types must also use a fixed size, never Auto.
            return singlePartDrawingPaperSize;
        }

        private static string BuildItemFailureMessage(bool printSucceeded, bool outputCreated)
        {
            if (!printSucceeded && !outputCreated)
                return "Tekla trả về Print thất bại và không tạo PDF.";

            if (!printSucceeded)
                return "Tekla trả về Print thất bại.";

            return "Tekla nhận lệnh Print nhưng không tìm thấy PDF đầu ra.";
        }

        private static string BuildDrawingFileBaseName(string mark, string revision)
        {
            string normalizedMark = NormalizeDisplayText(mark, "Drawing");
            string normalizedRevision = NormalizeDisplayText(revision, string.Empty);

            normalizedMark = SanitizeFileName(normalizedMark);

            string fileName = normalizedMark;

            if (
                !string.IsNullOrWhiteSpace(normalizedRevision)
                && normalizedRevision != "-"
                && !string.Equals(normalizedRevision, "UNKNOWN", StringComparison.OrdinalIgnoreCase)
            )
            {
                normalizedRevision = SanitizeFileName(normalizedRevision);
                fileName += "_REV_" + normalizedRevision;
            }

            return fileName;
        }

        private static string ResolveMergedFileBaseName(IList<DrawingPdfItemResult> successfulItems)
        {
            if (successfulItems != null)
            {
                foreach (
                    DrawingPdfItemResult item in successfulItems
                        .Where(currentItem => currentItem != null)
                        .OrderBy(currentItem => currentItem.Index)
                )
                {
                    string drawnBy = NormalizeDisplayText(item.DrawnBy, string.Empty);

                    if (
                        string.IsNullOrWhiteSpace(drawnBy)
                        || drawnBy == "-"
                        || string.Equals(drawnBy, "UNKNOWN", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        continue;
                    }

                    string safeDrawnBy = SanitizeFileName(drawnBy);
                    if (!string.IsNullOrWhiteSpace(safeDrawnBy))
                        return safeDrawnBy;
                }
            }

            return "TTSK_Merge";
        }

        private static string BuildUniqueOutputPath(
            string outputDirectory,
            string baseFileName,
            string extension
        )
        {
            string safeBaseName = SanitizeFileName(baseFileName);
            string normalizedExtension = string.IsNullOrWhiteSpace(extension) ? ".pdf" : extension;

            if (!normalizedExtension.StartsWith("."))
                normalizedExtension = "." + normalizedExtension;

            string candidate = Path.Combine(outputDirectory, safeBaseName + normalizedExtension);

            int duplicateNumber = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(
                    outputDirectory,
                    safeBaseName + "_" + duplicateNumber + normalizedExtension
                );
                duplicateNumber++;
            }

            return candidate;
        }

        private static bool WaitForCompletedPdf(string filePath, int timeoutMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            int elapsed = 0;
            long previousLength = -1;
            int stableChecks = 0;

            while (elapsed <= timeoutMilliseconds)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        FileInfo info = new FileInfo(filePath);
                        long currentLength = info.Length;

                        if (currentLength > 0)
                        {
                            if (currentLength == previousLength)
                            {
                                stableChecks++;
                                if (stableChecks >= 2)
                                    return true;
                            }
                            else
                            {
                                previousLength = currentLength;
                                stableChecks = 0;
                            }
                        }
                    }
                }
                catch { }

                Thread.Sleep(OutputWaitIntervalMilliseconds);
                elapsed += OutputWaitIntervalMilliseconds;
            }

            try
            {
                return File.Exists(filePath) && new FileInfo(filePath).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void MergePdfFiles(IList<string> inputPdfPaths, string outputPdfPath)
        {
            if (inputPdfPaths == null || inputPdfPaths.Count == 0)
                throw new ArgumentException("Danh sách PDF cần gộp đang trống.");

            if (string.IsNullOrWhiteSpace(outputPdfPath))
                throw new ArgumentException("Đường dẫn PDF tổng không hợp lệ.");

            string outputFolder = Path.GetDirectoryName(outputPdfPath);
            if (string.IsNullOrWhiteSpace(outputFolder))
                throw new ArgumentException("Không xác định được thư mục lưu PDF tổng.");

            Directory.CreateDirectory(outputFolder);

            PdfDocument outputDocument = new PdfDocument();

            try
            {
                outputDocument.Info.Title = "TTSK Merged Drawing PDF";
                outputDocument.Info.Subject = "Merged automatically from Tekla drawing PDF files.";
                outputDocument.Info.Creator = "TTSK AutoDim Plates";

                int importedPageCount = 0;

                foreach (string inputPdfPath in inputPdfPaths)
                {
                    if (string.IsNullOrWhiteSpace(inputPdfPath))
                        throw new IOException("Danh sách gộp có đường dẫn PDF rỗng.");

                    if (!File.Exists(inputPdfPath))
                    {
                        throw new FileNotFoundException(
                            "Không tìm thấy PDF con để gộp.",
                            inputPdfPath
                        );
                    }

                    PdfDocument inputDocument = null;

                    try
                    {
                        inputDocument = PdfReader.Open(inputPdfPath, PdfDocumentOpenMode.Import);

                        for (int pageIndex = 0; pageIndex < inputDocument.PageCount; pageIndex++)
                        {
                            outputDocument.AddPage(inputDocument.Pages[pageIndex]);
                            importedPageCount++;
                        }
                    }
                    finally
                    {
                        if (inputDocument != null)
                            inputDocument.Dispose();
                    }
                }

                if (importedPageCount == 0)
                {
                    throw new InvalidOperationException("Không đọc được trang PDF nào để gộp.");
                }

                outputDocument.Save(outputPdfPath);
            }
            finally
            {
                outputDocument.Dispose();
            }
        }

        private static void TryOpenOutputFolder(string outputDirectory)
        {
            try
            {
                if (
                    string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory)
                )
                {
                    return;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = outputDirectory;
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
            }
            catch { }
        }

        private static string ResolveModelPath(Model model)
        {
            try
            {
                ModelInfo info = model == null ? null : model.GetInfo();
                if (info != null && !string.IsNullOrWhiteSpace(info.ModelPath))
                    return info.ModelPath;
            }
            catch { }

            return null;
        }

        private static string ResolveDesktopDatedOutputFolder(DateTime printRunDate)
        {
            string desktopPath = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory
            );

            if (string.IsNullOrWhiteSpace(desktopPath))
            {
                desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }

            if (string.IsNullOrWhiteSpace(desktopPath))
            {
                desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            if (string.IsNullOrWhiteSpace(desktopPath))
                desktopPath = AppDomain.CurrentDomain.BaseDirectory;

            string rootOutputFolder = Path.Combine(desktopPath, OutputSubFolder);

            string dateFolderName = printRunDate.ToString("dd-MM-yy", CultureInfo.InvariantCulture);

            return Path.Combine(rootOutputFolder, dateFolderName);
        }

        private static string FindLatestDpmPrinterLog(string modelPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modelPath))
                    return null;

                string logDirectory = Path.Combine(modelPath, "logs");
                if (!Directory.Exists(logDirectory))
                    return null;

                string[] files = Directory.GetFiles(
                    logDirectory,
                    "DPMPrinter_*.log",
                    SearchOption.TopDirectoryOnly
                );

                if (files == null || files.Length == 0)
                    return null;

                return files.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static string ReadLogTail(string logFilePath, int maximumLineCount)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(logFilePath) || !File.Exists(logFilePath))
                {
                    return null;
                }

                string[] lines = File.ReadAllLines(logFilePath);
                if (lines.Length == 0)
                    return null;

                int takeCount = Math.Max(1, maximumLineCount);
                int startIndex = Math.Max(0, lines.Length - takeCount);

                return string.Join(Environment.NewLine, lines.Skip(startIndex).ToArray());
            }
            catch (Exception ex)
            {
                return "Không đọc được DPMPrinter log: " + ex.Message;
            }
        }

        private static string AppendLogTail(string logFilePath, int maximumLineCount)
        {
            string logTail = ReadLogTail(logFilePath, maximumLineCount);
            if (string.IsNullOrWhiteSpace(logTail))
                return string.Empty;

            return Environment.NewLine
                + Environment.NewLine
                + "DPMPrinter log gần nhất:"
                + Environment.NewLine
                + logTail;
        }

        private static string WriteDiagnosticFile(
            string outputDirectory,
            DrawingPdfPrintResult result
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(outputDirectory))
                {
                    outputDirectory = Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments
                    );
                }

                Directory.CreateDirectory(outputDirectory);

                string diagnosticPath = Path.Combine(
                    outputDirectory,
                    "TTSK_Print_Merge_Error_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt"
                );

                List<string> lines = new List<string>();
                lines.Add("================================================================================");
                lines.Add("                    TTSK AUTO DIM - BÁO CÁO LỖI IN BẢN VẼ");
                lines.Add("================================================================================");
                lines.Add("Thời gian chạy : " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
                lines.Add("Thư mục PDF    : " + SafeText(result.OutputDirectory));
                lines.Add("Chế độ in      : " + (result.MergeRequested ? "In và gộp PDF (Merge)" : "In các file PDF riêng"));
                lines.Add(string.Empty);

                int totalCount = result.RequestedDrawingCount;
                int successCount = result.SuccessfulDrawingCount;
                int failedCount = result.FailedDrawingCount;
                double percentage = totalCount > 0 ? ((double)successCount / totalCount) * 100.0 : 0;

                lines.Add("[1] TỔNG QUAN PHIÊN IN:");
                lines.Add("--------------------------------------------------------------------------------");
                lines.Add(string.Format(CultureInfo.InvariantCulture, "  • Tổng số bản vẽ cần in : {0} bản", totalCount));
                lines.Add(string.Format(CultureInfo.InvariantCulture, "  • In thành công          : {0} bản", successCount));
                lines.Add(string.Format(CultureInfo.InvariantCulture, "  • In thất bại (Lỗi)      : {0} bản", failedCount));
                lines.Add(string.Format(CultureInfo.InvariantCulture, "  • Tỷ lệ hoàn thành       : {0:0.#}% ({1}/{2})", percentage, successCount, totalCount));

                if (result.MergeRequested && result.MergeAttempted)
                {
                    lines.Add("  • Kết quả gộp PDF tổng   : " + (result.MergeSuccess ? "Thành công" : "Thất bại (" + SafeText(result.MergeMessage) + ")"));
                }
                lines.Add(string.Empty);

                // [2] CHI TIẾT CÁC BẢN VẼ BỊ LỖI
                List<DrawingPdfItemResult> failedItems = result.ItemResults != null
                    ? result.ItemResults.Where(item => item != null && !item.Success).ToList()
                    : new List<DrawingPdfItemResult>();

                if (failedItems.Count > 0)
                {
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "[2] CHI TIẾT CÁC BẢN VẼ BỊ LỖI ({0} BẢN):", failedItems.Count));
                    lines.Add("--------------------------------------------------------------------------------");

                    for (int i = 0; i < failedItems.Count; i++)
                    {
                        DrawingPdfItemResult item = failedItems[i];
                        lines.Add(string.Format(CultureInfo.InvariantCulture, "(!) Bản vẽ lỗi {0}:", i + 1));
                        lines.Add("    • Mark bản vẽ  : " + SafeText(item.Mark));
                        if (!string.IsNullOrWhiteSpace(item.DrawingName))
                        {
                            lines.Add("    • Tên bản vẽ   : " + item.DrawingName);
                        }
                        if (!string.IsNullOrWhiteSpace(item.Revision) && item.Revision != "-")
                        {
                            lines.Add("    • Ký hiệu REV  : " + item.Revision);
                        }
                        lines.Add("    • Nguyên nhân  : " + SafeText(item.Message));
                        if (!string.IsNullOrWhiteSpace(item.ExceptionDetails))
                        {
                            lines.Add("      [Chi tiết kỹ thuật: " + item.ExceptionDetails.Trim() + "]");
                        }
                        lines.Add(string.Empty);
                    }
                }

                // [3] CÁC BẢN VẼ ĐÃ IN THÀNH CÔNG
                List<DrawingPdfItemResult> successItems = result.ItemResults != null
                    ? result.ItemResults.Where(item => item != null && item.Success).ToList()
                    : new List<DrawingPdfItemResult>();

                if (successItems.Count > 0)
                {
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "[3] CÁC BẢN VẼ ĐÃ IN THÀNH CÔNG ({0} BẢN):", successItems.Count));
                    lines.Add("--------------------------------------------------------------------------------");

                    for (int i = 0; i < successItems.Count; i++)
                    {
                        DrawingPdfItemResult item = successItems[i];
                        string revText = (!string.IsNullOrWhiteSpace(item.Revision) && item.Revision != "-") ? " (REV: " + item.Revision + ")" : string.Empty;
                        lines.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "    ✓ [{0:00}] {1}{2} -> Đã tạo PDF thành công",
                            i + 1,
                            SafeText(item.Mark),
                            revText
                        ));
                    }
                    lines.Add(string.Empty);
                }

                lines.Add("================================================================================");
                lines.Add("* Gợi ý: Hãy kiểm tra lại các bản vẽ bị lỗi trong Document Manager của Tekla!");
                lines.Add("================================================================================");

                File.WriteAllLines(diagnosticPath, lines.ToArray(), Encoding.UTF8);
                return diagnosticPath;
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetDrawingDisplayText(Drawing drawing, int zeroBasedIndex)
        {
            if (drawing == null)
                return "Drawing_" + (zeroBasedIndex + 1);

            string[] propertyNames = new string[] { "Mark", "Name", "Title1", "Title2", "Title3" };

            foreach (string propertyName in propertyNames)
            {
                try
                {
                    PropertyInfo property = drawing
                        .GetType()
                        .GetProperty(
                            propertyName,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                        );

                    if (property == null || !property.CanRead)
                        continue;

                    object value = property.GetValue(drawing, null);
                    string text = value == null ? null : value.ToString();

                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }
                catch { }
            }

            return drawing.GetType().Name + "_" + (zeroBasedIndex + 1);
        }

        private static string NormalizeDisplayText(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback == null ? string.Empty : fallback;

            string text = value.Trim();
            return text == "-" && !string.IsNullOrEmpty(fallback) ? fallback : text;
        }

        private static string GetDeepestExceptionMessage(Exception exception)
        {
            if (exception == null)
                return "Lỗi không xác định.";

            Exception current = exception;
            while (current.InnerException != null)
                current = current.InnerException;

            return string.IsNullOrWhiteSpace(current.Message)
                ? exception.ToString()
                : current.Message;
        }

        private static string SafeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "UNKNOWN";

            char[] invalidChars = Path.GetInvalidFileNameChars();
            char[] characters = value.Trim().ToCharArray();

            for (int index = 0; index < characters.Length; index++)
            {
                if (invalidChars.Contains(characters[index]))
                    characters[index] = '_';
            }

            string sanitized = new string(characters).Trim();

            while (sanitized.EndsWith(".") || sanitized.EndsWith(" "))
                sanitized = sanitized.Substring(0, sanitized.Length - 1);

            return string.IsNullOrWhiteSpace(sanitized) ? "UNKNOWN" : sanitized;
        }
    }

    public static class ExternalPdfFileMerger
    {
        private const string DefaultMergedFileName = "TTSK_Merge";

        public static ExternalPdfMergeResult MergeSelectedFiles(IWin32Window owner)
        {
            ExternalPdfMergeResult result = new ExternalPdfMergeResult();
            string outputFilePath = null;

            try
            {
                List<string> selectedFiles;

                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Merge File - Chọn các file PDF cần gộp";
                    dialog.Filter = "PDF files (*.pdf)|*.pdf";
                    dialog.Multiselect = true;
                    dialog.CheckFileExists = true;
                    dialog.CheckPathExists = true;
                    dialog.RestoreDirectory = true;

                    if (dialog.ShowDialog(owner) != DialogResult.OK)
                    {
                        result.Cancelled = true;
                        result.Message = "Đã hủy chọn file PDF.";
                        return result;
                    }

                    selectedFiles = dialog
                        .FileNames.Where(path => !string.IsNullOrWhiteSpace(path))
                        .Select(Path.GetFullPath)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                result.SelectedFileCount = selectedFiles.Count;
                result.SourceFiles.AddRange(selectedFiles);

                if (selectedFiles.Count < 2)
                {
                    result.Message = "Merge File cần ít nhất 2 file PDF được chọn.";
                    return result;
                }

                foreach (string sourceFile in selectedFiles)
                {
                    if (!File.Exists(sourceFile))
                    {
                        result.Message = "Không tìm thấy file PDF đã chọn:\r\n" + sourceFile;
                        return result;
                    }
                }

                string outputDirectory = Path.GetDirectoryName(selectedFiles[0]);
                if (string.IsNullOrWhiteSpace(outputDirectory))
                {
                    result.Message = "Không xác định được thư mục chứa các file PDF đã chọn.";
                    return result;
                }

                string normalizedOutputDirectory = NormalizeDirectoryPath(outputDirectory);

                bool allFilesInSameFolder = selectedFiles.All(path =>
                    string.Equals(
                        NormalizeDirectoryPath(Path.GetDirectoryName(path)),
                        normalizedOutputDirectory,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                if (!allFilesInSameFolder)
                {
                    result.Message = "Hãy chọn các file PDF nằm trong cùng một folder Windows.";
                    return result;
                }

                outputFilePath = BuildUniqueOutputPath(
                    outputDirectory,
                    DefaultMergedFileName,
                    ".pdf"
                );

                result.OutputDirectory = outputDirectory;
                result.OutputFilePath = outputFilePath;

                MergePdfFiles(selectedFiles, outputFilePath);

                if (!VerifyMergedPdf(outputFilePath))
                {
                    throw new IOException("Đã tạo file tổng nhưng không xác minh được PDF đầu ra.");
                }

                result.MergeSuccess = true;

                List<string> cleanupErrors = new List<string>();

                foreach (string sourceFile in selectedFiles)
                {
                    try
                    {
                        File.Delete(sourceFile);

                        if (File.Exists(sourceFile))
                        {
                            cleanupErrors.Add("Không xóa được: " + sourceFile);
                            continue;
                        }

                        result.DeletedSourceFileCount++;
                    }
                    catch (Exception deleteException)
                    {
                        cleanupErrors.Add(
                            sourceFile + " | " + GetDeepestExceptionMessage(deleteException)
                        );
                    }
                }

                result.CleanupSuccess = cleanupErrors.Count == 0;
                result.CleanupDetails =
                    cleanupErrors.Count == 0
                        ? null
                        : string.Join(Environment.NewLine, cleanupErrors.ToArray());
                result.PartialSuccess = result.MergeSuccess && !result.CleanupSuccess;
                result.Success = result.MergeSuccess && result.CleanupSuccess;

                if (result.Success)
                {
                    result.Message =
                        "Đã gộp "
                        + selectedFiles.Count
                        + " file PDF và xóa toàn bộ file nguồn đã chọn.";
                }
                else
                {
                    result.Message =
                        "PDF tổng đã được tạo nhưng còn "
                        + cleanupErrors.Count
                        + " file nguồn không xóa được.";
                }

                TryOpenOutputFolder(outputDirectory);
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.PartialSuccess = false;
                result.Message = "Merge File lỗi: " + GetDeepestExceptionMessage(ex);

                if (!result.MergeSuccess && !string.IsNullOrWhiteSpace(outputFilePath))
                {
                    TryDeleteIncompleteOutput(outputFilePath);
                }

                return result;
            }
        }

        private static void MergePdfFiles(IList<string> inputPdfPaths, string outputPdfPath)
        {
            PdfDocument outputDocument = new PdfDocument();

            try
            {
                outputDocument.Info.Title = "TTSK Merge File";
                outputDocument.Info.Subject = "Merged from PDF files selected by the user.";
                outputDocument.Info.Creator = "TTSK AutoDim Plates";

                int importedPageCount = 0;

                foreach (string inputPdfPath in inputPdfPaths)
                {
                    PdfDocument inputDocument = null;

                    try
                    {
                        inputDocument = PdfReader.Open(inputPdfPath, PdfDocumentOpenMode.Import);

                        for (int pageIndex = 0; pageIndex < inputDocument.PageCount; pageIndex++)
                        {
                            outputDocument.AddPage(inputDocument.Pages[pageIndex]);
                            importedPageCount++;
                        }
                    }
                    finally
                    {
                        if (inputDocument != null)
                            inputDocument.Dispose();
                    }
                }

                if (importedPageCount == 0)
                {
                    throw new InvalidOperationException("Không đọc được trang PDF nào để gộp.");
                }

                outputDocument.Save(outputPdfPath);
            }
            finally
            {
                outputDocument.Dispose();
            }
        }

        private static bool VerifyMergedPdf(string filePath)
        {
            try
            {
                if (
                    string.IsNullOrWhiteSpace(filePath)
                    || !File.Exists(filePath)
                    || new FileInfo(filePath).Length <= 0
                )
                {
                    return false;
                }

                using (
                    PdfDocument verifyDocument = PdfReader.Open(
                        filePath,
                        PdfDocumentOpenMode.Import
                    )
                )
                {
                    return verifyDocument.PageCount > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string BuildUniqueOutputPath(
            string outputDirectory,
            string baseFileName,
            string extension
        )
        {
            string candidate = Path.Combine(outputDirectory, baseFileName + extension);
            int duplicateNumber = 2;

            while (File.Exists(candidate))
            {
                candidate = Path.Combine(
                    outputDirectory,
                    baseFileName + "_" + duplicateNumber + extension
                );
                duplicateNumber++;
            }

            return candidate;
        }

        private static string NormalizeDirectoryPath(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                return string.Empty;

            return Path.GetFullPath(directoryPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void TryOpenOutputFolder(string outputDirectory)
        {
            try
            {
                if (
                    string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory)
                )
                {
                    return;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = outputDirectory;
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
            }
            catch { }
        }

        private static void TryDeleteIncompleteOutput(string outputFilePath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(outputFilePath) && File.Exists(outputFilePath))
                {
                    File.Delete(outputFilePath);
                }
            }
            catch { }
        }

        private static string GetDeepestExceptionMessage(Exception exception)
        {
            if (exception == null)
                return "Lỗi không xác định.";

            Exception current = exception;
            while (current.InnerException != null)
                current = current.InnerException;

            return string.IsNullOrWhiteSpace(current.Message)
                ? exception.ToString()
                : current.Message;
        }
    }

    public sealed class ExternalPdfMergeResult
    {
        public ExternalPdfMergeResult()
        {
            SourceFiles = new List<string>();
        }

        public bool Success { get; set; }
        public bool PartialSuccess { get; set; }
        public bool Cancelled { get; set; }
        public bool MergeSuccess { get; set; }
        public bool CleanupSuccess { get; set; }
        public int SelectedFileCount { get; set; }
        public int DeletedSourceFileCount { get; set; }
        public string OutputDirectory { get; set; }
        public string OutputFilePath { get; set; }
        public string CleanupDetails { get; set; }
        public string Message { get; set; }
        public List<string> SourceFiles { get; private set; }
    }

    public sealed class DrawingPdfPrintJob
    {
        public Drawing Drawing { get; set; }
        public string Mark { get; set; }
        public string Revision { get; set; }
        public string DrawnBy { get; set; }
        public string DrawingName { get; set; }
    }

    public sealed class DrawingPdfItemResult
    {
        public int Index { get; set; }
        public bool Success { get; set; }
        public bool TeklaPrintReturnedSuccess { get; set; }
        public bool OutputFileVerified { get; set; }
        public bool ChildFileDeleted { get; set; }
        public string Mark { get; set; }
        public string Revision { get; set; }
        public string DrawnBy { get; set; }
        public string DrawingName { get; set; }
        public string OutputFilePath { get; set; }
        public string Message { get; set; }
        public string ExceptionDetails { get; set; }
    }

    public sealed class DrawingPdfPrintResult
    {
        public DrawingPdfPrintResult()
        {
            ItemResults = new List<DrawingPdfItemResult>();
        }

        public bool Success { get; set; }
        public bool PartialSuccess { get; set; }
        public bool Cancelled { get; set; }
        public int DrawingCount { get; set; }
        public int RequestedDrawingCount { get; set; }
        public int SuccessfulDrawingCount { get; set; }
        public int FailedDrawingCount { get; set; }
        public string OutputDirectory { get; set; }
        public string OutputFilePath { get; set; }

        public bool MergeRequested { get; set; }
        public bool MergeAttempted { get; set; }
        public bool MergeSuccess { get; set; }
        public string MergedFilePath { get; set; }
        public string MergeMessage { get; set; }
        public string MergeExceptionDetails { get; set; }

        public bool CleanupAttempted { get; set; }
        public bool CleanupSuccess { get; set; }
        public int DeletedChildFileCount { get; set; }
        public string CleanupMessage { get; set; }

        public string LogFilePath { get; set; }
        public string DiagnosticFilePath { get; set; }
        public string DiagnosticDetails { get; set; }
        public string Message { get; set; }
        public List<DrawingPdfItemResult> ItemResults { get; private set; }
    }
}
