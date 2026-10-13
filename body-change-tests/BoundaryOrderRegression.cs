using Character;
using UnityEngine;
namespace ALPregnancy;

internal static class BoundaryOrderRegression
{
    internal static void Run(Action<string,bool> check)
    {
        var h=new Human();h.Data.Custom.Body=HumanData.LoadFixture("base");
        var saved=h.Data.Custom.Body;var renderer=new SkinnedMeshRenderer();
        h.GameObject.Renderers=new[]{renderer};h.Body.BodyRenderers=new[]{new BodyEntry{Renderer=renderer}};
        var pregnancy=new PregnancyRecord{Pregnant=true,PregnancyProgress=.5};
        PregnancyRuntime.Records[h.Pointer]=pregnancy;
        MorphReadiness.Install(new HarmonyLib.Harmony());
        BodyChangeValues expected=null;
        bool bellyRan=false;
        PhaseTrace.Belly=actor=>{
            bellyRan=true;
            check("belly stage reads the original card reference",ReferenceEquals(actor.Data.Custom.Body,saved));
        };
        PhaseTrace.Binding=actor=>{
            check("binding stage always follows belly geometry",bellyRan);
            check("binding sees committed customization with original card restored",ReferenceEquals(actor.Data.Custom.Body,saved)&&actor.Body.Applied!=null);
            if(expected!=null)check("binding consumes final target body",actor.Body.Applied.Same(expected));
            check("post-belly shape leaves no dirty flags for next frame",!actor.Body._isUpdateShapeBody&&!actor.Body._isUpdateShapeBodyValue);
        };
        void Complete(Action change=null,bool ready=true)
        {
            h.Body.LateUpdate();change?.Invoke();bellyRan=false;PhaseTrace.Events.Clear();
            NativeVisualUpdate.Complete(h);
            check("visual callback completes synchronously or skips incomplete mesh",ready?PhaseTrace.Events.FirstOrDefault()=="belly"&&PhaseTrace.Events.LastOrDefault()=="collision":PhaseTrace.Events.Count==0);
            check("card reference never leaks outside callback",ReferenceEquals(h.Data.Custom.Body,saved));
        }
        Complete();expected=h.Body.Applied;
        check("first order is belly, preview, shape, binding, collision",PhaseTrace.Events.SequenceEqual(new[]{"belly","preview","shape","binding","collision"}));
        check("first application commits native shape only once",h.Body.ValueUpdates==1&&h.Body.ShapeUpdates==1);
        int released=PregnancyRuntime.Releases;
        Complete(()=>renderer.bones[0]=new Transform());
        check("changed skeleton releases meshes without resetting body",PregnancyRuntime.Releases==released+1&&h.Body.ShapeUpdates==1);
        Complete(()=>renderer.sharedMesh=new Mesh());
        check("changed mesh releases old meshes once",PregnancyRuntime.Releases==released+2);
        Complete(()=>h.GameObject.Renderers=h.GameObject.Renderers.Concat(new[]{new SkinnedMeshRenderer{name="shirt"}}).ToArray());
        check("new clothing releases old meshes once",PregnancyRuntime.Releases==released+3);
        Complete(()=>renderer.sharedMesh=null,false);Complete(()=>renderer.sharedMesh=new Mesh());
        int stableReleases=PregnancyRuntime.Releases;
        for(int i=0;i<100;i++)Complete();
        check("100 stable frames neither rebuild meshes nor rerun native body",PregnancyRuntime.Releases==stableReleases&&h.Body.ValueUpdates==1&&h.Body.ShapeUpdates==1);
        h.Body.SetUpdateShapeBodyValue();h.Body.SetUpdateShapeBody();Complete();
        check("game shape refresh reapplies once after belly",h.Body.ValueUpdates==3&&h.Body.ShapeUpdates==3&&PhaseTrace.Events.SequenceEqual(new[]{"belly","preview","shape","binding","collision"}));
        pregnancy.PregnancyProgress=.8;expected=null;int beforeProgress=PregnancyRuntime.Releases;Complete();expected=h.Body.Applied;
        check("progress change retains the just-built belly",PregnancyRuntime.Releases==beforeProgress&&h.Body.ShapeUpdates==4);
        var retained=expected;pregnancy.Pregnant=false;pregnancy.Births=2;expected=null;Complete();expected=h.Body.Applied;
        check("postpartum commits after belly",PhaseTrace.Events.IndexOf("shape")>PhaseTrace.Events.IndexOf("belly")&&!expected.Same(retained));
        typeof(MorphReadiness).GetMethod("BeforeHumanLoad",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!
            .Invoke(null,new object[]{h,typeof(Human).GetMethod("ReloadCoordinate")!});
        check("reload restores card body before native loading",h.Body.Applied.Same(BodyChangeValues.Read(saved)));
        Complete();check("post-reload callback reapplies after belly",PhaseTrace.Events.IndexOf("shape")==2);
        BodyChangeRuntime.Release(h);MorphReadiness.Forget(h);PregnancyRuntime.Records.Remove(h.Pointer);
        PhaseTrace.Belly=null;PhaseTrace.Binding=null;
        check("explicit removal restores the original rendered body",h.Body.Applied.Same(BodyChangeValues.Read(saved)));
    }
}
