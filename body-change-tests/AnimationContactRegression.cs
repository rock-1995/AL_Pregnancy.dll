using Character;
using HarmonyLib;
namespace ALPregnancy;

internal static class AnimationContactRegression
{
    internal static void Run(Action<string,bool> check)
    {
        var first=new Human();first.Data.Custom.Body=HumanData.LoadFixture("base");
        var second=new Human();second.Data.Custom.Body=HumanData.LoadFixture("base");
        second.Data.Custom.Body.shapeValueBody[3]=.3f;
        var savedFirst=first.Data.Custom.Body;var savedSecond=second.Data.Custom.Body;
        var original=BodyChangeValues.Read(savedFirst);
        var firstArray=savedFirst.shapeValueBody;
        var pregnant=new PregnancyRecord{Pregnant=true,PregnancyProgress=.7};
        var postpartum=new PregnancyRecord{Births=3};
        BodyChangeRuntime.ApplyAfterBelly(first,pregnant);
        BodyChangeRuntime.ApplyAfterBelly(second,postpartum);
        float target=first.Body.Applied.Shape[3],targetSecond=second.Body.Applied.Shape[3];
        // Native correction interpolates authored min/medium/max contact offsets.
        float Contact(float shape)=>shape<.5f ? -.08f+shape*2*.08f : (shape-.5f)*2*.13f;
        var actor=new AL.H.HActor{Human=first};
        check("old card-only IK path disagrees with the rendered chest",Math.Abs(Contact(actor.Breast)-Contact(target))>1e-6);
        int updates=first.Body.ShapeUpdates+second.Body.ShapeUpdates;
        int values=first.Body.ValueUpdates+second.Body.ValueUpdates;
        int extras=first.Body.ExtraCalls+second.Body.ExtraCalls;
        BodyChangeAnimation.BeforeSingle(first.Body,out var one);
        check("hand contact uses the committed visual chest size",Contact(actor.Breast)==Contact(target));
        check("all native IK morph inputs match the committed visual body",BodyChangeValues.Read(first.Data.Custom.Body).Same(first.Body.Applied));
        check("IK read leaves original card contents untouched",BodyChangeValues.Read(savedFirst).Same(original)&&ReferenceEquals(firstArray,savedFirst.shapeValueBody));
        BodyChangeAnimation.BeforePair(first.Body,second.Body,out var nested);
        check("nested two-actor IK sees both current bodies",actor.Breast==target&&second.Data.Custom.Body.shapeValueBody[3]==targetSecond);
        BodyChangeAnimation.AfterRead(null,nested);
        check("nested finalizer preserves outer scope and restores partner",actor.Breast==target&&ReferenceEquals(second.Data.Custom.Body,savedSecond));
        var failure=new InvalidOperationException("native contact failure");
        check("IK exception propagates without losing card restoration",ReferenceEquals(BodyChangeAnimation.AfterRead(failure,one),failure)&&ReferenceEquals(first.Data.Custom.Body,savedFirst));
        BodyChangeAnimation.AfterRead(null,one);
        check("IK finalizer is idempotent",ReferenceEquals(first.Data.Custom.Body,savedFirst));
        BodyChangeAnimation.BeforePair(first.Body,first.Body,out var duplicate);
        check("same actor on both sides never applies body delta twice",actor.Breast==target);
        BodyChangeAnimation.AfterRead(null,duplicate);
        check("same-actor pair restores original reference",ReferenceEquals(first.Data.Custom.Body,savedFirst));
        BodyChangeAnimation.BeforeActor(actor,out var sceneRead);
        check("scene breast and shape reads use current visual parameters",actor.Breast==target&&actor.GetShape(3)==target);
        BodyChangeAnimation.AfterRead(null,sceneRead);
        check("IK reads do not run shape updates or physics setters",updates==first.Body.ShapeUpdates+second.Body.ShapeUpdates&&values==first.Body.ValueUpdates+second.Body.ValueUpdates&&extras==first.Body.ExtraCalls+second.Body.ExtraCalls);
        check("animation leaves no pending body rebuild",!first.Body._isUpdateShapeBody&&!first.Body._isUpdateShapeBodyValue);

        var own=BodyChangeRuntime.BeginAfterBelly(first,pregnant);
        BodyChangeAnimation.BeforeSingle(first.Body,out var duringCommit);
        BodyChangeAnimation.AfterRead(null,duringCommit);
        check("IK called inside visual commit preserves the outer body scope",actor.Breast==target);
        BodyChangeRuntime.End(first,own);

        foreach(var flag in new[]{"disabled","preview","reload","disposed","inactive","lowpoly","male"})
        {
            PregnancyPlugin.Enabled.Value=flag!="disabled";
            PregnancyController.ManualPreviewActive=flag=="preview";
            first.IsReloading=flag=="reload";first.Disposed=flag=="disposed";
            first.GameObject.activeInHierarchy=flag!="inactive";first.HiPoly=flag!="lowpoly";first.Sex=flag=="male"?0:1;
            BodyChangeAnimation.BeforeSingle(first.Body,out var skipped);
            check("IK scope skips "+flag,ReferenceEquals(first.Data.Custom.Body,savedFirst));
            BodyChangeAnimation.AfterRead(null,skipped);
        }
        PregnancyPlugin.Enabled.Value=true;PregnancyController.ManualPreviewActive=false;
        first.IsReloading=first.Disposed=false;first.GameObject.activeInHierarchy=first.HiPoly=true;first.Sex=1;
        var stale=first.Body;first.Body=new HumanBody(first);
        BodyChangeAnimation.BeforeSingle(stale,out var staleRead);
        check("stale skeleton cannot activate old IK body",ReferenceEquals(first.Data.Custom.Body,savedFirst));
        BodyChangeAnimation.AfterRead(null,staleRead);first.Body=stale;

        BodyChangeAnimation.BeforeSingle(first.Body,out var reload);
        var replacement=HumanData.LoadFixture("reference");first.Data.Custom.Body=replacement;
        BodyChangeAnimation.AfterRead(null,reload);
        check("native body replacement during IK survives finalization",ReferenceEquals(first.Data.Custom.Body,replacement));
        first.Data.Custom.Body=savedFirst;
        BodyChangeRuntime.Release(first);BodyChangeRuntime.Release(second);
        BodyChangeAnimation.BeforePair(first.Body,second.Body,out var released);
        check("released actors cannot use stale visual IK parameters",ReferenceEquals(first.Data.Custom.Body,savedFirst)&&ReferenceEquals(second.Data.Custom.Body,savedSecond));
        BodyChangeAnimation.AfterRead(null,released);

        var harmony=new Harmony();BodyChangeAnimation.Install(harmony);
        check("seven IK, two scene and eight animator readers are covered",harmony.Patches.Count==17);
        foreach(var patch in harmony.Patches)
        {
            var names=patch.Method.GetParameters().Select(p=>p.Name).ToHashSet();
            check("prefix argument mapping: "+patch.Method,patch.Prefix.Method.GetParameters().All(p=>p.Name.StartsWith("__")||names.Contains(p.Name)));
            string finalizer=patch.Method.Name=="UpdateAnimeParam"?nameof(BodyChangeAnimation.AfterAnimator):nameof(BodyChangeAnimation.AfterRead);
            check("exception finalizer installed: "+patch.Method,patch.Finalizer?.Method?.Name==finalizer);
        }
        AnimatorParameterRegression.Run(check);
    }
}
