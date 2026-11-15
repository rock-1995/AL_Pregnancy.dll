using Character;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
namespace ALPregnancy;

internal static class CoverRegression
{
    internal static void Run(Action<string,bool> check)
    {
        bool Near(float a, float b) => Math.Abs(a-b)<1e-6;
        var baseline=BodyChangeValues.Read(HumanData.LoadFixture("base"));
        var reference=BodyChangeValues.Read(HumanData.LoadFixture("reference"));
        var delta=BodyChangeValues.Difference(baseline,reference);
        int key=Array.FindIndex(delta.Shape,d=>d>0);
        int zero=Array.FindIndex(delta.Shape,d=>d==0);
        var human=new Human(); human.Data.Custom.Body=HumanData.LoadFixture("base");
        var savedBody=human.Data.Custom.Body;
        var clothes=BodyChangeCovers.Current(human);
        var saved=new Il2CppReferenceArray<HumanDataClothes.CoverInfo>(4);
        saved[0]=new(){use=true}; saved[0].infoTable[key]=.2f; saved[0].infoTable[zero]=2f; saved[0].infoTable[999]=7;
        saved[1]=new(){use=true}; saved[1].infoTable[key]=.4f;
        saved[2]=new(){use=false}; saved[2].infoTable[key]=.3f;
        clothes.coverInfos=saved;
        PregnancyConfig.BodyBirths.Value=5;
        PregnancyConfig.BodyLower.Value=0; PregnancyConfig.BodyUpper.Value=100;
        var record=new PregnancyRecord {Births=5};
        float Expected(float source)=>BodyChangeMath.Add(source,delta.Shape[key],1,0,100);
        void Tick() => BodyChangeRuntime.ApplyAfterBelly(human,record);
        Tick();
        check("cover uses its own baseline plus pregnancy delta",Near(human.Body.Applied.Shape[key],Expected(.2f)));
        check("unaffected out-of-range cover remains unchanged",human.Body.Applied.Shape[zero]==2);
        check("saved cover array, tables and values remain intact",ReferenceEquals(clothes.coverInfos,saved)&&saved[0].infoTable[key]==.2f&&saved[0].infoTable[999]==7);
        int updates=human.Body.ValueUpdates, allocations=Il2CppReferenceArray<HumanDataClothes.CoverInfo>.Allocations;
        for(int i=0;i<100;i++) Tick();
        check("stable covered frames neither accumulate nor rebuild",Near(human.Body.Applied.Shape[key],Expected(.2f))&&updates==human.Body.ValueUpdates);
        check("stable frames allocate no new native cover arrays",allocations==Il2CppReferenceArray<HumanDataClothes.CoverInfo>.Allocations);
        var animation=BodyChangeRuntime.BeginAnimationRead(human.Body);
        check("native IK reads the same covered shape as rendering",Near(human.Cloth.Shape(human,key),human.Body.Applied.Shape[key]));
        check("temporary cover retains unknown keys and disabled entries",clothes.coverInfos[0].infoTable[999]==7&&!clothes.coverInfos[2].use&&clothes.coverInfos[2].infoTable[key]==.3f&&clothes.coverInfos[3]==null);
        BodyChangeRuntime.End(human,animation); BodyChangeRuntime.End(human,animation);
        check("animation restores body and covers idempotently",ReferenceEquals(clothes.coverInfos,saved)&&ReferenceEquals(human.Data.Custom.Body,savedBody));
        foreach(int garment in new[]{1,-1,2,0})
        {
            human.Cloth.VisibleGarment=garment; BodyChangeRuntime.NativeShapeUpdated(human.Body); Tick();
            float expected=garment==1?Expected(.4f):garment==0?Expected(.2f):Expected(baseline.Shape[key]);
            check("native garment selection is preserved: "+garment,Near(human.Body.Applied.Shape[key],expected));
        }
        saved[0].infoTable[key]=.6f; Tick();
        check("live cover edit recalculates from new original",Near(human.Body.Applied.Shape[key],Expected(.6f))&&saved[0].infoTable[key]==.6f);
        saved[0].use=false; Tick();
        check("disabling cover falls back to changed body",Near(human.Body.Applied.Shape[key],Expected(baseline.Shape[key])));
        saved[0].use=true; Tick();
        var replacement=new Il2CppReferenceArray<HumanDataClothes.CoverInfo>(1);
        replacement[0]=new(){use=true}; replacement[0].infoTable[key]=.1f;
        animation=BodyChangeRuntime.BeginAnimationRead(human.Body);
        clothes.coverInfos=replacement; BodyChangeRuntime.End(human,animation);
        check("native cover replacement during a scope survives restore",ReferenceEquals(clothes.coverInfos,replacement));
        Tick(); check("replacement outfit receives delta from its own baseline",Near(human.Body.Applied.Shape[key],Expected(.1f)));
        PregnancyPlugin.Enabled.Value=false; Tick();
        check("disabling plugin restores original covered appearance",Near(human.Body.Applied.Shape[key],.1f)&&ReferenceEquals(clothes.coverInfos,replacement));
        PregnancyPlugin.Enabled.Value=true;
        human.Body.ThrowExtra=true;
        try {Tick(); throw new Exception("Expected native failure");} catch(InvalidOperationException) { }
        check("native failure restores body and original cover data",ReferenceEquals(clothes.coverInfos,replacement)&&ReferenceEquals(human.Data.Custom.Body,savedBody)&&replacement[0].infoTable[key]==.1f);
        human.Body.ThrowExtra=false;
        // Saturate every changed body channel: only the cover can still move.
        for(int i=0;i<delta.Shape.Length;i++) if(delta.Shape[i]!=0) baseline.Shape[i]=delta.Shape[i]>0?1:0;
        for(int i=0;i<delta.Extra.Length;i++) if(delta.Extra[i]!=0) baseline.Extra[i]=delta.Extra[i]>0?1:0;
        baseline.Write(savedBody); PregnancyConfig.NippleColorMaximum.Value=0;
        check("cover-only fixture has no remaining body delta",baseline.Apply(delta,1,0,100,0).Same(baseline));
        Tick(); check("cover still changes when the body is already clamped",Near(human.Body.Applied.Shape[key],Expected(.1f)));
        BodyChangeRuntime.Release(human);
        check("release restores original outfit even with clamped body",Near(human.Body.Applied.Shape[key],.1f));
        PregnancyConfig.BodyLower.Value=-50; PregnancyConfig.BodyUpper.Value=150; PregnancyConfig.NippleColorMaximum.Value=.85f;
    }
}
