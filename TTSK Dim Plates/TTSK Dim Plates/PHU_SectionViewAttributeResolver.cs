#pragma warning disable 1633

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

namespace Tekla.Technology.Akit.UserScript
{
    public sealed class SectionViewAttributeResolution
    {
        public bool Success;
        public string AttributeName = "";
        public string Source = "";
        public string DrawingAttributeName = "";
        public string DrawingPropertyFile = "";
        public string EnabledCode = "";
        public string RawDrawingViews = "";
        public string Error = "";
        public string LiveDialogError = "";
        public readonly List<string> ActiveViewAttributeNames =
            new List<string>();
        public readonly List<string> MatchingPropertyFiles =
            new List<string>();
        public readonly List<string> LiveDialogValues =
            new List<string>();
    }

    // Reads the currently loaded Section View properties value from the
    // Drawing Properties dialog. The target mirrors Viewpropertive1.cs:
    // View creation tree -> Views tab -> row 6. This must run BEFORE
    // PHU_LoadStandardService changes the drawing/view attributes.
    public static class SectionViewAttributeResolver
    {
        private const string DrawingViewsKey = "dv.aDrawingViews";
        private const string SectionViewTypeCode = "7";
        private const string SinglePartDrawingPropertiesDialogId =
            "wdraw_dial";
        private const string AssemblyDrawingPropertiesDialogId =
            "adraw_dial";
        private const string DrawingPropertiesMenuTreeId =
            "gratCastUnitDrawingAttributesMenuTree";
        private const string DrawingPropertiesViewCreationNode =
            "View creation";
        private const string DrawingPropertiesMainContainerId = "contMain";
        private const string DrawingPropertiesViewsTabId = "tabViews";
        private const string DrawingPropertiesViewsTableId = "table_ViewsTable";
        private const int SectionViewTableRowIndex = 6;
        private const int WmClose = 0x0010;
        private const int WmGetText = 0x000D;
        private const int BmClick = 0x00F5;
        private const int CbGetCurrentSelection = 0x0147;
        private const int CbGetListText = 0x0148;
        private const uint SendMessageAbortIfHung = 0x0002;

