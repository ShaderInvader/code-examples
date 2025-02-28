Shader "Something Random/Core/Entity"
{
    Properties
    {
        [Header(Textures)][Space]
        _Color_Map ("Color Map", 2D) = "white" {}
        [NoScaleOffset] _Structure_Map ("Structure Map", 2D) = "bump" {}
        _Uv_Scrolling ("UV Scrolling", Vector) = (0,0,0,0)

        [Header(Effects)][Space]
        _BypassPredatorEffect ("Bypass Predator", Float) = 0
        _BypassPauseEffect ("Bypass Pause", Float) = 0

        [Header(Parameters)][Space]
        _Color_Tint ("Color Tint", Color) = (1,1,1,1)
        _Emission_Intensity ("Emission Intensity", Float) = 0
        _Occlusion_Intensity ("Occlusion Intensity", Float) = 1.0
        _MaskEmission_Intensity ("Mask Emission Intensity", Float) = 0.0
        [HDR]_MaskEmission_Color ("Mask Emission Color", Color) = (1,1,1,1)
        _OverrideFakeAmbientColor ("Override Fake Ambient Color", Color) = (1,1,1,1)
        _OverrideFakeAmbientMix ("Override Fake Ambient Mix", Float) = 0.0

        _TentacleFadeProgress ("Tentacle Fade Progress", Float) = 0.0
        _TentacleFadeSmoothness ("Tentacle Fade Smoothness", Float) = 0.0
        _TentacleFadeRadius ("Tentacle Fade Radius", Float) = 0.0
        _SpecialColor ("Special Color", Color) = (0,0,0,0)

        [Header(LightControl)][Space]
        _Main_Light_Offset ("Main Light Offset", Float) = 0
        _Main_Light_Smoothness ("Main Light Smoothness", Range(0, 1)) = 0.08
        _Additional_Lights_Offset ("Additional Lights Offset", Float) = 0
        _Additional_Lights_Smoothness ("Additional Lights Smoothness", Range(0, 1)) = 0.08
        _Override_Ambient_Color ("Override Ambient Color", Color) = (1,1,1,1)
        _Override_Ambient_Mix ("Override Ambient Mix", Range(0, 1)) = 0.0

        [Toggle(_ALLOW_COLOR_GRADING)] _Allow_Color_Grading ("Enable Alpha Test", Int) = 0
        [Toggle(_OVERRIDE_DISABLE_DESATURATION)] _Override_Disable_Desaturation ("Override Disable Desaturation", Int) = 0

        _Clip_Threshold ("__clip", Float) = 0.5
        _Surface("__surface", Float) = 0.0
        _Blend("__blend", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        _SrcBlend("__src", Float) = 1.0
        _DstBlend("__dst", Float) = 0.0
        _ZWrite("__zw", Float) = 1.0
        _ZTest("__zt", Float) = 4.0
        _StencilMask("Stencil mask", Int) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        HLSLINCLUDE

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            real4 _Color_Map_ST;
            real4 _Color_Map_TexelSize; // x = 1/width, y = 1/height, z = width, w = height

            real2 _Uv_Scrolling;

            real4 _Color_Tint;
            real _Emission_Intensity;
            real _Occlusion_Intensity;
            real _MaskEmission_Intensity;
            real4 _MaskEmission_Color;
            real _Clip_Threshold;

            half _TentacleFadeProgress;
            half _TentacleFadeSmoothness;
            half _TentacleFadeRadius;

            half4 _SpecialColor;

            real _Main_Light_Offset;
            real _Main_Light_Smoothness;
            real _Additional_Lights_Offset;
            real _Additional_Lights_Smoothness;

            real4 _Override_Ambient_Color;
            real _Override_Ambient_Mix;

            half _ShakeValue;
            half _ShakePower;
            half4 _FakeEntityAmbientColor;
            half _OverrideFakeAmbientMix;
            half4 _OverrideFakeAmbientColor;
        CBUFFER_END

        ENDHLSL

        Stencil
        {
            Ref [_StencilMask]
            Comp Greater
            Pass Keep
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            Blend[_SrcBlend][_DstBlend]
            ZWrite[_ZWrite]
            ZTest[_ZTest]
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 4.5

            #include "../Include/SRInput.hlsl"
            #include "../Include/SRCore.hlsl"
            #include "../Include/SRFog.hlsl"
            #include "../Include/SREffects.hlsl"
            #include "../Include/SRVertexEffects.hlsl"

            #pragma vertex ForwardPassVertex
            #pragma fragment ForwardPassFragment
            #pragma multi_compile_fog

            #pragma shader_feature_fragment _ALPHATEST_ON
            #pragma shader_feature_fragment _USE_SPECIAL_COLOR

            TEXTURE3D(_Vertex_Noise);
            SAMPLER(sampler_Vertex_Noise);

            VaryingsSR ForwardPassVertex(AttributesUV2 IN)
            {
                VaryingsSR OUT = (VaryingsSR)0;

                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normal);
                OUT.normalWS = normalInputs.normalWS;

                half tentacleFade = smoothstep(saturate(1.0 - _TentacleFadeProgress - _TentacleFadeSmoothness), saturate(1.0 -_TentacleFadeProgress + _TentacleFadeSmoothness), IN.uv.y);
                IN.position.xyz -= IN.normal * _TentacleFadeRadius * tentacleFade;

                VertexPositionInputs positionInputs = (VertexPositionInputs)0;
                GetVertexPositionWorld(IN.position.xyz, positionInputs);
                positionInputs.positionWS = ApplyVertexBreathingEffect(positionInputs.positionWS, TransformObjectToWorldNormal(IN.tangent), IN.distanceUV, _Vertex_Noise, sampler_Vertex_Noise);
                OUT.positionWS = positionInputs.positionWS;
                GetVertexPositionViewClipNDC(positionInputs.positionWS, positionInputs);
                OUT.positionOS = IN.position;
                positionInputs.positionVS.z += pow(length(positionInputs.positionVS.xy) * _ShakeValue, _ShakePower);
                OUT.positionCS = mul(GetViewToHClipMatrix(), float4(positionInputs.positionVS, 1.0));
                //OUT.positionCS = positionInputs.positionCS;

                OUT.viewDirWS = GetWorldSpaceViewDir(positionInputs.positionWS);

                OUTPUT_LIGHTMAP_UV(IN.lightmapUV, unity_LightmapST, OUT.lightmapUV);

                OUT.uv = TRANSFORM_TEX(IN.uv, _Color_Map);

                OUT.fogFactor.x = ComputeFogFactor(OUT.positionCS.z);
                OUT.fogFactor.yzw = VertexLighting(OUT.positionWS, OUT.normalWS);

                return OUT;
            }

            TEXTURE2D(_Color_Map);
            SAMPLER(sampler_Color_Map);

            TEXTURE2D(_Structure_Map);
            SAMPLER(sampler_Structure_Map);

            half3 _VolumeEntityAmbient;
            half _WorldCutoffHeight = 0.0;

            real4 ForwardPassFragment(VaryingsSR IN) : SV_Target
            {
                IN.uv += _Uv_Scrolling * _Time.y;
                ColorStructureOutput csOutput = DecodeColorStructureTextures(IN.uv, _Color_Map,
                                                                            sampler_Color_Map, _Structure_Map,
                                                                            sampler_Structure_Map, _Color_Tint,
                                                                            _Emission_Intensity, _Occlusion_Intensity, _Clip_Threshold);

                half tentacleFade = 1.0 - smoothstep(saturate(1.0 - _TentacleFadeProgress - _TentacleFadeSmoothness), saturate(1.0 - _TentacleFadeProgress + _TentacleFadeSmoothness), IN.uv.y);

                // Alpha Clipping
                if (_WorldCutoffHeight != 0.0)
                {
                    half cutoffHeightFactor = saturate(IN.positionWS.y - _WorldCutoffHeight);
                    clip(csOutput.albedo.a * tentacleFade * cutoffHeightFactor - 0.1);
                }
                else
                {
                    #ifdef _ALPHATEST_ON
                    clip(csOutput.albedo.a * tentacleFade - _Clip_Threshold);
                    #endif
                }

                SurfaceData surfaceData = InitializeSurfaceData(csOutput);
                InputData inputData = InitializeInputData(IN, csOutput.normal);

                real3 lightingColor = CalculateToonLighting(surfaceData, inputData,
                    _Main_Light_Offset, _Main_Light_Smoothness,
                _Additional_Lights_Offset, _Additional_Lights_Smoothness);

                half fakeAmbientIntensity = lerp(_FakeEntityAmbientColor.a, _OverrideFakeAmbientColor.a, _OverrideFakeAmbientMix);
                half3 fakeAmbientColor = lerp(_FakeEntityAmbientColor.rgb, _OverrideFakeAmbientColor.rgb, _OverrideFakeAmbientMix);
                lightingColor = lerp(lightingColor, fakeAmbientColor, fakeAmbientIntensity);

                real3 outputColor = csOutput.albedo * lightingColor + csOutput.albedo * csOutput.emissive;
                outputColor += csOutput.albedo.a * _MaskEmission_Intensity * _MaskEmission_Color;

                #ifdef _USE_SPECIAL_COLOR
                outputColor = lerp(outputColor, _SpecialColor.rgb, step(_SpecialColor.a, csOutput.albedo.a));
                #endif
                
                outputColor = MixCustomFog(outputColor, IN.fogFactor, IN.positionWS, IN.viewDirWS);

                outputColor = ApplyColorGrading(outputColor);
                outputColor = ApplyPredatorEffect(outputColor, IN.positionWS, IN.viewDirWS);
                outputColor = ApplyPauseEffect(outputColor);

                return real4(outputColor, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Shadows"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #include "../Include/SRInput.hlsl"
            #include "../Include/SRCore.hlsl"

            #pragma vertex ShadowPassVertex
	        #pragma fragment ShadowPassFragment

	        // Material Keywords
	        #pragma shader_feature_fragment _ALPHATEST_ON

	        // GPU Instancing
	        #pragma multi_compile_instancing

            struct AttributesShadows
            {
                float4 position   : POSITION;
                float3 normal     : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsShadows
            {
                float2 uv           : TEXCOORD0;
                float3 positionWS   : TEXCOORD1;
                float4 positionCS   : SV_POSITION;
            };

            // Shadow Casting Light geometric parameters. These variables are used when applying the shadow Normal Bias and are set by UnityEngine.Rendering.Universal.ShadowUtils.SetupShadowCasterConstantBuffer in com.unity.render-pipelines.universal/Runtime/ShadowUtils.cs
            // For Directional lights, _LightDirection is used when applying shadow Normal Bias.
            // For Spot lights and Point lights, _LightPosition is used to compute the actual light direction because it is different at each shadow caster geometry vertex.
            float3 _LightDirection;
            float3 _LightPosition;

            TEXTURE2D(_Color_Map);
            SAMPLER(sampler_Color_Map);

            float4 GetShadowPositionHClip(AttributesShadows IN, out float3 positionWS)
            {
                positionWS = TransformObjectToWorld(IN.position.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normal);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif

                return positionCS;
            }

            VaryingsShadows ShadowPassVertex(AttributesShadows IN)
            {
                VaryingsShadows output;
                UNITY_SETUP_INSTANCE_ID(IN);

                output.uv = TRANSFORM_TEX(IN.uv, _Color_Map);
                output.positionCS = GetShadowPositionHClip(IN, output.positionWS);
                return output;
            }

            half4 ShadowPassFragment(VaryingsShadows input) : SV_TARGET
            {
                half4 color = SAMPLE_TEXTURE2D(_Color_Map, sampler_Color_Map, input.uv).a * _Color_Tint;
            #if defined(_ALPHATEST_ON)
                clip(color.a - _Clip_Threshold);
            #endif
                return 0;
            }

            ENDHLSL
        }

        Pass
        {
            Name "META"
            Tags { "LightMode"="Meta" }

            Cull Off

            HLSLPROGRAM

            #pragma vertex MetaPassVertex
            #pragma fragment MetaPassFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            struct AttributesMeta
            {
                float4 position   : POSITION;
		        float3 normalOS     : NORMAL;
		        float2 uv0          : TEXCOORD0;
		        float2 uv1          : TEXCOORD1;
		        float2 uv2          : TEXCOORD2;
            };

            struct VaryingsMeta
            {
                float4 positionCS   : SV_POSITION;
		        float2 uv           : TEXCOORD0;
            };

            VaryingsMeta MetaPassVertex(AttributesMeta IN)
            {
                VaryingsMeta OUT = (VaryingsMeta)0;

                OUT.positionCS = MetaVertexPosition(IN.position, IN.uv1, IN.uv2, unity_LightmapST, unity_DynamicLightmapST);
                OUT.uv = TRANSFORM_TEX(IN.uv0, _Color_Map);

                return OUT;
            }

            TEXTURE2D(_Color_Map);
            SAMPLER(sampler_Color_Map);

            real4 MetaPassFragment(VaryingsMeta IN) : SV_Target
            {
                real4 color = SAMPLE_TEXTURE2D(_Color_Map, sampler_Color_Map, IN.uv);

                MetaInput meta;
                meta.Albedo = color * _Color_Tint;
                meta.Emission = color * _Emission_Intensity;

                return MetaFragment(meta);
            }

            ENDHLSL
        }

        Pass
        {
            // Normal-map enabled Bakery-specific meta pass
            Name "META_BAKERY"
            Tags { "LightMode" = "Meta" }
            Cull Off
            HLSLPROGRAM

            #pragma vertex MetaPassVertex
            #pragma fragment MetaPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            // Include Bakery meta pass
            #include "../Include/SRCore.hlsl"

            struct AttributesMetaBakery
            {
                float3 pos : POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float3 normal : NORMAL;
            };

            struct VaryingsMetaBakery
            {
                float4 pos       : SV_POSITION;
                float2 uv        : TEXCOORD0;
                float3 normal    : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
            };

            VaryingsMetaBakery MetaPassVertex(AttributesMetaBakery v)
            {
                VaryingsMetaBakery OUT = (VaryingsMetaBakery)0;
                OUT.pos = float4(((v.uv1.xy * unity_LightmapST.xy + unity_LightmapST.zw) * 2.0 - 1.0) * float2(1.0, -1.0), 0.5, 1.0);
                OUT.uv = v.uv0;
                OUT.normal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal).xyz);
                OUT.viewDirWS = GetWorldSpaceViewDir(mul((float3x3)unity_ObjectToWorld, v.pos));

                return OUT;
            }

            TEXTURE2D(_Color_Map);
            SAMPLER(sampler_Color_Map);

            TEXTURE2D(_Structure_Map);
            SAMPLER(sampler_Structure_Map);

            TEXTURE2D(bestFitNormalMap);

            float3 EncodeNormalBestFit(float3 n)
            {
                float3 nU = abs(n);
                float maxNAbs = max(nU.z, max(nU.x, nU.y));
                float2 TC = nU.z<maxNAbs? (nU.y<maxNAbs? nU.yz : nU.xz) : nU.xy;
                TC = TC.x<TC.y? TC.yx : TC.xy;
                TC.y /= TC.x;

                n /= maxNAbs;
                float fittingScale = bestFitNormalMap.Load(int3(TC.x*1023, TC.y*1023, 0)).a;
                n *= fittingScale;
                return n*0.5+0.5;
            }

            float4 MetaPassFragment(VaryingsMetaBakery IN) : SV_Target
            {
                MetaInput meta = (MetaInput)0;

                // Output custom normal to use with Bakery's "Baked Normal Map" mode
                if (unity_MetaFragmentControl.z)
                {
                    // Calculate custom normal
                    real4 sampledStructure = SAMPLE_TEXTURE2D(_Structure_Map, sampler_Structure_Map, IN.uv);
                    //real3 decodedNormal = DecodeNormal(sampledStructure.rg);
                    real3 worldNormal = CalculateNormalWithoutTangent(sampledStructure.rg, IN.normal, IN.viewDirWS, IN.uv);// TransformNormalMapToWorld(IN, decodedNormal);

                    // Output
                    return float4(EncodeNormalBestFit(worldNormal), 1.0);
                }

                // Regular Unity meta pass
                real4 color = SAMPLE_TEXTURE2D(_Color_Map, sampler_Color_Map, IN.uv);

                meta.Albedo = color * _Color_Tint;
                meta.Emission = color * _Emission_Intensity;

                return MetaFragment(meta);
            }
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags{ "LightMode" = "MotionVectors"}
            Tags { "RenderType" = "Opaque" }

            ZWrite[_ZWrite]
            Cull[_Cull]

            HLSLPROGRAM
            #include "../Include/SRMotionVectorCore.hlsl"

            #pragma vertex vert
            #pragma fragment frag

            ENDHLSL
        }

        Pass
        {
            // This pass is used mostly for correct gizmos depth rendering
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _ALPHATEST_ON

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/UnlitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }
    CustomEditor "Shaders.EntityShaderGUI"
}
