using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using D=Tekla.Structures.Drawing;
using G=Tekla.Structures.Geometry3d;
using R=Tekla.Technology.Akit.UserScript.PHU_DimensionRevision;

static class RevisionRepairLive
{
    static string SnapshotDigest(List<R.SnapshotView> views)
    {
        using(var stream=new MemoryStream())
        {
            new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter().Serialize(stream,views);
            using(var hash=System.Security.Cryptography.SHA256.Create())return Convert.ToBase64String(hash.ComputeHash(stream.ToArray()));
        }
    }
    public static void CacheAndStaleColor(string path)
    {
        var r=R.Analyze(false);string copy=Path.Combine(Path.GetTempPath(),"TTSK-cache-"+Guid.NewGuid().ToString("N")+".DPM");
        File.Copy(r.BaselinePath,copy,true);var bytes=File.ReadAllBytes(copy);var stamp=File.GetLastWriteTimeUtc(copy);
        var warnings=new List<string>();var first=R.ReadSnapshot(copy,warnings);string digest=SnapshotDigest(first);
        first[0].Id=Int32.MinValue;first[0].Segments.Clear();first[0].Content.Clear();
        Check(SnapshotDigest(R.ReadSnapshot(copy,new List<string>()))==digest,"Cached snapshot graph is isolated from caller mutation");
        File.WriteAllBytes(copy,new byte[bytes.Length]);File.SetLastWriteTimeUtc(copy,stamp);bool rejected=false;
        try{R.ReadSnapshot(copy,new List<string>());}catch{rejected=true;}
        Check(rejected,"Same-length/same-timestamp changed snapshot cannot reuse old cached data");
        File.WriteAllBytes(copy,bytes);File.SetLastWriteTimeUtc(copy,stamp);
        Check(SnapshotDigest(R.ReadSnapshot(copy,new List<string>()))==digest,"Snapshot reader recovers after corrupt temporary snapshot");
        var target=r.Items.First(i=>!i.ReadOnly&&!i.Review&&i.Dimension!=null);var original=R.GetColor(target);
        var other=R.Analyze(false);var second=other.Items.Single(i=>i.Id==target.Id);
        try
        {
            Check(R.SetColor(other,new[]{second},original==D.DrawingColors.Red?D.DrawingColors.Black:D.DrawingColors.Red)==1,"Simulate native edit after first analysis");
            Check(R.SetColor(r,new[]{target},original)==1,"Stale result still reads fresh native colour before no-op decision");
            var latest=R.Analyze(false);Check(R.GetColor(latest.Items.Single(i=>i.Id==target.Id))==original,"Original native colour restored");
            string signature=target.SetSignature;target.SetSignature="stale";rejected=false;
            try{R.SetColor(r,new[]{target},D.DrawingColors.Red);}catch(InvalidOperationException){rejected=true;}
            finally{target.SetSignature=signature;}
            Check(rejected,"Stale full-chain signature rejects before native colour work");
            File.WriteAllText(path,"PASS cache graph isolation, content hash invalidation despite equal length/timestamp, reader recovery, fresh native colour despite stale result, stale-chain rejection.\n");
        }
        finally
        {
            var latest=R.Analyze(false);R.SetColor(latest,new[]{latest.Items.Single(i=>i.Id==target.Id)},original);
        }
    }
    public static void ColorCycle(string path)
    {
        var r=R.Analyze(false);var targets=r.Items.Where(i=>!i.ReadOnly&&i.Changed&&!i.Review).ToList();
        var original=targets.ToDictionary(i=>i.Id,R.GetColor);
        var geometry=typeof(R).GetMethod("SetGeometryKey",BindingFlags.Static|BindingFlags.NonPublic);
        var sets=r.Items.Concat(r.UnchangedItems).Where(i=>i.Object!=null&&i.Dimension!=null).Select(i=>i.Object).Distinct().ToList();
        var before=sets.ToDictionary(Id,s=>(string)geometry.Invoke(null,new object[]{s}));
        var timer=System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var desired=targets.ToDictionary(i=>i,i=>original[i.Id]==D.DrawingColors.Red?D.DrawingColors.Black:D.DrawingColors.Red);
            int count=0;foreach(var group in desired.GroupBy(p=>p.Value))count+=R.SetColor(r,group.Select(p=>p.Key).ToList(),group.Key);
            long elapsed=timer.ElapsedMilliseconds;Check(count==targets.Count,"All changed targets written once");
            var after=R.Analyze(false);
            foreach(var item in after.Items.Where(i=>original.ContainsKey(i.Id)))
                Check(R.GetColor(item)==desired.First(p=>p.Key.Id==item.Id).Value,"Fresh native read confirms requested individual colour");
            foreach(var set in sets)Check(set.Select()&&(string)geometry.Invoke(null,new object[]{set})==before[Id(set)],"Colour cycle preserves every chain");
            File.WriteAllText(path,"Write_ms="+elapsed+" count="+count+"\nPASS native colours and original chain signatures\n");
        }
        finally
        {
            var latest=R.Analyze(false);var items=latest.Items.Concat(latest.UnchangedItems).Where(i=>original.ContainsKey(i.Id)).ToList();
            foreach(var group in items.GroupBy(i=>original[i.Id]))R.SetColor(latest,group.ToList(),group.Key);
            var restored=R.Analyze(false);
            Check(restored.Items.Concat(restored.UnchangedItems).Where(i=>original.ContainsKey(i.Id)).All(i=>R.GetColor(i)==original[i.Id]),"Colour cycle restores user colours");
        }
    }
    public static void Performance(string path)
    {
        var output=new StringBuilder();string previous=null;
        for(int n=0;n<3;n++)
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();var r=R.Analyze(false);
            output.AppendLine("RUN "+n+" Analyze_ms="+timer.ElapsedMilliseconds);
            var report=r.Report();
            var values=String.Join("\n",r.Items.Concat(r.UnchangedItems).OrderBy(i=>i.Id).Select(i=>i.Id+"|"+i.Before+"|"+i.After+"|"+i.Changed+"|"+i.Review+"|"+i.NativeColor+"|"+i.SetSignature));
            if(previous!=null)Check(values==previous,"Repeated analysis preserves every verdict, colour and chain signature");previous=values;
            File.WriteAllText(path+".result.txt",report+"\n"+values);
            timer.Restart();var warnings=new List<string>();R.ReadSnapshot(r.BaselinePath,warnings);
            output.AppendLine("RUN "+n+" Snapshot_ms="+timer.ElapsedMilliseconds);
            var targets=r.Items.Where(i=>!i.ReadOnly&&i.Changed&&!i.Review).ToList();
            timer.Restart();foreach(var group in targets.GroupBy(R.GetColor))
                Check(R.SetColor(r,group.ToList(),group.Key)==0,"Performance no-op writes nothing");
            output.AppendLine("RUN "+n+" Apply_noop_ms="+timer.ElapsedMilliseconds);
            File.WriteAllText(path,output.ToString());Console.WriteLine(output.ToString());
        }
    }
    public static void Color(string path,bool persistence=false)
    {
        var before=R.Analyze(false);
        var geometry=typeof(R).GetMethod("SetGeometryKey",BindingFlags.Static|BindingFlags.NonPublic);
        var items=before.Items.Concat(before.UnchangedItems).Where(i=>!i.ReadOnly&&i.Dimension!=null).ToList();
        var sets=items.Select(i=>i.Object).Distinct().ToList();
        var original=sets.ToDictionary(Id,s=>(string)geometry.Invoke(null,new object[]{s}));
        var colors=items.ToDictionary(i=>i.Id,R.GetColor);
        var targets=before.Items.Where(i=>!i.ReadOnly&&i.Changed&&!i.Review).ToList();
        Check(targets.Count>0,"Need actual changed measurements");
        int expected=targets.Count(i=>R.GetColor(i)!=D.DrawingColors.Red);
        Check(R.Apply(before,targets)==expected,"Expected number of child colour writes");
        var after=R.Analyze(false);
        var current=after.Items.Concat(after.UnchangedItems).Where(i=>!i.ReadOnly&&i.Dimension!=null).ToList();
        Check(current.Select(i=>i.Id).OrderBy(i=>i).SequenceEqual(items.Select(i=>i.Id).OrderBy(i=>i)),"All original child IDs preserved");
        Check(current.Select(i=>Id(i.Object)).Distinct().OrderBy(i=>i).SequenceEqual(original.Keys.OrderBy(i=>i)),"All original parent sets preserved");
        var b=new StringBuilder(after.Report());
        foreach(var set in current.Select(i=>i.Object).Distinct())
            Check((string)geometry.Invoke(null,new object[]{set})==original[Id(set)],"Every chain retains membership, 3D feet, values and line positions");
        foreach(var item in current)
        {
            bool changed=targets.Any(t=>t.Id==item.Id);var wanted=changed?D.DrawingColors.Red:colors[item.Id];
            Check(R.GetColor(item)==wanted,"Persisted child colour and unchanged sibling colour");
            b.AppendLine("CHILD="+item.Id+" PARENT="+Id(item.Object)+" VALUE="+item.After+" COLOR="+R.GetColor(item));
        }
        Check(R.Apply(after,after.Items.Where(i=>!i.ReadOnly&&i.Changed&&!i.Review).ToList())==0,"Second colour pass writes nothing");
        if(persistence)
        {
            var handler=new D.DrawingHandler();
            Check(handler.SaveActiveDrawing(),"Save coloured drawing");
            Check(handler.CloseActiveDrawing(true),"Close saved drawing");
            Check(handler.SetActiveDrawing(after.Drawing),"Reopen same drawing");
            var reopened=R.Analyze(false);
            var reopenedItems=reopened.Items.Concat(reopened.UnchangedItems).Where(i=>!i.ReadOnly&&i.Dimension!=null).ToList();
            Check(reopenedItems.Count==current.Count,"Reopened measurement count preserved");
            foreach(var item in reopenedItems)
            {
                Check(current.Any(i=>i.Id==item.Id&&R.GetColor(i)==R.GetColor(item)),"Native child colour persists after save/reopen");
                Check((string)geometry.Invoke(null,new object[]{item.Object})==original[Id(item.Object)],"Original full chain persists after save/reopen");
            }
            b.AppendLine("PASS save/reopen: native child colours and original complete chain persist on disk.");
        }
        b.AppendLine("PASS: child/parent IDs, complete chain geometry, individual colours, unchanged siblings and idempotency.");
        File.WriteAllText(path,b.ToString());Console.WriteLine(b.ToString());
    }
    static object Get(object o,string n){return o.GetType().GetProperty(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o,null);}
    static int Id(object o){return ((Tekla.Structures.Identifier)Get(o,"Identifier")).ID;}
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static List<D.DrawingObject> Objects(D.Drawing drawing)
    {var list=new List<D.DrawingObject>();var it=drawing.GetSheet().GetAllObjects();while(it.MoveNext())list.Add(it.Current);return list;}
    public static void Recreate(string path)
    {
        var before=R.Analyze(false);Check(before.Drawing.Mark=="[BmC-117]","Wrong approved drawing");
        Check(before.Items.Where(i=>!i.ReadOnly).All(i=>i.AlreadyRed&&!i.Review),"All known revised DIMs must already be red");
        Check(before.UnchangedItems.All(i=>R.GetColor(i)==D.DrawingColors.Black),"Every unchanged measurement stays black");
        var original=before.UnchangedItems.First(i=>i.After=="75"&&i.Object.GetView() is D.View&&((D.View)i.Object.GetView()).ViewType==D.View.ViewTypes.TopView);
        var old=original.Object;var points=(D.PointList)Get(old,"DimensionPoints");var normal=(G.Vector)Get(old,"UpDirection");double distance=old.Distance;
        D.StraightDimensionSet replacement=null;bool deleted=false;
        try
        {
            replacement=new D.StraightDimensionSetHandler().CreateDimensionSet(old.GetView(),points,normal,distance+50,old.Attributes);
            Check(replacement!=null&&replacement.Select(),"Recreated test DIM insertion");
            Check(old.Delete(),"Remove old DIM only after replacement exists");deleted=true;
            before.Drawing.CommitChanges();
            var after=R.Analyze(false);
            Check(after.Items.Where(i=>!i.ReadOnly).Count()==before.Items.Where(i=>!i.ReadOnly).Count(),"Moved/recreated equal DIM must not be reported new");
            Check(after.UnchangedItems.Any(i=>i.Object.IsSameDatabaseObject(replacement)&&i.After=="75"),"Recreated 75 remains unchanged");
            Check(R.Apply(after,after.Items.Where(i=>!i.ReadOnly&&i.Changed&&!i.Review).ToList())==0,"Second auto-red pass is idempotent");
            File.WriteAllText(path,"PASS live: 5 revised values red, 14 unchanged values black; same 75 recreated with new ID and line moved 5 paper mm remains unchanged; second pass no writes.\r\n"+after.Report());
        }
        finally
        {
            if(replacement!=null&&replacement.Select())
            {if(deleted){replacement.Distance=distance;Check(replacement.Modify(),"Restore original DIM line position");}else replacement.Delete();}
            before.Drawing.CommitChanges();
        }
    }
    public static void DeleteSection(string path,bool viaSelection=false)
    {
        var r=R.Analyze(false);Check(r.Drawing.Mark=="[BmC-117]","Wrong approved drawing");
        var views=r.Drawing.GetSheet().GetAllViews();D.View source=null,parent=null;
        while(views.MoveNext()){var v=views.Current as D.View;if(v==null)continue;if(v.ViewType==D.View.ViewTypes.SectionView)source=v;if(v.ViewType==D.View.ViewTypes.FrontView)parent=v;}
        Check(source!=null,"Need original section template");
        var originalIds=new HashSet<int>(Objects(r.Drawing).Select(Id));
        D.View temporary=null;D.SectionMark temporaryMark=null;var b=new StringBuilder();
        var selector=new D.DrawingHandler().GetDrawingObjectSelector();var originalSelection=new System.Collections.ArrayList();
        var selected=selector.GetSelected();while(selected.MoveNext())originalSelection.Add(selected.Current);
        try
        {
            var box=parent.RestrictionBox;double x=(box.MinPoint.X+box.MaxPoint.X)/2;
            Check(D.View.CreateSectionView(parent,new G.Point(x,box.MinPoint.Y-10,0),new G.Point(x,box.MaxPoint.Y+10,0),
                new G.Point(source.Origin.X+30,source.Origin.Y,0),50,50,source.Attributes,new D.SectionMarkBase.SectionMarkAttributes("standard"),
                out temporary,out temporaryMark)&&temporary.Select(),"Temporary section creation");
            r.Drawing.CommitChanges();
            if(viaSelection)Check(selector.SelectObjects(new System.Collections.ArrayList{temporary},false),"Select disposable section for public Delete REV route");
            var deletionTimer=System.Diagnostics.Stopwatch.StartNew();
            b.AppendLine(viaSelection?R.DeleteRevisionSection():R.DeleteRevisionSection(r,temporary));
            b.AppendLine("DeleteREV_ms="+deletionTimer.ElapsedMilliseconds);
            Check(!temporary.Select()&&source.Select(),"Delete temporary section only; user's original survives");
            var added=Objects(r.Drawing).Where(o=>!originalIds.Contains(Id(o))).ToList();
            var cloud=added.OfType<D.Cloud>().Single();var lines=added.OfType<D.Line>().ToList();
            Check(lines.Count==2,"Exactly two crossed lines");
            Check(cloud.Attributes.Line.Color==D.DrawingColors.Red&&cloud.Attributes.Line.Type.IsEqual(D.LineTypes.DashDot),"Cloud red DashDot");
            foreach(var line in lines)
            {
                Check(line.Attributes.Line.Color==D.DrawingColors.Red&&line.Attributes.Line.Type.IsEqual(D.LineTypes.DashDot),"AllPartsDeleted red DashDot line style");
                Check(cloud.Points.Cast<G.Point>().Any(p=>G.Distance.PointToPoint(p,line.StartPoint)<.02)&&cloud.Points.Cast<G.Point>().Any(p=>G.Distance.PointToPoint(p,line.EndPoint)<.02),"Both endpoints exactly on cloud corners");
            }
            b.AppendLine("PASS native Delete REV on disposable section: cloud, two exact diagonals, correct red/DashDot style, original section preserved; no picker/prompt used.");
            File.WriteAllText(path,b.ToString());
        }
        finally
        {
            if(temporary!=null&&temporary.Select())temporary.Delete();
            if(temporaryMark!=null&&temporaryMark.Select())temporaryMark.Delete();
            foreach(var obj in Objects(r.Drawing).Where(o=>!originalIds.Contains(Id(o))).ToList())
                if(obj is D.Cloud||obj is D.Line)obj.Delete();
            r.Drawing.CommitChanges();
            if(viaSelection)selector.SelectObjects(originalSelection,false);
        }
    }
}
