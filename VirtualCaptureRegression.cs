using ALPregnancy;
using System.Numerics;
using System.Text.Json;

internal static class VirtualCaptureRegression
{
    internal static void Run(string input,string output,Action<string,bool> check)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(input));var root=doc.RootElement;
        var inputMeshes=root.GetProperty("meshes").EnumerateArray().Where(m=>m.GetProperty("body").GetBoolean() && m.GetProperty("active").GetBoolean() && !m.GetProperty("mesh").GetString()!.Contains("armleg")).ToArray();
        var origin=V(root.GetProperty("frame").GetProperty("center"));
        var all=inputMeshes.SelectMany(m=>Points(m,"vertices")).Select(v=>v-origin).ToArray();
        var anatomy=root.GetProperty("anatomy");
        var profile=TorsoProfile.Build(all,anatomy.GetProperty("PelvicFloor").GetSingle(),0,anatomy.GetProperty("SampleMax").GetSingle());
        var p=JsonSerializer.Deserialize<VtxSettings>(root.GetProperty("settings").GetRawText())!;
        var plain=JsonSerializer.Deserialize<VtxSettings>(JsonSerializer.Serialize(p))!;plain.NavelEversion=plain.NavelProportion=0;
        var a=VirtualAxisMath.Reference(profile,1,p);
        var axis=new VirtualAxisMath.Axis(a.Anchor+origin,a.Upper+origin);
        var first=inputMeshes[0];var names=first.GetProperty("boneNames").EnumerateArray().Select(x=>x.GetString()!).ToArray();
        var poses=first.GetProperty("bindposesRowMajor").EnumerateArray().Select(Matrix).ToArray();
        int pelvisIndex=Array.IndexOf(names,"cf_s_waist01");
        int spineIndex=Enumerable.Range(0,names.Length).Where(i=>names[i].StartsWith("cf_s_spine")).OrderBy(i=>Vector3.DistanceSquared(Inverse(poses[i]).Translation,axis.Upper)).First();
        var meshOutputs=new List<object>();float maximumApproximation=0,minNavelArea=float.MaxValue;int navelFlips=0,patchChanged=0,totalSlots=0;
        var poseAngles=new[]{("capture",float.NaN),("upright",0f),("bend45",MathF.PI/4),("bend80",MathF.PI*80/180),("lie90",-MathF.PI/2)};
        var frameOutput=new List<object>();
        Matrix4x4[] Matrices(JsonElement m,string key,float angle)
        {
            if(float.IsNaN(angle))return m.GetProperty("skinToWorldRowMajor").EnumerateArray().Select(Matrix).ToArray();
            var ns=m.GetProperty("boneNames").EnumerateArray().Select(x=>x.GetString()!).ToArray();
            return ns.Select(n=>
            {
                if(key=="lie90")return Matrix4x4.CreateRotationX(angle);
                float f=n.Contains("spine03") || n.Contains("mune") || n.Contains("bust") || n.Contains("neck") || n.Contains("shoulder") || n.Contains("arm")?1:n.Contains("spine02")?.65f:n.Contains("spine01")?.22f:0;
                return Matrix4x4.CreateTranslation(0,-origin.Y,0)*Matrix4x4.CreateRotationX(angle*f)*Matrix4x4.CreateTranslation(0,origin.Y,0);
            }).ToArray();
        }
        foreach(var pose in poseAngles)
        {
            var matrices=Matrices(first,pose.Item1,pose.Item2);
            var result=VirtualAxisMath.Evaluate(axis,matrices[pelvisIndex],matrices[spineIndex],p);
            check($"Captured rig {pose.Item1}: finite virtual transform and fixed anchor",VirtualAxisMath.Finite(result.Transform) && Vector3.Distance(Vector3.Transform(axis.Anchor,result.Transform),Vector3.Transform(axis.Anchor,matrices[pelvisIndex]))<1e-4f);
            frameOutput.Add(new {name=pose.Item1,angle=result.AngleDegrees,pull=result.Pull});
        }
        foreach(var m in inputMeshes)
        {
            var original=Points(m,"vertices");var tris=m.GetProperty("triangles").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            var influence=m.GetProperty("influence").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            var breast=m.GetProperty("breastWeights").ValueKind==JsonValueKind.Null?new float[original.Length]:m.GetProperty("breastWeights").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            var weights=m.GetProperty("skinWeights").EnumerateArray().Select(row=>Enumerable.Range(0,4).Select(k=>new VirtualWeights.Influence(row[k*2].GetInt32(),row[k*2+1].GetSingle())).ToArray()).ToArray();
            Vector3[] Deform(VtxSettings setting)=>original.Select((v,i)=>v+(BellyShape.Deform(v-origin,profile,1,setting)-(v-origin))*influence[i]*(1-BellyShape.BreastRestore(breast[i],setting.BreastGuardStrength))).ToArray();
            var before=Deform(plain);var shape=Deform(p);
            var alpha=shape.Select((v,i)=>VirtualAxisMath.SurfaceWeight(Vector3.Distance(v,original[i]),(original[i]-origin).Y,profile,p)).ToArray();
            int virtualIndex=m.GetProperty("boneNames").GetArrayLength();
            var recipes=new List<VirtualWeights.Recipe>();var lookup=new Dictionary<VirtualWeights.Recipe,int>();
            int Slot(VirtualWeights.Recipe r){if(!lookup.TryGetValue(r,out int i)){i=virtualIndex+recipes.Count;recipes.Add(r);lookup[r]=i;}return i;}
            var packed=alpha.Select((value,i)=>VirtualWeights.Blend(weights[i],value,Slot)).ToArray();totalSlots+=recipes.Count;
            for(int i=0;i<shape.Length;i++)if(Vector3.Distance(shape[i],before[i])>1e-6f)patchChanged++;
            float radius=p.NavelRadius*profile.Span;
            bool outside=true;
            for(int i=0;i<shape.Length;i++)
            {
                var v=original[i]-origin;float r=v.X*v.X/(radius*radius)+(v.Y-profile.SkinNavelY)*(v.Y-profile.SkinNavelY)/(radius*radius*1.69f);
                if(r>=1 || v.Z<=profile.AxisAt(v.Y))outside &= shape[i]==before[i];
            }
            check("Captured "+m.GetProperty("renderer").GetString()+": navel correction leaves the surrounding abdomen unchanged",outside);
            for(int i=0;i<tris.Length;i+=3)
            {
                int x=tris[i],y=tris[i+1],z=tris[i+2];
                if(new[]{x,y,z}.All(j=>Vector3.Distance(shape[j],before[j])<1e-6f))continue;
                var n0=Vector3.Cross(before[y]-before[x],before[z]-before[x]);var n1=Vector3.Cross(shape[y]-shape[x],shape[z]-shape[x]);
                if(n0.LengthSquared()<1e-12f)continue;
                minNavelArea=MathF.Min(minNavelArea,n1.Length()/n0.Length());
                // Concavity reversal legitimately rotates dimple normals by >90
                // degrees. Folding is an orientation reversal of the local XY
                // material chart, not a negative before/after normal dot product.
                if(n0.Z*n1.Z<=0)navelFlips++;
            }
            bool patchEndpoints=true;
            foreach(float radiusSetting in new[]{.015f,.08f})
            foreach(float proportion in new[]{0f,1f})
            foreach(float eversion in new[]{0f,2f})
            {
                var endpoint=JsonSerializer.Deserialize<VtxSettings>(JsonSerializer.Serialize(p))!;
                endpoint.NavelRadius=radiusSetting;endpoint.NavelProportion=proportion;endpoint.NavelEversion=eversion;endpoint.NavelHeight=.03f;
                var positions=Deform(endpoint);
                for(int i=0;i<tris.Length;i+=3)
                {
                    int x=tris[i],y=tris[i+1],z=tris[i+2];
                    if(new[]{x,y,z}.All(j=>Vector3.Distance(positions[j],before[j])<1e-6f))continue;
                    var n0=Vector3.Cross(before[y]-before[x],before[z]-before[x]);var n1=Vector3.Cross(positions[y]-positions[x],positions[z]-positions[x]);
                    if(n0.LengthSquared()<1e-12f)continue;
                    patchEndpoints &= n0.Z*n1.Z>0 && float.IsFinite(n1.Length()) && n1.Length()>n0.Length()*.05f;
                }
            }
            check("Captured "+m.GetProperty("renderer").GetString()+": combined navel slider extremes preserve patch orientation",patchEndpoints);
            var cases=new List<object>();
            foreach(var pose in poseAngles)
            {
                var matrices=Matrices(m,pose.Item1,pose.Item2);var referenceMatrices=Matrices(first,pose.Item1,pose.Item2);
                var result=VirtualAxisMath.Evaluate(axis,referenceMatrices[pelvisIndex],referenceMatrices[spineIndex],p);
                var invPelvis=Inverse(referenceMatrices[pelvisIndex]);
                Matrix4x4 Transform(int k){if(k<virtualIndex)return matrices[k];var r=recipes[k-virtualIndex];return r.Bone<0?result.Transform:Matrix4x4.Lerp(matrices[r.Bone],result.Transform,r.VirtualShare);}
                Vector3 Sum(Vector3 v,IEnumerable<VirtualWeights.Influence> w)=>w.Aggregate(Vector3.Zero,(sum,k)=>sum+Vector3.Transform(v,Transform(k.Bone))*k.Weight);
                var native=shape.Select((v,i)=>Sum(v,weights[i])).ToArray();
                var changed=shape.Select((v,i)=>Sum(v,packed[i])).ToArray();
                for(int i=0;i<shape.Length;i++)
                {
                    var exact=Vector3.Lerp(native[i],Vector3.Transform(shape[i],result.Transform),alpha[i]);
                    maximumApproximation=MathF.Max(maximumApproximation,Vector3.Distance(Vector3.Transform(exact,invPelvis),Vector3.Transform(changed[i],invPelvis)));
                }
                check($"Captured {m.GetProperty("renderer").GetString()} {pose.Item1}: finite skinned geometry",changed.All(v=>float.IsFinite(v.X+v.Y+v.Z)));
                cases.Add(new {name=pose.Item1,native=native.Select(v=>C(Vector3.Transform(v,invPelvis))).ToArray(),corrected=changed.Select(v=>C(Vector3.Transform(v,invPelvis))).ToArray()});
            }
            meshOutputs.Add(new {name=m.GetProperty("renderer").GetString(),original=original.Select(C).ToArray(),before=before.Select(C).ToArray(),shape=shape.Select(C).ToArray(),triangles=tris,alpha,cases});
            Console.WriteLine($"VIRTUAL PALETTE: {m.GetProperty("renderer").GetString()} {virtualIndex} native + {recipes.Count} matrix slots");
        }
        Console.WriteLine($"VIRTUAL CAPTURE PRECHECK: patchVertices={patchChanged}, minPatchAreaRatio={minNavelArea:F5}, patchFlips={navelFlips}, matrixSlots={totalSlots}, maxEquationError={maximumApproximation:F6}");
        File.WriteAllText(output,JsonSerializer.Serialize(new {profile,origin=C(origin),settings=p,anchor=C(axis.Anchor),upper=C(axis.Upper),spine=names[spineIndex],poses=frameOutput,meshes=meshOutputs}));
        check("Captured navel patch changes existing vertices only",patchChanged>5 && patchChanged<200);
        check("Captured navel patch has no flipped or collapsed triangles",navelFlips==0 && minNavelArea>.15f);
        check("Captured four-weight factorization agrees with the full equation",maximumApproximation<1e-5f);
        Console.WriteLine($"VIRTUAL CAPTURE: spine={names[spineIndex]}, patchVertices={patchChanged}, minPatchAreaRatio={minNavelArea:F5}, patchFlips={navelFlips}, matrixSlots={totalSlots}, maxEquationError={maximumApproximation:F6} (span={profile.Span:F6})");
        File.WriteAllText(output,JsonSerializer.Serialize(new {profile,origin=C(origin),settings=p,anchor=C(axis.Anchor),upper=C(axis.Upper),spine=names[spineIndex],poses=frameOutput,meshes=meshOutputs}));
    }
    private static Matrix4x4 Inverse(Matrix4x4 m){Matrix4x4.Invert(m,out var r);return r;}
    private static Vector3 V(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle());
    private static float[] C(Vector3 v)=>new[]{v.X,v.Y,v.Z};
    private static Vector3[] Points(JsonElement m,string k)=>m.GetProperty(k).EnumerateArray().Select(V).ToArray();
    private static Matrix4x4 Matrix(JsonElement e){var a=e.EnumerateArray().Select(x=>x.GetSingle()).ToArray();return new(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);}
}
