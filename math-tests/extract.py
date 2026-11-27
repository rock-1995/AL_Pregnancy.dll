from pathlib import Path
import re
root=Path(__file__).parent
methods='FinalizeBodyAnchorContext TryGetBodySurfaceClothTarget TryFindNearestBodySurfaceTriangle TestBodySurfaceTrianglesForVertex TestBodySurfaceTriangle TryClosestPointOnTriangle BodyAnchorMiss ScaleAnchorMove IsValidBodyAnchorVertex TryFindNearestBodyAnchorPoint TestBodyAnchorPoint BodyAnchorBucketCoords BodyAnchorBucketKey IsFinite RepairClothDistortion BuildSpatialBuckets CollectSpatialNeighbors SpatialBucketCoords SpatialBucketKey BuildNearestBodyVertCache ComputeNormalWeldGroup ComputeWeldedNormalsOffline'.split()
types='BodyAnchorContext BodyAnchorMesh BodyAnchorPoint BodyAnchorBasis BodySurfaceTriangle BodySurfaceHit LocalFrame'.split()
def blocks(text,pattern):
    for m in re.finditer(pattern,text):
        start=m.start(); i=text.index('{',m.end()); depth=1; j=i+1
        while depth:
            depth+=(text[j]=='{')-(text[j]=='}'); j+=1
        yield text[start:j]
for name,file in [('Legacy',root/'BaselineBelly.cs.txt'),('Fast',root/'../src/BellyVertexMorph.cs')]:
    text=file.read_text(encoding='utf-8-sig'); parts=[]
    for t in types: parts+=blocks(text,r'private (?:class|struct) '+t+r'\b')
    for fn in methods: parts+=blocks(text,r'private static [\w\[\]<> ,]+\b'+fn+r'\s*\([^\{]+\)\s*')
    out='using UnityEngine;\nnamespace ALPregnancy;\ninternal static partial class '+name+'Geometry {\n'+'\n'.join(parts)+'\n}\n'
    (root/(name+'Geometry.g.cs')).write_text(out,encoding='utf-8')
    harness=(root/'Harness.cs.txt').read_text(encoding='utf-8').replace('GEOMETRY',name+'Geometry')
    (root/(name+'Harness.g.cs')).write_text(harness,encoding='utf-8')
(root/'FastSearch.g.cs').write_text((root/'../src/BodyAnchorSearch.cs').read_text(encoding='utf-8').replace('partial class BellyVertexMorph','partial class FastGeometry'),encoding='utf-8')
