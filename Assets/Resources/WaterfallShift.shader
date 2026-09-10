Shader "Sonar/WaterfallShift" {
 Properties { _MainTex("Previous",2D)="black"{} _ScanLine("Scan line",2D)="black"{} _TexelHeight("Texel Height",Float)=0.001 _TextureHeight("Texture Height",Float)=512 }
 SubShader { Cull Off ZWrite Off ZTest Always Pass { CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 sampler2D _MainTex; sampler2D _ScanLine; float _TexelHeight; float _TextureHeight;
 fixed4 frag(v2f_img i):SV_Target {
   // Quantize the vertical lookup to an exact texel center. WebGL/GLES can
   // otherwise sample slightly between rows during repeated ping-pong blits,
   // causing alternating/flickering background noise.
   float row = floor(i.uv.y * _TextureHeight);
   if(row >= _TextureHeight - 1.0) return tex2D(_ScanLine,float2(i.uv.x,.5));
   float sourceY = (row + 1.5) * _TexelHeight;
   return tex2D(_MainTex,float2(i.uv.x,sourceY));
 }
 ENDCG } }
}
