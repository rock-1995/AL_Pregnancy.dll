namespace ALPregnancy;
// GPU copying is exercised separately by clothing-runtime-tests. These suites
// exercise ownership and readiness with controllable success/failure results.
internal static class ReadableClothingMesh
{
    internal static bool Fail = false;
    internal static int Calls;
    internal static UnityEngine.Mesh TryCreate(UnityEngine.Mesh source, out string reason)
    {
        Calls++; reason = Fail ? "simulated backend failure" : null;
        return Fail ? null : new UnityEngine.Mesh { name=source.name, vertexCount=source.vertexCount, isReadable=true };
    }
}
