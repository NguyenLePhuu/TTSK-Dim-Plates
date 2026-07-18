#pragma warning disable 1633

using System;
using System.Text;
using System.Windows.Forms;

using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

namespace Tekla.Technology.Akit.UserScript
{
    // Slot 06 for MainForm:
    // Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot06.Run()
    public class PHU_AutoDimSlot06
    {
        public static void Run()
        {
            PHU_SectionViewPropertyDump.Run();
        }
    }

    // Read-only diagnostic. It uses the same resolver as Auto Section so the
    // value shown here is exactly the value passed to ViewAttributes.LoadAttributes.
    public class PHU_SectionViewPropertyDump
    {
        public static void Run()
        {
            try
            {
                DrawingHandler handler = new DrawingHandler();
                if (!handler.GetConnectionStatus())
                {
                    Show("Khong ket noi duoc Tekla Drawing.", true);
                    return;
                }

                Drawing drawing = handler.GetActiveDrawing();
                if (drawing == null)
                {
                    Show("Khong co active drawing.", true);
                    return;
                }

                Model model = new Model();
                SectionViewAttributeResolution resolution =
                    SectionViewAttributeResolver.Resolve(drawing, model);

                StringBuilder text = new StringBuilder();
                text.AppendLine("SECTION VIEW PROPERTY - READ ONLY");
                text.AppendLine();
                text.AppendLine("Drawing type: " +
                    drawing.GetType().FullName);
                text.AppendLine("Resolved: " +
                    (resolution.Success ? "YES" : "NO"));
                text.AppendLine("Section View properties: " +
                    Display(resolution.AttributeName));
                text.AppendLine("Resolved by: " +
                    Display(resolution.Source));
                text.AppendLine("Drawing.AttributeFilename: " +
                    Display(resolution.DrawingAttributeName));
                text.AppendLine("Drawing property file: " +
                    Display(resolution.DrawingPropertyFile));
                text.AppendLine("Section row state (raw): " +
                    Display(resolution.EnabledCode));
                text.AppendLine("Active view property hint(s): " +
                    (resolution.ActiveViewAttributeNames.Count == 0
                        ? "<none>"
                        : string.Join(
                            ", ",
                            resolution.ActiveViewAttributeNames.ToArray())));

                if (resolution.MatchingPropertyFiles.Count > 0)
                {
                    text.AppendLine();
                    text.AppendLine("Matching .wd/.ad file(s):");
                    for (int i = 0;
                        i < resolution.MatchingPropertyFiles.Count;
                        i++)
                    {
                        text.AppendLine("  " +
                            resolution.MatchingPropertyFiles[i]);
                    }
                }

                if (!string.IsNullOrWhiteSpace(
                    resolution.RawDrawingViews))
                {
                    text.AppendLine();
                    text.AppendLine("Raw dv.aDrawingViews:");
                    text.AppendLine(resolution.RawDrawingViews);
                }

                if (!resolution.Success)
                {
                    text.AppendLine();
                    text.AppendLine("Reason:");
                    text.AppendLine(Display(resolution.Error));
                }

                if (!string.IsNullOrWhiteSpace(
                    resolution.LiveDialogError))
                {
                    text.AppendLine();
                    text.AppendLine("Live dialog diagnostic:");
                    text.AppendLine(resolution.LiveDialogError);
                }

                if (resolution.LiveDialogValues.Count > 0)
                {
                    text.AppendLine();
                    text.AppendLine("Visible values read from Drawing Properties:");
                    int maximum = Math.Min(
                        resolution.LiveDialogValues.Count,
                        30);
                    for (int i = 0; i < maximum; i++)
                    {
                        text.AppendLine("  " +
                            resolution.LiveDialogValues[i]);
                    }
                }

                text.AppendLine();
                text.AppendLine(
                    "No Load/Modify/Apply and no Tekla file is written.");

                Show(text.ToString(), !resolution.Success);
            }
            catch (Exception ex)
            {
                Show(
                    "Section view property dump failed:\n" +
                    ex.GetType().Name + ": " + ex.Message,
                    true);
            }
        }

        private static string Display(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "<none>"
                : value;
        }

        private static void Show(string value, bool warning)
        {
            MessageBox.Show(
                value,
                "Dump Section View Properties",
                MessageBoxButtons.OK,
                warning
                    ? MessageBoxIcon.Warning
                    : MessageBoxIcon.Information);
        }
    }
}
