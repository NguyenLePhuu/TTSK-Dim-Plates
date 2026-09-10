using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using D = Tekla.Structures.Drawing;
using M = Tekla.Structures.Model;

namespace Tekla.Technology.Akit.UserScript
{
    // Copies an existing snapshot only. Never opens, updates, prints, saves or creates a drawing.
    public static class PHU_AutoDimSlot12
    {
        public static bool LastRunSucceeded { get; private set; }
        public static string LastRunMessage { get; private set; }
        public static string LastOutputDirectory { get; private set; }

        public static void Run()
        {
            LastRunSucceeded = false;
            try
            {
                LastOutputDirectory = CopySelected();
                LastRunSucceeded = true;
                LastRunMessage = "Da xuat PNG snapshot (canh dai 5000 px): " + LastOutputDirectory;
            }
            catch (Exception ex) { LastRunMessage = "Snapshot: " + ex.GetBaseException().Message; }
        }

        public sealed class Source
        {
            public string Mark, File, Guid, DrawnBy;
        }

        public static List<Source> FindSelected()
        {
            var handler=new D.DrawingHandler();
            var selection=handler.GetDrawingSelector().GetSelected();
            var drawings=new List<D.Drawing>();
            while(selection.MoveNext())drawings.Add(selection.Current);
            return FindSources(drawings);
        }

