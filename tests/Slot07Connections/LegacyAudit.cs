using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Linq;
using M = Tekla.Structures.Model;
using D = Tekla.Structures.Drawing;

static class LegacyAudit
{
    internal static void Run(string product, string output)
    {
        var a = Assembly.LoadFrom(Path.GetFullPath(product));
        Func<string, Type> type = n => a.GetType("Tekla.Technology.Akit.UserScript." + n, true);
        Func<string, string, object[], object> call = (n, m, p) => type(n).GetMethod(m, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, p);
        var model = new M.Model(); var handler = new D.DrawingHandler();
        if (!model.GetConnectionStatus() || !handler.GetConnectionStatus()) throw new Exception("API disconnected");
        var drawing = handler.GetActiveDrawing();
        var main = call("PHU_MainPartResolver", "Resolve", new object[] { model, drawing });
        var report = new StringBuilder("LEGACY READ-ONLY DIAGNOSIS; no drawing mutation.\n");
        var snapshot = Tekla.Technology.Akit.UserScript.PHU_Slot07_ColumnConnections.Read();
        foreach (var v in snapshot.Plans.Select(p => p.View).Distinct())
        {
            foreach (var part in v.Parts)
                report.AppendLine("PART VIEW=" + v.Id + " P=" + part.Id + " " + part.Profile + " " + part.Position
                    + " REF=" + String.Join(";", part.Reference) + " bounds=" + part.Vertices.Min(p=>p.X) + "," + part.Vertices.Max(p=>p.X)
                    + " Y=" + part.Vertices.Min(p=>p.Y) + "," + part.Vertices.Max(p=>p.Y));
        }
        using ((IDisposable)call("PHU_ColumnProjectRouter", "BeginInjaiSelection", new object[0]))
        {
            report.AppendLine("Drawing=" + drawing.Mark);
            report.AppendLine("Views=" + call("PHU_VerticalShapeViewLayoutContext", "Begin", new[] { model, drawing, main }));
            // Audit only: use the existing H spacing defaults to reveal topology;
            // this is NOT proof of final Box placement and is never a live writer.
            call("PHU_ColumnDimensionTierContext", "Configure", new[] { (object)true, Enum.ToObject(type("PHU_ColumnShapeFamily"), 1) });
            foreach (string engine in new[] { "PHU_InzaiColumnNeighborDimensionEngine", "PHU_InzaiColumnSpliceDimensionEngine", "PHU_InzaiColumnSectionDimensionEngine" })
                call(engine, "Configure", new object[] { true });
            call("PHU_InzaiColumnNeighborDimensionEngine", "Begin", new[] { model, drawing, main });
            call("PHU_ColumnGridDimensionEngine", "Configure", new object[] { true, 3 });
            report.AppendLine("Grid begin=" + call("PHU_ColumnGridDimensionEngine", "Begin", new[] { model, drawing, main }));
            foreach (var pair in new[] { new[] { "PHU_InzaiColumnNeighborDimensionEngine", "AuditPreparedPlans" }, new[] { "PHU_InzaiColumnSpliceDimensionEngine", "AuditPreparedPlans" }, new[] { "PHU_ColumnGridDimensionEngine", "AuditPreparedGeometry" }, new[] { "PHU_InzaiColumnSectionDimensionEngine", "AuditCurrentDrawingPlan" }, new[] { "PHU_InzaiColumnSpliceDimensionEngine", "AuditGeometryRegression" }, new[] { "PHU_InzaiColumnSectionDimensionEngine", "AuditGeometryRegression" }, new[] { "PHU_ColumnGridDimensionEngine", "AuditType2HorizontalGridRegression" } })
            {
                try { report.AppendLine(pair[0] + "\n" + call(pair[0], pair[1], new object[0])); }
                catch (Exception ex) { report.AppendLine(pair[0] + " ERROR " + ex.GetBaseException().Message); }
            }
        }
        File.WriteAllText(output, report.ToString());
        Console.WriteLine(report);
    }
}
