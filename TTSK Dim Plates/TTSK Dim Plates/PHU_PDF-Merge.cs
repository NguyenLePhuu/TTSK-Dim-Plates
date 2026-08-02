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
using System.Windows.Forms;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

namespace TTSK_AutoDim_Plates
{
    public static class DrawingPdfPrinter
    {
        private const string OutputSubFolder = "TTSK_PDF";
        private const string DrawnByUdaName = "DR_DRAWN_BY";
        private const int OutputWaitTimeoutMilliseconds = 60000;
        private const int OutputWaitIntervalMilliseconds = 250;
        private const int DelayBetweenDrawingsMilliseconds = 150;

        public static DrawingPdfPrintResult PrintToSeparatePdfs(
            IList<DrawingPdfPrintJob> jobs,
            IWin32Window owner)
        {
            return ExecutePdfWorkflow(
                jobs,
                owner,
                false);
        }

        public static DrawingPdfPrintResult PrintAndMergePdfs(
            IList<DrawingPdfPrintJob> jobs,
            IWin32Window owner)
        {
            return ExecutePdfWorkflow(
                jobs,
                owner,
                true);
        }

        private static DrawingPdfPrintResult ExecutePdfWorkflow(
            IList<DrawingPdfPrintJob> jobs,
            IWin32Window owner,
            bool mergeAndDeleteChildren)
        {
            DrawingPdfPrintResult result = new DrawingPdfPrintResult();
            string modelPath = null;
            string outputDirectory = null;
            DateTime printRunDate = DateTime.Now;

            result.MergeRequested = mergeAndDeleteChildren;

            try
            {
                List<DrawingPdfPrintJob> validJobs = jobs == null
                    ? new List<DrawingPdfPrintJob>()
                    : jobs
                        .Where(job => job != null && job.Drawing != null)
                        .ToList();

                result.RequestedDrawingCount = validJobs.Count;

                if (validJobs.Count == 0)
                {
                    result.Message =
                        "Không có drawing hợp lệ để xuất PDF. " +
                        "Hãy chọn drawing trong Document Manager rồi thử lại.";
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
                        "Drawing API chưa kết nối đúng với Tekla Structures. " +
                        "Hãy kiểm tra Tekla đang mở model và ứng dụng đang chạy bằng x64.";
                    return result;
                }

                Drawing activeDrawing = drawingHandler.GetActiveDrawing();
                if (activeDrawing != null)
                {
                    DialogResult continueResult = MessageBox.Show(
                        owner,
                        "Hiện đang có một drawing mở. Trong lúc xuất PDF, Tekla có thể đóng drawing đang mở.\r\n\r\n" +
                        "Hãy chắc chắn drawing đã được lưu. Tiếp tục?",
                        mergeAndDeleteChildren
                            ? "TTSK Merge PDF"
                            : "TTSK Print PDF",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);

                    if (continueResult != DialogResult.Yes)
                    {
                        result.Cancelled = true;
                        result.Message = "Đã hủy để bảo vệ drawing đang mở.";
                        return result;
                    }
                }

                for (int index = 0; index < validJobs.Count; index++)
                {
                    DrawingPdfPrintJob job = validJobs[index];
                    DrawingPdfItemResult itemResult = new DrawingPdfItemResult();
                    itemResult.Index = index;
                    itemResult.Mark = NormalizeDisplayText(
                        job.Mark,
                        "Drawing " + (index + 1));
                    itemResult.Revision = NormalizeDisplayText(
                        job.Revision,
                        string.Empty);
                    itemResult.DrawnBy = NormalizeDisplayText(
                        job.DrawnBy,
                        GetDrawingDrawnBy(job.Drawing));

                    string baseFileName = BuildDrawingFileBaseName(
                        itemResult.Mark,
                        itemResult.Revision);

                    string outputFilePath = BuildUniqueOutputPath(
                        outputDirectory,
                        baseFileName,
                        ".pdf");

                    itemResult.OutputFilePath = outputFilePath;
                    result.ItemResults.Add(itemResult);

                    try
                    {
                        Application.DoEvents();

                        DPMPrinterAttributes printAttributes =
                            CreatePdfPrintAttributes(
                                outputFilePath,
                                job.Drawing);

                        bool printSucceeded = drawingHandler.PrintDrawing(
                            job.Drawing,
                            printAttributes,
                            outputFilePath);

                        itemResult.TeklaPrintReturnedSuccess = printSucceeded;

                        bool outputCreated = WaitForCompletedPdf(
                            outputFilePath,
                            OutputWaitTimeoutMilliseconds);

                        itemResult.OutputFileVerified = outputCreated;

                        if (printSucceeded && outputCreated)
                        {
                            itemResult.Success = true;
                            itemResult.Message = "Đã tạo PDF.";
                            result.SuccessfulDrawingCount++;
                        }
                        else
                        {
                            itemResult.Success = false;
                            itemResult.Message = BuildItemFailureMessage(
                                printSucceeded,
                                outputCreated);
                            result.FailedDrawingCount++;
                        }
                    }
                    catch (Exception itemException)
                    {
                        itemResult.Success = false;
                        itemResult.Message =
                            "Print drawing lỗi: " +
                            GetDeepestExceptionMessage(itemException);
                        itemResult.ExceptionDetails = itemException.ToString();
                        result.FailedDrawingCount++;
                    }

                    Application.DoEvents();
                    Thread.Sleep(DelayBetweenDrawingsMilliseconds);
                }

                result.DrawingCount = result.SuccessfulDrawingCount;

                List<DrawingPdfItemResult> successfulItems = result.ItemResults
                    .Where(item =>
                        item != null &&
                        item.Success &&
                        item.OutputFileVerified &&
                        !string.IsNullOrWhiteSpace(item.OutputFilePath) &&
                        File.Exists(item.OutputFilePath))
                    .OrderBy(item => item.Index)
                    .ToList();

                if (mergeAndDeleteChildren)
                {
                    RunMergeAndCleanup(
                        successfulItems,
                        outputDirectory,
                        result);
                }

                bool allIndividualPdfsSucceeded =
                    result.SuccessfulDrawingCount == validJobs.Count &&
                    result.FailedDrawingCount == 0;

                if (mergeAndDeleteChildren)
                {
                    result.Success =
                        allIndividualPdfsSucceeded &&
                        result.MergeAttempted &&
                        result.MergeSuccess &&
                        result.CleanupAttempted &&
                        result.CleanupSuccess;
                }
                else
                {
                    result.Success = allIndividualPdfsSucceeded;
                }

                result.PartialSuccess =
                    result.SuccessfulDrawingCount > 0 &&
                    !result.Success;

                BuildFinalResult(
                    validJobs.Count,
                    mergeAndDeleteChildren,
                    modelPath,
                    result);

                if (result.SuccessfulDrawingCount > 0 || result.MergeSuccess)
                    TryOpenOutputFolder(outputDirectory);

                return result;
            }
            catch (Exception ex)
            {
                result.OutputDirectory = outputDirectory;
                result.OutputFilePath = outputDirectory;
                result.LogFilePath = FindLatestDpmPrinterLog(modelPath);
                result.DiagnosticDetails =
                    ex.ToString() +
                    AppendLogTail(result.LogFilePath, 20);
                result.Message =
                    (mergeAndDeleteChildren ? "Merge PDF lỗi: " : "Print PDF lỗi: ") +
                    GetDeepestExceptionMessage(ex);
                result.DiagnosticFilePath = WriteDiagnosticFile(
                    outputDirectory,
                    result);
                return result;
            }
        }

