using ALPregnancy;
using Character;
using UnityEngine;

int passed=0;
void Assert(bool value,string message) { if(!value)throw new Exception(message); }
Human Create()
{
    var human=new Human(); human.Body=new HumanBody {_human=human};
    var skin=new SkinnedMeshRenderer {name="body",sharedMesh=new Mesh {name="body"},bones=new[]{new Transform {name="pelvis"},new Transform {name="spine"}}};
    human.GameObject.Renderers.Add(skin);human.Body.BodyRenderers.Add(new BodyRenderer {Renderer=skin});
    human.GameObject.Renderers.Add(new SkinnedMeshRenderer {name="cloth_top",sharedMesh=new Mesh {name="cloth_top"}});
    return human;
}
void Complete(Human human) { Assert(MorphReadiness.EnterNativeBoundary(human),"native boundary rejected"); }
void Test(string name,Action test) { test();passed++;Console.WriteLine("PASS "+name); }
MorphReadiness.Install(new HarmonyLib.Harmony());

Test("stable completion checks only cached mesh identity without hierarchy or bone scans",()=>{
    var human=Create();Complete(human);Assert(MorphReadiness.ChangedAtBoundary(human),"initial not changed");MorphReadiness.ExitNativeBoundary();
    int scans=GameObject.HierarchyScans,bones=SkinnedMeshRenderer.BoneReads,meshes=SkinnedMeshRenderer.MeshReads;
    Complete(human);Assert(!MorphReadiness.ChangedAtBoundary(human),"stable flagged changed");
    Assert(GameObject.HierarchyScans==scans && SkinnedMeshRenderer.BoneReads==bones && SkinnedMeshRenderer.MeshReads==meshes+human.GameObject.Renderers.Count,"stable frame did more than cached identity checks");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(human);
});
Test("visibility event updates cached renderers without hierarchy or bones scan",()=>{
    var human=Create();Complete(human);MorphReadiness.ExitNativeBoundary();int scans=GameObject.HierarchyScans,bones=SkinnedMeshRenderer.BoneReads;
    human.GameObject.Renderers[1].enabled=false;NativeClothingChanges.Mark(human,NativeClothingChange.Visibility);Complete(human);
    Assert(MorphReadiness.ChangedAtBoundary(human) && !MorphReadiness.CurrentMeshes(human)[1].Active,"new visibility missing");
    Assert(GameObject.HierarchyScans==scans && SkinnedMeshRenderer.BoneReads==bones,"visibility did full scan");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(human);
});
Test("cloth topology probes once and preserves body state",()=>{
    var human=Create();Complete(human);MorphReadiness.ExitNativeBoundary();int scans=GameObject.HierarchyScans,releases=PregnancyRuntime.Releases;
    human.GameObject.Renderers.Add(new SkinnedMeshRenderer {name="cloth_bot",sharedMesh=new Mesh {name="cloth_bot"}});
    NativeClothingChanges.MarkTopology(human);Complete(human);
    Assert(GameObject.HierarchyScans==scans+1 && MorphReadiness.CurrentMeshes(human).Count==3,"topology not enumerated");
    Assert(PregnancyRuntime.Releases==releases,"clothing replacement released body");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(human);
});
Test("body mesh replacement invalidates whole morph",()=>{
    var human=Create();Complete(human);MorphReadiness.ExitNativeBoundary();int releases=PregnancyRuntime.Releases;
    human.GameObject.Renderers[0].sharedMesh=new Mesh {name="body"};NativeClothingChanges.MarkTopology(human);Complete(human);
    Assert(PregnancyRuntime.Releases==releases+1,"body replacement retained stale calibration");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(human);
});
Test("lease source to clone remains visibility-only",()=>{
    var human=Create();Complete(human);MorphReadiness.ExitNativeBoundary();int scans=GameObject.HierarchyScans,releases=PregnancyRuntime.Releases;
    var renderer=human.GameObject.Renderers[1];var original=renderer.sharedMesh;var clone=new Mesh {name=original.name};MeshLease.Sources[clone]=original;renderer.sharedMesh=clone;
    MorphReadiness.RememberLeasedMeshes(human);
    renderer.enabled=false;NativeClothingChanges.Mark(human,NativeClothingChange.Visibility);Complete(human);
    Assert(GameObject.HierarchyScans==scans && PregnancyRuntime.Releases==releases,"clone transition caused full probe");
    Assert(MorphReadiness.CurrentMeshes(human)[1].Mesh==clone,"snapshot did not learn clone");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(human);
});
Test("unexpected changed cloth mesh on visibility event falls back to probe",()=>{
    var human=Create();Complete(human);MorphReadiness.ExitNativeBoundary();int scans=GameObject.HierarchyScans,releases=PregnancyRuntime.Releases;
    human.GameObject.Renderers[1].sharedMesh=new Mesh {name="cloth_other"};NativeClothingChanges.Mark(human,NativeClothingChange.Visibility);Complete(human);
    Assert(GameObject.HierarchyScans==scans+1 && PregnancyRuntime.Releases==releases,"fallback did not preserve stable body");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(human);
});
Test("body renderer array identity guards a quiet native replacement",()=>{
    var human=Create();Complete(human);MorphReadiness.ExitNativeBoundary();int scans=GameObject.HierarchyScans;
    var rows=new NativeArray<BodyRenderer>();rows.AddRange(human.Body.BodyRenderers);human.Body.BodyRenderers=rows;
    Complete(human);Assert(GameObject.HierarchyScans==scans+1,"array replacement escaped probe");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(human);
});
Test("missing registered active body rejects and suspends only actor",()=>{
    var human=Create();Complete(human);MorphReadiness.ExitNativeBoundary();int releases=PregnancyRuntime.Releases,suspends=PregnancyController.Suspensions;
    human.GameObject.Renderers[0].sharedMesh=null;NativeClothingChanges.MarkTopology(human);
    Assert(!MorphReadiness.EnterNativeBoundary(human),"missing body accepted");Assert(PregnancyRuntime.Releases==releases+1 && PregnancyController.Suspensions==suspends+1,"missing body not suspended");MorphReadiness.Forget(human);
});
Test("lease-readable clothing classification does not alter body signature",()=>{
    var human=Create();var cloth=human.GameObject.Renderers[1];cloth.sharedMesh.isReadable=false;Complete(human);MorphReadiness.ExitNativeBoundary();int releases=PregnancyRuntime.Releases;
    var source=cloth.sharedMesh;var clone=new Mesh {name=source.name};MeshLease.Sources[clone]=source;cloth.sharedMesh=clone;
    NativeClothingChanges.MarkTopology(human);Complete(human);Assert(PregnancyRuntime.Releases==releases,"readable clone invalidated body");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(human);
});
Test("native NextFrame hide is observed without a clothing event",()=>{
    var human=Create();Complete(human);MorphReadiness.ExitNativeBoundary();int scans=GameObject.HierarchyScans,bones=SkinnedMeshRenderer.BoneReads,releases=PregnancyRuntime.Releases;
    human.GameObject.Renderers[1].gameObject.activeInHierarchy=false;
    Complete(human);Assert(MorphReadiness.ChangedAtBoundary(human) && !MorphReadiness.CurrentMeshes(human)[1].Active,"deferred hide missed");
    Assert(GameObject.HierarchyScans==scans && SkinnedMeshRenderer.BoneReads==bones && PregnancyRuntime.Releases==releases,"deferred hide rebuilt body");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(human);
});
Test("direct native renderer show is observed without an event",()=>{
    var human=Create();human.GameObject.Renderers[1].enabled=false;Complete(human);MorphReadiness.ExitNativeBoundary();int scans=GameObject.HierarchyScans,bones=SkinnedMeshRenderer.BoneReads;
    human.GameObject.Renderers[1].enabled=true;Complete(human);
    Assert(MorphReadiness.ChangedAtBoundary(human) && MorphReadiness.CurrentMeshes(human)[1].Active,"deferred show missed");
    Assert(GameObject.HierarchyScans==scans && SkinnedMeshRenderer.BoneReads==bones,"deferred show scanned geometry");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(human);
});
Test("destroyed cached clothing renderer forces a topology scan",()=>{
    var human=Create();Complete(human);MorphReadiness.ExitNativeBoundary();int scans=GameObject.HierarchyScans,releases=PregnancyRuntime.Releases;
    human.GameObject.Renderers[1].Destroyed=true;Complete(human);
    Assert(GameObject.HierarchyScans==scans+1 && MorphReadiness.CurrentMeshes(human).Count==1,"destroyed renderer remained in snapshot");
    Assert(PregnancyRuntime.Releases==releases,"destroyed clothing renderer released unchanged body");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(human);
});
Mesh Lease(SkinnedMeshRenderer renderer)
{
    var source=renderer.sharedMesh;var clone=new Mesh {name=source.name};
    MeshLease.Sources[clone]=source;renderer.sharedMesh=clone;return source;
}
Test("committed owned clones do not repeatedly invalidate themselves",()=>{
    var human=Create();Complete(human);foreach(var renderer in human.GameObject.Renderers)Lease(renderer);
    MorphReadiness.RememberLeasedMeshes(human);MorphReadiness.ExitNativeBoundary();int scans=GameObject.HierarchyScans,releases=PregnancyRuntime.Releases;
    for(int i=0;i<4;i++)
    { Complete(human);Assert(!MorphReadiness.ChangedAtBoundary(human),"own clone flagged as native replacement");MorphReadiness.ExitNativeBoundary(); }
    Assert(GameObject.HierarchyScans==scans && PregnancyRuntime.Releases==releases,"committed clone self-invalidated");MorphReadiness.Forget(human);
});
Test("silent native cloth swap to original source triggers incremental topology",()=>{
    var human=Create();Complete(human);Lease(human.GameObject.Renderers[0]);var source=Lease(human.GameObject.Renderers[1]);
    MorphReadiness.RememberLeasedMeshes(human);MorphReadiness.ExitNativeBoundary();int scans=GameObject.HierarchyScans,releases=PregnancyRuntime.Releases;
    human.GameObject.Renderers[1].sharedMesh=source;Complete(human);
    Assert(MorphReadiness.ChangedAtBoundary(human) && GameObject.HierarchyScans==scans+1,"same-source native cloth swap missed");
    Assert(PregnancyRuntime.Releases==releases,"cloth source restoration released body");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(human);
});
Test("silent native custom cloth with same underlying source triggers topology",()=>{
    var human=Create();Complete(human);var source=Lease(human.GameObject.Renderers[1]);MorphReadiness.RememberLeasedMeshes(human);MorphReadiness.ExitNativeBoundary();
    int scans=GameObject.HierarchyScans,releases=PregnancyRuntime.Releases;var native=new Mesh {name=source.name};MeshLease.Sources[native]=source;human.GameObject.Renderers[1].sharedMesh=native;
    Complete(human);Assert(MorphReadiness.ChangedAtBoundary(human) && GameObject.HierarchyScans==scans+1,"same-source custom cloth swap missed");
    Assert(PregnancyRuntime.Releases==releases,"custom clothing swap released body");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(human);
});
Test("silent same-source body restoration fully invalidates detached body records",()=>{
    var human=Create();Complete(human);var source=Lease(human.GameObject.Renderers[0]);MorphReadiness.RememberLeasedMeshes(human);MorphReadiness.ExitNativeBoundary();
    int releases=PregnancyRuntime.Releases;human.GameObject.Renderers[0].sharedMesh=source;Complete(human);
    Assert(PregnancyRuntime.Releases==releases+1,"same-source body restoration was treated as clothing");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(human);
});
Test("new custom body attachment with same source fully invalidates",()=>{
    var human=Create();Complete(human);var source=Lease(human.GameObject.Renderers[0]);MorphReadiness.RememberLeasedMeshes(human);MorphReadiness.ExitNativeBoundary();
    int releases=PregnancyRuntime.Releases;var native=new Mesh {name=source.name};MeshLease.Sources[native]=source;human.GameObject.Renderers[0].sharedMesh=native;
    NativeClothingChanges.MarkTopology(human);Complete(human);Assert(PregnancyRuntime.Releases==releases+1,"body attachment change masked by clothing event");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(human);
});
Test("body source restoration remains invalidating after body renderer array replacement",()=>{
    var human=Create();Complete(human);var source=Lease(human.GameObject.Renderers[0]);MorphReadiness.RememberLeasedMeshes(human);MorphReadiness.ExitNativeBoundary();
    int releases=PregnancyRuntime.Releases;var replacement=new NativeArray<BodyRenderer>();replacement.AddRange(human.Body.BodyRenderers);human.Body.BodyRenderers=replacement;
    human.GameObject.Renderers[0].sharedMesh=source;Complete(human);
    Assert(PregnancyRuntime.Releases==releases+1,"metadata guard hid detached body attachment");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(human);
});
Test("coordinate reload pending cannot hide same-source body detachment",()=>{
    var human=Create();Complete(human);var source=Lease(human.GameObject.Renderers[0]);MorphReadiness.RememberLeasedMeshes(human);MorphReadiness.ExitNativeBoundary();
    int releases=PregnancyRuntime.Releases;
    typeof(MorphReadiness).GetMethod("BeforeHumanLoad",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)
        .Invoke(null,new object[]{human,typeof(Human).GetMethod(nameof(Human.ReloadCoordinate))});
    human.GameObject.Renderers[0].sharedMesh=source;Complete(human);
    Assert(PregnancyRuntime.Releases==releases+1,"reload flag hid detached body attachment");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(human);
});
Test("leased snapshot identity and body roles remain actor-local",()=>{
    var a=Create();var b=Create();Complete(a);var originalA=Lease(a.GameObject.Renderers[0]);MorphReadiness.RememberLeasedMeshes(a);MorphReadiness.ExitNativeBoundary();
    Complete(b);foreach(var renderer in b.GameObject.Renderers)Lease(renderer);MorphReadiness.RememberLeasedMeshes(b);MorphReadiness.ExitNativeBoundary();int releases=PregnancyRuntime.Releases;
    a.GameObject.Renderers[0].sharedMesh=originalA;Complete(b);Assert(!MorphReadiness.ChangedAtBoundary(b),"other actor dirtied unchanged bindings");MorphReadiness.ExitNativeBoundary();
    Complete(a);Assert(PregnancyRuntime.Releases==releases+1,"own body replacement not isolated");MorphReadiness.ExitNativeBoundary();MorphReadiness.Forget(a);MorphReadiness.Forget(b);
});
Console.WriteLine($"COMPLETE {passed} production readiness tests passed");
