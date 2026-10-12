using System;
using System.Collections.Generic;
using System.Numerics;
namespace ALPregnancy;

// Rest-space material correction across mixed torso/skirt ownership. Native
// weights describe animation, not the location of a fabric displacement seam.
internal static class SkirtTransition
{
    readonly record struct Point(int X,int Y,int Z);
    readonly record struct Edge(int A,int B);
    internal static void Solve(Vector3[] original,Vector3[] direct,Vector3[] carried,
        bool[] mixed,int[] triangles,float span)
    {
        if(!Array.Exists(mixed, value=>value))return;
        int count=original.Length;float epsilon=MathF.Max(span*1e-6f,1e-9f);
        var map=new Dictionary<Point,int>();var group=new int[count];
        var positions=new List<Vector3>();var correction=new List<Vector3>();
        var members=new List<int>();var movable=new List<bool>();
        for(int i=0;i<count;i++)
        {
            var p=original[i];var key=new Point((int)Math.Round(p.X/epsilon),(int)Math.Round(p.Y/epsilon),(int)Math.Round(p.Z/epsilon));
            if(!map.TryGetValue(key,out int g))
            {g=positions.Count;map.Add(key,g);positions.Add(Vector3.Zero);correction.Add(Vector3.Zero);members.Add(0);movable.Add(true);}
            group[i]=g;positions[g]+=p;correction[g]+=carried[i]-direct[i];members[g]++;movable[g]&=mixed[i];
        }
        int n=positions.Count;var neighbors=new List<int>[n];
        for(int g=0;g<n;g++){positions[g]/=members[g];correction[g]/=members[g];neighbors[g]=new List<int>();}
        var edges=new HashSet<Edge>();
        for(int k=0;k+2<triangles.Length;k+=3)
        for(int j=0;j<3;j++)
        {
            int a=group[triangles[k+j]],b=group[triangles[k+(j+1)%3]];if(a==b)continue;
            var edge=new Edge(Math.Min(a,b),Math.Max(a,b));
            if(edges.Add(edge)){neighbors[a].Add(b);neighbors[b].Add(a);}
        }
        // A disconnected ornament with no fixed boundary has no interpolation
        // problem to solve. Keep its original carried result, instead of drifting.
        var anchored=new bool[n];var queue=new Queue<int>();
        for(int g=0;g<n;g++)if(!movable[g]){anchored[g]=true;queue.Enqueue(g);}
        while(queue.Count>0)
        {int g=queue.Dequeue();foreach(int j in neighbors[g])if(!anchored[j]){anchored[j]=true;queue.Enqueue(j);}}
        var ids=new List<int>();var weights=new float[n][];var diagonal=new float[n];
        for(int g=0;g<n;g++)
        {
            movable[g]&=anchored[g] && neighbors[g].Count>0;if(movable[g])ids.Add(g);
            weights[g]=new float[neighbors[g].Count];
            for(int k=0;k<neighbors[g].Count;k++)
            {float w=1/MathF.Max(Vector3.Distance(positions[g],positions[neighbors[g][k]]),epsilon);weights[g][k]=w;diagonal[g]+=w;}
        }
        if(ids.Count==0)return;
        // Jacobi-preconditioned conjugate gradients on the positive graph
        // Laplacian. All three correction coordinates share the same operator.
        var x=correction.ToArray();var residual=new Vector3[n];var direction=new Vector3[n];var product=new Vector3[n];
        double rho=0;
        foreach(int g in ids)
        {
            var sum=Vector3.Zero;
            for(int k=0;k<neighbors[g].Count;k++)sum+=(x[neighbors[g][k]]-x[g])*weights[g][k];
            residual[g]=sum;direction[g]=sum/diagonal[g];rho+=Vector3.Dot(sum,direction[g]);
        }
        double tolerance=span*span*1e-12; // Sub-micrometre relative material error.
        for(int iteration=0;iteration<ids.Count && rho>0;iteration++)
        {
            double denominator=0;
            foreach(int g in ids)
            {
                var sum=direction[g]*diagonal[g];
                for(int k=0;k<neighbors[g].Count;k++)if(movable[neighbors[g][k]])sum-=direction[neighbors[g][k]]*weights[g][k];
                product[g]=sum;denominator+=Vector3.Dot(direction[g],sum);
            }
            if(denominator<=0)break;
            float step=(float)(rho/denominator);double nextRho=0;float error=0;
            foreach(int g in ids)
            {
                x[g]+=direction[g]*step;residual[g]-=product[g]*step;
                var preconditioned=residual[g]/diagonal[g];nextRho+=Vector3.Dot(residual[g],preconditioned);
                error=MathF.Max(error,preconditioned.LengthSquared());
            }
            if(error<=tolerance)break;
            float beta=(float)(nextRho/rho);
            foreach(int g in ids)direction[g]=residual[g]/diagonal[g]+direction[g]*beta;
            rho=nextRho;
        }
        for(int i=0;i<count;i++)if(mixed[i] && movable[group[i]])carried[i]=direct[i]+x[group[i]];
    }
}
