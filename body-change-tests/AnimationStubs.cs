// Keep body arguments in their native positions/names: Harmony maps prefixes by
// name, including the bool-leading overload and the two-actor correction paths.
namespace ILLGAMES.Rigging
{
    using Character;
    public static class MotionIK
    {
        public static void GetCorrectShapeValues(bool isBlend, HumanBody body, int[] shapes) { }
        public static void GetCorrectShapeValues(HumanBody body, float keyFrame, int[] calcBlend) { }
        public static void GetCorrectShapeValuesParam2(HumanBody body1, HumanBody body2, float keyFrame, int[] calcBlend) { }
        public static void GetCorrectShapeValues(HumanBody body1, HumanBody body2, float keyFrame, object info, int mode, ref float ret) { }
        public static void GetCorrectShapeValues(HumanBody body1, HumanBody body2, float keyFrame, int[] calcBlend) { }
        public static void GetCorrectShapeValues(HumanBody body, float keyFrame, object calcBlend) { }
        public static void GetCorrectShapeValues(HumanBody body1, HumanBody body2, float keyFrame, object calcBlend) { }
    }
}
namespace AL.H
{
    public sealed class HScene
    {
        public static HScene Instance;
        public bool IsDisposed, Loaded=true;
        public Motion.Base.Controller Controller;
    }
    public sealed class HActor
    {
        public Character.Human Human;
        public float Breast => GetShape(3);
        public float GetShape(int index) => Human.Data.Custom.Body.shapeValueBody[index];
    }
}

namespace AL.H.Motion.Base
{
    public class Controller
    {
        public HActor[] _actors;
        public int Updates;
        public bool ThrowAfterRead;
        public float[] BreastParameters;
        public virtual void UpdateAnimeParam()
        {
            Updates++;
            // Model the native path found in Caress00: direct body read, then
            // write the female's chest size to BOTH participant animators.
            // Deliberately bypass HActor.Breast/GetShape and all MotionIK hooks.
            float breast=_actors[1].Human.Data.Custom.Body.shapeValueBody[3];
            BreastParameters=new[]{breast,breast};
            if(ThrowAfterRead)throw new InvalidOperationException("native animator parameter failure");
        }
    }
}
namespace AL.H.Motion.Caress.Caress00 { public class Controller : Base.Controller { public override void UpdateAnimeParam()=>base.UpdateAnimeParam(); } }
namespace AL.H.Motion.Caress.Caress01 { public class Controller : Base.Controller { public override void UpdateAnimeParam()=>base.UpdateAnimeParam(); } }
namespace AL.H.Motion.Service.Service00 { public class Controller : Base.Controller { public override void UpdateAnimeParam()=>base.UpdateAnimeParam(); } }
namespace AL.H.Motion.Service.Service01 { public class Controller : Base.Controller { public override void UpdateAnimeParam()=>base.UpdateAnimeParam(); } }
namespace AL.H.Motion.Insert.Insert00 { public class Controller : Base.Controller { public override void UpdateAnimeParam()=>base.UpdateAnimeParam(); } }
namespace AL.H.Motion.Insert.Insert01 { public class Controller : Base.Controller { public override void UpdateAnimeParam()=>base.UpdateAnimeParam(); } }
namespace AL.H.Motion.Les00 { public class Controller : Base.Controller { public override void UpdateAnimeParam()=>base.UpdateAnimeParam(); } }
namespace AL.H.Motion.Les01 { public class Controller : Base.Controller { public override void UpdateAnimeParam()=>base.UpdateAnimeParam(); } }
