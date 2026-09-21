using System;
using System.Collections.Generic;
using System.Linq;
using TTSK_AutoDim_Plates;
using P=TTSK_AutoDim_Plates.PHU_SectionLayoutPlanner;

class Program
{
    static int count;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
    static P.Item Item(double w,double h) {return new P.Item{Width=w,Height=h};}
    static void Main(string[] args)
    {
        if((args.Length==2||args.Length==3) && args[0]=="--portable-audit")
        {
            var asm=System.Reflection.Assembly.LoadFrom(System.IO.Path.GetFullPath(args[1]));
            // LoadFrom targets live outside this test EXE's probing directory.
            // Remoting resolves the reader by assembly identity when unwrapping it.
            AppDomain.CurrentDomain.AssemblyResolve+=(sender,resolve)=>
                new System.Reflection.AssemblyName(resolve.Name).Name==asm.GetName().Name?asm:null;
            var method=asm.GetType("TTSK_AutoDim_Plates.PHU_ArrangeView").GetMethod("AuditCurrentLayout");
            double gap=args.Length==3?Double.Parse(args[2],System.Globalization.CultureInfo.InvariantCulture):15d;
            Console.WriteLine(method.Invoke(null,new object[]{true,gap,false}));return;
        }
        if(args.Length>0 && args[0]=="--audit")
        {
            var model=new Tekla.Structures.Model.Model();
            Console.WriteLine("Model connected="+model.GetConnectionStatus());
            if(!model.GetConnectionStatus())throw new Exception("No model connection");
            var gaps=args.Length>1?args.Skip(1).Select(s=>Double.Parse(s,System.Globalization.CultureInfo.InvariantCulture)).ToArray():new[]{0d,5,15,30};
            foreach(double gap in gaps)foreach(bool horizontal in new[]{true,false})
                Console.WriteLine(PHU_ArrangeView.AuditCurrentLayout(horizontal,gap,false));
            return;
        }
        var paper=new P.Rect(15,15,826,579);
        PresentationTests.Run(Check);
        GapTests.Run(Check);
        ReadingOrderTests.Run(Check);
        var main=new P.Rect(121,214,655,502);
        var blocked=new List<P.Rect>{main,new P.Rect(700,55,820,265),new P.Rect(0,0,841,55)};
        var few=new[]{Item(73,66),Item(71,81),Item(89,63)};
        foreach(double gap in new[]{0d,1,5,15})
        {
            var p=P.Build(paper,few,blocked,main,gap,true,false);
            Check(p.Success,"few sections fit");
            Check(Math.Abs((p.Places.First().Left+p.Places.Last().Right)/2-paper.CenterX)<.01,"small gap remains centered");
            Check(P.Validate(paper,few,blocked,p.Places,gap),"few sections safe");
        }
        var lots=Enumerable.Range(0,50).Select(i=>Item(20,20)).ToArray();
        var smallPaper=new P.Rect(0,0,220,180);var smallMain=new P.Rect(40,90,150,150);
        var obstacles=new[]{smallMain,new P.Rect(160,70,220,120)};
        foreach(bool horizontal in new[]{true,false})foreach(bool up in new[]{true,false})
        {
            var p=P.Build(smallPaper,lots,obstacles,smallMain,2,horizontal,up);
            Check(p.Success,"50 sections wrap after row and right side fill");
            Check(P.ValidatePlan(smallPaper,lots,obstacles,smallMain,p),"many sections safe");
            Check(p.Places.Select(r=>Math.Round(r.Bottom)).Distinct().Count()>2,"multiple rows");
        }
        foreach(var grid in new[]{new P.Rect(550,40,825,320),new P.Rect(20,20,320,175),new P.Rect(300,70,550,210)})
        {
            var obstacles2=new[]{main,grid};var p=P.Build(paper,few,obstacles2,main,5,true,false);
            Check(p.Success&&P.ValidatePlan(paper,few,obstacles2,main,p),"resized relocated grid");
        }
        Check(!P.Build(paper,new[]{Item(900,100)},blocked,main,5,true,false).Success,"oversized rejects");
        var impossible=P.Build(smallPaper,Enumerable.Range(0,200).Select(i=>Item(50,50)).ToArray(),obstacles,smallMain,10,true,false);
        Check(!impossible.Success&&impossible.Places.Count==0,"impossible leaves no partial plan");
        Check(!P.Build(paper,few,blocked,main,Double.NaN,true,false).Success,"invalid gap");
        Check(!P.Build(paper,new[]{Item(Double.PositiveInfinity,10)},blocked,main,0,true,false).Success,"invalid extent");
        var none=new P.Rect[0];var solo=P.Build(paper,new[]{Item(50,50)},none,null,0,true,false);
        Check(solo.Success&&Math.Abs(solo.Places[0].CenterX-paper.CenterX)<.01,"no main centers");
        var random=new Random(7321);int successes=0;
        var many=Enumerable.Range(0,200).Select(i=>Item(20+(i%3),20+(i%5))).ToArray();
        var largePaper=new P.Rect(0,0,841,594);var largeMain=new P.Rect(100,250,500,470);
        var largeObstacles=new[]{largeMain,new P.Rect(650,30,820,350)};
        var largePlan=P.Build(largePaper,many,largeObstacles,largeMain,2,true,false);
        Check(largePlan.Success&&P.ValidatePlan(largePaper,many,largeObstacles,largeMain,largePlan),"200 mixed sections fit without count/column limit");
        for(int run=0;run<400;run++)
        {
            var sheet=new P.Rect(0,0,200+random.Next(500),200+random.Next(350));
            var m=new P.Rect(sheet.Width*.2,sheet.Height*.5,sheet.Width*.65,sheet.Height*.8);
            var grid=new P.Rect(sheet.Width*.72,10,sheet.Width-5,20+random.Next((int)(sheet.Height*.65)));
            var obs=new[]{m,grid};var items=Enumerable.Range(0,1+random.Next(35)).Select(i=>Item(10+random.Next(65),10+random.Next(65))).ToArray();
            double gap=random.Next(16);bool horiz=run%2==0,up=run%3==0;
            var p=P.Build(sheet,items,obs,m,gap,horiz,up);
            if(!p.Success){Check(p.Places.Count==0&&p.TriedZeroGap&&p.GlobalGap==0,"random failure atomic after zero GAP");continue;}
            successes++;Check(P.ValidatePlan(sheet,items,obs,m,p),"random layout valid");
            Check(p.GlobalGap<=gap&&p.RightGap<=gap,"automatic GAP only decreases");
            var again=P.Build(sheet,items,obs.Reverse().ToArray(),m,gap,horiz,up);
            Check(again.Success&&again.Places.Select(r=>r.ToString()).SequenceEqual(p.Places.Select(r=>r.ToString())),"stable obstacle ordering/idempotent plan");
        }
        Console.WriteLine("PASS "+count+" assertions; randomized successful layouts="+successes+"/400");
    }
}
