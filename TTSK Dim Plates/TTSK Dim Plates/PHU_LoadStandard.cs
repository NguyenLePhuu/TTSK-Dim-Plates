#pragma warning disable 1633

using System;
using System.Collections;
using System.IO;
using System.Threading;
using Tekla.Structures.Drawing;
using Tekla.Structures.Drawing.UI;
using Tekla.Structures.Model;
using Tekla.Structures.Model.Operations;

namespace Tekla.Technology.Akit.UserScript
{
    // Load Standard Service:
    // - Does NOT create temporary macro files.
    // - Does NOT delete or edit the fixed macro file.
    // - Auto-selects all drawing views, then runs the fixed Akit macro.
    // - Macro is resolved from the current software folder, so the whole folder can be moved.
    // - This class is independent from Slot 06, so Slot 06 can be used for another CS file later.
    public class PHU_LoadStandardService
    {
        public static bool LastRunSucceeded { get; private set; }
        public static string LastRunMessage { get; private set; }

        private const string FixedMacroFileName = "Phu_Macro_Loadstandard.cs";
        private const string AlternateFixedMacroFileName = "Phu_Macro_LoadStandard.cs";

        public static void Run()
        {
            LastRunSucceeded = false;
            LastRunMessage = string.Empty;

            try
            {
                DrawingHandler drawingHandler = new DrawingHandler();
                Drawing drawing = drawingHandler.GetActiveDrawing();

                if (drawing == null)
                {
                    LastRunMessage = "LoadStandard: không có active drawing.";
                    return;
                }

                if (!(drawing is SinglePartDrawing) &&
                    !(drawing is AssemblyDrawing))
                {
                    LastRunMessage = "LoadStandard: drawing hiện tại không phải SinglePartDrawing hoặc AssemblyDrawing.";
                    return;
                }

                Model model = new Model();
                if (!model.GetConnectionStatus())
                {
                    LastRunMessage = "LoadStandard: không kết nối được model.";
                    return;
                }

                int selectedViewCount = SelectAllViews(drawingHandler, drawing);
                if (selectedViewCount == 0)
                {
                    LastRunMessage = "LoadStandard: không tìm thấy view để select.";
                    return;
                }

                string checkedMacroPaths;
                string fixedMacroPath = ResolveFixedMacroPath(out checkedMacroPaths);
                if (string.IsNullOrEmpty(fixedMacroPath))
                {
                    LastRunMessage =
                        "LoadStandard: không tìm thấy macro cố định trong folder phần mềm. Đã kiểm tra: " +
                        checkedMacroPaths;
                    return;
                }

                string usedRunArgument;
                string runErrors;
                bool started = RunFixedMacroWithFallbackArguments(
                    fixedMacroPath,
                    FixedMacroFileName,
                    out usedRunArgument,
                    out runErrors);

                if (!started)
                {
                    LastRunMessage =
                        "LoadStandard: Tekla không chạy được macro cố định. " +
                        "MacroPath=" + fixedMacroPath +
                        " | Tried=" + usedRunArgument +
                        " | Errors=" + runErrors +
                        " | Lưu ý: LoadStandard chỉ gọi đúng macro trong folder phần mềm này. Nếu Tekla không chấp nhận absolute path, hãy thêm folder phần mềm/macro này vào XS_MACRO_DIRECTORY.";
                    return;
                }

                string waitError;
                if (!WaitForRunningMacroToFinish(out waitError))
                {
                    LastRunMessage =
                        "LoadStandard: macro hình học chưa hoàn tất. " +
                        waitError;
                    return;
                }

                LastRunSucceeded = true;
                LastRunMessage =
                    "LoadStandard: đã load tiêu chuẩn hình học cho " + selectedViewCount +
                    " view. Macro=" + fixedMacroPath +
                    " | RunArg=" + usedRunArgument;
            }
            catch (Exception ex)
            {
                Exception real = ex.InnerException ?? ex;
                LastRunMessage =
                    "LoadStandard " + real.GetType().Name + ": " + real.Message;
            }
        }

