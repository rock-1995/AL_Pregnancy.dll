namespace ALPregnancy;

internal static class VirtualWeights
{
    internal readonly record struct Influence(int Bone,float Weight);
    // Bone=-1 is pure virtual. Other slots fuse one original bone with virtual.
    internal readonly record struct Recipe(int Bone,float VirtualShare);
    internal static Influence[] Blend(IReadOnlyList<Influence> original,float alpha,Func<Recipe,int> slot)
    {
        alpha=Math.Clamp(alpha,0,1);
        if(alpha<=0)return original.ToArray();
        if(alpha>=1)return new[]{new Influence(slot(new Recipe(-1,1)),1)};
        var native=original.Where(x=>x.Weight>0).GroupBy(x=>x.Bone).Select(g=>new Influence(g.Key,g.Sum(x=>x.Weight)))
            .OrderByDescending(x=>x.Weight).ThenBy(x=>x.Bone).ToArray();
        var result=new List<Influence>();
        if(native.Length<4)
        {
            result.AddRange(native.Select(x=>x with {Weight=x.Weight*(1-alpha)}));
            result.Add(new Influence(slot(new Recipe(-1,1)),alpha));
        }
        else
        {
            result.AddRange(native.Take(3).Select(x=>x with {Weight=x.Weight*(1-alpha)}));
            float weak=native[3].Weight*(1-alpha),combined=weak+alpha;
            result.Add(new Influence(slot(new Recipe(native[3].Bone,alpha/combined)),combined));
        }
        return result.OrderByDescending(x=>x.Weight).ThenBy(x=>x.Bone).ToArray();
    }
}
