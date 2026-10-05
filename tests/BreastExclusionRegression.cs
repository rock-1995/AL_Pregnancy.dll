using ALPregnancy;
using System.Numerics;

internal static class BreastExclusionRegression
{
    internal static void Run(Action<string,bool> check)
    {
        var mask=BreastExclusion.Build(new[]{0f,.00001f,.2f,1f,0f,0f},6,new[]{0,1,2,3,1,5});
        check("Weak lower-breast bone ownership is fully excluded",mask[1] && mask[2] && mask[3]);
        check("Exclusion shares UV welds without expanding into neighboring abdomen",mask[4] && !mask[0] && !mask[5]);
        check("Meshes without breast ownership keep their entire old domain",BreastExclusion.Build(null,7).All(x=>!x));
        var original=Enumerable.Range(0,6).Select(i=>new Vector3(i,i+1,i+2)).ToArray();
        var proposed=original.Select(v=>v+new Vector3(4,5,6)).ToArray();var saved=(Vector3[])proposed.Clone();
        BreastExclusion.Restore(original,proposed,mask);
        check("Breast position restore is exact",Enumerable.Range(0,6).Where(i=>mask[i]).All(i=>proposed[i]==original[i]));
        check("Position restore leaves non-breast deformation bit-exact",Enumerable.Range(0,6).Where(i=>!mask[i]).All(i=>proposed[i]==saved[i]));
        var tangents=Enumerable.Repeat(new Vector4(1,0,0,-1),6).ToArray();var changed=Enumerable.Repeat(new Vector4(0,1,0,1),6).ToArray();
        BreastExclusion.Restore(tangents,changed,mask);
        check("Excluded shading retains authored tangent handedness",Enumerable.Range(0,6).All(i=>changed[i]==(mask[i]?tangents[i]:new Vector4(0,1,0,1))));
        var parts=new[]{new SkinSurfaceShading.Part {Original=new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitY},Deformed=new[]{Vector3.Zero,Vector3.UnitX,new Vector3(0,1,1)},Normals=Enumerable.Repeat(Vector3.UnitZ,3).ToArray(),Tangents=Enumerable.Repeat(new Vector4(1,0,0,-1),3).ToArray(),Triangles=new[]{0,1,2}}};
        var result=SkinSurfaceShading.Apply(parts,1).Parts[0];var protectedMask=new[]{true,false,false};
        var other=result.Normals[1];BreastExclusion.Restore(parts[0].Normals,result.Normals,protectedMask);
        check("Adjacent abdominal shading cannot rotate excluded breast normals",result.Normals[0]==Vector3.UnitZ && result.Normals[1]==other);
    }
}