        private delegate bool EnumWindowsCallback(
            IntPtr windowHandle,
            IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(
            EnumWindowsCallback callback,
            IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(
            IntPtr parentWindow,
            EnumWindowsCallback callback,
            IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr windowHandle,
            out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(
            IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(
            IntPtr windowHandle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(
            IntPtr windowHandle,
            StringBuilder text,
            int maximumCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(
            IntPtr windowHandle,
            StringBuilder className,
            int maximumCount);

        [DllImport(
            "user32.dll",
            EntryPoint = "SendMessageTimeoutW",
            CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeoutText(
            IntPtr windowHandle,
            uint message,
            IntPtr wParam,
            StringBuilder lParam,
            uint flags,
            uint timeout,
            out IntPtr result);

        [DllImport(
            "user32.dll",
            EntryPoint = "SendMessageTimeoutW")]
        private static extern IntPtr SendMessageTimeoutPointer(
            IntPtr windowHandle,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            uint flags,
            uint timeout,
            out IntPtr result);

        private sealed class DrawingViewRow
        {
            public string TypeCode = "";
            public string EnabledCode = "";
            public string ReservedValue = "";
            public string ViewAttributeName = "";
        }

        private sealed class DrawingPropertyData
        {
            public string File = "";
            public string RawDrawingViews = "";
            public readonly List<DrawingViewRow> Rows =
                new List<DrawingViewRow>();
        }

        private sealed class CandidateMatch
        {
            public DrawingPropertyData Data;
            public DrawingViewRow SectionRow;
            public int Score;
        }

        private sealed class NativeWindowInfo
        {
            public IntPtr Handle;
            public string Text = "";
            public string ClassName = "";
        }

        public static SectionViewAttributeResolution Resolve(
            Drawing drawing,
            Model model)
        {
            SectionViewAttributeResolution result =
                new SectionViewAttributeResolution();

            if (drawing == null)
            {
                result.Error = "Active drawing is null.";
                return result;
            }

            bool isSinglePart = drawing is SinglePartDrawing;
            bool isAssembly = drawing is AssemblyDrawing;
            if (!isSinglePart && !isAssembly)
            {
                result.Error =
                    "Drawing is not SinglePartDrawing or AssemblyDrawing.";
                return result;
            }

            string extension = isSinglePart ? ".wd" : ".ad";
            string modelPath = ReadModelPath(model);

            result.DrawingAttributeName =
                ReadAttributeFilename(drawing);
            ReadActiveViewAttributeNames(
                drawing,
                result.ActiveViewAttributeNames);

            // Authoritative source: read the View properties cell that Tekla
            // has currently loaded for the Section row in Drawing Properties.
            // This targets the same tree/tab/table/row as Viewpropertive1.cs,
            // without invoking its deselect/Modify/Apply/OK commands.
            List<string> viewAttributeFiles =
                CollectAttributeFiles(".vi", modelPath);
            if (TryResolveFromLiveDrawingPropertiesDialog(
                result,
                viewAttributeFiles,
                isAssembly))
            {
                return result;
            }

            // Legacy read-only fallbacks remain available for diagnostics and
            // compatibility if the live dialog cannot be inspected.
            string exactPropertyFile = ResolveAttributeFile(
                result.DrawingAttributeName,
                extension,
                modelPath);

            if (!string.IsNullOrWhiteSpace(exactPropertyFile))
            {
                DrawingPropertyData exactData =
                    ReadDrawingProperty(exactPropertyFile);
                DrawingViewRow exactSection =
                    FindSectionRow(exactData);

                if (exactSection != null)
                {
                    return CompleteFromPropertyFile(
                        result,
                        exactData,
                        exactSection,
                        "Fallback: Drawing.AttributeFilename");
                }
            }

            // If a real section view already exists, its loaded ViewAttributes
            // are more reliable than guessing a drawing property file.
            string existingSectionAttribute =
                ReadExistingSectionViewAttribute(drawing);
            if (!string.IsNullOrWhiteSpace(existingSectionAttribute))
            {
                result.Success = true;
                result.AttributeName = existingSectionAttribute;
                result.Source = "Fallback: Existing SectionView.Attributes";
                return result;
            }

            List<string> derivedPropertyNames = new List<string>();
            List<string> candidateFiles = new List<string>();

            for (int i = 0;
                i < result.ActiveViewAttributeNames.Count;
                i++)
            {
                List<string> derived = BuildDrawingPropertyNameCandidates(
                    result.ActiveViewAttributeNames[i]);

                for (int j = 0; j < derived.Count; j++)
                {
                    AddUnique(derivedPropertyNames, derived[j]);
                    AddUnique(
                        candidateFiles,
                        ResolveAttributeFile(
                            derived[j],
                            extension,
                            modelPath));
                }
            }

            List<string> allPropertyFiles =
                CollectAttributeFiles(extension, modelPath);
            for (int i = 0; i < allPropertyFiles.Count; i++)
                AddUnique(candidateFiles, allPropertyFiles[i]);

            List<CandidateMatch> matches = new List<CandidateMatch>();
            for (int i = 0; i < candidateFiles.Count; i++)
            {
                DrawingPropertyData data =
                    ReadDrawingProperty(candidateFiles[i]);
                DrawingViewRow sectionRow = FindSectionRow(data);
                if (sectionRow == null)
                    continue;

                int score = ScoreCandidate(
                    data,
                    result.ActiveViewAttributeNames,
                    derivedPropertyNames);
                if (score <= 0)
                    continue;

                CandidateMatch match = new CandidateMatch();
                match.Data = data;
                match.SectionRow = sectionRow;
                match.Score = score;
                matches.Add(match);
            }

            if (matches.Count == 0)
            {
                result.Error =
                    "Cannot identify the original drawing property. " +
                    "Drawing.AttributeFilename is empty and current view " +
                    "attributes do not uniquely reference a .wd/.ad file. " +
                    "Targeted Drawing Properties Views row 6 also failed: " +
                    result.LiveDialogError;
                return result;
            }

            int bestScore = int.MinValue;
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Score > bestScore)
                    bestScore = matches[i].Score;
            }

            List<CandidateMatch> bestMatches = new List<CandidateMatch>();
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Score == bestScore)
                {
                    bestMatches.Add(matches[i]);
                    AddUnique(
                        result.MatchingPropertyFiles,
                        matches[i].Data.File);
                }
            }

            string commonSectionAttribute = NormalizeViewAttributeName(
                bestMatches[0].SectionRow.ViewAttributeName);
            for (int i = 1; i < bestMatches.Count; i++)
            {
                string candidateSectionAttribute =
                    NormalizeViewAttributeName(
                        bestMatches[i].SectionRow.ViewAttributeName);

                if (!string.Equals(
                    commonSectionAttribute,
                    candidateSectionAttribute,
                    StringComparison.OrdinalIgnoreCase))
                {
                    result.Error =
                        "Ambiguous drawing property match. More than one " +
                        ".wd/.ad file matches the active views but their " +
                        "Section views properties are different. Targeted " +
                        "Drawing Properties Views row 6 also failed: " +
                        result.LiveDialogError;
                    return result;
                }
            }

            string source = bestMatches.Count == 1
                ? "Fallback: Matched active view attributes"
                : "Fallback: Matched active views; all best files use same Section property";

            return CompleteFromPropertyFile(
                result,
                bestMatches[0].Data,
                bestMatches[0].SectionRow,
                source);
        }

        private static bool TryResolveFromLiveDrawingPropertiesDialog(
            SectionViewAttributeResolution result,
            List<string> viewAttributeFiles,
            bool isAssemblyDrawing)
        {
            if (viewAttributeFiles == null ||
                viewAttributeFiles.Count == 0)
            {
                result.LiveDialogError =
                    "No .vi View properties files are available for comparison.";
                return false;
            }

            Process teklaProcess = FindTeklaProcess();
            if (teklaProcess == null)
            {
                result.LiveDialogError =
                    "TeklaStructures process was not found.";
                return false;
            }

            List<NativeWindowInfo> beforeWindows =
                GetVisibleNativeWindows(teklaProcess.Id);
            HashSet<long> beforeHandles = new HashSet<long>();
            for (int i = 0; i < beforeWindows.Count; i++)
                beforeHandles.Add(beforeWindows[i].Handle.ToInt64());

            string invokeError = "";
            Thread dialogThread = new Thread(delegate()
            {
                try
                {
                    InvokeDisplayDrawingPropertiesDialog();
                }
                catch (Exception ex)
                {
                    Exception real = ex.InnerException ?? ex;
                    invokeError = real.GetType().Name + ": " +
                        real.Message;
                }
            });
            dialogThread.IsBackground = true;
            try
            {
                dialogThread.SetApartmentState(ApartmentState.STA);
            }
            catch
            {
            }
            dialogThread.Start();

            IntPtr dialogHandle = IntPtr.Zero;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                Thread.Sleep(100);
                List<NativeWindowInfo> currentWindows =
                    GetVisibleNativeWindows(teklaProcess.Id);

                int bestScore = int.MinValue;

                for (int windowIndex = 0;
                    windowIndex < currentWindows.Count;
                    windowIndex++)
                {
                    NativeWindowInfo window =
                        currentWindows[windowIndex];
                    long handleValue = window.Handle.ToInt64();
                    if (handleValue == 0 ||
                        beforeHandles.Contains(handleValue) ||
                        IsIgnoredNativeWindowClass(window.ClassName))
                    {
                        continue;
                    }

                    int score = ScoreNativeDialogWindow(window);
                    if (score <= bestScore)
                        continue;

                    bestScore = score;
                    dialogHandle = window.Handle;
                }

                if (dialogHandle != IntPtr.Zero)
                    break;

                if (!dialogThread.IsAlive &&
                    !string.IsNullOrWhiteSpace(invokeError))
                {
                    break;
                }
            }

            if (dialogHandle == IntPtr.Zero)
            {
                result.LiveDialogError =
                    "Drawing Properties dialog did not open" +
                    (string.IsNullOrWhiteSpace(invokeError)
                        ? "."
                        : ": " + invokeError);
                return false;
            }

            IntPtr viewPropertiesDialogHandle = IntPtr.Zero;
            try
            {
                string targetError;
                if (!TryTargetSectionViewRowWithAkit(
                    teklaProcess.Id,
                    isAssemblyDrawing,
                    out targetError))
                {
                    result.LiveDialogError =
                        "Drawing Properties dialog opened, but Views row 6 " +
                        "could not be selected: " + targetError;
                    return false;
                }

                string openViewPropertiesError;
                if (!TryOpenSelectedViewPropertiesDialog(
                    dialogHandle,
                    teklaProcess.Id,
                    out viewPropertiesDialogHandle,
                    out openViewPropertiesError))
                {
                    result.LiveDialogError =
                        "Views row 6 was selected, but its View properties " +
                        "dialog did not open: " + openViewPropertiesError;
                    return false;
                }

                Thread.Sleep(250);

                List<string> preferredValues = new List<string>();
                CollectNativeWindowValues(
                    viewPropertiesDialogHandle,
                    result.LiveDialogValues,
                    preferredValues);

                string viewAttributeFile =
                    FindViewAttributeFileFromDialogValues(
                    preferredValues,
                    viewAttributeFiles);

                if (string.IsNullOrWhiteSpace(viewAttributeFile))
                {
                    viewAttributeFile =
                        FindViewAttributeFileFromDialogValues(
                        result.LiveDialogValues,
                        viewAttributeFiles);
                }

                if (string.IsNullOrWhiteSpace(viewAttributeFile))
                {
                    Thread.Sleep(300);
                    CollectNativeWindowValues(
                        viewPropertiesDialogHandle,
                        result.LiveDialogValues,
                        preferredValues);

                    viewAttributeFile =
                        FindViewAttributeFileFromDialogValues(
                            preferredValues,
                            viewAttributeFiles);

                    if (string.IsNullOrWhiteSpace(viewAttributeFile))
                    {
                        viewAttributeFile =
                            FindViewAttributeFileFromDialogValues(
                                result.LiveDialogValues,
                                viewAttributeFiles);
                    }
                }

                if (string.IsNullOrWhiteSpace(viewAttributeFile))
                {
                    result.LiveDialogError =
                        "The View properties dialog for Views row 6 opened, " +
                        "but its Save/Load value did not match a known .vi file.";
                    return false;
                }

                result.Success = true;
                result.AttributeName = Path.GetFileNameWithoutExtension(
                    viewAttributeFile);
                result.Source =
                    isAssemblyDrawing
                        ? "Live Assembly Drawing Properties / Views row 6 / View properties"
                        : "Live Drawing Properties / Views row 6 / View properties";
                result.Error = "";
                result.LiveDialogError = "";
                return true;
            }
            catch (Exception ex)
            {
                Exception real = ex.InnerException ?? ex;
                result.LiveDialogError = real.GetType().Name + ": " +
                    real.Message;
                return false;
            }
            finally
            {
                CloseNativeWindowAndWait(
                    viewPropertiesDialogHandle,
                    1000);
                CloseNativeWindowAndWait(
                    dialogHandle,
                    1000);
                try
                {
                    dialogThread.Join(1000);
                }
                catch
                {
                }
            }
        }

        private static Process FindTeklaProcess()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(
                    "TeklaStructures");
                for (int i = 0; i < processes.Length; i++)
                {
                    if (processes[i].MainWindowHandle != IntPtr.Zero)
                        return processes[i];
                }
            }
            catch
            {
            }

            return null;
        }

        private static List<NativeWindowInfo> GetVisibleNativeWindows(
            int processId)
        {
            List<NativeWindowInfo> result =
                new List<NativeWindowInfo>();

            try
            {
                EnumWindowsCallback callback = delegate(
                    IntPtr windowHandle,
                    IntPtr parameter)
                {
                    uint ownerProcessId;
                    GetWindowThreadProcessId(
                        windowHandle,
                        out ownerProcessId);

                    if (ownerProcessId != (uint)processId ||
                        !IsWindowVisible(windowHandle))
                    {
                        return true;
                    }

                    NativeWindowInfo info = new NativeWindowInfo();
                    info.Handle = windowHandle;
                    info.Text = ReadNativeWindowText(windowHandle);
                    info.ClassName = ReadNativeWindowClass(windowHandle);
                    result.Add(info);
                    return true;
                };

                EnumWindows(callback, IntPtr.Zero);
            }
            catch
            {
            }

            return result;
        }

        private static bool IsIgnoredNativeWindowClass(
            string className)
        {
            if (string.IsNullOrWhiteSpace(className))
                return false;

            return className.IndexOf(
                    "ComboLBox",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                className.IndexOf(
                    "tooltips_class",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                className.IndexOf(
                    "IME",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int ScoreNativeDialogWindow(
            NativeWindowInfo window)
        {
            int score = 0;
            if (window == null)
                return score;

            if (!string.IsNullOrWhiteSpace(window.Text) &&
                window.Text.IndexOf(
                    "propert",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 100;
            }

            if (!string.IsNullOrWhiteSpace(window.ClassName) &&
                window.ClassName.StartsWith(
                    "Afx",
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
            }

            if (string.Equals(
                window.ClassName,
                "#32770",
                StringComparison.OrdinalIgnoreCase))
            {
                score += 40;
            }

            return score;
        }

        private static void CollectNativeWindowValues(
            IntPtr dialogHandle,
            List<string> allValues,
            List<string> preferredValues)
        {
            if (dialogHandle == IntPtr.Zero)
                return;

            AddNativeControlValue(
                dialogHandle,
                allValues,
                preferredValues);

            try
            {
                EnumWindowsCallback callback = delegate(
                    IntPtr childHandle,
                    IntPtr parameter)
                {
                    AddNativeControlValue(
                        childHandle,
                        allValues,
                        preferredValues);
                    return true;
                };

                EnumChildWindows(
                    dialogHandle,
                    callback,
                    IntPtr.Zero);
            }
            catch
            {
            }
        }

        private static void AddNativeControlValue(
            IntPtr windowHandle,
            List<string> allValues,
            List<string> preferredValues)
        {
            // Tekla creates the controls for inactive property pages too.
            // Reading only visible controls prevents a hidden page's
            // "standard" value from being mistaken for the selected row.
            if (!IsWindowVisible(windowHandle))
                return;

            string className = ReadNativeWindowClass(windowHandle);
            string text = ReadNativeWindowText(windowHandle);

            AddUniqueLimited(allValues, text, 150);

            bool isPreferredControl =
                className.IndexOf(
                    "ComboBox",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                className.IndexOf(
                    "Edit",
                    StringComparison.OrdinalIgnoreCase) >= 0;

            if (isPreferredControl)
                AddUniqueLimited(preferredValues, text, 150);

            if (className.IndexOf(
                "ComboBox",
                StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string selectedText = ReadNativeComboSelection(
                    windowHandle);
                AddUniqueLimited(allValues, selectedText, 150);
                AddUniqueLimited(
                    preferredValues,
                    selectedText,
                    150);
            }
        }

        private static string ReadNativeWindowClass(
            IntPtr windowHandle)
        {
            try
            {
                StringBuilder value = new StringBuilder(256);
                GetClassName(
                    windowHandle,
                    value,
                    value.Capacity);
                return value.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static string ReadNativeWindowText(
            IntPtr windowHandle)
        {
            try
            {
                StringBuilder value = new StringBuilder(1024);
                GetWindowText(
                    windowHandle,
                    value,
                    value.Capacity);
                if (value.Length > 0)
                    return value.ToString();

                IntPtr messageResult;
                SendMessageTimeoutText(
                    windowHandle,
                    WmGetText,
                    new IntPtr(value.Capacity),
                    value,
                    SendMessageAbortIfHung,
                    200,
                    out messageResult);
                return value.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static string ReadNativeComboSelection(
            IntPtr comboHandle)
        {
            try
            {
                IntPtr selectionResult;
                SendMessageTimeoutPointer(
                    comboHandle,
                    CbGetCurrentSelection,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    SendMessageAbortIfHung,
                    200,
                    out selectionResult);

                int selectionIndex = selectionResult.ToInt32();
                if (selectionIndex < 0)
                    return "";

                StringBuilder value = new StringBuilder(1024);
                IntPtr textResult;
                SendMessageTimeoutText(
                    comboHandle,
                    CbGetListText,
                    new IntPtr(selectionIndex),
                    value,
                    SendMessageAbortIfHung,
                    200,
                    out textResult);
                return value.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static void InvokeDisplayDrawingPropertiesDialog()
        {
            System.Reflection.Assembly assembly =
                typeof(DrawingHandler).Assembly;
            Type proxyType = assembly.GetType(
                "Tekla.Structures.DrawingInternal.DelegateProxy",
                true);
            MethodInfo getDelegate = proxyType.GetMethod(
                "get_Delegate",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static);
            if (getDelegate == null)
                throw new MissingMethodException(
                    proxyType.FullName,
                    "get_Delegate");

            object drawingDelegate = getDelegate.Invoke(null, null);
            if (drawingDelegate == null)
                throw new InvalidOperationException(
                    "Drawing delegate is null.");

            Type delegateType = assembly.GetType(
                "Tekla.Structures.DrawingInternal.ICDelegate",
                true);
            MethodInfo display = delegateType.GetMethod(
                "ExportDisplayDrawingPropertiesDialog");
            if (display == null)
                throw new MissingMethodException(
                    delegateType.FullName,
                    "ExportDisplayDrawingPropertiesDialog");

            display.Invoke(drawingDelegate, null);
        }

        private static bool TryTargetSectionViewRowWithAkit(
            int teklaProcessId,
            bool isAssemblyDrawing,
            out string error)
        {
            error = "";
            object proxy = null;
            MethodInfo unsubscribe = null;
            bool subscribed = false;

            try
            {
                string teklaBin = "";
                try
                {
                    Process teklaProcess = Process.GetProcessById(
                        teklaProcessId);
                    if (teklaProcess.MainModule != null)
                    {
                        teklaBin = Path.GetDirectoryName(
                            teklaProcess.MainModule.FileName);
                    }
                }
                catch
                {
                }

                if (string.IsNullOrWhiteSpace(teklaBin))
                {
                    teklaBin = Path.GetDirectoryName(
                        typeof(DrawingHandler).Assembly.Location);
                }
                string macroAkitFile = Path.Combine(
                    teklaBin,
                    "Tekla.Macros.Akit.dll");
                string akitFile = Path.Combine(teklaBin, "Akit5.dll");

                if (!File.Exists(macroAkitFile) || !File.Exists(akitFile))
                {
                    error = "Tekla Akit assemblies were not found.";
                    return false;
                }

                System.Reflection.Assembly macroAkitAssembly =
                    System.Reflection.Assembly.LoadFrom(macroAkitFile);
                System.Reflection.Assembly akitAssembly =
                    System.Reflection.Assembly.LoadFrom(akitFile);

                Type proxyType = macroAkitAssembly.GetType(
                    "Tekla.Macros.Akit.DynamicScriptMessengerClientProxy",
                    true);
                Type scriptType = akitAssembly.GetType(
                    "Tekla.Technology.Akit.IScript",
                    true);

                MethodInfo create = proxyType.GetMethod(
                    "Create",
                    BindingFlags.Public | BindingFlags.Static);
                MethodInfo subscribe = proxyType.GetMethod(
                    "Subscribe",
                    BindingFlags.Public | BindingFlags.Instance);
                unsubscribe = proxyType.GetMethod(
                    "Unsubscribe",
                    BindingFlags.Public | BindingFlags.Instance);
                MethodInfo getRemoteScript = proxyType.GetMethod(
                    "GetRemoteScriptAdapter",
                    BindingFlags.Public | BindingFlags.Instance);

                if (create == null || subscribe == null ||
                    unsubscribe == null || getRemoteScript == null)
                {
                    error = "Required Tekla Akit messenger methods are missing.";
                    return false;
                }

                proxy = create.Invoke(null, new object[] { null });
                if (proxy == null)
                {
                    error = "Tekla Akit messenger proxy is null.";
                    return false;
                }

                object subscribeResult = subscribe.Invoke(
                    proxy,
                    new object[] { teklaProcessId });
                subscribed = subscribeResult is bool &&
                    (bool)subscribeResult;
                if (!subscribed)
                {
                    error = "Cannot subscribe to the active Tekla process.";
                    return false;
                }

                object script = getRemoteScript.Invoke(
                    proxy,
                    new object[] { teklaProcessId });
                if (script == null)
                {
                    error = "Tekla remote Akit script adapter is null.";
                    return false;
                }

                MethodInfo tabChange = scriptType.GetMethod(
                    "TabChange",
                    new Type[]
                    {
                        typeof(string),
                        typeof(string),
                        typeof(string)
                    });
                MethodInfo treeSelect = scriptType.GetMethod(
                    "TreeSelect",
                    new Type[]
                    {
                        typeof(string),
                        typeof(string),
                        typeof(string)
                    });
                MethodInfo tableSelect = scriptType.GetMethod(
                    "TableSelect",
                    new Type[]
                    {
                        typeof(string),
                        typeof(string),
                        typeof(int[])
                    });

                if (treeSelect == null || tableSelect == null ||
                    (!isAssemblyDrawing && tabChange == null))
                {
                    error = "Required Tekla Akit dialog methods are missing.";
                    return false;
                }

                string drawingPropertiesDialogId = isAssemblyDrawing
                    ? AssemblyDrawingPropertiesDialogId
                    : SinglePartDrawingPropertiesDialogId;

                treeSelect.Invoke(
                    script,
                    new object[]
                    {
                        drawingPropertiesDialogId,
                        DrawingPropertiesMenuTreeId,
                        DrawingPropertiesViewCreationNode
                    });
                Thread.Sleep(150);
                if (!isAssemblyDrawing)
                {
                    tabChange.Invoke(
                        script,
                        new object[]
                        {
                            drawingPropertiesDialogId,
                            DrawingPropertiesMainContainerId,
                            DrawingPropertiesViewsTabId
                        });
                    Thread.Sleep(150);
                }
                tableSelect.Invoke(
                    script,
                    new object[]
                    {
                        drawingPropertiesDialogId,
                        DrawingPropertiesViewsTableId,
                        new int[] { SectionViewTableRowIndex }
                    });
                return true;
            }
            catch (Exception ex)
            {
                Exception real = ex.InnerException ?? ex;
                error = real.GetType().Name + ": " + real.Message;
                return false;
            }
            finally
            {
                if (subscribed && proxy != null && unsubscribe != null)
                {
                    try
                    {
                        unsubscribe.Invoke(
                            proxy,
                            new object[] { teklaProcessId });
                    }
                    catch
                    {
                    }
                }

                DisposeAkitMessengerProxy(proxy);
            }
        }

        private static void DisposeAkitMessengerProxy(object proxy)
        {
            if (proxy == null)
                return;

            try
            {
                Type proxyType = proxy.GetType();
                PropertyInfo messengerClientProperty = proxyType.GetProperty(
                    "MessengerClient",
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance);
                object messengerClient = messengerClientProperty == null
                    ? null
                    : messengerClientProperty.GetValue(proxy, null);
                if (messengerClient == null)
                    return;

                PropertyInfo messengerProperty =
                    messengerClient.GetType().GetProperty(
                        "Messenger",
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance);
                object messenger = messengerProperty == null
                    ? null
                    : messengerProperty.GetValue(messengerClient, null);

                IDisposable disposableMessenger = messenger as IDisposable;
                if (disposableMessenger != null)
                    disposableMessenger.Dispose();
            }
            catch
            {
            }
        }

        private static bool TryOpenSelectedViewPropertiesDialog(
            IntPtr drawingPropertiesDialogHandle,
            int teklaProcessId,
            out IntPtr viewPropertiesDialogHandle,
            out string error)
        {
            viewPropertiesDialogHandle = IntPtr.Zero;
            error = "";

            IntPtr viewPropertiesButton =
                FindVisibleNativeChildButton(
                    drawingPropertiesDialogHandle,
                    "View properties");
            if (viewPropertiesButton == IntPtr.Zero)
            {
                error = "The visible View properties button was not found.";
                return false;
            }

            List<NativeWindowInfo> beforeWindows =
                GetVisibleNativeWindows(teklaProcessId);
            HashSet<long> beforeHandles = new HashSet<long>();
            for (int i = 0; i < beforeWindows.Count; i++)
                beforeHandles.Add(beforeWindows[i].Handle.ToInt64());

            if (!PostMessage(
                viewPropertiesButton,
                BmClick,
                IntPtr.Zero,
                IntPtr.Zero))
            {
                error = "Cannot click the View properties button.";
                return false;
            }

            for (int attempt = 0; attempt < 40; attempt++)
            {
                Thread.Sleep(100);
                List<NativeWindowInfo> currentWindows =
                    GetVisibleNativeWindows(teklaProcessId);
                int bestScore = int.MinValue;

                for (int windowIndex = 0;
                    windowIndex < currentWindows.Count;
                    windowIndex++)
                {
                    NativeWindowInfo window =
                        currentWindows[windowIndex];
                    long handleValue = window.Handle.ToInt64();
                    if (handleValue == 0 ||
                        beforeHandles.Contains(handleValue) ||
                        IsIgnoredNativeWindowClass(window.ClassName))
                    {
                        continue;
                    }

                    int score = ScoreNativeDialogWindow(window);
                    if (score <= bestScore)
                        continue;

                    bestScore = score;
                    viewPropertiesDialogHandle = window.Handle;
                }

                if (viewPropertiesDialogHandle != IntPtr.Zero)
                    return true;
            }

            error = "No new View properties window was detected.";
            return false;
        }

        private static IntPtr FindVisibleNativeChildButton(
            IntPtr parentWindow,
            string expectedText)
        {
            IntPtr result = IntPtr.Zero;
            if (parentWindow == IntPtr.Zero ||
                string.IsNullOrWhiteSpace(expectedText))
            {
                return result;
            }

            try
            {
                EnumWindowsCallback callback = delegate(
                    IntPtr childHandle,
                    IntPtr parameter)
                {
                    if (!IsWindowVisible(childHandle))
                        return true;

                    string className = ReadNativeWindowClass(childHandle);
                    if (!string.Equals(
                        className,
                        "Button",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    string text = ReadNativeWindowText(childHandle)
                        .Replace("&", "")
                        .Trim();
                    if (!string.Equals(
                        text,
                        expectedText,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    result = childHandle;
                    return false;
                };

                EnumChildWindows(
                    parentWindow,
                    callback,
                    IntPtr.Zero);
            }
            catch
            {
            }

            return result;
        }

        private static string FindViewAttributeFileFromDialogValues(
            List<string> values,
            List<string> viewAttributeFiles)
        {
            if (values == null || viewAttributeFiles == null)
                return "";

            for (int valueIndex = 0;
                valueIndex < values.Count;
                valueIndex++)
            {
                string dialogValue = NormalizeDialogViewAttributeName(
                    values[valueIndex]);
                if (string.IsNullOrWhiteSpace(dialogValue))
                    continue;

                for (int fileIndex = 0;
                    fileIndex < viewAttributeFiles.Count;
                    fileIndex++)
                {
                    string file = viewAttributeFiles[fileIndex];
                    if (string.IsNullOrWhiteSpace(file))
                        continue;

                    string baseName = Path.GetFileNameWithoutExtension(file);
                    if (!string.Equals(
                        dialogValue,
                        baseName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return file;
                }
            }

            return "";
        }

        private static string NormalizeDialogViewAttributeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string result = value.Trim().Trim('"').Trim('*').Trim();
            try
            {
                string extension = Path.GetExtension(result);
                if (string.Equals(extension, ".vi",
                    StringComparison.OrdinalIgnoreCase))
                {
                    result = Path.GetFileNameWithoutExtension(result);
                }
            }
            catch
            {
            }

            return result;
        }

        private static void CloseNativeWindowAndWait(
            IntPtr nativeWindowHandle,
            int timeoutMilliseconds)
        {
            try
            {
                if (nativeWindowHandle == IntPtr.Zero)
                    return;

                PostMessage(
                    nativeWindowHandle,
                    WmClose,
                    IntPtr.Zero,
                    IntPtr.Zero);

                int elapsedMilliseconds = 0;
                while (IsWindow(nativeWindowHandle) &&
                    elapsedMilliseconds < timeoutMilliseconds)
                {
                    Thread.Sleep(50);
                    elapsedMilliseconds += 50;
                }
            }
            catch
            {
            }
        }

        private static void AddUniqueLimited(
            List<string> values,
            string value,
            int maximumCount)
        {
            if (values == null ||
                values.Count >= maximumCount ||
                string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            AddUnique(values, value.Trim());
        }

        private static SectionViewAttributeResolution CompleteFromPropertyFile(
            SectionViewAttributeResolution result,
            DrawingPropertyData data,
            DrawingViewRow sectionRow,
            string source)
        {
            string attributeName = NormalizeViewAttributeName(
                sectionRow.ViewAttributeName);

            if (string.IsNullOrWhiteSpace(attributeName))
            {
                result.Error =
                    "Section views row exists but View properties is empty.";
                return result;
            }

            result.Success = true;
            result.AttributeName = attributeName;
            result.Source = source;
            result.DrawingPropertyFile = data.File;
            result.EnabledCode = sectionRow.EnabledCode;
            result.RawDrawingViews = data.RawDrawingViews;
            AddUnique(result.MatchingPropertyFiles, data.File);
            return result;
        }

        private static string NormalizeViewAttributeName(string value)
        {
            // Tekla represents an omitted property name as the standard View
            // property when the row is otherwise usable.
            return string.IsNullOrWhiteSpace(value)
                ? "standard"
                : value.Trim().Trim('"');
        }

        private static int ScoreCandidate(
            DrawingPropertyData data,
            List<string> activeViewAttributeNames,
            List<string> derivedPropertyNames)
        {
            int score = 0;
            string propertyBaseName = Path.GetFileNameWithoutExtension(
                data.File);

            for (int i = 0; i < derivedPropertyNames.Count; i++)
            {
                if (string.Equals(
                    propertyBaseName,
                    derivedPropertyNames[i],
                    StringComparison.OrdinalIgnoreCase))
                {
                    score += 1000;
                    break;
                }
            }

            for (int hintIndex = 0;
                hintIndex < activeViewAttributeNames.Count;
                hintIndex++)
            {
                bool found = false;
                for (int rowIndex = 0;
                    rowIndex < data.Rows.Count;
                    rowIndex++)
                {
                    if (string.Equals(
                        data.Rows[rowIndex].ViewAttributeName,
                        activeViewAttributeNames[hintIndex],
                        StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                    score += 100;
            }

            return score;
        }

        private static DrawingPropertyData ReadDrawingProperty(string file)
        {
            DrawingPropertyData data = new DrawingPropertyData();
            data.File = file ?? "";

            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                return data;

            try
            {
                string[] lines = File.ReadAllLines(file, Encoding.Default);
                for (int lineIndex = 0;
                    lineIndex < lines.Length;
                    lineIndex++)
                {
                    string line = lines[lineIndex];
                    if (line == null ||
                        !line.TrimStart().StartsWith(
                            DrawingViewsKey,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int firstQuote = line.IndexOf('"');
                    int lastQuote = line.LastIndexOf('"');
                    if (firstQuote < 0 || lastQuote <= firstQuote)
                        break;

                    data.RawDrawingViews = line.Substring(
                        firstQuote + 1,
                        lastQuote - firstQuote - 1);

                    string[] values = data.RawDrawingViews.Split(
                        new char[] { ',' },
                        StringSplitOptions.None);

                    for (int valueIndex = 0;
                        valueIndex + 3 < values.Length;
                        valueIndex += 4)
                    {
                        DrawingViewRow row = new DrawingViewRow();
                        row.TypeCode = values[valueIndex].Trim();
                        row.EnabledCode = values[valueIndex + 1].Trim();
                        row.ReservedValue = values[valueIndex + 2].Trim();
                        row.ViewAttributeName = values[valueIndex + 3]
                            .Trim()
                            .Trim('"');
                        data.Rows.Add(row);
                    }

                    break;
                }
            }
            catch
            {
            }

            return data;
        }

        private static DrawingViewRow FindSectionRow(
            DrawingPropertyData data)
        {
            if (data == null)
                return null;

            for (int i = 0; i < data.Rows.Count; i++)
            {
                if (string.Equals(
                    data.Rows[i].TypeCode,
                    SectionViewTypeCode,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return data.Rows[i];
                }
            }

            return null;
        }

        private static string ReadExistingSectionViewAttribute(
            Drawing drawing)
        {
            try
            {
                DrawingObjectEnumerator views =
                    drawing.GetSheet().GetAllViews();
                while (views.MoveNext())
                {
                    View view = views.Current as View;
                    if (view == null || !IsSectionView(view))
                        continue;

                    string value = ReadAttributeFilename(view.Attributes);
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }
            catch
            {
            }

            return "";
        }

        private static bool IsSectionView(View view)
        {
            if (view == null)
                return false;

            string typeName = view.ViewType.ToString();
            return string.Equals(
                    typeName,
                    "SectionView",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    typeName,
                    "Section",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void ReadActiveViewAttributeNames(
            Drawing drawing,
            List<string> result)
        {
            try
            {
                DrawingObjectEnumerator views =
                    drawing.GetSheet().GetAllViews();
                while (views.MoveNext())
                {
                    View view = views.Current as View;
                    if (view == null)
                        continue;

                    AddUnique(
                        result,
                        ReadAttributeFilename(view.Attributes));
                }
            }
            catch
            {
            }
        }

        private static string ReadAttributeFilename(object source)
        {
            if (source == null)
                return "";

            for (Type type = source.GetType();
                type != null;
                type = type.BaseType)
            {
                try
                {
                    PropertyInfo property = type.GetProperty(
                        "AttributeFilename",
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.DeclaredOnly);

                    if (property != null &&
                        property.GetIndexParameters().Length == 0)
                    {
                        object value = property.GetValue(source, null);
                        if (value != null &&
                            !string.IsNullOrWhiteSpace(value.ToString()))
                        {
                            return value.ToString().Trim();
                        }
                    }
                }
                catch
                {
                }

                try
                {
                    string[] fieldNames = new string[]
                    {
                        "AttributeFilename",
                        "_AttributeFilename"
                    };

                    for (int i = 0; i < fieldNames.Length; i++)
                    {
                        FieldInfo field = type.GetField(
                            fieldNames[i],
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.DeclaredOnly);

                        if (field == null)
                            continue;

                        object value = field.GetValue(source);
                        if (value != null &&
                            !string.IsNullOrWhiteSpace(value.ToString()))
                        {
                            return value.ToString().Trim();
                        }
                    }
                }
                catch
                {
                }
            }

            return "";
        }

        private static List<string> BuildDrawingPropertyNameCandidates(
            string viewAttributeName)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(viewAttributeName))
                return result;

            string name = Path.GetFileNameWithoutExtension(
                viewAttributeName.Trim().Trim('"'));
            if (name.StartsWith(
                "new_",
                StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(4);
            }

            AddUnique(result, name);

            int lastUnderscore = name.LastIndexOf('_');
            if (lastUnderscore > 0 &&
                lastUnderscore < name.Length - 1)
            {
                int suffixNumber;
                if (int.TryParse(
                    name.Substring(lastUnderscore + 1),
                    out suffixNumber))
                {
                    AddUnique(
                        result,
                        name.Substring(0, lastUnderscore));
                }
            }

            return result;
        }

        private static List<string> CollectAttributeFiles(
            string extension,
            string modelPath)
        {
            List<string> result = new List<string>();
            string suffix = extension.TrimStart('.');

            if (!string.IsNullOrWhiteSpace(modelPath))
            {
                try
                {
                    string directory = Path.Combine(
                        modelPath,
                        "attributes");
                    if (Directory.Exists(directory))
                    {
                        string[] files = Directory.GetFiles(
                            directory,
                            "*." + suffix,
                            SearchOption.TopDirectoryOnly);
                        for (int i = 0; i < files.Length; i++)
                            AddUnique(result, files[i]);
                    }
                }
                catch
                {
                }
            }

            try
            {
                List<string> names =
                    Tekla.Structures.Dialog.UIControls.EnvironmentFiles
                        .GetAttributeFiles(suffix);
                if (names != null)
                {
                    for (int i = 0; i < names.Count; i++)
                    {
                        string file = ResolveAttributeFile(
                            names[i],
                            extension,
                            modelPath);
                        AddUnique(result, file);
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private static string ResolveAttributeFile(
            string attributeName,
            string expectedExtension,
            string modelPath)
        {
            if (string.IsNullOrWhiteSpace(attributeName))
                return "";

            string trimmedName = attributeName.Trim().Trim('"');
            try
            {
                if (File.Exists(trimmedName))
                    return Path.GetFullPath(trimmedName);
            }
            catch
            {
            }

            string fileName = Path.GetFileName(trimmedName);
            if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
                fileName += expectedExtension;

            if (!string.IsNullOrWhiteSpace(modelPath))
            {
                try
                {
                    string file = Path.Combine(
                        Path.Combine(modelPath, "attributes"),
                        fileName);
                    if (File.Exists(file))
                        return file;
                }
                catch
                {
                }
            }

            try
            {
                FileInfo file =
                    Tekla.Structures.Dialog.UIControls.EnvironmentFiles
                        .GetAttributeFile(fileName);
                if (file != null && file.Exists)
                    return file.FullName;
            }
            catch
            {
            }

            return "";
        }

        private static string ReadModelPath(Model model)
        {
            try
            {
                if (model == null || !model.GetConnectionStatus())
                    return "";

                ModelInfo info = model.GetInfo();
                return info == null ? "" : (info.ModelPath ?? "");
            }
            catch
            {
                return "";
            }
        }

        private static void AddUnique(
            List<string> values,
            string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value))
                return;

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(
                    values[i],
                    value,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            values.Add(value);
        }
    }
}
