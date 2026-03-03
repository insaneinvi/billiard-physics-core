Shader "BilliardPhysics/AimAssist/CapsuleLine"
{
    // Renders a LineRenderer strip with:
    //  - Rounded capsule end-caps (SDF-based, no extra geometry required).
    //  - Smooth anti-aliased edges via smoothstep + fwidth (stable at any distance).
    //
    // Requirements on the LineRenderer:
    //  - textureMode = LineTextureMode.Stretch  (uv.x goes 0→1 along the line).
    //  - numCapVertices = 0  (flat quad; the shader clips the corners).
    //  - Set the _Aspect property (line length / line width) via MaterialPropertyBlock
    //    each frame so the end-caps remain circular regardless of line proportions.

    Properties
    {
        _Color  ("Color",            Color) = (1,1,1,1)
        _Aspect ("Aspect (L / W)",   Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent+1"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                fixed4 color : COLOR;
            };

            fixed4 _Color;
            float  _Aspect;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // ── Capsule SDF ──────────────────────────────────────────────────
                // Map UV into a coordinate space where the capsule has radius 0.5.
                //   p.x ∈ [-aspect/2, +aspect/2]  (along the line)
                //   p.y ∈ [-0.5,      +0.5]       (across the line)
                //
                // Capsule: straight section half-length = max(aspect/2 - 0.5, 0),
                //          rounded end caps with radius 0.5.
                float aspect  = max(_Aspect, 1e-4);
                float2 p;
                p.x = (i.uv.x - 0.5) * aspect;
                p.y =  i.uv.y - 0.5;

                float halfLen = max(aspect * 0.5 - 0.5, 0.0);
                float2 q = float2(max(abs(p.x) - halfLen, 0.0), p.y);
                float  d = length(q) - 0.5;   // < 0 inside, 0 on surface, > 0 outside

                // ── Smooth anti-aliasing (~1-pixel transition via smoothstep) ────
                float fw    = max(fwidth(d), 1e-6);
                float alpha = 1.0 - smoothstep(0.0, fw * 1.5, d);

                fixed4 col = i.color;
                col.a *= alpha;
                return col;
            }
            ENDCG
        }
    }

    FallBack "Transparent/VertexLit"
}
