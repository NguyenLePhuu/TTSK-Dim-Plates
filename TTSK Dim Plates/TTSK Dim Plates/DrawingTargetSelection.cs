using System;
using System.Collections.Generic;

namespace TTSK_AutoDim_Plates
{
    internal static class DrawingTargetSelection
    {
        internal sealed class Result<T>
        {
            internal readonly List<T> Drawings = new List<T>();
            internal string Source;
            internal bool IsActive;
        }

        internal static Result<T> Resolve<T>(bool preferActive, T active,
            Func<IList<T>> rows, Func<IList<T>> documentManager, Func<T,T,bool> same) where T : class
        {
            var result=new Result<T>();
            if(preferActive && active!=null)
            {result.Drawings.Add(active);result.Source="bản vẽ đang mở";result.IsActive=true;return result;}
            var selected=rows();
            result.Source="dòng đang chọn trong danh sách";
            if(selected.Count==0){selected=documentManager();result.Source="Document Manager";}
            foreach(var drawing in selected)
                if(drawing!=null&&!result.Drawings.Exists(existing=>same(existing,drawing)))result.Drawings.Add(drawing);
            if(result.Drawings.Count==0&&active!=null)
            {result.Drawings.Add(active);result.Source="bản vẽ đang mở";result.IsActive=true;}
            return result;
        }
    }
}
