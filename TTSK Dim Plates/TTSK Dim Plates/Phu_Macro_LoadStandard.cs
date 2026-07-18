#pragma warning disable 1633 // Unrecognized #pragma directive
#pragma reference "Tekla.Macros.Akit"
#pragma reference "Tekla.Macros.Runtime"
#pragma reference "Tekla.Structures.Drawing"
#pragma warning restore 1633 // Unrecognized #pragma directive

namespace UserMacros
{
    public sealed class Macro
    {
        [Tekla.Macros.Runtime.MacroEntryPointAttribute()]
        public static void Run(Tekla.Macros.Runtime.IMacroRuntime runtime)
        {
            Tekla.Macros.Akit.IAkitScriptHost akit =
                runtime.Get<Tekla.Macros.Akit.IAkitScriptHost>();

            bool isSinglePartDrawing = IsSinglePartDrawing();
            string standardName = isSinglePartDrawing
                ? "()_Geo_Standard_Part"
                : "()_Geo_Standard";

            LoadGeometryStandard(
                akit,
                isSinglePartDrawing,
                standardName);
        }

        private static void LoadGeometryStandard(
            Tekla.Macros.Akit.IAkitScriptHost akit,
            bool isSinglePartDrawing,
            string standardName)
        {
            // The Akit commands below target view_dial. On the first run of a
            // Tekla session, and again after Section properties have changed
            // Tekla's dialog state, it may not exist. This preparation is part
            // of the common load method so EVERY Geometry Standard load must
            // open View Properties immediately before loading the standard.
            OpenViewPropertiesBeforeGeometryStandardLoad(akit);

            // Single part macro recorder had this step before Edit settings.
            // Assembly macro recorder did not have it, so only run it for SinglePartDrawing.
            if (isSinglePartDrawing)
            {
                akit.TreeSelect(
                    "view_dial",
                    "gratCastUnitDrawingAttributesMenuTree",
                    "Attributes");
            }

            akit.PushButton("btnEditSettings", "view_dial");
            akit.ValueChange("vclassifier_dial", "mnuLoad", standardName);
            akit.PushButton("btnLoad", "vclassifier_dial");
            akit.PushButton("btnModify", "vclassifier_dial");
            akit.PushButton("btnApply", "vclassifier_dial");
            akit.PushButton("btnOk", "vclassifier_dial");
            akit.PushButton("view_modify", "view_dial");
            akit.PushButton("view_apply", "view_dial");
            akit.PushButton("view_ok", "view_dial");
        }

        private static void OpenViewPropertiesBeforeGeometryStandardLoad(
            Tekla.Macros.Akit.IAkitScriptHost akit)
        {
            akit.Callback(
                "acmd_display_attr_dialogview_dial",
                "",
                "main_frame");
            System.Threading.Thread.Sleep(50);
        }

        private static bool IsSinglePartDrawing()
        {
            try
            {
                Tekla.Structures.Drawing.DrawingHandler drawingHandler =
                    new Tekla.Structures.Drawing.DrawingHandler();

                Tekla.Structures.Drawing.Drawing drawing =
                    drawingHandler.GetActiveDrawing();

                if (drawing is Tekla.Structures.Drawing.SinglePartDrawing)
                    return true;

                if (drawing is Tekla.Structures.Drawing.AssemblyDrawing)
                    return false;

                // Extra fallback for Tekla versions/environments where the runtime type
                // name is easier to detect than direct casting.
                if (drawing != null)
                {
                    string typeName = drawing.GetType().FullName;
                    if (typeName != null && typeName.IndexOf("SinglePartDrawing") >= 0)
                        return true;
                }
            }
            catch
            {
            }

            // Safe default: Assembly standard, because the assembly recorder does not need TreeSelect.
            return false;
        }
    }
}
