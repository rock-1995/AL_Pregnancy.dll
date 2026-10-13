using Character;
using ALPregnancy;
using UnityEngine;
using MagicaCloth2;
using M=System.Numerics.Matrix4x4;
using V=System.Numerics.Vector3;
using Q=System.Numerics.Quaternion;

internal static class AxisDriverRegression
{
    internal static void Run(Action<string,bool> check)
    {
        var parent=new Transform{Id=101,name="cf_j_waist01"};
        var root=new Transform{Id=102,name="cf_y_waist01",parent=parent};
        var skin=new Transform{Id=103,name="cf_s_waist01",parent=root};
        var restPosition=new V(-.000000135f,.017f,-.011f);
        var restRotation=Q.CreateFromYawPitchRoll(.04f,-.03f,.01f);
        var setup=new RenderSetupSerializeData{transformArray=new[]{root},
            transformLocalPositions=new[]{new Float3{x=restPosition.X,y=restPosition.Y,z=restPosition.Z}},
            transformLocalRotations=new[]{new Rotation{value=new Float4{x=restRotation.X,y=restRotation.Y,z=restRotation.Z,w=restRotation.W}}}};
        var entry=new HumanMagicaSpringSetting.BoneData{Spring=new(){SerializeData=new(){rootBones=new(){root}},Data2=new(){initData=new(){clothSetupDataList=new(){setup}}}}};
        var human=new Human{Body=new(){magicaSpringSetting=new(){Waist=new[]{entry}}}};
        var driver=AxisPoseDriver.Capture(human,skin,HumanMagicaSpringSetting.Type.Waist);
        check("Axis correction resolves the actual waist physics root",driver.RemovedPhysicsRoots.SequenceEqual(new[]{"cf_y_waist01"}));
        foreach(float animation in new[]{-.6f,0,.7f})
        foreach(float jiggle in new[]{-.2f,0,.2f})
        foreach(float bodyScale in new[]{.75f,1,1.3f})
        {
            var animated=M.CreateRotationX(animation)*M.CreateRotationY(.8f)*M.CreateTranslation(2,8,-3);
            root.localScale=new(1.1f,.9f,1.05f);
            var restLocal=M.CreateScale(1.1f,.9f,1.05f)*M.CreateFromQuaternion(restRotation)*M.CreateTranslation(restPosition);
            var jiggleLocal=restLocal*M.CreateRotationZ(jiggle)*M.CreateTranslation(jiggle*.1f,-jiggle*.05f,0);
            var customize=M.CreateScale(bodyScale,.95f,1.12f)*M.CreateTranslation(.01f,.02f,.03f);
            parent.localToWorldMatrix=new(){Value=animated};root.localToWorldMatrix=new(){Value=jiggleLocal*animated};skin.localToWorldMatrix=new(){Value=customize*jiggleLocal*animated};
            var expected=customize*restLocal*animated;
            float error=0;
            foreach(var p in new[]{V.Zero,V.UnitX,V.UnitY,V.UnitZ,new V(1,2,3)})error=Math.Max(error,V.Distance(V.Transform(p,driver.World()),V.Transform(p,expected)));
            check($"Retains animation {animation}, body scale {bodyScale}; removes jiggle {jiggle}",error<5e-6f);
            check("Driver never modifies native skin pose",skin.localToWorldMatrix.Value==customize*jiggleLocal*animated);
        }
        // A nonphysical bone must keep the exact original matrix, not be rebound.
        var plain=new Transform{Id=104,name="cf_s_spine02",parent=parent,localToWorldMatrix=new(){Value=M.CreateScale(.84f,1.02f,1.13f)*parent.localToWorldMatrix.Value}};
        check("Nonphysical axis source stays bit-exact",AxisPoseDriver.Capture(human,plain,HumanMagicaSpringSetting.Type.Waist).World()==plain.localToWorldMatrix.Value);
    }
}