        private static int SelectAllViews(
            DrawingHandler drawingHandler,
            Drawing drawing)
        {
            ContainerView sheet = drawing.GetSheet();
            if (sheet == null)
                return 0;

            ArrayList viewsToSelect = new ArrayList();
            DrawingObjectEnumerator views = sheet.GetAllViews();

            while (views.MoveNext())
            {
                View view = views.Current as View;
                if (view != null)
                    viewsToSelect.Add(view);
            }

            if (viewsToSelect.Count == 0)
                return 0;

            DrawingObjectSelector selector =
                drawingHandler.GetDrawingObjectSelector();

            selector.SelectObjects(viewsToSelect, false);
            return viewsToSelect.Count;
        }

        private static string ResolveFixedMacroPath(out string checkedMacroPaths)
        {
            checkedMacroPaths = string.Empty;

            try
            {
                ArrayList softwareDirectories = BuildSoftwareDirectoryCandidates();

                for (int i = 0; i < softwareDirectories.Count; i++)
                {
                    string softwareDirectory = softwareDirectories[i] as string;
                    if (string.IsNullOrEmpty(softwareDirectory))
                        continue;

                    string currentCheckedPaths;
                    string foundPath = CheckMacroInSoftwareDirectory(
                        softwareDirectory,
                        out currentCheckedPaths);

                    checkedMacroPaths = AppendValue(
                        checkedMacroPaths,
                        currentCheckedPaths);

                    if (!string.IsNullOrEmpty(foundPath))
                        return foundPath;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string CheckMacroInSoftwareDirectory(
            string softwareDirectory,
            out string checkedMacroPaths)
        {
            checkedMacroPaths = string.Empty;

            string directPath = Path.Combine(
                softwareDirectory,
                FixedMacroFileName);

            checkedMacroPaths = AppendValue(checkedMacroPaths, directPath);
            if (File.Exists(directPath))
                return directPath;

            string directAlternatePath = Path.Combine(
                softwareDirectory,
                AlternateFixedMacroFileName);

            checkedMacroPaths = AppendValue(checkedMacroPaths, directAlternatePath);
            if (File.Exists(directAlternatePath))
                return directAlternatePath;

            string drawingsPath = Path.Combine(
                Path.Combine(
                    Path.Combine(softwareDirectory, "macros"),
                    "drawings"),
                FixedMacroFileName);

            checkedMacroPaths = AppendValue(checkedMacroPaths, drawingsPath);
            if (File.Exists(drawingsPath))
                return drawingsPath;

            string drawingsAlternatePath = Path.Combine(
                Path.Combine(
                    Path.Combine(softwareDirectory, "macros"),
                    "drawings"),
                AlternateFixedMacroFileName);

            checkedMacroPaths = AppendValue(checkedMacroPaths, drawingsAlternatePath);
            if (File.Exists(drawingsAlternatePath))
                return drawingsAlternatePath;

            return null;
        }

        private static ArrayList BuildSoftwareDirectoryCandidates()
        {
            ArrayList directories = new ArrayList();

            AddDirectoryAndParents(
                directories,
                GetAssemblyDirectory(typeof(PHU_LoadStandardService).Assembly),
                5);

            AddDirectoryAndParents(
                directories,
                GetAssemblyDirectory(System.Reflection.Assembly.GetExecutingAssembly()),
                5);

            AddDirectoryAndParents(
                directories,
                AppDomain.CurrentDomain.BaseDirectory,
                5);

            AddDirectoryAndParents(
                directories,
                Directory.GetCurrentDirectory(),
                2);

            return directories;
        }

        private static string GetAssemblyDirectory(System.Reflection.Assembly assembly)
        {
            try
            {
                if (assembly == null)
                    return null;

                string location = assembly.Location;
                if (string.IsNullOrEmpty(location))
                    return null;

                return Path.GetDirectoryName(location);
            }
            catch
            {
            }

            return null;
        }

        private static void AddDirectoryAndParents(
            ArrayList directories,
            string directory,
            int parentLevel)
        {
            if (string.IsNullOrEmpty(directory))
                return;

            string current = directory;

            for (int i = 0; i <= parentLevel; i++)
            {
                AddDirectoryCandidate(directories, current);

                try
                {
                    DirectoryInfo parent = Directory.GetParent(current);
                    if (parent == null)
                        break;

                    current = parent.FullName;
                }
                catch
                {
                    break;
                }
            }
        }

        private static void AddDirectoryCandidate(
            ArrayList directories,
            string directory)
        {
            if (directories == null || string.IsNullOrEmpty(directory))
                return;

            try
            {
                string fullPath = Path.GetFullPath(directory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (!Directory.Exists(fullPath))
                    return;

                for (int i = 0; i < directories.Count; i++)
                {
                    string existing = directories[i] as string;
                    if (string.Equals(existing, fullPath, StringComparison.OrdinalIgnoreCase))
                        return;
                }

                directories.Add(fullPath);
            }
            catch
            {
            }
        }

        private static bool RunFixedMacroWithFallbackArguments(
            string fixedMacroPath,
            string macroFileName,
            out string usedRunArgument,
            out string runErrors)
        {
            usedRunArgument = string.Empty;
            runErrors = string.Empty;

            string[] candidates = BuildRunArgumentCandidates(
                fixedMacroPath,
                macroFileName);

            for (int i = 0; i < candidates.Length; i++)
            {
                string argument = candidates[i];
                if (string.IsNullOrEmpty(argument))
                    continue;

                try
                {
                    bool started = Operation.RunMacro(argument);
                    usedRunArgument = AppendValue(usedRunArgument, argument);

                    if (started)
                        return true;

                    runErrors = AppendValue(
                        runErrors,
                        argument + " => returned false");
                }
                catch (Exception ex)
                {
                    usedRunArgument = AppendValue(usedRunArgument, argument);
                    runErrors = AppendValue(
                        runErrors,
                        argument + " => " + ex.GetType().Name + ": " + ex.Message);
                }

                Thread.Sleep(150);
            }

            return false;
        }

        private static bool WaitForRunningMacroToFinish(out string error)
        {
            error = string.Empty;
            const int timeoutMilliseconds = 20000;
            const int pollMilliseconds = 50;
            const int idlePollsAfterRunning = 2;
            const int idlePollsWithoutObservedRunning = 4;

            bool observedRunning = false;
            int idlePollCount = 0;
            int elapsedMilliseconds = 0;

            while (elapsedMilliseconds <= timeoutMilliseconds)
            {
                bool isRunning;
                try
                {
                    isRunning = Operation.IsMacroRunning();
                }
                catch (Exception ex)
                {
                    error =
                        "Không kiểm tra được trạng thái macro: " +
                        ex.GetType().Name + ": " + ex.Message;
                    return false;
                }

                if (isRunning)
                {
                    observedRunning = true;
                    idlePollCount = 0;
                }
                else
                {
                    idlePollCount++;
                    int requiredIdlePolls = observedRunning
                        ? idlePollsAfterRunning
                        : idlePollsWithoutObservedRunning;

                    if (idlePollCount >= requiredIdlePolls)
                    {
                        Thread.Sleep(50);
                        return true;
                    }
                }

                Thread.Sleep(pollMilliseconds);
                elapsedMilliseconds += pollMilliseconds;
            }

            error =
                "Quá thời gian chờ " + timeoutMilliseconds +
                " ms; Tekla vẫn báo macro đang chạy.";
            return false;
        }

        private static string[] BuildRunArgumentCandidates(
            string fixedMacroPath,
            string macroFileName)
        {
            // Do not try macroFileName-only or drawings-relative arguments here,
            // because those may be resolved by Tekla from another XS_MACRO_DIRECTORY.
            // Slot06 is intentionally locked to the exact macro file in the software folder.
            return new string[]
            {
                fixedMacroPath
            };
        }

        private static string AppendValue(string current, string value)
        {
            if (string.IsNullOrEmpty(current))
                return value;

            return current + " ; " + value;
        }
    }
}
