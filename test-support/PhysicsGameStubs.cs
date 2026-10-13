namespace Character {
    public class Human { public IntPtr Pointer;public UnityEngine.GameObject GameObject=new();public HumanBody Body=new(); }
    public class HumanBody { public UnityEngine.GameObject objHitBody;public HumanMagicaSpringSetting magicaSpringSetting; }
    public class HumanMagicaSpringSetting {
        public enum Type { Bust,Hip,Arm,Leg,Belly,Back,Spine,Waist }
        public class BoneData { public MagicaCloth2.MagicaCloth Spring; }
        public BoneData[] Bust,Hip,Waist,Spine;
        public BoneData[] Get(Type type)=>type switch {Type.Bust=>Bust,Type.Hip=>Hip,Type.Waist=>Waist,Type.Spine=>Spine,_=>null};
    }
}
namespace MagicaCloth2 {
    public class MagicaCloth {
        public ClothSerializeDataEX SerializeData;
        public ClothSerializeData2 Data2;
        public ClothSerializeData2 GetSerializeData2()=>Data2;
    }
    public class ClothSerializeDataEX { public List<UnityEngine.Transform> rootBones; }
    public class ClothSerializeData2 { public ClothInitSerializeData initData; }
    public class ClothInitSerializeData { public List<RenderSetupSerializeData> clothSetupDataList; }
    public class RenderSetupSerializeData {
        public UnityEngine.Transform[] transformArray;
        public Float3[] transformLocalPositions;
        public Rotation[] transformLocalRotations;
    }
    public struct Float3 {public float x,y,z;}
    public struct Float4 {public float x,y,z,w;}
    public struct Rotation {public Float4 value;}
}
