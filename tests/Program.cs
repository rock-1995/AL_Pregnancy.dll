using ALPregnancy;
using System.Numerics;
using System.Text.Json;

int passed = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}

if(args.Length>1 && args[0]=="--clothing-replay") { SupplementalClothingReplay.Run(args[1],Check);return; }
SupplementalClothingRegression.Run(Check);
ReleaseDefaultsRegression.Run(Check);
StartupConfigRegression.Run(Check);
LowerTransitionRegression.Run(Check);
UpperTransitionRegression.Run(Check);
UpperRestrictionsRegression.Run(Check);

// The user trace: a renderer named o_body held o_body_armleg, had 0
// waist-weighted vertices, and lacked a spine bindpose. It must not anchor a belly.
Check("User-trace limb mesh is rejected", TorsoSelectionPolicy.Score("o_body_armleg", true, true, false, 0, true) < 0);
Check("A torso with rest-pose bones can be selected", TorsoSelectionPolicy.Score("o_body_base", true, true, true, 1000, true) > 0);
Check("Limb name is rejected even with incidental waist weights", TorsoSelectionPolicy.Score("o_body_armleg", true, true, true, 2000, true) < 0);
Check("Clothing-hidden torso can still anchor visible garments", TorsoSelectionPolicy.Score("o_body_base", true, true, true, 1000, false) > 0);
Check("Missing spine never enables live-pose fallback", TorsoSelectionPolicy.Score("o_body_base", true, true, false, 1000, true) < 0);
Check("Unreadable torso cannot masquerade as success", TorsoSelectionPolicy.Score("o_body_base", false, false, true, 1000, true) < 0);
Check("Shared original assets cannot be selected for modification", TorsoSelectionPolicy.Score("o_body_base", true, false, true, 1000, true) < 0);
Check("Visible equivalent preferred over inactive alternative", TorsoSelectionPolicy.Score("o_body_base", true, true, true, 1000, true) > TorsoSelectionPolicy.Score("o_body_base", true, true, true, 1000, false));
var candidates = JsonSerializer.Deserialize<Candidate[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "user-trace-0.1.1.json")))!;
var chosen = candidates.OrderByDescending(x => TorsoSelectionPolicy.Score(x.Mesh, x.Readable, true, x.Waist >= 0 && x.Spine >= 0, x.Abdomen, x.Active)).First();
Check("Captured trace selects visible lower torso instead of larger hidden onepi", chosen.Mesh == "o_lower_type01" && chosen.Active);
Check("Visible mesh beats every larger hidden valid candidate", candidates.Where(x => !x.Active).All(x => TorsoSelectionPolicy.Score(x.Mesh, x.Readable, true, x.Waist >= 0 && x.Spine >= 0, x.Abdomen, x.Active) < TorsoSelectionPolicy.Score(chosen.Mesh, true, true, true, chosen.Abdomen, true)));
var bodyPieces = candidates.Where(x => TorsoSelectionPolicy.IsBodyPiece(true, x.Renderer, x.Mesh)).ToArray();
Check("Visible upper torso routes to body deformation", bodyPieces.Any(x => x.Renderer == "o_upper_type01" && x.Active));
Check("Visible lower torso routes to body deformation", bodyPieces.Any(x => x.Renderer == "o_lower_type01" && x.Active));
Check("Registered onepi body variant is not misclassified as clothing", TorsoSelectionPolicy.IsBodyPiece(true, "o_onepi_type04", "o_onepi_type04"));
Check("Unregistered onepi garment is not classified as a body", !TorsoSelectionPolicy.IsBodyPiece(false, "o_onepi_dress", "o_onepi_dress"));