        private static void RunMergeAndCleanup(
            IList<DrawingPdfItemResult> successfulItems,
            string outputDirectory,
            DrawingPdfPrintResult result)
        {
            if (successfulItems == null || successfulItems.Count == 0)
            {
                result.MergeAttempted = false;
                result.MergeSuccess = false;
                result.MergeMessage =
                    "Không có PDF con thành công trong phiên hiện tại để gộp.";
                return;
            }

            result.MergeAttempted = true;

            try
            {
                List<string> currentRunPdfFiles = successfulItems
                    .Select(item => item.OutputFilePath)
                    .ToList();

                string mergedBaseName = ResolveMergedFileBaseName(
                    successfulItems);

                string mergedFilePath = BuildUniqueOutputPath(
                    outputDirectory,
                    mergedBaseName,
                    ".pdf");

                MergePdfFiles(
                    currentRunPdfFiles,
                    mergedFilePath);

                bool mergedFileVerified = WaitForCompletedPdf(
                    mergedFilePath,
                    OutputWaitTimeoutMilliseconds);

                if (!mergedFileVerified)
                {
                    throw new IOException(
                        "PDF đã được gộp nhưng không xác minh được file đầu ra.");
                }

                result.MergeSuccess = true;
                result.MergedFilePath = mergedFilePath;
                result.MergeMessage =
                    "Đã gộp " + currentRunPdfFiles.Count +
                    " PDF con thành một PDF nhiều trang.";

                DeleteCurrentRunChildFiles(
                    successfulItems,
                    result);
            }
            catch (Exception mergeException)
            {
                result.MergeSuccess = false;
                result.MergeMessage =
                    "Gộp PDF lỗi: " +
                    GetDeepestExceptionMessage(mergeException);
                result.MergeExceptionDetails = mergeException.ToString();
            }
        }

