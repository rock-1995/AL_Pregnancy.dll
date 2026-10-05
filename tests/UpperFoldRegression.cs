using ALPregnancy;
using System.Numerics;
using System.Text.Json;

internal static class UpperFoldRegression
{
    internal static void Run(string input, string output, Action<string,bool> check, bool excludeBreasts=false)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(input)); var root=doc.RootElement;
        var p=JsonSerializer.Deserialize<VtxSettings>(root.GetProperty("settings").GetRawText())!;
        var torso=JsonSerializer.Deserialize<TorsoProfile>(root.GetProperty("anatomy").GetRawText())!;
        var f=root.GetProperty("frame");var center=V(f.GetProperty("center"));var up=V(f.GetProperty("up"));var right=V(f.GetProperty("right"));var forward=V(f.GetProperty("forward"));
        var frame=new Matrix4x4(right.X,right.Y,right.Z,0,up.X,up.Y,up.Z,0,forward.X,forward.Y,forward.Z,0,center.X,center.Y,center.Z,1);
        Matrix4x4.Invert(frame,out var inverseFrame);
        var meshes=root.GetProperty("meshes").EnumerateArray().Where(m=>m.GetProperty("active").GetBoolean() && m.GetProperty("body").GetBoolean() && m.GetProperty("virtualBoneIndex").GetInt32()>0).ToArray();
        var names=meshes[0].GetProperty("boneNames").EnumerateArray().Select(x=>x.GetString()).ToArray();
        var pelvis=M(meshes[0].GetProperty("skinToWorldRowMajor")[Array.IndexOf(names,"cf_s_waist01")]);
        var frameWorld=frame*pelvis;Matrix4x4.Invert(frameWorld,out var inverseWorld);
        var capturedVirtual=M(root.GetProperty("virtualAxis").GetProperty("transform"));
        var materials=new List<Vector3[]>();var bases=new List<Vector3[]>();var posed=new List<Vector3[]>();var skins=new List<Matrix4x4[]>();var triangles=new List<int[]>();
        var exclusions=new List<bool[]>();var nativeOriginal=new List<Vector3[]>();int excludedVertices=0;bool staticExact=true;
        foreach(var m in meshes)
        {
            var o=Points(m,"vertices");var d=Points(m,"deformed");var toRef=M(m.GetProperty("toReferenceRowMajor"));
            var breast=m.GetProperty("breastWeights");var mask=BreastExclusion.Build(breast.ValueKind==JsonValueKind.Array?breast.EnumerateArray().Select(x=>x.GetSingle()).ToArray():null,o.Length);
            if(!excludeBreasts)Array.Clear(mask,0,mask.Length);
            exclusions.Add(mask);excludedVertices+=mask.Count(x=>x);
            var oldD=(Vector3[])d.Clone();BreastExclusion.Restore(o,d,mask);
            staticExact &= Enumerable.Range(0,d.Length).All(i=>d[i]==(mask[i]?o[i]:oldD[i]));
            var native=m.GetProperty("skinToWorldRowMajor").EnumerateArray().Select(M).ToArray();var w=m.GetProperty("originalSkinWeights");
            var material=o.Select(v=>Vector3.Transform(v,toRef*inverseFrame)/torso.Span).ToArray();var local=new Vector3[o.Length];var sk=new Matrix4x4[o.Length];
            var nativePose=new Vector3[o.Length];
            for(int i=0;i<o.Length;i++)
            {
                var n=new Matrix4x4();for(int k=0;k<4;k++)n+=native[w[i][k*2].GetInt32()]*w[i][k*2+1].GetSingle();
                float alpha=VirtualAxisMath.SurfaceWeight(Vector3.Distance(Vector3.Transform(o[i],toRef),Vector3.Transform(d[i],toRef)),material[i].Y*torso.Span,torso,p);
                if(mask[i])alpha=0;
                nativePose[i]=Vector3.Transform(Vector3.Transform(o[i],n),inverseWorld)/torso.Span;
                sk[i]=Matrix4x4.Lerp(n,toRef*capturedVirtual,alpha);
                local[i]=Vector3.Transform(Vector3.Transform(d[i],sk[i]),inverseWorld)/torso.Span;
            }
            materials.Add(material);bases.Add(d);posed.Add(local);skins.Add(sk);triangles.Add(m.GetProperty("triangles").EnumerateArray().Select(x=>x.GetInt32()).ToArray());
            nativeOriginal.Add(nativePose);
        }
        var timer=System.Diagnostics.Stopwatch.StartNew();
        var field=new UpperFoldSupport(torso,materials.Select((v,i)=>new UpperFoldSupport.Surface(v,triangles[i])).ToArray());
        double buildMs=timer.Elapsed.TotalMilliseconds;
        check("Support probes use continuous triangle surface across body pieces",field.ValidColumns>=13);
        var probes=new Vector3[field.Probes.Length];
        void Sample(IReadOnlyList<Vector3[]> source)
        {
            for(int i=0;i<probes.Length;i++) { var q=field.Probes[i]; if(q.Valid)probes[i]=source[q.Surface][q.A]*q.Bary.X+source[q.Surface][q.B]*q.Bary.Y+source[q.Surface][q.C]*q.Bary.Z; }
        }
        Sample(posed);field.Update(probes,0);
        check("Support strength zero is exactly the old pose",field.Delta.All(x=>x==Vector3.Zero));
        field.Update(probes,float.NaN);check("Nonfinite support is disabled",field.MaxCorrection==0);
        field.Update(probes,1);
        check("Archived fold activates support",field.ActiveColumns>3 && field.MaxCorrection>.04f);
        float Reversal(int column,bool corrected)
        {
            int offset=column*UpperFoldSupport.Rows;var chord=probes[offset+UpperFoldSupport.Rows-1]-probes[offset];chord.X=0;chord=Vector3.Normalize(chord);
            float sum=0;for(int i=1;i<UpperFoldSupport.Rows;i++) { var delta=probes[offset+i]-probes[offset+i-1];if(corrected)delta+=field.Delta[offset+i]-field.Delta[offset+i-1];sum+=MathF.Max(0,-Vector3.Dot(delta,chord)); }return sum;
        }
        float before=Reversal(UpperFoldSupport.Columns/2,false),after=Reversal(UpperFoldSupport.Columns/2,true);
        check("Central folded strip backtracking reduced at least 85 percent",after<before*.15f);
        check("Support has zero endpoint displacement",Enumerable.Range(0,UpperFoldSupport.Columns).All(c=>field.Delta[c*UpperFoldSupport.Rows]==Vector3.Zero && field.Delta[(c+1)*UpperFoldSupport.Rows-1]==Vector3.Zero));
        check("Support correction bounded",field.MaxCorrection<=.25001f);
        var exports=new List<object>();float inverseError=0,minOutward=0;int changed=0;float minClothGap=float.MaxValue;
        for(int s=0;s<meshes.Length;s++)
        {
            var corrected=new Vector3[posed[s].Length];var correctedMesh=(Vector3[])bases[s].Clone();
            for(int i=0;i<corrected.Length;i++)
            {
                var delta=exclusions[s][i]?Vector3.Zero:field.Correction(materials[s][i],posed[s][i],false);corrected[i]=posed[s][i]+delta;
                float angle=field.Angle(materials[s][i]);var radial=new Vector3(MathF.Sin(angle),0,MathF.Cos(angle));
                minOutward=MathF.Min(minOutward,Vector3.Dot(delta,radial));
                if(delta.LengthSquared()<1e-12f)continue;changed++;
                Matrix4x4.Invert(skins[s][i],out var inverseSkin);
                var worldDelta=Vector3.TransformNormal(delta*torso.Span,frameWorld);
                correctedMesh[i]+=Vector3.TransformNormal(worldDelta,inverseSkin);
                var actual=Vector3.Transform(Vector3.Transform(correctedMesh[i],skins[s][i]),inverseWorld)/torso.Span;
                inverseError=MathF.Max(inverseError,Vector3.Distance(actual,corrected[i]));
            }
            exports.Add(new{name=meshes[s].GetProperty("mesh").GetString(),material=materials[s].Select(C),before=posed[s].Select(C),after=corrected.Select(C),triangles=triangles[s]});
        }
        check("No inward radial body displacement",minOutward>-.0001f);
        if(excludeBreasts)
        {
            check("Real capture contains breast and lower-breast vertices",excludedVertices>500);
            check("Static exclusion preserves every other captured vertex exactly",staticExact);
            float breastError=0;
            foreach(float strength in new[]{0f,1f,4f})
            {
                field.Update(probes,strength);
                for(int s=0;s<meshes.Length;s++)for(int i=0;i<posed[s].Length;i++)if(exclusions[s][i])
                {
                    var delta=BreastExclusion.Contains(exclusions[s],i)?Vector3.Zero:field.Correction(materials[s][i],posed[s][i],false);
                    breastError=MathF.Max(breastError,Vector3.Distance(posed[s][i]+delta,nativeOriginal[s][i]));
                }
            }
            check("Excluded real breast vertices follow native animation at support 0, 1 and 4",breastError<1e-6f);
            field.Update(probes,1);
        }
        check("Inverse skin compensation reaches requested posed position",inverseError<2e-5f);
        check("Upper patch leaves material points below navel and on back unchanged",field.Correction(new(0,0,.3f),new(0,0,.6f),false)==Vector3.Zero && field.Correction(new(0,.4f,-.4f),new(0,.4f,-.4f),false)==Vector3.Zero);
        int clothSamples=0;
        for(int i=0;i<field.Probes.Length;i++)
        {
            var q=field.Probes[i];if(!q.Valid || field.Delta[i].LengthSquared()<1e-8f)continue;
            float angle=field.Angle(q.Material);var radial=new Vector3(MathF.Sin(angle),0,MathF.Cos(angle));
            var bodyDelta=field.Correction(q.Material,probes[i],false);
            foreach(float gap in new[]{-.001f,.005f,.02f,.08f})
            {
                var cloth=probes[i]+radial*gap;
                var clothing=cloth+field.Correction(q.Material,cloth,true);float actual=Vector3.Dot(clothing-(probes[i]+bodyDelta),radial);
                minClothGap=MathF.Min(minClothGap,actual-MathF.Max(0,gap));clothSamples++;
            }
        }
        check("Garment samples preserve positive clearance and repair small initial penetration",minClothGap>-.0001f);
        // Neutral material surface has no backtracking, irrespective of numbering.
        Sample(materials);field.Update(probes,1);check("Neutral torso surface does not activate patch",field.MaxCorrection<1e-6f);
        var staticShape=meshes.Select((m,s)=>bases[s].Select(v=>Vector3.Transform(v,M(m.GetProperty("toReferenceRowMajor"))*inverseFrame)/torso.Span).ToArray()).ToArray();
        Sample(staticShape);field.Update(probes,1);
        // The hard native-breast boundary intentionally differs from the old
        // smoothly expanded static surface. Keep the old no-correction assertion
        // on the old capture mode; the exclusion mode checks the new boundary.
        if(!excludeBreasts)check("Expanded static belly is not needlessly corrected",field.MaxCorrection<1e-6f);
        else check("Static breast exclusion is not undone by support evaluation",staticShape.Select((v,s)=>v.Select((x,i)=>!exclusions[s][i] || Vector3.Distance(x,materials[s][i])<1e-6f).All(x=>x)).All(x=>x));
        bool stress=true;
        for(int step=0;step<=24;step++)
        {
            float mix=step/24f;
            Sample(materials.Select((v,s)=>v.Select((x,i)=>Vector3.Lerp(staticShape[s][i],posed[s][i],mix)).ToArray()).ToArray());field.Update(probes,1);
            stress &= field.Delta.All(x=>float.IsFinite(x.LengthSquared()) && x.Length()<=.25001f);
        }
        check("Pose sweep keeps correction finite and bounded",stress);
        var fallbackSurfaces=materials.Select((v,i)=>new UpperFoldSupport.Surface(v,triangles[i],1)).ToArray();
        var hiddenOnly=new UpperFoldSupport(torso,fallbackSurfaces);
        check("Hidden-body reference geometry supplies the same garment support chart",hiddenOnly.Probes.Zip(field.Probes).All(q=>q.First.Valid==q.Second.Valid && (!q.First.Valid || Vector3.Distance(q.First.Material,q.Second.Material)<1e-5f)));
        var shifted=materials.Select(v=>v.Select(x=>new Vector3(x.X*1.1f,x.Y,x.Z*1.1f)).ToArray()).ToArray();
        var visibleFirst=new UpperFoldSupport(torso,materials.Select((v,i)=>new UpperFoldSupport.Surface(v,triangles[i],0)).Concat(shifted.Select((v,i)=>new UpperFoldSupport.Surface(v,triangles[i],1))).ToArray());
        check("Visible triangles take priority over larger hidden-body variants",visibleFirst.Probes.Zip(field.Probes).All(q=>q.First.Valid==q.Second.Valid && (!q.First.Valid || Vector3.Distance(q.First.Material,q.Second.Material)<1e-5f)));
        var again=new UpperFoldSupport(torso,materials.Select((v,i)=>new UpperFoldSupport.Surface(v,triangles[i].Chunk(3).Reverse().SelectMany(t=>t).ToArray())).ToArray());
        check("Triangle iteration order does not change material probe locations",again.Probes.Zip(field.Probes).All(q=>q.First.Valid==q.Second.Valid && (!q.First.Valid || Vector3.Distance(q.First.Material,q.Second.Material)<1e-5f)));
        // Differential shading follows a known geometric tilt and restores exactly.
        var vv=new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,1,0),new Vector3(0,0,0)};
        var nn=Enumerable.Repeat(Vector3.UnitZ,4).ToArray();var tt=Enumerable.Repeat(new Vector4(1,0,0,-1),4).ToArray();
        var shade=new PoseSurfaceNormals(vv,new[]{0,1,2,3,1,2},new[]{0,1,2,0},Enumerable.Repeat(true,4).ToArray());
        var outN=new Vector3[4];var outT=new Vector4[4];var tilt=Matrix4x4.CreateRotationX(.5f);
        shade.Apply(vv.Select(x=>Vector3.Transform(x,tilt)).ToArray(),nn,tt,outN,outT);
        check("Support normals follow known tilt and welded seam",outN.All(x=>Vector3.Distance(x,Vector3.TransformNormal(Vector3.UnitZ,tilt))<1e-5f) && outN[0]==outN[3]);
        check("Support tangent handedness retained",outT.All(x=>x.W==-1 && MathF.Abs(Vector3.Dot(new(x.X,x.Y,x.Z),outN[0]))<1e-5f));
        shade.Apply(vv,nn,tt,outN,outT);check("Shading returns exactly to authored base",outN.SequenceEqual(nn) && outT.SequenceEqual(tt));
        Sample(posed);field.Update(probes,1);var baseline=(Vector3[])field.Delta.Clone();
        field.Update(probes,0);field.Update(probes,1);check("Repeated poses have no accumulated displacement or temporal state",field.Delta.SequenceEqual(baseline));
        field.Update(probes,4);var maximum=(Vector3[])field.Delta.Clone();
        check("Requested range reaches four times the normal correction",maximum.Zip(baseline).All(x=>Vector3.Distance(x.First,x.Second*4)<1e-6f && float.IsFinite(x.First.LengthSquared())));
        field.Update(probes,8);check("Out-of-range support clamps to four",field.Delta.SequenceEqual(maximum));
        timer.Restart();for(int i=0;i<100;i++)field.Update(probes,1);double updateMs=timer.Elapsed.TotalMilliseconds/100;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output,JsonSerializer.Serialize(new{sourceVersion=root.GetProperty("version").GetString(),excludeBreasts,excludedVertices,span=torso.Span,field.Low,field.High,field.ValidColumns,field.ActiveColumns,field.MaxCorrection,buildMs,fieldUpdateMs=updateMs,beforeReversal=before,afterReversal=after,changedVertices=changed,inverseError,minOutward,minClothGap,clothSamples,meshes=exports}));
        Console.WriteLine($"SUPPORT: columns={field.ValidColumns}, vertices={changed}, reversal={before:F6}->{after:F6}, inverseError={inverseError:G4}, clothSamples={clothSamples}, setup={buildMs:F2}ms, field={updateMs:F4}ms");
    }
    private static Vector3 V(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle());
    private static float[] C(Vector3 p)=>new[]{p.X,p.Y,p.Z};
    private static Vector3[] Points(JsonElement m,string k)=>m.GetProperty(k).EnumerateArray().Select(V).ToArray();
    private static Matrix4x4 M(JsonElement e){var a=e.EnumerateArray().Select(x=>x.GetSingle()).ToArray();return new(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);}
}
