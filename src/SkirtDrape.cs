using System;
using System.Numerics;

namespace ALPregnancy;

// Rest-space offsets only. Pose rotations always come from the game's solver.
internal static class SkirtDrape
{
    internal static void Resolve(int[] parents, Vector3[] sampled, bool[] skirt,
        float tolerance, out Vector3[] offsets, out bool[] released)
    {
        int count=sampled.Length;
        var result=(Vector3[])sampled.Clone();
        var free=new bool[count];var state=new byte[count];
        // Release the last supported bone, one joint before the first decrease.
        // Inspect each native chain independently, before carrying any offsets.
        for(int i=0;i<count;i++)
        {
            int parent=parents[i];
            if(skirt[i] && parent>=0 && parent<count && skirt[parent] &&
                sampled[parent].Length()>tolerance &&
                sampled[i].Length()+tolerance<sampled[parent].Length())
                free[parent]=true;
        }
        void Visit(int i)
        {
            if(state[i]==2)return;
            if(state[i]==1)throw new ArgumentException("Cyclic skirt hierarchy.");
            state[i]=1;
            int parent=parents[i];
            if(skirt[i] && parent>=0 && parent<count && skirt[parent])
            {
                Visit(parent);
                // Descendants keep this chain's last supported displacement.
                if(free[parent])
                {result[i]=result[parent];free[i]=true;}
            }
            state[i]=2;
        }
        for(int i=0;i<count;i++)Visit(i);
        offsets=result;released=free;
    }
}
