using ALPregnancy;
using System.Numerics;
using BepInEx.Configuration;

internal sealed class DailySequenceRandom(params double[] values) : Random
{
    internal int Calls;
    public override double NextDouble() => values[Calls++ % values.Length];
}

internal static class GrowthScheduleRegression
{
    internal static void Run(Action<string, bool> check)
    {
        static bool Near(double a, double b, double eps = 1e-6) => Math.Abs(a - b) < eps;
        var rules = new PregnancyRules();
        double[] times = { 0, .20, .45, .70, 1 };
        float[] stages = { 0, .33333334f, .44444445f, .5555556f, 1 };
        var record = new PregnancyRecord { Pregnant = true, GrowthStartScale = 10 };
        for (int i = 0; i < times.Length; i++)
        {
            record.PregnancyProgress = times[i];
            check("Default stage landmark at pregnancy " + times[i], record.Growth(rules) == stages[i]);
        }
        bool linear = true;
        for (int i = 0; i < 4; i++) for (int k = 1; k < 10; k++)
        {
            double u = k / 10d;
            linear &= Near(PregnancyGrowthSchedule.ShapeStage(times[i] + (times[i + 1] - times[i]) * u, rules), stages[i] + (stages[i + 1] - stages[i]) * u);
        }
        check("All four time intervals are linear, with no pregnancy smoothstep", linear);
        var early = PregnancyGrowthSchedule.ShapeStage(.1, rules);
        check("First stage advances by ten percent of pregnancy instead of waiting until midterm", early > .16f);
        rules.GrowthStart = .1;
        check("The explicit start setting alone controls the initial wait", PregnancyGrowthSchedule.ShapeStage(.09, rules) == 0 && PregnancyGrowthSchedule.ShapeStage(.1, rules) == 0 && PregnancyGrowthSchedule.ShapeStage(.11, rules) > 0);
        rules.GrowthStart = .9; rules.GrowthEarlyEnd = .1; rules.GrowthMiddleEnd = .1; rules.GrowthLateStart = .1;
        var timing = PregnancyGrowthSchedule.Resolve(rules);
        bool ordered = timing.Start < timing.Early && timing.Early < timing.Middle && timing.Middle < timing.Late && timing.Late < 1;
        float previous = 0;
        for (int i = 0; i <= 1000; i++) { float next = PregnancyGrowthSchedule.ShapeStage(i / 1000d, rules); ordered &= float.IsFinite(next) && next >= previous; previous = next; }
        check("Overlapping/reversed settings stay ordered, finite and monotonic", ordered && previous == 1);
        rules = new(); record.Pregnant = false;
        check("Nonpregnant actor remains undeformed", record.Growth(rules) == 0);
        var torso = new TorsoProfile { PelvicFloor=9.23f, Pubis=9.45f, Navel=10.685f, Ribs=12.19f, SampleMin=9.23f, SampleMax=12.52f,
            Front=Enumerable.Repeat(.7f,21).ToArray(), Back=Enumerable.Repeat(-1f,21).ToArray(), Width=Enumerable.Repeat(1.4f,21).ToArray() };
        var settings = new VtxSettings();
        bool sizesLinear = true;
        for (int i = 0; i < 4; i++)
        {
            var a = BellyShape.Growth(torso, stages[i], settings); var b = BellyShape.Growth(torso, stages[i + 1], settings);
            foreach (double u in new[] { .01, .1, .5, .9, .99 })
            {
                var shape = BellyShape.Growth(torso, PregnancyGrowthSchedule.ShapeStage(times[i] + (times[i + 1] - times[i]) * u, rules), settings);
                sizesLinear &= Near(shape.Depth, a.Depth + (b.Depth - a.Depth) * u, 2e-6) && Near(shape.HalfWidth, a.HalfWidth + (b.HalfWidth - a.HalfWidth) * u, 2e-6);
            }
        }
        check("Production shape width and depth grow linearly inside every scheduled stage", sizesLinear);
        var sample = new Vector3(0, torso.Navel, .7f);
        float z90 = BellyShape.Deform(sample, torso, PregnancyGrowthSchedule.ShapeStage(.90, rules), settings).Z;
        float z95 = BellyShape.Deform(sample, torso, PregnancyGrowthSchedule.ShapeStage(.95, rules), settings).Z;
        float z99 = BellyShape.Deform(sample, torso, PregnancyGrowthSchedule.ShapeStage(.99, rules), settings).Z;
        float z100 = BellyShape.Deform(sample, torso, 1, settings).Z;
        check("Actual skin surface still expands at 90, 95, 99 and 100 percent", z95 > z90 && z99 > z95 && z100 > z99);
        bool same = true;
        foreach (float stage in stages) for (int i=0;i<21;i++)
        {
            var v = new Vector3((i%3-1)*.3f, torso.Pubis+i*.13f, .7f);
            same &= Vector3.Distance(BellyShape.Deform(v,torso,stage,settings), AcceptedBellyShape024.Deform(v,torso,stage,settings)) < 1e-5f;
        }
        check("Approved landmark and full-term shapes match the defaults-only backup", same);

        var world = new PregnancyWorld(); var p = world.Register("daily", "Daily", 1); p.Pregnant = true; p.GestationVariationSample = 0;
        rules.GestationDays = 4; rules.GestationVariation = .1;
        var rolls = new DailySequenceRandom(0, 1, .5);
        PregnancySimulation.NextDay(world,rules,rolls); double day1=p.PregnancyProgress;
        PregnancySimulation.NextDay(world,rules,rolls); double day2=p.PregnancyProgress;
        PregnancySimulation.NextDay(world,rules,rolls);
        check("Independent daily rolls produce 0.225 then 0.275 then 0.25", Near(day1,.225) && Near(day2-day1,.275) && Near(p.PregnancyProgress-day2,.25) && rolls.Calls==3);
        double before=p.PregnancyProgress; rules.GestationVariation=0;
        PregnancySimulation.NextDay(world,rules,rolls);
        check("Zero variation consumes no daily roll and completes birth exactly once", rolls.Calls==3 && !p.IsPregnant && p.Births==1);
        var pair = new PregnancyWorld(); var a1=pair.Register("a","A",1);var a2=pair.Register("b","B",1);a1.Pregnant=a2.Pregnant=true;
        rules.GestationVariation=.1; var pairRolls=new DailySequenceRandom(0,1); PregnancySimulation.NextDay(pair,rules,pairRolls);
        check("Two pregnant actors receive separate daily samples", Near(a1.PregnancyProgress,.225) && Near(a2.PregnancyProgress,.275) && pairRolls.Calls==2);
        rules.GestationVariation=1;
        check("100 percent variation supports zero to double growth without reversal", rules.DailyIncrement(0)==0 && rules.DailyIncrement(1)==.5);
        var clone=pair.Clone();clone.Validate();
        check("New daily model keeps existing normalized saves and legacy metadata readable", Near(clone.Characters["a"].PregnancyProgress,a1.PregnancyProgress));

        var config=new ConfigFile(Path.Combine(AppContext.BaseDirectory,"unsaved-timing.cfg"),false){SaveOnConfigSet=false};PregnancyConfig.Bind(config);
        check("F1 defaults return to original mode and durations", PregnancyConfig.Mode.Value==ConceptionMode.SimpleCycle && PregnancyConfig.Gestation.Value==280 && PregnancyConfig.Recovery.Value==180);
        record.Pregnant=true;record.PregnancyProgress=.1;float prior=record.Growth(PregnancyConfig.Rules);int revision=PregnancyConfig.Revision;
        PregnancyConfig.GrowthEarlyEnd.Value=10;
        check("F1 timing changes reach live rules without advancing pregnancy", PregnancyConfig.Revision==revision+1 && record.PregnancyProgress==.1 && record.Growth(PregnancyConfig.Rules)>prior && Near(record.Growth(PregnancyConfig.Rules),1d/3));
        PregnancyConfig.Variation.Value=10;PregnancyConfig.Gestation.Value=4;
        check("F1 percent controls the next day's range directly", Near(PregnancyConfig.Rules.DailyIncrement(0),.225) && Near(PregnancyConfig.Rules.DailyIncrement(1),.275));
    }
}
