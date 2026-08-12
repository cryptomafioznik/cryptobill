// Вершинный цвет × HDR-интенсивность. Единственный шейдер мира.
//
// ЗАЧЕМ СВОЙ, а не Sprites/Default. Вершинный цвет меша хранится 8-битным и физически
// не может быть больше 1.0, а bloom ловит только то, что ЯРЧЕ 1.0 в HDR-буфере.
// Поэтому яркость выносится в материал: _Intensity > 1 делает объект источником
// свечения для пост-обработки (горячая кромка свечей, солнце, маркеры), _Intensity = 1
// оставляет обычную геометрию в точности как раньше.
//
// Лежит в Resources: шейдер, на который не ссылается ни один ассет, вырезается из
// билда, и Shader.Find на устройстве возвращает null — на маке при этом всё работает.
Shader "ChartRunner/VertexColorHDR"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 1.0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
            float _Intensity;
            CBUFFER_END

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                return half4(IN.color.rgb * _Intensity, IN.color.a);
            }
            ENDHLSL
        }
    }

    // Built-in фолбэк: если проект окажется вне URP, мир не должен стать розовым.
    Fallback "Sprites/Default"
}