        public static List<Source> FindSources(IList<D.Drawing> drawings)
        {
            var model = new M.Model();
            var handler = new D.DrawingHandler();
            if (!model.GetConnectionStatus() || !handler.GetConnectionStatus())
                throw new InvalidOperationException("Khong ket noi Tekla.");
            string folder = Path.GetFullPath(Path.Combine(model.GetInfo().ModelPath, "drawings", "Snapshots"));
            var result = new List<Source>();
            var util = typeof(D.Drawing).Assembly.GetType("Tekla.Structures.Drawing.Internal.DgUtil", true);
            var getFile = util.GetMethod("GetDgFileNameByDrawingGuid", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (getFile == null) throw new NotSupportedException("Tekla version khong cung cap tra ten snapshot.");
            var seen=new HashSet<System.Guid>();
            foreach (D.Drawing drawing in drawings)
            {
                if(drawing==null)throw new InvalidOperationException("Danh sach load co drawing rong.");
                var property = drawing.GetType().GetProperty("Identifier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var identifier = property == null ? null : property.GetValue(drawing, null) as Tekla.Structures.Identifier;
                if (identifier == null || identifier.GUID == System.Guid.Empty)
                    throw new InvalidOperationException("Khong doc duoc GUID ban ve " + drawing.Mark);
                if(!seen.Add(identifier.GUID))continue;
                string dg = Convert.ToString(getFile.Invoke(null, new object[] { identifier.GUID }));
                if (String.IsNullOrWhiteSpace(dg)) throw new InvalidOperationException("Khong tra duoc file DG " + drawing.Mark);
                string file = Path.GetFullPath(Path.Combine(folder, Path.GetFileName(dg) + ".DPM"));
                if (!file.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Snapshot path invalid.");
                if (!System.IO.File.Exists(file))
                    throw new FileNotFoundException("Ban ve " + drawing.Mark + " chua co snapshot. Khong tao moi snapshot.", file);
                using (var stream = System.IO.File.OpenRead(file))
                    if (stream.ReadByte() != 'D' || stream.ReadByte() != 'P' || stream.ReadByte() != 'M')
                        throw new InvalidDataException("File khong co header DPM: " + file);
                result.Add(new Source { Mark = drawing.Mark, File = file, Guid = identifier.GUID.ToString(),
                    DrawnBy = TTSK_AutoDim_Plates.DrawingPdfPrinter.GetDrawingDrawnBy(drawing) });
            }
            if (result.Count == 0) throw new InvalidOperationException("Hay chon ban ve trong Document manager.");
            return result;
        }

        public static string CopySelected()
        {
            return ExportSources(FindSelected());
        }

        public static string ResolveFolderName(IList<Source> sources)
        {
            foreach(var source in sources)
            {
                string value=(source.DrawnBy??String.Empty).Trim();
                if(value.Length==0||value=="-"||String.Equals(value,"UNKNOWN",StringComparison.OrdinalIgnoreCase))continue;
                foreach(char c in Path.GetInvalidFileNameChars())value=value.Replace(c,'_');
                value=value.Trim().TrimEnd('.');
                if(value.Length>0)return "Snapshot_"+value;
            }
            return "Snapshot_UNKNOWN";
        }

        public static string ExportSources(IList<Source> sources)
        {
            if(sources==null||sources.Count==0)throw new InvalidOperationException("Danh sach snapshot rong.");
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (String.IsNullOrWhiteSpace(desktop)) throw new InvalidOperationException("Khong tim thay Desktop.");
            string folder = Path.Combine(desktop, ResolveFolderName(sources));
            Directory.CreateDirectory(folder);
            string job = Path.Combine(Path.GetTempPath(),"TTSK_SnapshotJobs",System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(job);
            var request = new List<string>();
            var outputs = new List<string>();
            int index=0;
            var reserved=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var source in sources)
            {
                string mark=source.Mark.Trim('[',']');
                foreach(char c in Path.GetInvalidFileNameChars())mark=mark.Replace(c,'_');
                string stem=(++index).ToString("00")+"_"+mark;
                string png=Path.Combine(folder,stem+".png");int suffix=1;
                while(File.Exists(png)||!reserved.Add(png))png=Path.Combine(folder,stem+"_"+(suffix++)+".png");
                request.Add(source.File); request.Add(png); outputs.Add(png);
            }
            string manifest=Path.Combine(job,"request.txt");
            File.WriteAllLines(manifest,request,Encoding.UTF8);
            var start = new System.Diagnostics.ProcessStartInfo(PrepareWorker(),
                "--snapshot-png-worker \"" + manifest + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                WorkingDirectory = job
            };
            using (var worker = System.Diagnostics.Process.Start(start))
            {
                if (worker == null) throw new IOException("Khong khoi dong duoc PNG worker.");
                int timeout=(int)Math.Min(Int32.MaxValue,180000L*Math.Max(1,sources.Count));
                if (!worker.WaitForExit(timeout))
                {
                    worker.Kill();
                    throw new TimeoutException("PNG worker qua thoi gian: " + folder);
                }
                if (worker.ExitCode != 0)
                    throw new IOException("PNG chua hoan tat: " + (File.Exists(Path.Combine(job,"PNG-error.txt"))
                        ? File.ReadAllText(Path.Combine(job,"PNG-error.txt")) : folder));
            }
            foreach (string png in outputs)
            {
                using (var image = System.Drawing.Image.FromFile(png))
                    if (Math.Max(image.Width,image.Height) != 5000) throw new IOException("PNG size verification failed.");
            }
            return folder;
        }

        public static int RenderFolder(string manifest, string teklaBin)
        {
            string folder=Path.GetDirectoryName(manifest);
            try
            {
                string[] requests=File.ReadAllLines(manifest,Encoding.UTF8);
                if(requests.Length==0||requests.Length%2!=0)throw new IOException("Invalid snapshot request.");
                var model = new M.Model();
                if (!model.GetConnectionStatus()) throw new InvalidOperationException("Can Tekla model dang mo de doc cau hinh font/net.");
                var assembly = Assembly.LoadFrom(Path.Combine(teklaBin,"DPMPrinter.dll"));
                var pdf = Assembly.LoadFrom(Path.Combine(teklaBin,"PdfSharp-Hybrid.dll"));
                Func<string,object[],object> make = (name,values) => Activator.CreateInstance(assembly.GetType("Tekla.Structures.DPMPrinter."+name,true),values);
                var adv = make("DefaultAdvancedOptionHandler",new object[0]);
                var settings = make("DefaultSettingsFileHandler",new[]{adv});
                var paper = make("DefaultPaperSettingsHandler",new[]{settings});
                // Initializing print options populates required rendering defaults; no settings are saved.
                var print = make("PrintOptions",new[]{paper});
                var colors = make("DefaultColorTableHandler",new object[0]);
                var handler = make("DefaultDpmHandler",new[]{colors,adv});
                var printer = make("DpmPrinter",new[]{handler,make("DefaultModelHandler",new object[0]),(object)false});
                for(int requestIndex=0;requestIndex<requests.Length;requestIndex+=2)
                {
                    string source=requests[requestIndex], output=requests[requestIndex+1];
                    string originalHash = Hash(source);
                    var data = make("DpmData",new object[0]);
                    try
                    {
                        data.GetType().GetMethod("LoadFromFile").Invoke(data,new object[]{source});
                        var size = data.GetType().GetProperty("Size").GetValue(data,null);
                        double mmW = Number(size,"X"), mmH = Number(size,"Y");
                        if (mmW<=0 || mmH<=0 || Double.IsNaN(mmW+mmH) || Double.IsInfinity(mmW+mmH)) throw new IOException("Invalid DPM paper size.");
                        double ratio=5000.0/Math.Max(mmW,mmH);
                        int width=Math.Max(1,(int)Math.Round(mmW*ratio)), height=Math.Max(1,(int)Math.Round(mmH*ratio));
                        double ptW=mmW*72.0/25.4, ptH=mmH*72.0/25.4;
                        var options=make("DpmPrinterOptions",new object[0]);
                        options.GetType().GetProperty("ScaleFactor").SetValue(options,1.0,null);
                        if(File.Exists(output)) throw new IOException("Khong ghi de PNG da co: "+output);
                        using(var bitmap=new System.Drawing.Bitmap(width,height))
                        {
                            bitmap.SetResolution(72,72);
                            using(var graphics=System.Drawing.Graphics.FromImage(bitmap))
                            {
                                graphics.Clear(System.Drawing.Color.White);
                                var canvas=Activator.CreateInstance(pdf.GetType("PdfSharp.Drawing.XSize"),new object[]{(double)width,(double)height});
                                var unit=Enum.Parse(pdf.GetType("PdfSharp.Drawing.XGraphicsUnit"),"Point");
                                var factory=Array.Find(pdf.GetType("PdfSharp.Drawing.XGraphics").GetMethods(),m=>m.Name=="FromGraphics"&&m.GetParameters().Length==3);
                                var gfx=factory.Invoke(null,new[]{(object)graphics,canvas,unit});
                                try
                                {
                                    gfx.GetType().GetMethod("ScaleTransform",new[]{typeof(double),typeof(double)}).Invoke(gfx,new object[]{width/ptW,width/ptW});
                                    var wrapper=make("XGraphicsWrapper",new[]{gfx});
                                    var rect=Activator.CreateInstance(pdf.GetType("PdfSharp.Drawing.XRect"),new object[]{0.0,0.0,ptW,ptH});
                                    printer.GetType().GetMethod("Draw").Invoke(printer,new[]{data,options,wrapper,1.0,0.05,rect,rect});
                                }
                                finally { ((IDisposable)gfx).Dispose(); }
                            }
                            bitmap.SetResolution((float)(width/mmW*25.4),(float)(height/mmH*25.4));
                            bitmap.Save(output,System.Drawing.Imaging.ImageFormat.Png);
                        }
                        if(Hash(source)!=originalHash)throw new IOException("DPM copy changed during rendering.");
                    }
                    finally { var disposable=data as IDisposable;if(disposable!=null)disposable.Dispose(); }
                }
                return 0;
            }
            catch(Exception ex)
            {
                File.WriteAllText(Path.Combine(folder,"PNG-error.txt"),ex.GetBaseException().ToString());
                return 1;
            }
        }
        private static string PrepareWorker()
        {
            string own=typeof(PHU_AutoDimSlot12).Assembly.Location;
            string folder=Path.Combine(Path.GetTempPath(),"TTSK_SnapshotWorker",Hash(own));
            Directory.CreateDirectory(folder);
            string exe=Path.Combine(folder,"SnapshotWorker.exe");
            if(!File.Exists(exe))File.Copy(own,exe,false);
            string config=exe+".config";
            if(File.Exists(config))return exe;
            string bin=Path.GetDirectoryName(typeof(M.Model).Assembly.Location);
            var doc=new System.Xml.XmlDocument();
            doc.LoadXml("<configuration><startup><supportedRuntime version='v4.0' sku='.NETFramework,Version=v4.8'/></startup><runtime><assemblyBinding xmlns='urn:schemas-microsoft-com:asm.v1'/></runtime></configuration>");
            var binding=doc.DocumentElement["runtime"].FirstChild;
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files=new List<string>(Directory.GetFiles(bin,"Tekla*.dll"));
            foreach(string file in new[]{"Trimble.Remoting.dll","DPMPrinter.dll","PdfSharp-Hybrid.dll","System.Memory.dll","System.Buffers.dll","System.Numerics.Vectors.dll","System.Runtime.CompilerServices.Unsafe.dll","System.Threading.Tasks.Extensions.dll","Microsoft.Bcl.AsyncInterfaces.dll","Microsoft.Extensions.DependencyInjection.Abstractions.dll","Microsoft.Extensions.Logging.Abstractions.dll"})
                if(File.Exists(Path.Combine(bin,file)))files.Add(Path.Combine(bin,file));
            foreach(string file in files)
            {
                AssemblyName name;try{name=AssemblyName.GetAssemblyName(file);}catch(BadImageFormatException){continue;}
                if(!names.Add(name.Name))continue;
                const string ns="urn:schemas-microsoft-com:asm.v1";
                var dep=doc.CreateElement("dependentAssembly",ns);
                var id=doc.CreateElement("assemblyIdentity",ns);id.SetAttribute("name",name.Name);id.SetAttribute("culture","neutral");
                byte[] token=name.GetPublicKeyToken();if(token!=null&&token.Length>0)id.SetAttribute("publicKeyToken",BitConverter.ToString(token).Replace("-","").ToLowerInvariant());
                dep.AppendChild(id);
                var redirect=doc.CreateElement("bindingRedirect",ns);redirect.SetAttribute("oldVersion","0.0.0.0-"+name.Version);redirect.SetAttribute("newVersion",name.Version.ToString());dep.AppendChild(redirect);
                var code=doc.CreateElement("codeBase",ns);code.SetAttribute("version",name.Version.ToString());code.SetAttribute("href",new Uri(file).AbsoluteUri);dep.AppendChild(code);binding.AppendChild(dep);
            }
            doc.Save(config);
            return exe;
        }
        private static double Number(object value,string name)
        {
            var p=value.GetType().GetProperty(name);
            return Convert.ToDouble(p!=null?p.GetValue(value,null):value.GetType().GetField(name).GetValue(value));
        }
        private static string Hash(string file)
        {
            using (var sha = SHA256.Create())
            using (var stream = System.IO.File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
}
