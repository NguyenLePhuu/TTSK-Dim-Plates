using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Tekla.Technology.Akit.UserScript;

static class QuickCheckUiTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Set(object form,string name,object value){form.GetType().GetField(name,Flags).SetValue(form,value);}
    static object Get(object form,string name){return form.GetType().GetField(name,Flags).GetValue(form);}
    static void Call(object form,string name){form.GetType().GetMethod(name,Flags).Invoke(form,null);}
    static void Key(object form,Keys key){form.GetType().GetMethod("ProcessCmdKey",Flags).Invoke(form,new object[]{new Message(),key});}
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        foreach(bool dark in new[]{false,true})
        using(var form=new PHU_DimensionReportForm(new PHU_DimensionCheck.Result(),dark,
            ()=>new PHU_DimensionCheck.Result {Drawing="New active drawing",Dimensions=10,Views=2}))
        {
            RevisionTests.ShowPreview(form);Application.DoEvents();Call(form,"OpenRevision");
            var result=new PHU_DimensionRevision.Result {Compared=12,Unchanged=12};
            Set(form,"revisionResult",result);
            Call(form,"UpdateRevisionSuccess");
            var tick=(Control)Get(form,"revisionSuccessCheck");
            if(tick.Visible)throw new Exception("Idle revision showed success");
            Set(form,"revisionCompleted",true);Call(form,"UpdateRevisionSummary");
            if(!tick.Visible)throw new Exception("Completed unchanged revision has no tick");
            using(var bitmap=new Bitmap(form.Width,form.Height))
            {form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,dark?"revision-dark.png":"revision-light.png"));}
            result.Warnings.Add("Incomplete coverage");Call(form,"UpdateRevisionSuccess");
            if(tick.Visible)throw new Exception("Warning showed success");
            result.Warnings.Clear();result.Items.Add(new PHU_DimensionRevision.Item {Changed=true});
            Set(form,"revisionFilterIndex",4);Call(form,"UpdateRevisionSuccess");
            if(tick.Visible)throw new Exception("Filtered changes showed success");
            Set(form,"checking",true);Key(form,Keys.C);Key(form,Keys.R);
            if(Get(form,"revisionPanel")==null)throw new Exception("Busy shortcut changed screens");
            Set(form,"checking",false);Key(form,Keys.C);Application.DoEvents();
            if(Get(form,"revisionPanel")!=null||((PHU_DimensionCheck.Result)Get(form,"result")).Drawing!="New active drawing")
                throw new Exception("C did not switch and recheck current drawing");
            Call(form,"OpenRevision");
            if(Get(form,"revisionResult")!=null)throw new Exception("Reopened revision retained stale result");
        }
        Console.WriteLine("PASS quick check shortcuts and Revision success states, both themes.");
    }
}
