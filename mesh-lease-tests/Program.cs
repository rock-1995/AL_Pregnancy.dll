using ALPregnancy;
using Character;
using UnityEngine;
using UObject = UnityEngine.Object;

int passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Test(string name, Action body) { body(); passed++; Console.WriteLine("PASS " + name); }
Mesh Source(string name) => new() { name = name };
SkinnedMeshRenderer Renderer(string name, Mesh mesh) => new() { name = name, sharedMesh = mesh, localBounds = new Bounds(42) };
MeshLease.Observation[] Observe(params SkinnedMeshRenderer[] renderers) => renderers.Select(r => new MeshLease.Observation(r, r.sharedMesh, r.GetInstanceID(), r.sharedMesh?.GetInstanceID() ?? 0, r.enabled && r.gameObject.activeInHierarchy)).ToArray();

Test("single clothing replacement preserves body and other clothing clone identities", () =>
{
    using var lease = new MeshLease();
    var human = new Human(); var body = Renderer("body", Source("body")); var top = Renderer("top", Source("top")); var bot = Renderer("bot", Source("bot"));
    lease.Acquire(human, Observe(body, top, bot));
    var bodyClone = body.sharedMesh; var topClone = top.sharedMesh; var botClone = bot.sharedMesh;
    var replacement = Source("new top"); top.sharedMesh = replacement;
    int clones = UObject.Clones;
    var observations = Observe(body, top, bot);
    Check(lease.CheckChanges(observations).Mesh, "replacement must be detected");
    lease.Refresh(human, observations);
    Check(UObject.Clones == clones + 1, "replacement should create exactly one clone");
    Check(body.sharedMesh == bodyClone && bot.sharedMesh == botClone, "unchanged clone pointers changed");
    Check(top.sharedMesh != replacement && MeshLease.OriginalForReadiness(top.sharedMesh) == replacement, "new top not leased");
    Check(FluidMeshLifetime.Pending.Any(p => p.Clone == topClone), "old top not retired");
    Check(lease.ReadableCount == 3 && lease.UnreadableCount == 0, "lease counts wrong");
    Check(!lease.CheckChanges(Observe(body, top, bot)).Mesh, "new clone was not recorded");
});
Test("removed renderer is restored before deferred ownership retirement", () =>
{
    using var lease = new MeshLease(); var human = new Human(); var source = Source("top"); var top = Renderer("top", source); var body = Renderer("body", Source("body"));
    lease.Acquire(human, Observe(body, top)); var topClone = top.sharedMesh;
    top.localBounds = new Bounds(99);
    lease.Refresh(human, Observe(body));
    Check(top.sharedMesh == source && top.localBounds == new Bounds(42), "removed renderer original/bounds not restored");
    Check(lease.ReadableCount == 1 && lease.UnreadableCount == 0, "removed count retained");
    Check(MeshLease.Owns(topClone), "ownership must protect clone until deferred collection");
    Check((source.hideFlags & HideFlags.DontUnloadUnusedAsset) != 0, "original protection removed before retirement");
    FluidMeshLifetime.Collect();
    Check(!MeshLease.Owns(topClone) && topClone == null, "retired clone still owned/alive after collection");
    Check(source.hideFlags == HideFlags.None, "retired original protection leaked");
});
Test("unchanged unreadable observations do not retry GPU extraction on unrelated replacements", () =>
{
    using var lease = new MeshLease(); var human = new Human(); var unreadable = Source("gpu opaque"); unreadable.isReadable = false;
    var gpu = Renderer("gpu", unreadable); gpu.ClothingEligible=true; var top = Renderer("top", Source("top"));
    lease.Acquire(human, Observe(gpu, top)); int attempts = ReadableClothingMesh.Attempts;
    Check(lease.UnreadableCount == 1 && lease.ReadableCount == 1, "initial unreadable count wrong");
    for (int i = 0; i < 3; i++) { top.sharedMesh = Source("top" + i); lease.Refresh(human, Observe(gpu, top)); }
    Check(ReadableClothingMesh.Attempts == attempts, "unchanged unreadable observation retried");
    Check(lease.UnreadableCount == 1 && lease.ReadableCount == 1, "refresh unreadable count wrong");
    Check(!lease.CheckChanges(Observe(gpu, top)).Mesh, "unreadable observation repeatedly dirty");
});
Test("visibility changes do not require clone replacement", () =>
{
    using var lease = new MeshLease(); var body = Renderer("body", Source("body")); var top = Renderer("top", Source("top"));
    lease.Acquire(new Human(), Observe(body, top)); var bodyClone = body.sharedMesh; var topClone = top.sharedMesh; int clones = UObject.Clones;
    top.gameObject.activeInHierarchy = false; var change = lease.CheckChanges(Observe(body, top));
    Check(!change.Mesh && change.Visibility, "visibility became topology change");
    Check(!lease.CheckChanges(Observe(body, top)).Visibility, "visibility not consumed");
    Check(body.sharedMesh == bodyClone && top.sharedMesh == topClone && UObject.Clones == clones, "visibility replaced meshes");
});
Test("empty optional meshes are ignored until populated and then acquired once", () =>
{
    using var lease = new MeshLease(); var human = new Human(); var body = Renderer("body", Source("body")); var optional = Renderer("optional", null);
    lease.Acquire(human, Observe(body, optional));
    Check(!lease.CheckChanges(Observe(body, optional)).Mesh, "empty optional renderer repeatedly dirty");
    optional.sharedMesh = Source("optional"); int clones = UObject.Clones;
    Check(lease.CheckChanges(Observe(body, optional)).Mesh, "optional arrival not detected");
    lease.Refresh(human, Observe(body, optional));
    Check(UObject.Clones == clones + 1 && lease.ReadableCount == 2, "optional arrival rebuilt other meshes");
});
Test("all entries removed restores sources and leaves an empty stable lease", () =>
{
    using var lease = new MeshLease(); var human = new Human(); var source = Source("body"); var body = Renderer("body", source);
    lease.Acquire(human, Observe(body)); lease.Refresh(human, Array.Empty<MeshLease.Observation>());
    Check(body.sharedMesh == source && lease.ReadableCount == 0 && lease.UnreadableCount == 0, "empty refresh failed");
    Check(!lease.CheckChanges(Array.Empty<MeshLease.Observation>()).Mesh, "empty observation dirty");
});
Test("shared source protection survives retirement of only one clone", () =>
{
    using var lease = new MeshLease(); var human = new Human(); var source = Source("shared"); var a = Renderer("a", source); var b = Renderer("b", source);
    lease.Acquire(human, Observe(a, b)); lease.Refresh(human, Observe(b)); FluidMeshLifetime.Collect();
    Check((source.hideFlags & HideFlags.DontUnloadUnusedAsset) != 0, "remaining clone lost source hold");
    lease.Dispose(); Check(source.hideFlags == HideFlags.None, "last shared source hold leaked");
});
Test("Dispose restores originals bounds ownership and existing asset flags", () =>
{
    var lease = new MeshLease(); var human = new Human(); var source = Source("body"); source.hideFlags = HideFlags.DontUnloadUnusedAsset;
    var body = Renderer("body", source); var topSource = Source("top"); var top = Renderer("top", topSource);
    lease.Acquire(human, Observe(body, top)); var bodyClone = body.sharedMesh; var topClone = top.sharedMesh; body.localBounds = new Bounds(87);
    lease.Dispose();
    Check(body.sharedMesh == source && top.sharedMesh == topSource && body.localBounds == new Bounds(42), "Dispose did not restore baseline");
    Check(!MeshLease.Owns(bodyClone) && !MeshLease.Owns(topClone) && FluidMeshLifetime.Pending.Count == 0, "Dispose retirement incomplete");
    Check(source.hideFlags == HideFlags.DontUnloadUnusedAsset && topSource.hideFlags == HideFlags.None, "asset flags were not restored correctly");
    Check(lease.ReadableCount == 0 && lease.UnreadableCount == 0, "Dispose count not cleared");
});
Test("known unreadable hosiery uses the existing clothing classifier and retains its exact source", () =>
{
    using var lease=new MeshLease(); var human=new Human(); var source=Source("o_panst"); source.isReadable=false;
    var readable=Source("GPU copy"); source.ReadbackResult=readable;
    var hosiery=Renderer("o_panst",source); hosiery.ClothingEligible=true; hosiery.ExpectedActor=human;
    int attempts=ReadableClothingMesh.Attempts;
    lease.Acquire(human,Observe(hosiery));
    Check(ReadableClothingMesh.Attempts==attempts+1 && hosiery.sharedMesh==readable,"recognized hosiery did not install the readable copy");
    Check(readable.name==source.name && MeshLease.Owns(readable) && MeshLease.OriginalForReadiness(readable)==source,"hosiery lease lost its original identity");
    Check(!source.isReadable && source.vertexCount==100 && !source.Destroyed,"lease mutated or destroyed the unreadable source");
    lease.Dispose();
    Check(hosiery.sharedMesh==source && source.hideFlags==HideFlags.None && !source.Destroyed,"disposing hosiery did not restore the exact native source");
});
Test("path-based clothing eligibility is honored without duplicating garment keywords", () =>
{
    using var lease=new MeshLease(); var human=new Human(); var source=Source("generic mesh"); source.isReadable=false;
    source.ReadbackResult=Source("copy"); var renderer=Renderer("generic renderer",source);
    renderer.ClothingEligible=true; renderer.ExpectedActor=human;
    int attempts=ReadableClothingMesh.Attempts, classifications=BellyVertexMorph.Classifications;
    lease.Acquire(human,Observe(renderer));
    Check(BellyVertexMorph.Classifications==classifications+1 && ReadableClothingMesh.Attempts==attempts+1,"lease bypassed the existing full renderer/path classification");
    Check(renderer.sharedMesh==source.ReadbackResult,"path-identified garment not leased");
});
Test("unrecognized unreadable renderers do not enter clothing GPU reconstruction", () =>
{
    using var lease=new MeshLease(); var human=new Human(); var source=Source("other"); source.isReadable=false; source.ReadbackResult=Source("unused");
    var renderer=Renderer("other",source); int attempts=ReadableClothingMesh.Attempts;
    lease.Acquire(human,Observe(renderer));
    Check(ReadableClothingMesh.Attempts==attempts && renderer.sharedMesh==source,"non-clothing renderer entered reconstruction");
    Check(lease.UnreadableCount==1 && lease.ReadableCount==0,"non-clothing unreadable counts changed");
});
Test("one actor registration precedes all unreadable clothing classification in acquisition", () =>
{
    using var lease=new MeshLease(); var human=new Human(); var other=new Human(); BodyMeshSelection.Register(other);
    var a=Renderer("a",Source("a")); var b=Renderer("b",Source("b"));
    foreach(var renderer in new[]{a,b}) {renderer.sharedMesh.isReadable=false;renderer.ClothingEligible=true;renderer.ExpectedActor=human;}
    int registrations=BodyMeshSelection.Registrations;
    lease.Acquire(human,Observe(a,b));
    Check(BodyMeshSelection.Registrations==registrations+1,"Acquire did not register the current actor once");
});
Test("readable hosiery copy survives toggles and unrelated replacement without another GPU read", () =>
{
    using var lease=new MeshLease(); var human=new Human(); var source=Source("panst"); source.isReadable=false; source.ReadbackResult=Source("copy");
    var hosiery=Renderer("panst",source);hosiery.ClothingEligible=true;
    var top=Renderer("top",Source("top"));lease.Acquire(human,Observe(hosiery,top));var copy=hosiery.sharedMesh;int attempts=ReadableClothingMesh.Attempts;
    for(int i=0;i<3;i++)
    {
        hosiery.enabled=!hosiery.enabled;var changes=lease.CheckChanges(Observe(hosiery,top));
        Check(!changes.Mesh && changes.Visibility,"hosiery visibility incorrectly became topology");
        top.sharedMesh=Source("replacement"+i);lease.Refresh(human,Observe(hosiery,top));
        Check(hosiery.sharedMesh==copy && ReadableClothingMesh.Attempts==attempts,"cached hosiery was reconstructed during unrelated changes");
    }
});
Console.WriteLine($"{passed} production MeshLease checks passed.");
Console.WriteLine("Classifier answers and GPU buffers are stubs; tests verify production lease routing/lifetime, not real asset classification or GPU data correctness.");
