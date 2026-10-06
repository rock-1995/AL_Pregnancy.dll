using ALPregnancy;
using System.Numerics;
using System.Text.Json;

internal static class SupplementalClothingReplay
{
    internal static void Run(string file,Action<string,bool> check)
    {
        using var d=JsonDocument.Parse(File.ReadAllText(file));var root=d.RootElement;
        string[] Names(JsonElement e)=>e.EnumerateArray().Select(x=>x.GetString()).ToArray();
        Matrix4x4[] Matrices(JsonElement e)=>e.EnumerateArray().Select(x=>{var v=x.EnumerateArray().Select(f=>f.GetSingle()).ToArray();return new Matrix4x4(v[0],v[1],v[2],v[3],v[4],v[5],v[6],v[7],v[8],v[9],v[10],v[11],v[12],v[13],v[14],v[15]);}).ToArray();
        var targetNames=Names(root.GetProperty("bodyNames"));var targetPoses=Matrices(root.GetProperty("bodyBinds"));
        var profile=JsonSerializer.Deserialize<TorsoProfile>(root.GetProperty("profile"));
        var c=root.GetProperty("center");var center=new Vector3(c[0].GetSingle(),c[1].GetSingle(),c[2].GetSingle());
        foreach(var garment in root.GetProperty("garments").EnumerateArray())
        {
            string name=garment.GetProperty("name").GetString();var names=Names(garment.GetProperty("names"));var binds=Matrices(garment.GetProperty("binds"));
            bool valid=RestSpaceMapping.TryCreate(names,binds,targetNames,targetPoses,out var map,out var back,out var shared,out var error);
            string mode="exact";
            if(!valid) {mode="aliased";valid=RestSpaceMapping.TryCreateAliased(names,binds,targetNames,targetPoses,out map,out back,out shared,out error);}
            Console.WriteLine($"REPLAY {name}: valid={valid}, mode={mode}, shared={shared}, error={error}");
            if(name.Contains("neck"))continue; // No torso anchors: rejected by existing mapping.
            check(name+" has a valid rest-space route",valid);
            var original=garment.GetProperty("vertices").EnumerateArray().Select(v=>new Vector3(v[0].GetSingle(),v[1].GetSingle(),v[2].GetSingle())).ToArray();
            float roundtrip=original.Max(v=>Vector3.Distance(v,Vector3.Transform(Vector3.Transform(v,map),back)));
            check(name+" coordinate conversion round-trips",roundtrip<1e-4f);
            foreach(float stage in new[]{0f,.45f,.7f,1f})
            {
                var settings=new VtxSettings();int moved=0;float max=0;bool finite=true;
                foreach(var v in original)
                {
                    var reference=Vector3.Transform(v,map);var local=reference-center;
                    var delta=(BellyShape.Deform(local,profile,stage,settings)-local)*BellyShape.DeformationInfluence(1,local.Y,profile,settings);
                    var target=Vector3.Transform(reference+delta*settings.ClothOtherMult,back);
                    finite &= float.IsFinite(target.X+target.Y+target.Z);
                    float distance=Vector3.Distance(v,target);if(distance>1e-4f)moved++;max=Math.Max(max,distance);
                }
                check(name+" finite deformation",finite);
                Console.WriteLine($"REPLAY {name}: stage={stage}, moved={moved}/{original.Length}, maxDelta={max}");
                check(name+" zero is identity / late stage moves",stage==0?max<1e-4f:stage<.7f || moved>0);
            }
        }
    }
}
