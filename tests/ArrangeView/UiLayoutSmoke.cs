using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

static class UiLayoutSmoke
{
    internal static void Run(string executable,string imagePath)
    {
        var asm=Assembly.LoadFrom(Path.GetFullPath(executable));
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
            string p=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable)),new AssemblyName(e.Name).Name+".dll");
            return File.Exists(p)?Assembly.LoadFrom(p):null;
        };
        using(var form=(Form)Activator.CreateInstance(asm.GetType("TTSK_AutoDim_Plates.MainForm",true)))
        {
            var type=form.GetType();const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var panel=(Panel)type.GetField("slideArrangePanel",flags).GetValue(form);
            var combo=(Control)type.GetField("cboArrangeLayout",flags).GetValue(form);
            if((int)combo.GetType().GetProperty("SelectedIndex").GetValue(combo,null)!=0)throw new Exception("Legacy not default");
            foreach(bool horizontal in new[]{true,false})
            {
                type.GetField("arrangeSectionHorizontal",flags).SetValue(form,horizontal);
                type.GetMethod("ApplyArrangeOptionStyles",flags).Invoke(form,null);
                if(combo.Enabled==horizontal)throw new Exception("Mode enablement incorrect");
            }
            foreach(Control other in panel.Controls)
                if(other!=combo && other.Bounds.IntersectsWith(combo.Bounds) && other.Height>0)
                    throw new Exception("Mode selector overlaps control: "+other.Text);
            var indexProperty=combo.GetType().GetProperty("SelectedIndex");
            var click=combo.GetType().GetMethod("OnMouseDown",flags);
            click.Invoke(combo,new object[]{new MouseEventArgs(MouseButtons.Left,1,combo.Width-8,20,0)});
            if((int)indexProperty.GetValue(combo,null)!=1)throw new Exception("Right icon does not select grid");
            click.Invoke(combo,new object[]{new MouseEventArgs(MouseButtons.Left,1,8,20,0)});
            if((int)indexProperty.GetValue(combo,null)!=0)throw new Exception("Left icon does not restore legacy");
            combo.GetType().GetMethod("OnKeyDown",flags).Invoke(combo,new object[]{new KeyEventArgs(Keys.Right)});
            if((int)indexProperty.GetValue(combo,null)!=1)throw new Exception("Keyboard grid selection failed");
            combo.GetType().GetMethod("OnKeyDown",flags).Invoke(combo,new object[]{new KeyEventArgs(Keys.Left)});
            if((int)indexProperty.GetValue(combo,null)!=0)throw new Exception("Keyboard legacy selection failed");
            combo.Enabled=false;
            click.Invoke(combo,new object[]{new MouseEventArgs(MouseButtons.Left,1,combo.Width-8,20,0)});
            if((int)indexProperty.GetValue(combo,null)!=0)throw new Exception("Disabled icons changed mode");
            combo.Enabled=true;
            using(var host=new Form())
            {
                panel.Parent.Controls.Remove(panel);host.Controls.Add(panel);panel.Location=new Point(0,0);panel.Visible=true;
                host.ClientSize=panel.Size;host.StartPosition=FormStartPosition.Manual;
                host.Location=new Point(-10000,-10000);host.ShowInTaskbar=false;
                host.Show();Application.DoEvents();panel.CreateControl();
                using(var bitmap=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bitmap,panel.ClientRectangle);bitmap.Save(imagePath);}
                combo.GetType().GetProperty("SelectedIndex").SetValue(combo,1,null);
                foreach(bool dark in new[]{false,true})
                {
                    type.GetField("_darkMode",flags).SetValue(form,dark);
                    type.GetMethod("ApplyTheme",flags).Invoke(form,null);
                    if((int)combo.GetType().GetProperty("SelectedIndex").GetValue(combo,null)!=1)throw new Exception("Theme reset layout mode");
                    panel.Visible=true;Application.DoEvents();
                    using(var bitmap=new Bitmap(panel.Width,panel.Height)) {
                        panel.DrawToBitmap(bitmap,panel.ClientRectangle);
                        bitmap.Save(Path.Combine(Path.GetDirectoryName(imagePath),dark?"grid-ui-dark.png":"grid-ui-light.png"));
                    }
                }
            }
            Console.WriteLine("PASS UI: legacy default, two selectable modes, no control overlap");
        }
    }
}
