using Character;
using AL.H;
namespace ALPregnancy;

internal static class AnimatorParameterRegression
{
    internal static void Run(Action<string,bool> check)
    {
        var male=new Human{Sex=0};
        var female=new Human();female.Data.Custom.Body=HumanData.LoadFixture("base");
        var saved=female.Data.Custom.Body;
        var original=BodyChangeValues.Read(saved);
        var record=new PregnancyRecord{Pregnant=true,PregnancyProgress=.9};
        var actors=new[]{new HActor{Human=male},new HActor{Human=female}};
        var controller=new AL.H.Motion.Caress.Caress00.Controller{_actors=actors};
        HScene.Instance=new HScene{Controller=controller};
        controller.UpdateAnimeParam();
        float before=controller.BreastParameters[0];
        BodyChangeRuntime.ApplyAfterBelly(female,record);
        float target=female.Body.Applied.Shape[3];
        check("live F8 progress changes chest after scene entry",target!=before);
        check("both animators refresh to displayed chest after live progress change",controller.BreastParameters.All(x=>x==target)&&controller.Updates==2);
        check("live refresh restores original card immediately",ReferenceEquals(female.Data.Custom.Body,saved)&&BodyChangeValues.Read(saved).Same(original));
        for(int i=0;i<100;i++)BodyChangeRuntime.ApplyAfterBelly(female,record);
        check("stable frames do not repeatedly refresh animation parameters",controller.Updates==2);
        female.Body.SetUpdateShapeBodyValue();female.Body.SetUpdateShapeBody();female.Body.LateUpdate();
        BodyChangeRuntime.ApplyAfterBelly(female,record);
        check("same shape reapplied by native game does not refresh animator again",controller.Updates==2);

        // Reproduce the previous release's uncovered read, then exercise every
        // new native-method prefix/finalizer independently of the explicit refresh.
        foreach(var type in BodyChangeAnimation.AnimatorReaders)
        {
            var switched=(AL.H.Motion.Base.Controller)Activator.CreateInstance(type);
            switched._actors=actors;
            switched.UpdateAnimeParam();
            check("direct native getter reproduces old-size animation: "+type,switched.BreastParameters[0]==before&&before!=target);
            BodyChangeAnimation.BeforeAnimator(switched,out var read);
            switched.UpdateAnimeParam();BodyChangeAnimation.AfterAnimator(null,read);
            check("motion switch uses visual chest: "+type,switched.BreastParameters.All(x=>x==target));
            check("motion switch preserves saved card: "+type,ReferenceEquals(female.Data.Custom.Body,saved));
        }

        controller.ThrowAfterRead=true;
        record.PregnancyProgress=.95;
        try {BodyChangeRuntime.ApplyAfterBelly(female,record);throw new Exception("expected animator failure");}
        catch(InvalidOperationException){}
        check("failed explicit animation refresh still restores card",ReferenceEquals(female.Data.Custom.Body,saved));
        int failedUpdates=controller.Updates;
        controller.ThrowAfterRead=false;BodyChangeRuntime.ApplyAfterBelly(female,record);
        check("failed refresh remains pending and retries",controller.Updates==failedUpdates+1&&controller.BreastParameters[0]==female.Body.Applied.Shape[3]);

        BodyChangeAnimation.BeforeAnimator(controller,out var outer);
        BodyChangeAnimation.BeforeAnimator(controller,out var inner);
        BodyChangeAnimation.AfterAnimator(null,inner);
        check("nested animator read does not release outer body",female.Data.Custom.Body.shapeValueBody[3]==female.Body.Applied.Shape[3]);
        var failure=new InvalidOperationException("motion switch failure");
        check("native animator failure preserves exception",ReferenceEquals(BodyChangeAnimation.AfterAnimator(failure,outer),failure));
        check("native animator failure restores all actors",ReferenceEquals(female.Data.Custom.Body,saved));
        BodyChangeAnimation.AfterAnimator(null,outer);

        // Three participants, two changed bodies; no assumption that index 1 is
        // the only woman when temporarily providing the native controller data.
        var other=new Human();other.Data.Custom.Body=HumanData.LoadFixture("base");
        var otherSaved=other.Data.Custom.Body;
        BodyChangeRuntime.ApplyAfterBelly(other,new PregnancyRecord{Births=4});
        controller._actors=new[]{actors[0],actors[1],new HActor{Human=other}};
        BodyChangeAnimation.BeforeAnimator(controller,out var three);
        check("three-person reader exposes both modified bodies",female.Data.Custom.Body.shapeValueBody[3]==female.Body.Applied.Shape[3]&&other.Data.Custom.Body.shapeValueBody[3]==other.Body.Applied.Shape[3]);
        BodyChangeAnimation.AfterAnimator(null,three);
        check("three-person reader restores both cards",ReferenceEquals(female.Data.Custom.Body,saved)&&ReferenceEquals(other.Data.Custom.Body,otherSaved));
        BodyChangeRuntime.Release(other);
        controller._actors=actors;

        HScene.Instance.Loaded=false;
        int beforeNotReady=controller.Updates;record.PregnancyProgress=.5;
        BodyChangeRuntime.ApplyAfterBelly(female,record);
        check("unready scene defers parameter refresh",controller.Updates==beforeNotReady);
        HScene.Instance.Loaded=true;BodyChangeRuntime.ApplyAfterBelly(female,record);
        check("pending refresh applies when scene becomes ready",controller.Updates==beforeNotReady+1&&controller.BreastParameters[0]==female.Body.Applied.Shape[3]);
        BodyChangeRuntime.Release(female);
        check("removing body changes also restores native animator parameters",controller.BreastParameters.All(x=>x==before));
        HScene.Instance=null;
    }
}
