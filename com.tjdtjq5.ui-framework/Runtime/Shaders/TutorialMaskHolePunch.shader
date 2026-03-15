Shader "Tjdtjq5/UI/TutorialMaskHolePunch"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _StencilRef ("Stencil Ref", Int) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-1"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Stencil
        {
            Ref [_StencilRef]
            Comp Always
            Pass Replace
        }

        // 화면에 아무것도 그리지 않음 — Stencil만 기록
        ColorMask 0
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 스프라이트 alpha가 0이면 Stencil도 기록 안 됨
                // → 원형/둥근사각 sprite로 구멍 모양 결정
                fixed4 col = tex2D(_MainTex, i.uv);
                clip(col.a - 0.01);
                return 0;
            }
            ENDCG
        }
    }
}
