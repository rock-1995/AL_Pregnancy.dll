namespace UnityEngine { public partial struct Matrix4x4 {
public float m00 { get => Rows.M11; set => Rows.M11=value; }
public float m01 { get => Rows.M21; set => Rows.M21=value; }
public float m02 { get => Rows.M31; set => Rows.M31=value; }
public float m03 { get => Rows.M41; set => Rows.M41=value; }
public float m10 { get => Rows.M12; set => Rows.M12=value; }
public float m11 { get => Rows.M22; set => Rows.M22=value; }
public float m12 { get => Rows.M32; set => Rows.M32=value; }
public float m13 { get => Rows.M42; set => Rows.M42=value; }
public float m20 { get => Rows.M13; set => Rows.M13=value; }
public float m21 { get => Rows.M23; set => Rows.M23=value; }
public float m22 { get => Rows.M33; set => Rows.M33=value; }
public float m23 { get => Rows.M43; set => Rows.M43=value; }
public float m30 { get => Rows.M14; set => Rows.M14=value; }
public float m31 { get => Rows.M24; set => Rows.M24=value; }
public float m32 { get => Rows.M34; set => Rows.M34=value; }
public float m33 { get => Rows.M44; set => Rows.M44=value; }
public float this[int r,int c] {get {switch(r*4+c){case 0:return m00;case 1:return m01;case 2:return m02;case 3:return m03;case 4:return m10;case 5:return m11;case 6:return m12;case 7:return m13;case 8:return m20;case 9:return m21;case 10:return m22;case 11:return m23;case 12:return m30;case 13:return m31;case 14:return m32;case 15:return m33;default:throw new System.Exception();}}set{switch(r*4+c){case 0:m00=value;break;case 1:m01=value;break;case 2:m02=value;break;case 3:m03=value;break;case 4:m10=value;break;case 5:m11=value;break;case 6:m12=value;break;case 7:m13=value;break;case 8:m20=value;break;case 9:m21=value;break;case 10:m22=value;break;case 11:m23=value;break;case 12:m30=value;break;case 13:m31=value;break;case 14:m32=value;break;case 15:m33=value;break;default:throw new System.Exception();}}}
}}