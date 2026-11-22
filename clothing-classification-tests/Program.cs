using System.Text.Json;
using UnityEngine;
using Character;

ALPregnancy.BellyVertexMorph.RunClassificationTests();

namespace UnityEngine
{
    internal sealed class Transform { internal string name; internal Transform parent; }
    internal sealed class GameObject { internal string name; }
    internal sealed class Mesh { internal string name; internal bool isReadable; internal int vertexCount=100; }
    internal sealed class SkinnedMeshRenderer
    {
        private static int next; private readonly int id=++next;
        internal string name; internal Mesh sharedMesh; internal Transform transform;
        internal GameObject gameObject;
        internal int GetInstanceID()=>id;
    }
}
namespace Character
{
    internal sealed class Human { internal Body Body=new(); }
    internal sealed class Body { internal Entry[] BodyRenderers=Array.Empty<Entry>(); }
    internal sealed class Entry { internal SkinnedMeshRenderer Renderer; }
}
namespace ALPregnancy
{
    internal static partial class BellyVertexMorph
    {
        private static SkinnedMeshRenderer Renderer(string path,string meshName)
        {
            Transform last=null;
            foreach(var part in path.Split('/'))last=new Transform{name=part,parent=last};
            return new(){name=last.name,transform=last,sharedMesh=new Mesh{name=meshName},gameObject=new(){name=last.name}};
        }
        private static void Check(bool condition,string message) { if(!condition)throw new Exception(message); }
        internal static void RunClassificationTests()
        {
            int passed=0;BodyMeshSelection.Register(new Human());
            foreach(string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory,"panst-assets.jsonl")))
            {
                using var document=JsonDocument.Parse(line);var item=document.RootElement;
                if(item.GetProperty("kind").GetString()!="SkinnedMeshRenderer")continue;
                string path=item.GetProperty("path").GetString(),mesh=item.GetProperty("mesh").GetString();
                var renderer=Renderer(path,mesh);
                Check(!SupplementalClothing.Matches(renderer.name+"/"+mesh),"Asset unexpectedly matches the old supplemental-only gate.");
                Check(IsClothingRenderer(renderer),"Unreadable garment not eligible: "+path);
                Check(!IsBodyMorphClothSMR(renderer,null),"Unreadable original unexpectedly entered deformation: "+path);
                renderer.sharedMesh.isReadable=true;
                Check(IsBodyMorphClothSMR(renderer,null),"Readable leased garment not eligible for morph: "+path);
                passed++;Console.WriteLine("PASS asset path "+path);
            }
            Check(passed==20,"Expected exact 20 renderers from the inspected local bundle.");
            var body=Renderer("p_cf_panst_00/mo_panst_00/n_panst00/body_lower/o_lower_type01","o_lower_type01");
            var human=new Human{Body=new(){BodyRenderers=new[]{new Entry{Renderer=body}}}};
            BodyMeshSelection.Register(human);Check(!IsClothingRenderer(body),"Registered native body piece was classified as clothing.");
            BodyMeshSelection.Register(new Human());Check(IsClothingRenderer(body),"Previous actor registration leaked into garment classification.");
            passed++;Console.WriteLine("PASS native body registration takes precedence and is actor-local");
            foreach(string slot in new[]{"add_etc","add_arm","add_leg"})
                Check(IsClothingRenderer(Renderer("root/"+slot+"/generic","generic")),"Existing supplemental slot lost eligibility: "+slot);
            passed++;Console.WriteLine("PASS existing supplemental slots remain eligible through their paths");
            Check(!IsClothingRenderer(Renderer("root/a_n_waist/panst_accessory","panst_accessory")),"Freely placed waist accessory entered clothing path.");
            Check(!IsClothingRenderer(Renderer("root/hair/panst_hair","panst_hair")),"Hair entered clothing path.");
            passed++;Console.WriteLine("PASS existing accessory and face/hair exclusions remain intact");
            Console.WriteLine($"{passed} production clothing classification checks passed; methods regenerated from current source.");
            Console.WriteLine("Fixtures retain inspected bundle names/paths; this tests routing and body registration, not native GPU readback.");
        }
    }
}