        private static void DeleteCurrentRunChildFiles(
            IList<DrawingPdfItemResult> successfulItems,
            DrawingPdfPrintResult result)
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
                        cleanupErrors.Add(
                            "Không xóa được: " + item.OutputFilePath);
                        continue;
                    }

                    item.ChildFileDeleted = true;
                    result.DeletedChildFileCount++;
                }
                catch (Exception deleteException)
                {
                    cleanupErrors.Add(
                        item.OutputFilePath + " | " +
                        GetDeepestExceptionMessage(deleteException));
                }
            }

            result.CleanupSuccess = cleanupErrors.Count == 0;

            if (result.CleanupSuccess)
            {
                result.CleanupMessage =
                    "Đã xóa " + result.DeletedChildFileCount +
                    " PDF con vừa tạo trong phiên Merge này.";
            }
            else
            {
                result.CleanupMessage =
                    "PDF tổng đã được tạo nhưng còn " +
                    cleanupErrors.Count +
                    " PDF con không xóa được." +
                    Environment.NewLine +
                    string.Join(
                        Environment.NewLine,
                        cleanupErrors.ToArray());
            }
        }

        private static void BuildFinalResult(
            int requestedCount,
            bool mergeAndDeleteChildren,
            string modelPath,
            DrawingPdfPrintResult result)
        {
            if (result.Success)
            {
                if (mergeAndDeleteChildren)
                {
                    result.Message =
                        "Đã tạo PDF tổng và xóa " +
                        result.DeletedChildFileCount +
                        " PDF con của phiên này.";
                }
                else
                {
                    result.Message =
                        "Đã tạo " + result.SuccessfulDrawingCount +
                        " file PDF riêng thành công.";
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
                    "PDF MERGE ERROR" +
                    Environment.NewLine +
                    result.MergeExceptionDetails);
            }

            if (!string.IsNullOrWhiteSpace(logTail))
            {
                diagnostics.Add(
                    "DPMPRINTER LOG TAIL" +
                    Environment.NewLine +
                    logTail);
            }

            result.DiagnosticDetails = string.Join(
                Environment.NewLine + Environment.NewLine,
                diagnostics.ToArray());

            if (!mergeAndDeleteChildren)
            {
                result.Message =
                    "Đã tạo " + result.SuccessfulDrawingCount + "/" +
                    requestedCount + " file PDF riêng. Lỗi: " +
                    result.FailedDrawingCount + " drawing.";
            }
            else if (!result.MergeSuccess)
            {
                result.Message =
                    "Đã tạo " + result.SuccessfulDrawingCount + "/" +
                    requestedCount + " PDF con nhưng không tạo được PDF tổng. " +
                    result.MergeMessage;
            }
            else if (!result.CleanupSuccess)
            {
                result.Message =
                    "PDF tổng đã được tạo nhưng chưa xóa hết PDF con. " +
                    result.CleanupMessage;
            }
            else
            {
                result.Message =
                    "Đã tạo PDF tổng từ " + result.SuccessfulDrawingCount + "/" +
                    requestedCount + " drawing. Có " +
                    result.FailedDrawingCount + " drawing xuất lỗi.";
            }

            result.DiagnosticFilePath = WriteDiagnosticFile(
                result.OutputDirectory,
                result);
        }

        public static string GetDrawingDrawnBy(Drawing drawing)
        {
            if (drawing == null)
                return string.Empty;

            string value;

            if (TryGetDrawingStringUserProperty(
                drawing,
                DrawnByUdaName,
                out value))
            {
                return value;
            }

            // Compatibility fallback for custom environments that expose the same
            // Document Manager value without the standard DR_ prefix.
            if (TryGetDrawingStringUserProperty(
                drawing,
                "DRAWN_BY",
                out value))
            {
                return value;
            }

            return string.Empty;
        }

        private static bool TryGetDrawingStringUserProperty(
            Drawing drawing,
            string propertyName,
            out string value)
        {
            value = string.Empty;

            if (drawing == null || string.IsNullOrWhiteSpace(propertyName))
                return false;

            try
            {
                string propertyValue = string.Empty;
                bool found = drawing.GetUserProperty(
                    propertyName,
                    ref propertyValue);

                if (found && !string.IsNullOrWhiteSpace(propertyValue))
                {
                    value = propertyValue.Trim();
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                drawing.Select();

                string propertyValue = string.Empty;
                bool found = drawing.GetUserProperty(
                    propertyName,
                    ref propertyValue);

                if (found && !string.IsNullOrWhiteSpace(propertyValue))
                {
                    value = propertyValue.Trim();
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        /// <summary>
        /// Compatibility wrapper. The old single-PDF call now uses Merge mode.
        /// </summary>
        public static DrawingPdfPrintResult PrintToSinglePdf(
            IList<Drawing> drawings,
            IWin32Window owner)
        {
            List<DrawingPdfPrintJob> jobs = new List<DrawingPdfPrintJob>();

            if (drawings != null)
            {
                for (int index = 0; index < drawings.Count; index++)
                {
                    Drawing drawing = drawings[index];
                    if (drawing == null)
                        continue;

                    jobs.Add(new DrawingPdfPrintJob
                    {
                        Drawing = drawing,
                        Mark = TryGetDrawingDisplayText(drawing, index),
                        Revision = string.Empty,
                        DrawnBy = GetDrawingDrawnBy(drawing)
                    });
                }
            }

            return PrintAndMergePdfs(jobs, owner);
        }

        private static DPMPrinterAttributes CreatePdfPrintAttributes(
            string outputFilePath,
            Drawing drawing)
        {
            DPMPrinterAttributes printAttributes = new DPMPrinterAttributes();
            printAttributes.OutputType = DotPrintOutputType.PDF;
            printAttributes.OutputFileName = outputFilePath;
            printAttributes.OpenFileWhenFinished = false;
            printAttributes.Orientation = DotPrintOrientationType.Auto;
            printAttributes.ColorMode = (DotPrintColor)0;
            printAttributes.PaperSize = GetPdfPaperSize(drawing);
            printAttributes.ScalingMethod = DotPrintScalingType.Auto;
            printAttributes.PrintToMultipleSheet = DotPrintToMultipleSheet.Off;
            return printAttributes;
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

        private static string BuildItemFailureMessage(
            bool printSucceeded,
            bool outputCreated)
        {
            if (!printSucceeded && !outputCreated)
                return "Tekla trả về Print thất bại và không tạo PDF.";

            if (!printSucceeded)
                return "Tekla trả về Print thất bại.";

            return "Tekla nhận lệnh Print nhưng không tìm thấy PDF đầu ra.";
        }

        private static string BuildDrawingFileBaseName(
            string mark,
            string revision)
        {
            string normalizedMark = NormalizeDisplayText(
                mark,
                "Drawing");
            string normalizedRevision = NormalizeDisplayText(
                revision,
                string.Empty);

            normalizedMark = SanitizeFileName(normalizedMark);

            string fileName = normalizedMark;

            if (!string.IsNullOrWhiteSpace(normalizedRevision) &&
                normalizedRevision != "-" &&
                !string.Equals(
                    normalizedRevision,
                    "UNKNOWN",
                    StringComparison.OrdinalIgnoreCase))
            {
                normalizedRevision = SanitizeFileName(normalizedRevision);
                fileName += "_REV_" + normalizedRevision;
            }

            return fileName;
        }

        private static string ResolveMergedFileBaseName(
            IList<DrawingPdfItemResult> successfulItems)
        {
            if (successfulItems != null)
            {
                foreach (DrawingPdfItemResult item in successfulItems
                    .Where(currentItem => currentItem != null)
                    .OrderBy(currentItem => currentItem.Index))
                {
                    string drawnBy = NormalizeDisplayText(
                        item.DrawnBy,
                        string.Empty);

                    if (string.IsNullOrWhiteSpace(drawnBy) ||
                        drawnBy == "-" ||
                        string.Equals(
                            drawnBy,
                            "UNKNOWN",
                            StringComparison.OrdinalIgnoreCase))
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
            string extension)
        {
            string safeBaseName = SanitizeFileName(baseFileName);
            string normalizedExtension = string.IsNullOrWhiteSpace(extension)
                ? ".pdf"
                : extension;

            if (!normalizedExtension.StartsWith("."))
                normalizedExtension = "." + normalizedExtension;

            string candidate = Path.Combine(
                outputDirectory,
                safeBaseName + normalizedExtension);

            int duplicateNumber = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(
                    outputDirectory,
                    safeBaseName + "_" + duplicateNumber + normalizedExtension);
                duplicateNumber++;
            }

            return candidate;
        }

        private static bool WaitForCompletedPdf(
            string filePath,
            int timeoutMilliseconds)
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
                catch
                {
                }

                Thread.Sleep(OutputWaitIntervalMilliseconds);
                elapsed += OutputWaitIntervalMilliseconds;
            }

            try
            {
                return File.Exists(filePath) &&
                       new FileInfo(filePath).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void MergePdfFiles(
            IList<string> inputPdfPaths,
            string outputPdfPath)
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
                outputDocument.Info.Subject =
                    "Merged automatically from Tekla drawing PDF files.";
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
                            inputPdfPath);
                    }

                    PdfDocument inputDocument = null;

                    try
                    {
                        inputDocument = PdfReader.Open(
                            inputPdfPath,
                            PdfDocumentOpenMode.Import);

                        for (int pageIndex = 0;
                             pageIndex < inputDocument.PageCount;
                             pageIndex++)
                        {
                            outputDocument.AddPage(
                                inputDocument.Pages[pageIndex]);
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
                    throw new InvalidOperationException(
                        "Không đọc được trang PDF nào để gộp.");
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
                if (string.IsNullOrWhiteSpace(outputDirectory) ||
                    !Directory.Exists(outputDirectory))
                {
                    return;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = outputDirectory;
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
            }
            catch
            {
            }
        }

        private static string ResolveModelPath(Model model)
        {
            try
            {
                ModelInfo info = model == null ? null : model.GetInfo();
                if (info != null && !string.IsNullOrWhiteSpace(info.ModelPath))
                    return info.ModelPath;
            }
            catch
            {
            }

            return null;
        }

        private static string ResolveDesktopDatedOutputFolder(DateTime printRunDate)
        {
            string desktopPath = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);

            if (string.IsNullOrWhiteSpace(desktopPath))
            {
                desktopPath = Environment.GetFolderPath(
                    Environment.SpecialFolder.Desktop);
            }

            if (string.IsNullOrWhiteSpace(desktopPath))
            {
                desktopPath = Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments);
            }

            if (string.IsNullOrWhiteSpace(desktopPath))
                desktopPath = AppDomain.CurrentDomain.BaseDirectory;

            string rootOutputFolder = Path.Combine(
                desktopPath,
                OutputSubFolder);

            string dateFolderName = printRunDate.ToString(
                "dd-MM-yy",
                CultureInfo.InvariantCulture);

            return Path.Combine(
                rootOutputFolder,
                dateFolderName);
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
                    SearchOption.TopDirectoryOnly);

                if (files == null || files.Length == 0)
                    return null;

                return files
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static string ReadLogTail(
            string logFilePath,
            int maximumLineCount)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(logFilePath) ||
                    !File.Exists(logFilePath))
                {
                    return null;
                }

                string[] lines = File.ReadAllLines(logFilePath);
                if (lines.Length == 0)
                    return null;

                int takeCount = Math.Max(1, maximumLineCount);
                int startIndex = Math.Max(0, lines.Length - takeCount);

                return string.Join(
                    Environment.NewLine,
                    lines.Skip(startIndex).ToArray());
            }
            catch (Exception ex)
            {
                return "Không đọc được DPMPrinter log: " + ex.Message;
            }
        }

        private static string AppendLogTail(
            string logFilePath,
            int maximumLineCount)
        {
            string logTail = ReadLogTail(logFilePath, maximumLineCount);
            if (string.IsNullOrWhiteSpace(logTail))
                return string.Empty;

            return Environment.NewLine +
                   Environment.NewLine +
                   "DPMPrinter log gần nhất:" +
                   Environment.NewLine +
                   logTail;
        }

        private static string WriteDiagnosticFile(
            string outputDirectory,
            DrawingPdfPrintResult result)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(outputDirectory))
                {
                    outputDirectory = Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments);
                }

                Directory.CreateDirectory(outputDirectory);

                string diagnosticPath = Path.Combine(
                    outputDirectory,
                    "TTSK_Print_Merge_Error_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                    ".txt");

                List<string> lines = new List<string>();
                lines.Add("TTSK PRINT / MERGE PDF DIAGNOSTIC");
                lines.Add("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                lines.Add("Merge requested: " + result.MergeRequested);
                lines.Add("Requested drawings: " + result.RequestedDrawingCount);
                lines.Add("Successful drawings: " + result.SuccessfulDrawingCount);
                lines.Add("Failed drawings: " + result.FailedDrawingCount);
                lines.Add("Output folder: " + SafeText(result.OutputDirectory));
                lines.Add("Merge attempted: " + result.MergeAttempted);
                lines.Add("Merge success: " + result.MergeSuccess);
                lines.Add("Merged file: " + SafeText(result.MergedFilePath));
                lines.Add("Merge message: " + SafeText(result.MergeMessage));
                lines.Add("Cleanup attempted: " + result.CleanupAttempted);
                lines.Add("Cleanup success: " + result.CleanupSuccess);
                lines.Add("Deleted child files: " + result.DeletedChildFileCount);
                lines.Add("Cleanup message: " + SafeText(result.CleanupMessage));
                lines.Add("DPMPrinter log: " + SafeText(result.LogFilePath));
                lines.Add("Message: " + SafeText(result.Message));

                if (!string.IsNullOrWhiteSpace(result.MergeExceptionDetails))
                {
                    lines.Add(string.Empty);
                    lines.Add("PDF MERGE EXCEPTION");
                    lines.Add(result.MergeExceptionDetails);
                }

                if (result.ItemResults != null && result.ItemResults.Count > 0)
                {
                    lines.Add(string.Empty);
                    lines.Add("DRAWING RESULTS");

                    foreach (DrawingPdfItemResult item in result.ItemResults)
                    {
                        lines.Add(
                            (item.Index + 1).ToString("000") +
                            " | " + (item.Success ? "OK" : "ERROR") +
                            " | MARK=" + SafeText(item.Mark) +
                            " | REV=" + SafeText(item.Revision) +
                            " | DRAWN_BY=" + SafeText(item.DrawnBy) +
                            " | Tekla=" + item.TeklaPrintReturnedSuccess +
                            " | FileVerified=" + item.OutputFileVerified +
                            " | ChildDeleted=" + item.ChildFileDeleted +
                            " | File=" + SafeText(item.OutputFilePath) +
                            " | Message=" + SafeText(item.Message));

                        if (!string.IsNullOrWhiteSpace(item.ExceptionDetails))
                            lines.Add(item.ExceptionDetails);
                    }
                }

                if (!string.IsNullOrWhiteSpace(result.DiagnosticDetails))
                {
                    lines.Add(string.Empty);
                    lines.Add("DIAGNOSTIC DETAILS");
                    lines.Add(result.DiagnosticDetails);
                }

                File.WriteAllLines(diagnosticPath, lines.ToArray());
                return diagnosticPath;
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetDrawingDisplayText(
            Drawing drawing,
            int zeroBasedIndex)
        {
            if (drawing == null)
                return "Drawing_" + (zeroBasedIndex + 1);

            string[] propertyNames = new string[]
            {
                "Mark",
                "Name",
                "Title1",
                "Title2",
                "Title3"
            };

            foreach (string propertyName in propertyNames)
            {
                try
                {
                    PropertyInfo property = drawing.GetType().GetProperty(
                        propertyName,
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic);

                    if (property == null || !property.CanRead)
                        continue;

                    object value = property.GetValue(drawing, null);
                    string text = value == null ? null : value.ToString();

                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }
                catch
                {
                }
            }

            return drawing.GetType().Name + "_" + (zeroBasedIndex + 1);
        }

        private static string NormalizeDisplayText(
            string value,
            string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback == null ? string.Empty : fallback;

            string text = value.Trim();
            return text == "-" && !string.IsNullOrEmpty(fallback)
                ? fallback
                : text;
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

            return string.IsNullOrWhiteSpace(sanitized)
                ? "UNKNOWN"
                : sanitized;
        }
    }

    public static class ExternalPdfFileMerger
    {
        private const string DefaultMergedFileName = "TTSK_Merge";

        public static ExternalPdfMergeResult MergeSelectedFiles(
            IWin32Window owner)
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

                    selectedFiles = dialog.FileNames
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Select(Path.GetFullPath)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                result.SelectedFileCount = selectedFiles.Count;
                result.SourceFiles.AddRange(selectedFiles);

                if (selectedFiles.Count < 2)
                {
                    result.Message =
                        "Merge File cần ít nhất 2 file PDF được chọn.";
                    return result;
                }

                foreach (string sourceFile in selectedFiles)
                {
                    if (!File.Exists(sourceFile))
                    {
                        result.Message =
                            "Không tìm thấy file PDF đã chọn:\r\n" + sourceFile;
                        return result;
                    }
                }

                string outputDirectory = Path.GetDirectoryName(selectedFiles[0]);
                if (string.IsNullOrWhiteSpace(outputDirectory))
                {
                    result.Message =
                        "Không xác định được thư mục chứa các file PDF đã chọn.";
                    return result;
                }

                string normalizedOutputDirectory = NormalizeDirectoryPath(
                    outputDirectory);

                bool allFilesInSameFolder = selectedFiles.All(path =>
                    string.Equals(
                        NormalizeDirectoryPath(Path.GetDirectoryName(path)),
                        normalizedOutputDirectory,
                        StringComparison.OrdinalIgnoreCase));

                if (!allFilesInSameFolder)
                {
                    result.Message =
                        "Hãy chọn các file PDF nằm trong cùng một folder Windows.";
                    return result;
                }

                outputFilePath = BuildUniqueOutputPath(
                    outputDirectory,
                    DefaultMergedFileName,
                    ".pdf");

                result.OutputDirectory = outputDirectory;
                result.OutputFilePath = outputFilePath;

                MergePdfFiles(selectedFiles, outputFilePath);

                if (!VerifyMergedPdf(outputFilePath))
                {
                    throw new IOException(
                        "Đã tạo file tổng nhưng không xác minh được PDF đầu ra.");
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
                            sourceFile + " | " +
                            GetDeepestExceptionMessage(deleteException));
                    }
                }

                result.CleanupSuccess = cleanupErrors.Count == 0;
                result.CleanupDetails = cleanupErrors.Count == 0
                    ? null
                    : string.Join(Environment.NewLine, cleanupErrors.ToArray());
                result.PartialSuccess =
                    result.MergeSuccess && !result.CleanupSuccess;
                result.Success =
                    result.MergeSuccess && result.CleanupSuccess;

                if (result.Success)
                {
                    result.Message =
                        "Đã gộp " + selectedFiles.Count +
                        " file PDF và xóa toàn bộ file nguồn đã chọn.";
                }
                else
                {
                    result.Message =
                        "PDF tổng đã được tạo nhưng còn " +
                        cleanupErrors.Count +
                        " file nguồn không xóa được.";
                }

                TryOpenOutputFolder(outputDirectory);
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.PartialSuccess = false;
                result.Message =
                    "Merge File lỗi: " + GetDeepestExceptionMessage(ex);

                if (!result.MergeSuccess &&
                    !string.IsNullOrWhiteSpace(outputFilePath))
                {
                    TryDeleteIncompleteOutput(outputFilePath);
                }

                return result;
            }
        }

        private static void MergePdfFiles(
            IList<string> inputPdfPaths,
            string outputPdfPath)
        {
            PdfDocument outputDocument = new PdfDocument();

            try
            {
                outputDocument.Info.Title = "TTSK Merge File";
                outputDocument.Info.Subject =
                    "Merged from PDF files selected by the user.";
                outputDocument.Info.Creator = "TTSK AutoDim Plates";

                int importedPageCount = 0;

                foreach (string inputPdfPath in inputPdfPaths)
                {
                    PdfDocument inputDocument = null;

                    try
                    {
                        inputDocument = PdfReader.Open(
                            inputPdfPath,
                            PdfDocumentOpenMode.Import);

                        for (int pageIndex = 0;
                             pageIndex < inputDocument.PageCount;
                             pageIndex++)
                        {
                            outputDocument.AddPage(
                                inputDocument.Pages[pageIndex]);
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
                    throw new InvalidOperationException(
                        "Không đọc được trang PDF nào để gộp.");
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
                if (string.IsNullOrWhiteSpace(filePath) ||
                    !File.Exists(filePath) ||
                    new FileInfo(filePath).Length <= 0)
                {
                    return false;
                }

                using (PdfDocument verifyDocument = PdfReader.Open(
                    filePath,
                    PdfDocumentOpenMode.Import))
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
            string extension)
        {
            string candidate = Path.Combine(
                outputDirectory,
                baseFileName + extension);
            int duplicateNumber = 2;

            while (File.Exists(candidate))
            {
                candidate = Path.Combine(
                    outputDirectory,
                    baseFileName + "_" + duplicateNumber + extension);
                duplicateNumber++;
            }

            return candidate;
        }

        private static string NormalizeDirectoryPath(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                return string.Empty;

            return Path.GetFullPath(directoryPath)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
        }

        private static void TryOpenOutputFolder(string outputDirectory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(outputDirectory) ||
                    !Directory.Exists(outputDirectory))
                {
                    return;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = outputDirectory;
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
            }
            catch
            {
            }
        }

        private static void TryDeleteIncompleteOutput(string outputFilePath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(outputFilePath) &&
                    File.Exists(outputFilePath))
                {
                    File.Delete(outputFilePath);
                }
            }
            catch
            {
            }
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