string[] names = { "waist", "spine", "hip" };
Matrix4x4[] boneRest = {
    Matrix4x4.CreateTranslation(0, 10, 0),
    Matrix4x4.CreateRotationZ(0.12f) * Matrix4x4.CreateTranslation(0, 11, 0),
    Matrix4x4.CreateTranslation(1, 9, 0.5f)
};
Matrix4x4[] Poses(Matrix4x4 meshRest) => boneRest.Select(b => { Matrix4x4.Invert(b, out var inverse); return meshRest * inverse; }).ToArray();
void MapTest(string label, Matrix4x4 sourceRest, Matrix4x4 targetRest)
{
    bool ok = RestSpaceMapping.TryCreate(names, Poses(sourceRest), names, Poses(targetRest), out var map, out var reverse, out int shared, out float error);
    Check(label + " constructs valid mapping", ok && shared == 3 && error < 0.0001f);
    var point = new Vector3(0.3f, 9.7f, 0.6f);
    Vector3 targetPoint = Vector3.Transform(point, map);
    Check(label + " preserves world position", Vector3.Distance(Vector3.Transform(point, sourceRest), Vector3.Transform(targetPoint, targetRest)) < 0.0001f);
    Check(label + " round trips", Vector3.Distance(point, Vector3.Transform(targetPoint, reverse)) < 0.0001f);
}
MapTest("Identical bind spaces", Matrix4x4.Identity, Matrix4x4.Identity);
MapTest("Translation plus rotated scaled mesh", Matrix4x4.CreateScale(2, 3, 4) * Matrix4x4.CreateRotationX(0.7f) * Matrix4x4.CreateTranslation(3, 5, -2), Matrix4x4.CreateRotationY(-0.4f) * Matrix4x4.CreateTranslation(-1, 2, 1));
Check("Singular bind matrix rejected", !RestSpaceMapping.TryCreate(new[] { "waist" }, new[] { default(Matrix4x4) }, new[] { "waist" }, new[] { Matrix4x4.Identity }, out _, out _, out _, out _));
Check("No common bone rejected", !RestSpaceMapping.TryCreate(new[] { "waist" }, new[] { Matrix4x4.Identity }, new[] { "other" }, new[] { Matrix4x4.Identity }, out _, out _, out _, out _));
var badPoses = Poses(Matrix4x4.Identity);
badPoses[1] *= Matrix4x4.CreateTranslation(100, 0, 0);
Check("Inconsistent rest skeleton rejected", !RestSpaceMapping.TryCreate(names, badPoses, names, Poses(Matrix4x4.Identity), out _, out _, out _, out _));
ShadingRegression.Run(Check);
ShapeRigRegression.Run(Check);
VirtualAxisRegression.Run(Check);
RenderFixRegression.Run(Check);
PregnancyRegression.Run(Check);
PregnancyNormalizedRegression.Run(Check);
PregnancyDebugRegression.Run(Check);
PregnancyLiveRegression.Run(Check);
GrowthScheduleRegression.Run(Check);
if(args.Length>4)RenderFixRegression.Capture(args[4],Check);

if(args.Length>0 && args[0]=="--collision")
{
    CollisionRegression.Run(args[1],args[2],Check);
    Console.WriteLine($"{passed} regression checks passed.");
    return;
}



if(args.Length>0 && args[0]=="--breast-exclusion")
{
    try
    {
        BreastExclusionRegression.Run(Check);
        UpperFoldRegression.Run(args[1],args[2],Check,true);
    }
    catch(Exception e) { Console.Error.WriteLine(e); Environment.Exit(1); }
    Console.WriteLine($"{passed} regression checks passed.");
    return;
}
if(args.Length>0 && args[0]=="--support")
{
    UpperFoldRegression.Run(args[1],args[2],Check);
    Console.WriteLine($"{passed} regression checks passed.");
    return;
}
if(args.Length>0 && args[0]=="--pose")
{
    PoseTransitionRegression.Run(args[1],args[2],Check);
    Console.WriteLine($"{passed} regression checks passed.");
    return;
}
if(args.Length>0 && (args[0]=="--section" || args[0]=="--uppersection"))
{
    PoseTransitionRegression.Run(args[1],args[2]+".pose.json",Check);
    SectionSimulation.Export(args[1],args[2],Check,args[0]=="--uppersection");
    Console.WriteLine($"{passed} regression checks passed.");
    return;
}
if(args.Length>0)
    try { CapturedGrowthRegression.Run(args[0],args.Length>1 ? args[1] : null,Check); }
    catch(Exception e) { Console.Error.WriteLine(e); Environment.Exit(1); }
if(args.Length>3)
    try { VirtualCaptureRegression.Run(args[2],args[3],Check); }
    catch(Exception e) { Console.Error.WriteLine(e); Environment.Exit(1); }
Console.WriteLine($"{passed} regression checks passed.");

sealed class Candidate
{
    public string Renderer { get; set; } = "";
    public string Mesh { get; set; } = "";
    public bool Active { get; set; }
    public bool Readable { get; set; }
    public int Vertices { get; set; }
    public int Waist { get; set; }
    public int Spine { get; set; }
    public int Abdomen { get; set; }
}

