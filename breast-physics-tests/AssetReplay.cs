using System.Text.Json;
using Character;
using UnityEngine;
using ALPregnancy;

internal static class AssetReplay
{
    internal static void Run(Action<string,bool> check, string fixturePath)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(fixturePath));
        var d=doc.RootElement;
        var transforms=new Dictionary<int,Transform>();
        foreach(var n in d.GetProperty("nodes").EnumerateArray())
            transforms.Add(n.GetProperty("id").GetInt32(),new Transform{Id=n.GetProperty("id").GetInt32(),name=n.GetProperty("name").GetString()});
        foreach(var n in d.GetProperty("nodes").EnumerateArray())
            transforms.TryGetValue(n.GetProperty("parent").GetInt32(),out transforms[n.GetProperty("id").GetInt32()].parent);
        var byName=transforms.Values.ToDictionary(t=>t.name);
        var roots=d.GetProperty("groups").GetProperty("Bust").EnumerateArray().Select(i=>transforms[i.GetInt32()]).ToList();
        var h=new Human{Body=new(){magicaSpringSetting=new(){Bust=new[]{new HumanMagicaSpringSetting.BoneData{Spring=new(){SerializeData=new(){rootBones=roots}}}}}}};
        check("Actual AL asset has left/right bust01 physical roots",roots.Select(t=>t.name).OrderBy(x=>x).SequenceEqual(new[]{"cf_j_bust01_L","cf_j_bust01_R"}));
        foreach(var m in d.GetProperty("meshes").EnumerateArray())
        {
            var names=m.GetProperty("boneNames").EnumerateArray().Select(x=>x.GetString()).ToArray();
            var renderer=new SkinnedMeshRenderer{bones=names.Select(n=>byName[n]).ToArray()};
            var mesh=new Mesh{name=m.GetProperty("name").GetString(),boneWeights=m.GetProperty("weights").EnumerateArray().Select(w=>new BoneWeight{
                boneIndex0=w[0].GetInt32(),weight0=w[1].GetSingle(),boneIndex1=w[2].GetInt32(),weight1=w[3].GetSingle(),
                boneIndex2=w[4].GetInt32(),weight2=w[5].GetSingle(),boneIndex3=w[6].GetInt32(),weight3=w[7].GetSingle()}).ToArray()};
            var expected=m.GetProperty("expectedPhysicsWeights").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            var actual=BellyVertexMorph.TestWeights(h,mesh,renderer);
            check("Actual hierarchy and all captured weights: "+mesh.name,actual.Length==expected.Length&&actual.Zip(expected).All(v=>Math.Abs(v.First-v.Second)<2e-7f));
            check("Exact physical vertex count: "+mesh.name,actual.Count(w=>w>0)==(mesh.name.Contains("upper")?1782:0));
        }
    }
}
