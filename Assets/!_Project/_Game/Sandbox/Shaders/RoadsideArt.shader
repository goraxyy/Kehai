// The roadside art's surface (Codex's, 27 November; adopted into the game 28 November with fog
// added, so the art fogs with the store in the replays and wherever RenderSettings.fog is on).
Shader "Kehai/Roadside Art"
{
 Properties
 {
  [MainTexture] _BaseMap("Original albedo",2D)="white"{}
  _BaseColor("Tint",Color)=(1,1,1,1)
  _FinishMap("Metallic and roughness",2D)="white"{}
  _Irradiance("Indirect light and contact",2D)="white"{}
  _ShopReflection("Shop HDR reflection",2D)="black"{}
  _RoadReflection("Road HDR reflection",2D)="black"{}
  _Mode("Surface type",Float)=0
  _Glow("Emission",Float)=1
  _Wall("Upper wall paint",Float)=0
  _Bump("Finish relief",Float)=0
  _Alpha("Opacity",Float)=1
  _Cutoff("Alpha cutoff",Float)=.4
  _Src("Source blend",Float)=1
  _Dst("Destination blend",Float)=0
  _ZWrite("Depth write",Float)=1
  [Enum(UnityEngine.Rendering.CullMode)] _Cull("Face culling",Float)=2
 }
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
  HLSLINCLUDE
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);TEXTURE2D(_FinishMap);
   TEXTURE2D(_Irradiance);SAMPLER(sampler_Irradiance);
   TEXTURE2D(_ShopReflection);TEXTURE2D(_RoadReflection);SAMPLER(sampler_ShopReflection);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor;float _Mode,_Glow,_Wall,_Bump,_Alpha,_Cutoff,_Src,_Dst,_ZWrite,_Cull;
   CBUFFER_END
   float4x4 _MarketWorldToLocal;
   float _MarketPowerBound;
   TEXTURE2D(_MarketPower); SAMPLER(sampler_MarketPower);
   half Circuit(float3 local){
       return _MarketPowerBound > .5 ? SAMPLE_TEXTURE2D(_MarketPower,sampler_MarketPower,saturate(local.xz/94+.5)).r : 1;
   }
   struct In{float4 pos:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
  ENDHLSL
  Cull [_Cull]
  Pass
  {
   Name "Market physical surface"
   Tags{"LightMode"="UniversalForwardOnly"} ZWrite [_ZWrite] Blend [_Src] [_Dst]
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma target 3.5
   #pragma multi_compile_instancing
   #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
   #pragma multi_compile _ _LIGHT_LAYERS
   #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile_fog
   struct Out{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float3 w:TEXCOORD1;half3 n:TEXCOORD2;float3 local:TEXCOORD3;half3 vertexLighting:TEXCOORD4;float fog:TEXCOORD5; UNITY_VERTEX_INPUT_INSTANCE_ID};
   Out Vert(In v){
    UNITY_SETUP_INSTANCE_ID(v);Out o;UNITY_TRANSFER_INSTANCE_ID(v,o);o.w=TransformObjectToWorld(v.pos.xyz);o.pos=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(v.normal);o.vertexLighting=0;o.fog=ComputeFogFactor(o.pos.z);
    #ifdef _ADDITIONAL_LIGHTS_VERTEX
     o.vertexLighting=VertexLighting(o.w,o.n);
    #endif
    o.uv=v.uv;o.local=_MarketPowerBound>.5?mul(_MarketWorldToLocal,float4(o.w,1)).xyz:o.w;
    if(_Mode>1.5&&_Mode<2.5)o.uv.y+=.25*fmod(floor(_Time.y/3),4);return o;
   }
   half3 DecodeProbe(float2 uv,half outside){
    half4 q=outside>.5?SAMPLE_TEXTURE2D_LOD(_RoadReflection,sampler_ShopReflection,uv,0):SAMPLE_TEXTURE2D_LOD(_ShopReflection,sampler_ShopReflection,uv,0);
    return q.rgb*q.a*10;
   }
   half3 Environment(float3 r,half rough,half outside){
    float u=.5+atan2(r.x,r.z)/(2*PI);float v=clamp(.5+asin(clamp(r.y,-1.,1.))/PI,.002,.998);
    float f=clamp((sqrt(rough+.012)-.255)*6.7,0.,5.);float lo=floor(f);
    return lerp(DecodeProbe(float2(u,(v+lo)/6),outside),DecodeProbe(float2(u,(v+min(5.,lo+1))/6),outside),frac(f));
   }
   half4 Frag(Out i):SV_Target{
    UNITY_SETUP_INSTANCE_ID(i);
    half power=Circuit(i.local);
    half4 t=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv);
    if(_Mode>3.5)clip(t.a-_Cutoff);
    if(_Mode>.5&&_Mode<2.5)return half4(MixFog(t.rgb*_Glow*power,i.fog),1);
    float3 n=normalize(i.n);float3 v=GetWorldSpaceNormalizeViewDir(i.w);
    half2 f=SAMPLE_TEXTURE2D(_FinishMap,sampler_BaseMap,i.uv).rg;
    half rough=clamp(f.g,.055,.99), metal=saturate(f.r);
    if(_Bump>.00001){
     float lumin=dot(t.rgb,float3(.22,.64,.14));float3 dx=ddx(i.w),dy=ddy(i.w);
     float3 r1=cross(dy,n),r2=cross(n,dx);float det=dot(dx,r1);
     if(abs(det)>1e-9)n=normalize(n-sign(det)*(ddx(lumin)*r1+ddy(lumin)*r2)*_Bump/max(abs(det),1e-9));
    }
    half3 albedo=t.rgb;
    if(_Wall>.5 && i.local.y>2.8)albedo*=half3(.111,.145,.157);
    half outside=step(20.05,i.local.z)+step(18.9,abs(i.local.x))+step(i.local.z,-20.15);
    float3 R=reflect(-v,n); if(_MarketPowerBound>.5)R=normalize(mul((float3x3)_MarketWorldToLocal,R));
    half nov=saturate(abs(dot(n,v)));half3 refl=Environment(R,rough,outside)*lerp(outside>.5?.17:.012,1,power);
    if(_Mode>2.5&&_Mode<3.5){
       half fres=.044+.89*pow(1-nov,5);return half4(MixFog(refl+half3(.019,.031,.033),i.fog),min(.82,fres+.035));
    }
    half4 field=SAMPLE_TEXTURE2D(_Irradiance,sampler_Irradiance,saturate(i.local.xz/94+.5));
    half ao=lerp(field.a,1,saturate(i.local.y/.65));half alpha=1;BRDFData brdf;
    InitializeBRDFData(albedo,metal,half3(.04,.04,.04),1-rough,alpha,brdf);
    InputData inputData=(InputData)0;inputData.positionWS=i.w;inputData.normalWS=n;inputData.viewDirectionWS=v;inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.pos);
    half3 col=brdf.diffuse*field.rgb*(.095+.10*saturate(n.y))*.95*lerp(.018,1,power);
    Light mainLight=GetMainLight(TransformWorldToShadowCoord(i.w));
    #ifdef _LIGHT_LAYERS
     if(IsMatchingLightLayer(mainLight.layerMask,GetMeshRenderingLayer()))
    #endif
    col+=LightingPhysicallyBased(brdf,mainLight,n,v)*lerp(.12*power,1,saturate(outside));
    #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
     uint lightCount=GetAdditionalLightsCount();
     LIGHT_LOOP_BEGIN(lightCount)
      Light l=GetAdditionalLight(lightIndex,i.w,half4(1,1,1,1));
      #ifdef _LIGHT_LAYERS
       if(IsMatchingLightLayer(l.layerMask,GetMeshRenderingLayer()))
      #endif
      col+=LightingPhysicallyBased(brdf,l,n,v);
     LIGHT_LOOP_END
    #endif
    col+=brdf.diffuse*i.vertexLighting;
    half3 F0=lerp(half3(.042,.042,.042),albedo,metal);half3 fres=F0+(max(F0,half3(1-rough,1-rough,1-rough))-F0)*pow(1-nov,5);
    col+=refl*fres/(1+rough*rough);return half4(MixFog(col*ao,i.fog),1);
   }
   ENDHLSL
  }
  Pass
  {
   Name "ShadowCaster"
   Tags{"LightMode"="ShadowCaster"}
   ColorMask 0 ZWrite On ZTest LEqual
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex DepthVertex
   #pragma fragment DepthFragment
   #pragma multi_compile_instancing
   #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
   #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
   float3 _LightDirection;
   float3 _LightPosition;
   struct DepthVaryings {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;};
   DepthVaryings DepthVertex(In input){
      UNITY_SETUP_INSTANCE_ID(input);
      DepthVaryings o;
      o.uv=input.uv;
      float3 p=TransformObjectToWorld(input.pos.xyz);
      #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 d=normalize(_LightPosition-p);
      #else
        float3 d=_LightDirection;
      #endif
      float3 n=TransformObjectToWorldNormal(input.normal);
      o.pos=ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p,n,d)));
      return o;
   }
   half4 DepthFragment(DepthVaryings i):SV_Target {
      if(_Mode>3.5)clip(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a-_Cutoff);
      return 0;
   }
   ENDHLSL
  }
  Pass
  {
   Name "DepthOnly"
   Tags{"LightMode"="DepthOnly"}
   ColorMask 0 ZWrite On ZTest LEqual
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex DepthVertex
   #pragma fragment DepthFragment
   #pragma multi_compile_instancing
   #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
   struct DepthVaryings {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;};
   DepthVaryings DepthVertex(In input){
      UNITY_SETUP_INSTANCE_ID(input);
      DepthVaryings o;
      o.uv=input.uv;
      float3 p=TransformObjectToWorld(input.pos.xyz);
      o.pos=TransformWorldToHClip(p);
      return o;
   }
   half4 DepthFragment(DepthVaryings i):SV_Target {
      if(_Mode>3.5)clip(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a-_Cutoff);
      return 0;
   }
   ENDHLSL
  }
 }
}
