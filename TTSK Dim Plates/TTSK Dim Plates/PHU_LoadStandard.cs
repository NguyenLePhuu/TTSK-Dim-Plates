#pragma warning disable 1633

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Tekla.Structures.Drawing;
using Tekla.Structures.Drawing.UI;
using Tekla.Structures.Model;

namespace Tekla.Technology.Akit.UserScript
{
    // Loads the Geometry Standard directly through Tekla's remote Akit adapter.
    // No macro file is started and no file is created in the Tekla model/project.
    public class PHU_LoadStandardService
    {
        public static bool LastRunSucceeded { get; private set; }
        public static string LastRunMessage { get; private set; }

        private const string SinglePartGeometryStandard =
            "()_Geo_Standard_Part";
        private const string AssemblyGeometryStandard =
            "()_Geo_Standard";
        private const string ViewDialogId = "view_dial";
        private const string ClassifierDialogId = "vclassifier_dial";
        private const int DialogReadyTimeoutMilliseconds = 5000;
        private const int DialogCloseTimeoutMilliseconds = 3000;
        private const int PollMilliseconds = 50;

        private static readonly object GeometryStandardLoadSyncRoot =
            new object();
        private static bool GeometryStandardLoadIsRunning;

        private delegate bool EnumWindowsCallback(
            IntPtr windowHandle,
            IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(
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

        [DllImport(
            "user32.dll",
            CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(
            IntPtr windowHandle,
            StringBuilder text,
            int maximumCount);

        [DllImport(
            "user32.dll",
            CharSet = CharSet.Unicode)]
        private static extern int GetClassName(
            IntPtr windowHandle,
            StringBuilder className,
            int maximumCount);

        private sealed class NativeWindowInfo
        {
            public IntPtr Handle;
            public string Text = "";
            public string ClassName = "";
            public bool Visible;
        }

        public static void Run()
        {
            lock (GeometryStandardLoadSyncRoot)
            {
                if (GeometryStandardLoadIsRunning)
                {
                    LastRunSucceeded = false;
                    LastRunMessage =
                        "LoadStandard: mot lan load Geometry Standard khac dang chay.";
                    return;
                }

                GeometryStandardLoadIsRunning = true;
            }

            try
            {
                RunLocked();
            }
            finally
            {
                lock (GeometryStandardLoadSyncRoot)
                {
                    GeometryStandardLoadIsRunning = false;
                }
            }
        }

        private static void RunLocked()
        {
            LastRunSucceeded = false;
            LastRunMessage = string.Empty;

            try
            {
                DrawingHandler drawingHandler = new DrawingHandler();
                Drawing drawing = drawingHandler.GetActiveDrawing();

                if (drawing == null)
                {
                    LastRunMessage =
                        "LoadStandard: khong co active drawing.";
                    return;
                }

                bool isSinglePartDrawing = drawing is SinglePartDrawing;
                bool isAssemblyDrawing = drawing is AssemblyDrawing;
                if (!isSinglePartDrawing && !isAssemblyDrawing)
                {
                    LastRunMessage =
                        "LoadStandard: drawing hien tai khong phai SinglePartDrawing hoac AssemblyDrawing.";
                    return;
                }

                Model model = new Model();
                if (!model.GetConnectionStatus())
                {
                    LastRunMessage =
                        "LoadStandard: khong ket noi duoc model.";
                    return;
                }

                string selectionError;
                int selectedViewCount = SelectGeometryViews(
                    drawingHandler,
                    drawing,
                    out selectionError);
                if (selectedViewCount == 0)
                {
                    LastRunMessage = !string.IsNullOrWhiteSpace(selectionError)
                        ? "LoadStandard: " + selectionError
                        : "LoadStandard: khong tim thay Geometry view de select.";
                    return;
                }

                string standardName = isSinglePartDrawing
                    ? SinglePartGeometryStandard
                    : AssemblyGeometryStandard;
                string loadError;
                if (!LoadGeometryStandardDirectly(
                        isSinglePartDrawing,
                        standardName,
                        out loadError))
                {
                    LastRunMessage =
                        "LoadStandard: khong load duoc " + standardName +
                        ". " + loadError;
                    return;
                }

                LastRunSucceeded = true;
                LastRunMessage =
                    "LoadStandard: da load " + standardName +
                    " cho " + selectedViewCount +
                    " Geometry view bang Akit truc tiep.";
            }
            catch (Exception ex)
            {
                Exception real = UnwrapException(ex);
                LastRunMessage =
                    "LoadStandard " + real.GetType().Name +
                    ": " + real.Message;
            }
        }

        private static int SelectGeometryViews(
            DrawingHandler drawingHandler,
            Drawing drawing,
            out string error)
        {
            error = string.Empty;
            ContainerView sheet = drawing.GetSheet();
            if (sheet == null)
                return 0;

            ArrayList viewsToSelect = new ArrayList();
            DrawingObjectEnumerator views = sheet.GetAllViews();

            while (views.MoveNext())
            {
                View view = views.Current as View;
                if (view != null && !IsSectionView(view))
                    viewsToSelect.Add(view);
            }

            if (viewsToSelect.Count == 0)
                return 0;

            DrawingObjectSelector selector =
                drawingHandler.GetDrawingObjectSelector();

            if (!selector.SelectObjects(viewsToSelect, false))
            {
                error = "Tekla tu choi select cac Geometry view.";
                return 0;
            }

            const int selectionTimeoutMilliseconds = 1000;
            int elapsedMilliseconds = 0;

            while (elapsedMilliseconds <= selectionTimeoutMilliseconds)
            {
                if (CountSelectedGeometryViews(selector) == viewsToSelect.Count)
                    return viewsToSelect.Count;

                PumpMessagesAndWait();
                elapsedMilliseconds += PollMilliseconds;
            }

            error =
                "Tekla chua xac nhan du so Geometry view da select (" +
                viewsToSelect.Count + ").";
            return 0;
        }

        private static int CountSelectedGeometryViews(
            DrawingObjectSelector selector)
        {
            if (selector == null)
                return 0;

            int count = 0;
            DrawingObjectEnumerator selectedObjects = selector.GetSelected();
            while (selectedObjects.MoveNext())
            {
                View selectedView = selectedObjects.Current as View;
                if (selectedView != null && !IsSectionView(selectedView))
                    count++;
            }

            return count;
        }

        private static bool IsSectionView(View view)
        {
            try
            {
                if (view == null)
                    return false;

                string viewType = view.ViewType.ToString();
                return string.Equals(
                        viewType,
                        "SectionView",
                        StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(viewType) &&
                     viewType.IndexOf(
                         "Section",
                         StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch
            {
                return false;
            }
        }

        private static bool LoadGeometryStandardDirectly(
            bool isSinglePartDrawing,
            string standardName,
            out string error)
        {
            error = string.Empty;
            object proxy = null;
            object script = null;
            MethodInfo pushButton = null;
            IntPtr viewPropertiesHandle = IntPtr.Zero;
            IntPtr classifierHandle = IntPtr.Zero;

            try
            {
                Process teklaProcess = FindTeklaProcess();
                if (teklaProcess == null)
                {
                    error = "Khong tim thay process TeklaStructures dang hoat dong.";
                    return false;
                }

                string teklaBin = GetTeklaBinDirectory(teklaProcess);
                string macroAkitFile = Path.Combine(
                    teklaBin,
                    "Tekla.Macros.Akit.dll");
                string akitFile = Path.Combine(teklaBin, "Akit5.dll");

                if (!File.Exists(macroAkitFile) || !File.Exists(akitFile))
                {
                    error =
                        "Khong tim thay Tekla.Macros.Akit.dll hoac Akit5.dll trong " +
                        teklaBin + ".";
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
                MethodInfo getRemoteScript = proxyType.GetMethod(
                    "GetRemoteScriptAdapter",
                    BindingFlags.Public | BindingFlags.Instance);
                MethodInfo callback = scriptType.GetMethod(
                    "Callback",
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
                pushButton = scriptType.GetMethod(
                    "PushButton",
                    new Type[]
                    {
                        typeof(string),
                        typeof(string)
                    });
                MethodInfo valueChange = scriptType.GetMethod(
                    "ValueChange",
                    new Type[]
                    {
                        typeof(string),
                        typeof(string),
                        typeof(string)
                    });

                if (create == null || getRemoteScript == null ||
                    callback == null || treeSelect == null ||
                    pushButton == null || valueChange == null)
                {
                    error = "Tekla Akit thieu method bat buoc.";
                    return false;
                }

                proxy = InvokeMethod(create, null, new object[] { null });
                if (proxy == null)
                {
                    error = "Tekla Akit messenger proxy is null.";
                    return false;
                }

                script = InvokeMethod(
                    getRemoteScript,
                    proxy,
                    new object[] { teklaProcess.Id });
                if (script == null)
                {
                    error = "Tekla remote Akit script adapter is null.";
                    return false;
                }

                // Tekla keeps view_dial as a valid hidden cached HWND after OK.
                // Re-calling Callback on that cached dialog can toggle it and was
                // the cause of the previous first-run/manual-open conflict.
                viewPropertiesHandle = FindViewPropertiesDialog(
                    teklaProcess.Id);
                if (viewPropertiesHandle == IntPtr.Zero)
                {
                    InvokeMethod(
                        callback,
                        script,
                        new object[]
                        {
                            "acmd_display_attr_dialog",
                            ViewDialogId,
                            "main_frame"
                        });

                    if (!WaitForViewPropertiesDialog(
                            teklaProcess.Id,
                            out viewPropertiesHandle))
                    {
                        error =
                            "Buoc khoi tao view_dial khong tao duoc View properties.";
                        return false;
                    }
                }

                if (isSinglePartDrawing)
                {
                    InvokeMethod(
                        treeSelect,
                        script,
                        new object[]
                        {
                            ViewDialogId,
                            "gratCastUnitDrawingAttributesMenuTree",
                            "Attributes"
                        });
                }

                InvokeMethod(
                    pushButton,
                    script,
                    new object[] { "btnEditSettings", ViewDialogId });

                if (!WaitForClassifierDialog(
                        teklaProcess.Id,
                        out classifierHandle))
                {
                    error =
                        "btnEditSettings khong mo duoc Object level settings for view.";
                    return false;
                }

                InvokeMethod(
                    valueChange,
                    script,
                    new object[]
                    {
                        ClassifierDialogId,
                        "mnuLoad",
                        standardName
                    });
                InvokeMethod(
                    pushButton,
                    script,
                    new object[] { "btnLoad", ClassifierDialogId });
                InvokeMethod(
                    pushButton,
                    script,
                    new object[] { "btnModify", ClassifierDialogId });
                InvokeMethod(
                    pushButton,
                    script,
                    new object[] { "btnApply", ClassifierDialogId });
                InvokeMethod(
                    pushButton,
                    script,
                    new object[] { "btnOk", ClassifierDialogId });

                if (!WaitForWindowHiddenOrClosed(
                        classifierHandle,
                        DialogCloseTimeoutMilliseconds))
                {
                    error =
                        "Object level settings van con visible sau btnOk.";
                    return false;
                }

                InvokeMethod(
                    pushButton,
                    script,
                    new object[] { "view_modify", ViewDialogId });
                InvokeMethod(
                    pushButton,
                    script,
                    new object[] { "view_apply", ViewDialogId });
                InvokeMethod(
                    pushButton,
                    script,
                    new object[] { "view_ok", ViewDialogId });

                if (!WaitForWindowHiddenOrClosed(
                        viewPropertiesHandle,
                        DialogCloseTimeoutMilliseconds))
                {
                    error = "View properties van con visible sau view_ok.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Exception real = UnwrapException(ex);
                error = real.GetType().Name + ": " + real.Message;
                return false;
            }
            finally
            {
                // Only close dialogs that this transaction left visible.
                // Never WM_CLOSE/destroy Tekla's hidden cached view_dial.
                TryCloseVisibleAkitDialog(
                    pushButton,
                    script,
                    classifierHandle,
                    "btnOk",
                    ClassifierDialogId);
                TryCloseVisibleAkitDialog(
                    pushButton,
                    script,
                    viewPropertiesHandle,
                    "view_ok",
                    ViewDialogId);
                DisposeAkitMessengerProxy(proxy);
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

        private static string GetTeklaBinDirectory(Process teklaProcess)
        {
            try
            {
                if (teklaProcess != null && teklaProcess.MainModule != null)
                {
                    string directory = Path.GetDirectoryName(
                        teklaProcess.MainModule.FileName);
                    if (!string.IsNullOrWhiteSpace(directory))
                        return directory;
                }
            }
            catch
            {
            }

            return Path.GetDirectoryName(
                typeof(DrawingHandler).Assembly.Location);
        }

        private static bool WaitForViewPropertiesDialog(
            int teklaProcessId,
            out IntPtr handle)
        {
            handle = IntPtr.Zero;
            int elapsedMilliseconds = 0;

            while (elapsedMilliseconds <= DialogReadyTimeoutMilliseconds)
            {
                handle = FindViewPropertiesDialog(teklaProcessId);
                if (handle != IntPtr.Zero)
                    return true;

                PumpMessagesAndWait();
                elapsedMilliseconds += PollMilliseconds;
            }

            return false;
        }

        private static bool WaitForClassifierDialog(
            int teklaProcessId,
            out IntPtr handle)
        {
            handle = IntPtr.Zero;
            int elapsedMilliseconds = 0;

            while (elapsedMilliseconds <= DialogReadyTimeoutMilliseconds)
            {
                handle = FindClassifierDialog(teklaProcessId);
                if (handle != IntPtr.Zero && IsWindowVisible(handle))
                    return true;

                PumpMessagesAndWait();
                elapsedMilliseconds += PollMilliseconds;
            }

            return false;
        }

        private static bool WaitForWindowHiddenOrClosed(
            IntPtr handle,
            int timeoutMilliseconds)
        {
            if (handle == IntPtr.Zero)
                return true;

            int elapsedMilliseconds = 0;
            while (elapsedMilliseconds <= timeoutMilliseconds)
            {
                if (!IsWindow(handle) || !IsWindowVisible(handle))
                    return true;

                PumpMessagesAndWait();
                elapsedMilliseconds += PollMilliseconds;
            }

            return !IsWindow(handle) || !IsWindowVisible(handle);
        }

        private static IntPtr FindViewPropertiesDialog(int processId)
        {
            List<NativeWindowInfo> windows = GetNativeWindows(processId);
            for (int i = 0; i < windows.Count; i++)
            {
                NativeWindowInfo window = windows[i];
                if (IsViewPropertiesTitle(window.Text) &&
                    IsTeklaDialogClass(window.ClassName))
                {
                    return window.Handle;
                }
            }

            return IntPtr.Zero;
        }

        private static IntPtr FindClassifierDialog(int processId)
        {
            List<NativeWindowInfo> windows = GetNativeWindows(processId);
            for (int i = 0; i < windows.Count; i++)
            {
                NativeWindowInfo window = windows[i];
                if (window.Visible &&
                    IsClassifierDialogTitle(window.Text) &&
                    IsTeklaDialogClass(window.ClassName))
                {
                    return window.Handle;
                }
            }

            return IntPtr.Zero;
        }

        private static List<NativeWindowInfo> GetNativeWindows(int processId)
        {
            List<NativeWindowInfo> windows =
                new List<NativeWindowInfo>();

            try
            {
                EnumWindowsCallback callback = delegate (
                    IntPtr windowHandle,
                    IntPtr parameter)
                {
                    uint ownerProcessId;
                    GetWindowThreadProcessId(
                        windowHandle,
                        out ownerProcessId);
                    if (ownerProcessId != (uint)processId)
                        return true;

                    NativeWindowInfo info = new NativeWindowInfo();
                    info.Handle = windowHandle;
                    info.Text = ReadWindowText(windowHandle);
                    info.ClassName = ReadWindowClass(windowHandle);
                    info.Visible = IsWindowVisible(windowHandle);
                    windows.Add(info);
                    return true;
                };

                EnumWindows(callback, IntPtr.Zero);
            }
            catch
            {
            }

            return windows;
        }

        private static bool IsViewPropertiesTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;

            return title.IndexOf(
                    "View properties",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsClassifierDialogTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;

            return title.IndexOf(
                    "Object level settings",
                    StringComparison.OrdinalIgnoreCase) >= 0 &&
                title.IndexOf(
                    "view",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsTeklaDialogClass(string className)
        {
            if (string.IsNullOrWhiteSpace(className))
                return false;

            return className.StartsWith(
                    "Afx:",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    className,
                    "#32770",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadWindowText(IntPtr windowHandle)
        {
            try
            {
                StringBuilder text = new StringBuilder(512);
                GetWindowText(windowHandle, text, text.Capacity);
                return text.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ReadWindowClass(IntPtr windowHandle)
        {
            try
            {
                StringBuilder className = new StringBuilder(256);
                GetClassName(
                    windowHandle,
                    className,
                    className.Capacity);
                return className.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static object InvokeMethod(
            MethodInfo method,
            object target,
            object[] arguments)
        {
            if (method == null)
                throw new InvalidOperationException(
                    "Required Tekla Akit method is null.");

            try
            {
                return method.Invoke(target, arguments);
            }
            catch (TargetInvocationException ex)
            {
                throw UnwrapException(ex);
            }
        }

        private static Exception UnwrapException(Exception exception)
        {
            Exception current = exception;
            while (current is TargetInvocationException &&
                   current.InnerException != null)
            {
                current = current.InnerException;
            }

            return current ?? exception;
        }

        private static void TryCloseVisibleAkitDialog(
            MethodInfo pushButton,
            object script,
            IntPtr handle,
            string buttonId,
            string dialogId)
        {
            if (pushButton == null || script == null ||
                handle == IntPtr.Zero || !IsWindow(handle) ||
                !IsWindowVisible(handle))
            {
                return;
            }

            try
            {
                InvokeMethod(
                    pushButton,
                    script,
                    new object[] { buttonId, dialogId });
                WaitForWindowHiddenOrClosed(handle, 1000);
            }
            catch
            {
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

        private static void PumpMessagesAndWait()
        {
            try
            {
                System.Windows.Forms.Application.DoEvents();
            }
            catch
            {
            }

            Thread.Sleep(PollMilliseconds);
        }
    }
}
