using System;
using System.Reflection;
using Tekla.Structures.Model;
using Tekla.Structures.Drawing;
using System.Windows.Forms;
using System.Linq;

// Read-only live probe. No mutation API, macro, selection change or model-folder file access.
internal static class Program
{
    static object startupSnapshot;
    static object GetStartupSnapshot() { return startupSnapshot; }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            // Probe báo lỗi qua exit code/stderr, không mở hộp exception WinForms làm gián đoạn người dùng.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            AppDomain.CurrentDomain.AssemblyResolve += (sender, e) => {
                string path = System.IO.Path.Combine(Environment.GetEnvironmentVariable("TeklaBinPath"), new AssemblyName(e.Name).Name + ".dll");
                return System.IO.File.Exists(path) ? System.Reflection.Assembly.LoadFrom(path) : null;
            };
            var assembly = System.Reflection.Assembly.LoadFrom(args[0]);
            if (args.Length > 1 && args[1] == "theme-logo")
            {
                SetProcessDPIAware();
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (var form = (Form)Activator.CreateInstance(assembly.GetType("TTSK_AutoDim_Plates.MainForm")))
                {
                    var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                    form.Show(); Application.DoEvents();
                    var handle = form.Handle;
                    var selector = form.GetType().GetField("_darkMode", flags);
                    var apply = form.GetType().GetMethod("ApplyTheme", flags);
                    double light = -1, darkValue = -1;
                    foreach (bool dark in new[] { false, true, false })
                    {
                        selector.SetValue(form, dark); apply.Invoke(form, null);
                        form.Refresh(); Application.DoEvents();
                        var picture = (PictureBox)form.GetType().GetField("mainBrandLogo", flags).GetValue(form);
                        using (var expected = new System.Drawing.Bitmap(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(args[0]), "Resources", dark ? "logo_dark.png" : "logo.png")))
                        {
                            var actual = (System.Drawing.Bitmap)picture.Image;
                            for (int y = 950; y < actual.Height; y += 7) for (int x = 0; x < actual.Width; x += 7)
                            {
                                var a = actual.GetPixel(x,y); var b = expected.GetPixel(x,y);
                                if (Math.Abs(a.A-b.A)>2 || (a.A>240 && (Math.Abs(a.R-b.R)>2 || Math.Abs(a.G-b.G)>2 || Math.Abs(a.B-b.B)>2)))
                                    throw new Exception("Header logo not following theme " + dark + " at " + x + "," + y + ": " + a + " vs " + b);
                            }
                        }
                        foreach (int kind in new[] { 0, 1 })
                        {
                            IntPtr iconHandle = SendMessage(form.Handle, 0x7F, new IntPtr(kind), IntPtr.Zero);
                            if (iconHandle == IntPtr.Zero) throw new Exception("Native title/taskbar icon missing");
                            using (var borrowed = System.Drawing.Icon.FromHandle(iconHandle))
                            using (var icon = borrowed.ToBitmap())
                            {
                                double sum = 0; int count = 0;
                                for (int y = (int)(icon.Height * .80); y < icon.Height; y++) for (int x = 0; x < icon.Width; x++)
                                { var pixel = icon.GetPixel(x,y); if (pixel.A > 10) { sum += (pixel.R + pixel.G + pixel.B) / 3.0; count++; } }
                                double brightness = sum / Math.Max(1,count);
                                if (kind == 1) { if (dark) darkValue = brightness; else light = brightness; }
                                icon.Save(System.IO.Path.Combine(args[2], "icon-" + (dark ? "dark" : "light") + "-" + kind + ".png"));
                            }
                        }
                        Console.WriteLine("PASS: header + WM_GETICON small/big follow " + (dark ? "dark" : "light"));
                        using (var preview = new System.Drawing.Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(preview, new System.Drawing.Rectangle(0,0,form.Width,form.Height));
                            preview.Save(System.IO.Path.Combine(args[2], "main-logo-" + (dark ? "dark" : "light") + ".png"));
                        }
                    }
                    if (darkValue < light + 50) throw new Exception("Taskbar text did not become light: " + light + " / " + darkValue);
                    Console.WriteLine("PASS: native large-icon lettering luminance light=" + light + " dark=" + darkValue);
                    form.Close();
                }
                return 0;
            }
            if (args.Length > 1 && args[1] == "logo-render")
            {
                var render = assembly.GetType("TTSK_AutoDim_Plates.CompanyLogoRenderer").GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Static);
                string sourcePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(args[0]), "Resources", "logo.png");
                using (var source = new System.Drawing.Bitmap(sourcePath))
                {
                    using (var identity = (System.Drawing.Bitmap)render.Invoke(null, new object[] { source, source.Width }))
                        for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++)
                        {
                            var a = source.GetPixel(x, y); var b = identity.GetPixel(x, y);
                            if (a.A != b.A || (a.A == 255 && a.ToArgb() != b.ToArgb())) throw new Exception("Logo identity/color changed at " + x + "," + y + " " + a + " -> " + b);
                        }
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    using (var scaled = (System.Drawing.Bitmap)render.Invoke(null, new object[] { source, 375 }))
                    using (var comparison = new System.Drawing.Bitmap(790, scaled.Height + 42))
                    using (var g = System.Drawing.Graphics.FromImage(comparison))
                    {
                        long ms = watch.ElapsedMilliseconds;
                        if (scaled.GetPixel(0, 0).A != 0 || scaled.Height != (int)Math.Round(375.0 * source.Height / source.Width)) throw new Exception("Alpha/aspect changed");
                        g.Clear(System.Drawing.Color.FromArgb(245,247,251));
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(source, new System.Drawing.Rectangle(10,32,375,scaled.Height));
                        g.DrawImageUnscaled(scaled, 405,32);
                        g.DrawString("Bicubic (before)", System.Drawing.SystemFonts.DefaultFont, System.Drawing.Brushes.Black,10,8);
                        g.DrawString("Cached pixel renderer", System.Drawing.SystemFonts.DefaultFont, System.Drawing.Brushes.Black,405,8);
                        comparison.Save(System.IO.Path.Combine(args[2], "logo-render-comparison.png"));
                        Console.WriteLine("PASS: source identity, opaque colors, alpha, aspect; 375 px render_ms=" + ms);
                    }
                }
                using (var flat = new System.Drawing.Bitmap(7,7))
                {
                    using (var g=System.Drawing.Graphics.FromImage(flat)) g.Clear(System.Drawing.Color.FromArgb(255,246,111,26));
                    using (var enlarged=(System.Drawing.Bitmap)render.Invoke(null,new object[]{flat,43}))
                        for(int y=0;y<enlarged.Height;y++) for(int x=0;x<enlarged.Width;x++)
                            if(enlarged.GetPixel(x,y).ToArgb()!=flat.GetPixel(0,0).ToArgb())throw new Exception("Flat logo color shifted");
                }
                Console.WriteLine("PASS: constant brand color unchanged through resampling");
                return 0;
            }
            if (args.Length > 1 && args[1].StartsWith("wait-", StringComparison.Ordinal))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var coordinatorType = assembly.GetType("TTSK_AutoDim_Plates.TeklaSessionCoordinator");
                var flags = BindingFlags.NonPublic | BindingFlags.Static;
                object real = coordinatorType.GetMethod("Discover", flags).Invoke(null, null);
                var realList = (System.Collections.IList)real;
                if (args[1] != "wait-cancel" && realList.Count == 0) throw new Exception("Live Tekla session required for this handoff test");
                var listType = real.GetType();
                startupSnapshot = Activator.CreateInstance(listType);
                if (args[1] == "wait-ready") ((System.Collections.IList)startupSnapshot).Add(realList[0]);
                var funcType = typeof(Func<>).MakeGenericType(listType);
                var body = System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Call(typeof(Program).GetMethod("GetStartupSnapshot", flags)), listType);
                var source = System.Linq.Expressions.Expression.Lambda(funcType, body).Compile();
                var splashType = assembly.GetType("TTSK_AutoDim_Plates.TeklaStartupForm");
                using (var form = (Form)Activator.CreateInstance(splashType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { source }, null))
                using (var driver = new Timer { Interval = 100 })
                {
                    form.TopMost = true;
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    int step = 0;
                    driver.Tick += delegate
                    {
                        string status = (string)splashType.GetProperty("StatusText", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form, null);
                        if (watch.ElapsedMilliseconds > 20000) { Console.Error.WriteLine("Startup wait test timeout: " + status); Environment.Exit(1); }
                        if (step == 0 && args[1] == "wait-ready" && status == "Đang kết nối dự án…")
                        {
                            step = 2;
                            if (form.Opacity != 0 || (bool)splashType.GetProperty("WaitingPresentation", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form, null))
                                throw new Exception("Ready Tekla displayed waiting form");
                            var logo = splashType.GetField("logoWindow", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                            var pixels = (System.Drawing.Bitmap)logo.GetType().GetField("surface", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(logo);
                            if (pixels.GetPixel(0, 0).A != 0) throw new Exception("Logo-only has opaque background");
                            var point = (System.Drawing.Point)logo.GetType().GetField("position", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(logo);
                            DwmFlush();
                            using (var image = new System.Drawing.Bitmap(pixels.Width, pixels.Height))
                            {
                                using (var g = System.Drawing.Graphics.FromImage(image)) g.CopyFromScreen(point, System.Drawing.Point.Empty, pixels.Size);
                                image.Save(System.IO.Path.Combine(args[2], "startup-logo-only.png"));
                            }
                            Console.WriteLine("PASS: ready Tekla -> exact logo-only window, transparent corners; waiting form invisible");
                        }
                        if (step == 0 && status == "Đang chờ Tekla Structures…")
                        {
                            step = -1; // Không tái nhập khi chụp sau animation qua DoEvents.
                            Console.WriteLine("PASS: no Tekla -> waiting, no error dialog; first_paint_ms=" + splashType.GetProperty("FirstPaintMilliseconds", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form, null));
                            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Tekla.Structures.Model" || a.GetName().Name == "Tekla.Structures.Drawing"))
                                throw new Exception("Waiting host loaded Tekla API");
                            if (form.Opacity != 1 || !(bool)splashType.GetProperty("WaitingPresentation", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form, null))
                                throw new Exception("No Tekla did not reveal wait form");
                            form.Refresh();
                            System.Threading.Thread.Sleep(300); Application.DoEvents(); DwmFlush();
                            using (var image = new System.Drawing.Bitmap(form.Width, form.Height))
                            {
                                using (var graphics = System.Drawing.Graphics.FromImage(image)) graphics.CopyFromScreen(form.PointToScreen(System.Drawing.Point.Empty), System.Drawing.Point.Empty, form.Size);
                                image.Save(System.IO.Path.Combine(args[2], form.BackColor.GetBrightness() < .4F ? "startup-dark.png" : "startup-light.png"));
                            }
                            if (args[1] == "wait-cancel") { form.Close(); step = 3; return; }
                            var loading = Activator.CreateInstance(realList[0].GetType(), true);
                            loading.GetType().GetField("Title", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(loading, "Tekla Structures");
                            var pending = (System.Collections.IList)Activator.CreateInstance(listType); pending.Add(loading);
                            startupSnapshot = pending; step = 1;
                        }
                        else if (step == 1 && status == "Đang chờ Tekla mở model…")
                        {
                            Console.WriteLine("PASS: Tekla without model -> waiting; no proxy initialized");
                            var ready = (System.Collections.IList)Activator.CreateInstance(listType);
                            object target = realList[0];
                            if (args[1] == "wait-retry")
                            {
                                target = target.GetType().GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);
                                var titleField = target.GetType().GetField("Title", BindingFlags.NonPublic | BindingFlags.Instance);
                                var folder = target.GetType().GetProperty("ModelFolder", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target, null).ToString();
                                titleField.SetValue(target, titleField.GetValue(target).ToString().Replace(folder, folder + "_STALE"));
                            }
                            ready.Add(target);
                            startupSnapshot = ready; step = 2;
                        }
                        else if (step == 2 && args[1] == "wait-retry" && status == "Tekla chưa sẵn sàng. Đang chờ…")
                        {
                            if (!form.Visible || !form.Enabled) throw new Exception("Retry lost waiting UI");
                            Console.WriteLine("PASS: rejected stale model in child; host stays responsive and retries fresh process");
                            var ready = (System.Collections.IList)Activator.CreateInstance(listType); ready.Add(realList[0]); startupSnapshot = ready; step = 4;
                        }
                        else if (step == 2 && args[1] == "wait-abort" && status == "Đang kết nối dự án…")
                        {
                            form.Close(); step = 3;
                            Console.WriteLine("PASS: canceled while connection child was starting");
                        }
                    };
                    form.Shown += delegate { driver.Start(); };
                    Application.Run(form);
                    if (step != 2 && step != 3 && step != 4) throw new Exception("Unexpected startup exit");
                    if ((int)splashType.GetProperty("ExitCode", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form, null) != 0) throw new Exception("Startup returned error");
                    Console.WriteLine("PASS: " + (step == 3 ? "cancel waiting exits cleanly" : "ready model verified -> main handoff") + "; elapsed_ms=" + watch.ElapsedMilliseconds);
                }
                return 0;
            }
            if (args.Length > 1 && args[1] == "preview")
            {
                bool native = args.Length > 3 && args[3] == "native";
                if (native) SetProcessDPIAware(); // Kiểm cửa sổ thật ở DPI màn hình, không chỉ DrawToBitmap.
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var targetType = assembly.GetType("TTSK_AutoDim_Plates.TeklaSessionTarget");
                var list = (System.Collections.IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(targetType));
                foreach (var name in new[] { "27-000)HULIC_B", "27-027)Sinjuku_Slope", "27-028)Tokyo_Central_Long_Project_Name" })
                {
                    var target = Activator.CreateInstance(targetType, true);
                    targetType.GetField("Title", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, "Tekla Structures - Educational version - D:\\" + name + "  - View 1 - HS-3D");
                    targetType.GetField("Id", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, list.Count + 100);
                    list.Add(target);
                }
                var pickerType = assembly.GetType("TTSK_AutoDim_Plates.TeklaProjectPickerForm");
                foreach (bool dark in new[] { false, true })
                using (var form = (Form)Activator.CreateInstance(pickerType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { list, dark, list[0], 101 }, null))
                using (var backdrop = new Form { BackColor = System.Drawing.Color.FromArgb(210, 223, 232), FormBorderStyle = FormBorderStyle.None,
                    ShowInTaskbar = false, TopMost = true, StartPosition = FormStartPosition.Manual, Bounds = Screen.PrimaryScreen.WorkingArea })
                {
                    if (native) backdrop.Show();
                    form.Show(); Application.DoEvents();
                    form.Location = new System.Drawing.Point(backdrop.Left + (backdrop.Width - form.Width) / 2,
                        backdrop.Top + (backdrop.Height - form.Height) / 2);
                    form.Refresh(); Application.DoEvents();
                    if (native) { System.Threading.Thread.Sleep(300); Application.DoEvents(); DwmFlush(); } // Chờ animation mở cửa sổ kết thúc trước capture.
                    if (Descendants(form).Any(c => c is CheckBox || c is ListBox)) throw new Exception("Legacy selection controls remain");
                    var cards = Descendants(form).OfType<Button>().Where(b => b.Tag != null).ToArray();
                    if (((TableLayoutPanel)cards[0].Parent).RowStyles.Cast<RowStyle>().Any(r => Math.Abs(r.Height - 110 * form.DeviceDpi / 96F) > .1F))
                        throw new Exception("Card rows accumulated DPI scale");
                    if (cards.Length != 3 || cards.Any(b => b.Height < 95 || b.Width < 550)) throw new Exception("Card layout is too small");
                    using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
                    {
                        if (native)
                        {
                            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                                graphics.CopyFromScreen(form.PointToScreen(System.Drawing.Point.Empty), System.Drawing.Point.Empty, form.Size);
                        }
                        else
                        {
                            // WM_PRINT của Form không chụp được frame DWM và có thể vẽ lại caption hệ thống.
                            // Bitmap chỉ kiểm nội dung client; mép ngoài luôn kiểm bằng native capture.
                            using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.Clear(form.BackColor);
                            foreach (Control content in form.Controls) content.DrawToBitmap(bitmap, content.Bounds);
                        }
                        bitmap.Save(System.IO.Path.Combine(args[2], "picker-" + (dark ? "dark" : "light") + (native ? "-native" : "") + ".png"));
                        foreach (var button in Descendants(form).OfType<Button>())
                        {
                            foreach (var corner in new[] { new System.Drawing.Point(1, 1), new System.Drawing.Point(button.Width - 2, 1),
                                new System.Drawing.Point(1, button.Height - 2), new System.Drawing.Point(button.Width - 2, button.Height - 2) })
                            {
                                var point = form.PointToClient(button.PointToScreen(corner));
                                if (point.X < 0 || point.X >= bitmap.Width || point.Y < 0 || point.Y >= bitmap.Height)
                                    throw new Exception("Button outside form: " + button.Text + " point=" + point);
                                var actual = bitmap.GetPixel(point.X, point.Y);
                                if (Math.Abs(actual.R - form.BackColor.R) > 2 || Math.Abs(actual.G - form.BackColor.G) > 2 || Math.Abs(actual.B - form.BackColor.B) > 2)
                                    throw new Exception("Black/wrong button corner: " + button.Text + " " + actual);
                            }
                        }
                        if (form.Region != null || Descendants(form).Any(c => c.Region != null))
                            throw new Exception("Binary window region still clips corners");
                        if (native)
                        {
                            int cornerPreference;
                            if (DwmGetWindowAttribute(form.Handle, 33, out cornerPreference, sizeof(int)) != 0 || cornerPreference != 2)
                                throw new Exception("Native rounded-corner preference missing");
                            if (bitmap.GetPixel(0, 0).ToArgb() == form.BackColor.ToArgb())
                                throw new Exception("Native form corner is square");
                            VerifySmoothCorners(bitmap, form.BackColor);
                        }
                        string file = "picker-" + (dark ? "dark" : "light") + (native ? "-native" : "") + ".png";
                        bitmap.Save(System.IO.Path.Combine(args[2], file));
                    }
                    cards[1].PerformClick();
                    if (!object.ReferenceEquals(pickerType.GetProperty("SelectedTarget", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form, null), list[1]))
                        throw new Exception("One-click card chose wrong project");
                    Console.WriteLine("PASS: " + (dark ? "dark" : "light") + " no black button corners; "
                        + (native ? "native smooth frame" : "client bitmap paint") + "; DPI=" + form.DeviceDpi + "; one-click selection");
                }
                return 0;
            }
            if (args.Length > 1 && args[1] == "startup")
            {
                foreach (string key in Environment.GetEnvironmentVariables().Keys)
                    if (key.StartsWith("TTSK_SESSION_", StringComparison.Ordinal)) Environment.SetEnvironmentVariable(key, null);
                bool chose = false, opened = false, sawSplash = false;
                var driver = new System.Threading.Thread(() => {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    while (watch.ElapsedMilliseconds < 20000 && !opened)
                    {
                        System.Threading.Thread.Sleep(200);
                        try
                        {
                            foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
                            {
                                if (!form.IsHandleCreated) continue;
                                form.BeginInvoke((Action)(() => {
                                    if (form.Text == "Chọn dự án Tekla" && !chose)
                                    { chose = true; Descendants(form).OfType<Button>().First(b => b.Tag != null).PerformClick(); }
                                    else if (form.GetType().Name == "MainForm" && !opened)
                                    { opened = true; Console.WriteLine("PASS: fresh startup picker -> " + form.Text); form.Close(); }
                                    else if (form.GetType().Name == "TeklaStartupForm") sawSplash = true;
                                }));
                            }
                        }
                        catch (InvalidOperationException) { }
                    }
                    if (!opened) Environment.Exit(1);
                });
                driver.IsBackground = true; driver.Start();
                int result = (int)assembly.GetType("TTSK_AutoDim_Plates.Program").GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { new string[0] });
                opened = true;
                if (!chose || !sawSplash || result != 0) throw new Exception("Fresh multi-session startup failed");
                Console.WriteLine("PASS: startup logo -> picker -> verified main handoff");
                return 0;
            }
            var coordinator = assembly.GetType("TTSK_AutoDim_Plates.TeklaSessionCoordinator", true);
            Func<string, object[], object> call = (name, values) => coordinator.GetMethod(name,
                BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, values);
            if (args.Length > 1 && args[1] == "readiness")
            {
                var targets = ((System.Collections.IEnumerable)call("Discover", null)).Cast<object>();
                if (!targets.Any(t => (bool)call("IsReady", new[] { t })))
                { Console.WriteLine("SKIP: no ready Tekla API session for live tests"); return 3; }
                return 0;
            }
            if (args.Length > 1 && args[1] == "audit-pure")
            {
                var targetType = assembly.GetType("TTSK_AutoDim_Plates.TeklaSessionTarget");
                foreach (string path in new[] { @"D:\27-000)HULIC_B", @"D:\Project - Tower A", @"\\server\models\Project - Tower B" })
                foreach (string suffix in new[] { "  - View 1 - HS-3D", "  - W   [B-12]", " - View 1 - HS-3D", "", " ", "   ", "\t" })
                {
                    var target = Activator.CreateInstance(targetType, true);
                    string title = "Tekla Structures - Educational version - " + path + suffix;
                    targetType.GetField("Title", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, title);
                    string parsed = (string)targetType.GetProperty("ModelFolder", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target, null);
                    if (parsed != path || !(bool)call("MatchesModelTitle", new object[] { title, path })) throw new Exception("Bad model title parsing: " + title);
                    if ((bool)call("MatchesModelTitle", new object[] { title, path.Substring(0, path.Length - 1) })) throw new Exception("Path prefix incorrectly accepted");
                }
                Console.WriteLine("PASS: 21 title/path cases including trailing spaces, tabs, UNC and exact path boundaries");
                if((bool)call("MatchesModelTitle",new object[]{@"Tekla Structures - D:\Model_A - Tower B ",@"D:\Model_A"}))
                    throw new Exception("Hyphenated folder prefix accepted as same model");
                if((bool)call("MatchesModelTitle",new object[]{null,@"D:\Model_A"}))throw new Exception("Null title accepted");
                Func<string, int, long, string, object> make = (folder, id, started, session) => {
                    var target = Activator.CreateInstance(targetType, true); var f=BindingFlags.NonPublic|BindingFlags.Instance;
                    targetType.GetField("Title",f).SetValue(target,"Tekla Structures - Educational version - " + folder + " ");
                    targetType.GetField("Id",f).SetValue(target,id); targetType.GetField("Started",f).SetValue(target,started);
                    targetType.GetField("Session",f).SetValue(target,session); return target;
                };
                var bound=make(@"D:\Model_A",11,22,"Console");
                Func<object,string,bool> same=(target,path)=>(bool)call("SameWorkspace",new object[]{target,bound,path});
                if(!same(make(@"d:\model_a",11,22,"Console"),@"D:\Model_A\"))throw new Exception("Same model normalization failed");
                foreach(var changed in new[]{make(@"D:\Model_B",11,22,"Console"),make(@"D:\Model_A",12,22,"Console"),
                    make(@"D:\Model_A",11,23,"Console"),make(@"D:\Model_A",11,22,"Console-11"),make("",11,22,"Console")})
                    if(same(changed,@"D:\Model_A"))throw new Exception("Changed workspace incorrectly accepted");
                if(same(null,@"D:\Model_A")||same(bound,null))throw new Exception("Unavailable workspace accepted");
                Console.WriteLine("PASS: workspace identity: same PID/new model, PID reuse, channel change, missing model, normalized same model");
                return 0;
            }
            if (args.Length > 1 && args[1] == "benchmark")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var targets = ((System.Collections.IEnumerable)call("Discover", null)).Cast<object>().ToList();
                Console.WriteLine("discover_ms=" + watch.ElapsedMilliseconds);
                var target = targets[0]; var f = BindingFlags.NonPublic | BindingFlags.Instance;
                Environment.SetEnvironmentVariable("TTSK_SESSION_PID", target.GetType().GetField("Id", f).GetValue(target).ToString());
                Environment.SetEnvironmentVariable("TTSK_SESSION_START", target.GetType().GetField("Started", f).GetValue(target).ToString());
                Environment.SetEnvironmentVariable("TTSK_SESSION_KNOWN", string.Join(";", targets.Select(t =>
                    t.GetType().GetField("Id", f).GetValue(t) + ":" + t.GetType().GetField("Started", f).GetValue(t))));
                watch.Restart(); call("Prepare", null); Console.WriteLine("prepare_ms=" + watch.ElapsedMilliseconds);
                watch.Restart(); call("VerifyConnection", null); Console.WriteLine("verify_ms=" + watch.ElapsedMilliseconds);
                watch.Restart();
                using (var form = (Form)Activator.CreateInstance(assembly.GetType("TTSK_AutoDim_Plates.MainForm")))
                {
                    Console.WriteLine("main_construct_ms=" + watch.ElapsedMilliseconds);
                    form.Shown += delegate { Console.WriteLine("main_shown_ms=" + watch.ElapsedMilliseconds); form.BeginInvoke((Action)(() => form.Close())); };
                    Application.Run(form);
                }
                return 0;
            }
            if (args.Length == 1)
            {
                var targets = (System.Collections.IEnumerable)call("Discover", null);
                int count = 0;
                foreach (var target in targets)
                {
                    var type = target.GetType();
                    Console.WriteLine(target + " | channel=" + type.GetField("Session", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target)
                        + " | started=" + type.GetField("Started", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target));
                    if (!(bool)call("IsReady", new[] { target })) throw new Exception("Live session readiness failed");
                    count++;
                }
                return count >= 2 ? 0 : 2;
            }
            Environment.SetEnvironmentVariable("TTSK_SESSION_PID", args[1]);
            Environment.SetEnvironmentVariable("TTSK_SESSION_START", args[2]);
            call("Prepare", null);
            if (args.Length > 3 && args[3] == "inspect")
            {
                ReadApis();
                using (var process = System.Diagnostics.Process.GetProcessById(int.Parse(args[1])))
                    Console.WriteLine("WindowTitle=[" + process.MainWindowTitle + "]; length=" + process.MainWindowTitle.Length);
                return 0;
            }
            call("VerifyConnection", null);
            ReadApis();
            call("VerifyConnection", null); // Repeated preflight must stay on the same model/channel.
            Console.WriteLine("PASS: repeated session preflight");
            if (args.Length > 3 && args[3] == "watch-snapshots")
            {
                Application.EnableVisualStyles();
                using (var form=(Form)Activator.CreateInstance(assembly.GetType("TTSK_AutoDim_Plates.MainForm")))
                {
                    var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                    var targets=(System.Collections.IList)call("Discover",null); var listType=targets.GetType();
                    var current=targets.Cast<object>().First(t=>(int)t.GetType().GetField("Id",flags).GetValue(t)==int.Parse(args[1]));
                    var delegateType=typeof(Func<>).MakeGenericType(listType);
                    var body=System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Call(typeof(Program).GetMethod("GetStartupSnapshot",BindingFlags.NonPublic|BindingFlags.Static)),listType);
                    form.GetType().GetField("workspaceDiscovery",flags).SetValue(form,System.Linq.Expressions.Expression.Lambda(delegateType,body).Compile());
                    Action<string,object> set=(name,value)=>form.GetType().GetField(name,flags).SetValue(form,value);
                    Func<string,object> get=name=>form.GetType().GetField(name,flags).GetValue(form);
                    Func<System.Threading.Tasks.Task> poll=()=> (System.Threading.Tasks.Task)form.GetType().GetMethod("PollTeklaWorkspaceAsync",flags).Invoke(form,null);
                    Func<object,string,object> cloneTitle=(target,path)=> {
                        var copy=target.GetType().GetMethod("MemberwiseClone",flags).Invoke(target,null);
                        copy.GetType().GetField("Title",flags).SetValue(copy,"Tekla Structures - Educational version - " + path + " "); return copy;
                    };
                    Action<object[]> snapshot=items=> {var list=(System.Collections.IList)Activator.CreateInstance(listType);foreach(var item in items)list.Add(item);startupSnapshot=list;};
                    form.Shown+=async delegate {
                        ((Timer)get("sessionTimer")).Stop();
                        snapshot(new[]{current}); await poll();
                        if((bool)get("workspaceRefreshRequired")||get("sessionCandidateKey")!=null)throw new Exception("Unchanged workspace reconnects");
                        snapshot(new object[0]); set("operationRunning",true); await poll();
                        if((bool)get("workspaceRefreshRequired"))throw new Exception("Busy scan changed workspace state");
                        set("operationRunning",false); await poll();
                        if(!(bool)get("workspaceRefreshRequired"))throw new Exception("Closed workspace not blocked");
                        if((bool)form.GetType().GetMethod("EnsureSessionCommandTarget",flags).Invoke(form,null))throw new Exception("Closed workspace command allowed");
                        snapshot(new[]{cloneTitle(current,"Không có model")}); await poll();
                        if(get("sessionCandidateKey")!=null)throw new Exception("Loading title treated as ready");
                        snapshot(new[]{current}); await poll();
                        if(get("sessionCandidateKey")==null)throw new Exception("Reopen same model after observed close not reconnected");
                        set("workspaceRefreshRequired",false); set("sessionCandidateKey",null);
                        snapshot(new[]{cloneTitle(current,@"Z:\model-B")}); await poll(); string first=(string)get("sessionCandidateKey");
                        snapshot(new[]{cloneTitle(current,@"Z:\model-C")}); await poll();
                        if(first==null||first==(string)get("sessionCandidateKey")||(bool)get("sessionSwitching"))throw new Exception("Changing title bypassed stabilization");
                        set("workspaceRefreshRequired",false); set("sessionCandidateKey",null);
                        snapshot(new[]{current}); await poll();
                        if(get("sessionCandidateKey")!=null)throw new Exception("Return to original left a switch pending");
                        set("sessionRetryAfterUtc",DateTime.UtcNow.AddSeconds(30)); snapshot(new object[0]); await poll();
                        if((bool)get("workspaceRefreshRequired"))throw new Exception("Retry backoff ignored");
                        Console.WriteLine("PASS: idle unchanged, busy, closed/loading, reopen same path, changing title stabilization, return-to-original, retry cooldown");
                        set("sessionRetryAfterUtc",DateTime.MinValue); form.Close();
                    };
                    Application.Run(form);
                }
                return 0;
            }
            if (args.Length > 3 && args[3] == "same-pid")
            {
                Application.EnableVisualStyles();
                using (var form=(Form)Activator.CreateInstance(assembly.GetType("TTSK_AutoDim_Plates.MainForm")))
                using (var timeout=new Timer {Interval=100})
                {
                    var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                    var modelPath=coordinator.GetProperty("ModelPath",BindingFlags.NonPublic|BindingFlags.Static);
                    string livePath=(string)modelPath.GetValue(null,null);
                    modelPath.SetValue(null,@"Z:\previous-workspace-for-test",null); // Chỉ trạng thái TTSK, không đổi Tekla thật.
                    coordinator.GetField("FollowForeground",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,false);
                    var busy=form.GetType().GetField("operationRunning",flags); busy.SetValue(form,true);
                    var watch=System.Diagnostics.Stopwatch.StartNew(); bool released=false,blocked=false;
                    timeout.Tick+=delegate {
                        if(watch.ElapsedMilliseconds>18000)throw new Exception("Same-PID monitor did not hand off");
                        if(watch.ElapsedMilliseconds<1300) {
                            if((bool)form.GetType().GetField("sessionSwitching",flags).GetValue(form))throw new Exception("Switched while busy");
                        } else if(!released) {
                            busy.SetValue(form,false); released=true;
                            Console.WriteLine("PASS: model change deferred while operation busy");
                        }
                        if(!blocked && (bool)form.GetType().GetField("workspaceRefreshRequired",flags).GetValue(form)) {
                            if((bool)form.GetType().GetMethod("EnsureSessionCommandTarget",flags).Invoke(form,null))throw new Exception("Stale workspace command allowed");
                            blocked=true; Console.WriteLine("PASS: stale commands blocked before same-PID handoff");
                        }
                    };
                    form.Shown+=delegate { timeout.Start(); };
                    Application.Run(form);
                    if(!released||!blocked)throw new Exception("Same-PID lifecycle was incomplete");
                    Console.WriteLine("PASS: periodic detection with no foreground event; fixed mode; same PID -> " + livePath);
                }
                return 0;
            }
            if (args.Length > 3 && args[3] == "ui")
            {
                Application.EnableVisualStyles();
                using (var form = (Form)Activator.CreateInstance(assembly.GetType("TTSK_AutoDim_Plates.MainForm")))
                {
                    var type = form.GetType();
                    var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                    Action<string, object> set = (name, value) => type.GetField(name, flags).SetValue(form, value);
                    Func<string, object[], object> invoke = (name, values) => type.GetMethod(name, flags).Invoke(form, values);
                    var caption = (Label)type.GetField("mainBrandSubtitle", flags).GetValue(form);
                    string connectedPath = (string)coordinator.GetProperty("ModelPath", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null);
                    if (!caption.Text.Contains(System.IO.Path.GetFileName(connectedPath))) throw new Exception("Wrong project caption");
                    var targets = ((System.Collections.IEnumerable)call("Discover", null)).Cast<object>().ToList();
                    var other = targets.First(t => (int)t.GetType().GetField("Id", flags).GetValue(t) != int.Parse(args[1]));
                    int otherId = (int)other.GetType().GetField("Id", flags).GetValue(other);
                    invoke("ObserveTeklaForeground", new object[] { otherId });
                    set("operationRunning", true);
                    if ((bool)invoke("CanSwitchTeklaSession", null)) throw new Exception("Switch allowed during operation");
                    if ((bool)invoke("EnsureSessionCommandTarget", null)) throw new Exception("Command accepted for pending project");
                    set("operationRunning", false);
                    invoke("ObserveTeklaForeground", new object[] { int.Parse(args[1]) });
                    if (!(bool)invoke("EnsureSessionCommandTarget", null)) throw new Exception("Returning to current project did not cancel pending switch");
                    foreach (var field in new[] { "_isBatchRunning", "_pdfCommandRunning", "_snapshotExportRunning", "_fitAndCleanupRunning", "dimensionCheckOpen", "_updateDialogOpen", "batchShortcutPending" })
                    {
                        set(field, true);
                        if ((bool)invoke("CanSwitchTeklaSession", null)) throw new Exception("Switch allowed while " + field);
                        set(field, false);
                    }
                    Console.WriteLine("PASS: UI project caption, pending focus guard and return-to-current");
                    // Show only the TTSK test form; no command or Tekla focus change. Exercise real process handoff.
                    coordinator.GetField("FollowForeground", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
                    form.Shown += delegate { form.BeginInvoke((Action)(async () => {
                        if (!(bool)invoke("CanSwitchTeklaSession", null)) throw new Exception("Idle switch blocked");
                        foreach (var field in new[] { "operationRunning", "_isBatchRunning", "_pdfCommandRunning", "_snapshotExportRunning", "_fitAndCleanupRunning", "dimensionCheckOpen", "_updateDialogOpen", "batchShortcutPending" })
                        {
                            set(field, true);
                            if ((bool)invoke("CanSwitchTeklaSession", null)) throw new Exception("Switch allowed while " + field);
                            set(field, false);
                        }
                        Console.WriteLine("PASS: all busy guards with visible form");
                        var identity = other.GetType().GetField("Started", flags);
                        long valid = (long)identity.GetValue(other);
                        identity.SetValue(other, valid - 1);
                        invoke("SwitchTeklaSession", new object[] { other });
                        var wait = System.Diagnostics.Stopwatch.StartNew();
                        while ((bool)type.GetField("sessionSwitching", flags).GetValue(form) && wait.ElapsedMilliseconds < 35000)
                            await System.Threading.Tasks.Task.Delay(100);
                        identity.SetValue(other, valid);
                        if (form.IsDisposed || !form.Enabled || !(bool)invoke("CanSwitchTeklaSession", null)) throw new Exception("Failed handoff lost original UI");
                        call("VerifyConnection", null);
                        Console.WriteLine("PASS: failed handoff preserved original UI and connection");
                        coordinator.GetField("FollowForeground", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, true);
                        var known = (System.Collections.Generic.HashSet<string>)type.GetField("knownTeklaSessions", flags).GetValue(form);
                        string otherKey = otherId + ":" + valid;
                        known.Remove(otherKey); // Mô phỏng phiên Tekla thứ hai vừa xuất hiện, không mở/đóng Tekla thật.
                        int prompted = 0;
                        var pickerDriver = new Timer { Interval = 100 };
                        pickerDriver.Tick += delegate {
                            var picker = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == "TeklaProjectPickerForm");
                            if (picker == null) return;
                            if ((bool)type.GetField("sessionSwitching", flags).GetValue(form)) throw new Exception("Switched before project choice");
                            if (prompted++ == 0)
                            {
                                picker.DialogResult = DialogResult.Cancel; picker.Close();
                                if (form.IsDisposed || !form.Enabled) throw new Exception("Cancel lost original UI");
                                var repeat = new Timer { Interval = 200 };
                                repeat.Tick += delegate { repeat.Stop(); repeat.Dispose(); known.Remove(otherKey); invoke("ObserveTeklaForeground", new object[] { otherId }); };
                                repeat.Start();
                                Console.WriteLine("PASS: new-session picker shown before switching; cancel kept original UI");
                            }
                            else
                            {
                                pickerDriver.Stop(); pickerDriver.Dispose();
                                Descendants(picker).OfType<Button>().First(b => b.Tag != null && (int)b.Tag.GetType().GetField("Id", flags).GetValue(b.Tag) == otherId).PerformClick();
                                Console.WriteLine("PASS: new-session card selected target");
                            }
                        };
                        pickerDriver.Start();
                        invoke("ObserveTeklaForeground", new object[] { otherId });
                        Console.WriteLine("Testing new-session picker through timer after observed Tekla focus");
                    })); };
                    var timeout = new Timer { Interval = 45000 };
                    bool expired = false;
                    timeout.Tick += delegate { expired = true; Console.Error.WriteLine("UI test timeout"); Environment.Exit(1); };
                    timeout.Start();
                    Application.Run(form);
                    timeout.Dispose();
                    if (expired) throw new Exception("UI handoff timed out");
                    Console.WriteLine("PASS: parent closed after verified child readiness; target=" + otherId);
                }
            }
            if (args.Length > 3 && args[3] == "guard")
            {
                var property = coordinator.GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic);
                var target = property.GetValue(null, null);
                var started = target.GetType().GetField("Started", BindingFlags.Instance | BindingFlags.NonPublic);
                long previous = (long)started.GetValue(target);
                started.SetValue(target, previous - 1);
                try { call("VerifyConnection", null); throw new Exception("Stale identity was accepted"); }
                catch (TargetInvocationException) { Console.WriteLine("PASS: stale process identity rejected"); }
                finally { started.SetValue(target, previous); }
                var path = coordinator.GetProperty("ModelPath", BindingFlags.NonPublic | BindingFlags.Static);
                object original = path.GetValue(null, null);
                path.SetValue(null, @"Z:\different-model", null);
                try { call("VerifyConnection", null); throw new Exception("Changed model was accepted"); }
                catch (TargetInvocationException) { Console.WriteLine("PASS: changed model context rejected"); }
                finally { path.SetValue(null, original, null); }
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetBaseException()); return 1; }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void ReadApis()
    {
        var model = new Model();
        var handler = new DrawingHandler();
        Console.WriteLine("Model=" + model.GetConnectionStatus() + "; Drawing=" + handler.GetConnectionStatus());
        Console.WriteLine("Path=" + model.GetInfo().ModelPath);
        var drawing = handler.GetActiveDrawing();
        Console.WriteLine("Active=" + (drawing == null ? "<none>" : drawing.GetType().Name + " " + drawing.Mark + " " + drawing.Name));
    }

    static System.Collections.Generic.IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmFlush();
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    static void VerifySmoothCorners(System.Drawing.Bitmap bitmap, System.Drawing.Color fill)
    {
        // Mép compositor phải có pixel chuyển sắc, khác cả nền ruột và nền phía sau/viền.
        foreach (var origin in new[] { new System.Drawing.Point(0, 0), new System.Drawing.Point(bitmap.Width - 1, 0),
            new System.Drawing.Point(0, bitmap.Height - 1), new System.Drawing.Point(bitmap.Width - 1, bitmap.Height - 1) })
        {
            var colors = new System.Collections.Generic.HashSet<int>();
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
                colors.Add(bitmap.GetPixel(origin.X == 0 ? x : origin.X - x, origin.Y == 0 ? y : origin.Y - y).ToArgb());
            if (colors.Count < 6) throw new Exception("Corner lacks antialias coverage: " + origin + " colors=" + colors.Count);
            Console.WriteLine("PASS: corner " + origin + " has " + colors.Count + " composited colors");
        }
    }
}
