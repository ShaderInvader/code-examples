using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static EditorCommon.Editor.CommonEditorGUI;
using static Shaders.ShaderGUIHelpers;

namespace Shaders
{
    public class EntityShaderGUI : ShaderGUI
    {
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Material target = (materialEditor.target as Material);
            if (target == null)
            {
                return;
            }

            Material[] targets = Array.ConvertAll(materialEditor.targets, x => (Material)x);

            EditorStyles.label.normal.textColor = Color.white;
            EditorStyles.label.hover.textColor = Color.white;

            DrawBackgroundColor("#FF8C7D");
            DrawCopyrightHeader("Core / Entity Shader");
            DrawMaterialRenderMode(targets);
            DrawColorStructureTextureFields(materialEditor, properties);
            DrawEmissionOcclusionAndMaskIntensityFields(materialEditor, properties);
            DrawEffectBypassFields(targets);

            using (new HighlightBoxVertical("Fake Ambient"))
            {
                GUILayout.Space(8.0f);
                MaterialProperty overrideFakeAmbientColor = FindProperty("_OverrideFakeAmbientColor", properties);
                MaterialProperty overrideFakeAmbientMix  = FindProperty("_OverrideFakeAmbientMix", properties);

                materialEditor.ColorProperty(overrideFakeAmbientColor, "Override Fake Ambient Color");
                materialEditor.FloatProperty(overrideFakeAmbientMix, "Override Fake Ambient Mix");
            }

            using (new HighlightBoxVertical("Tentacle Effect (needs alpha clipping)"))
            {
                GUILayout.Space(8.0f);
                MaterialProperty tentacleFadeProgress   = FindProperty("_TentacleFadeProgress", properties);
                MaterialProperty tentacleFadeSmoothness = FindProperty("_TentacleFadeSmoothness", properties);
                MaterialProperty tentacleFadeRadius    = FindProperty("_TentacleFadeRadius", properties);

                materialEditor.FloatProperty(tentacleFadeProgress, "Tentacle Fade Progress");
                materialEditor.FloatProperty(tentacleFadeSmoothness, "Tentacle Fade Smoothness");
                materialEditor.FloatProperty(tentacleFadeRadius, "Tentacle Fade Radius");
            }
            
            using (new HighlightBoxVertical("Special Color"))
            {
                GUILayout.Space(8.0f);
                LocalKeyword enableSpecialColor = new(target.shader, "_USE_SPECIAL_COLOR");

                MultiMaterialField(targets,
                    material => material.IsKeywordEnabled(enableSpecialColor),
                    value => EditorGUILayout.Toggle("Enable Special Color", value),
                    (material, value) => material.SetKeyword(enableSpecialColor, value),
                    out bool _);
                
                MaterialProperty specialColor = FindProperty("_SpecialColor", properties);
                materialEditor.ColorProperty(specialColor, "Special Color");
            }

            using (new HighlightBoxVertical("Lighting"))
            {
                GUILayout.Space(8.0f);
                GUILayout.Label("Main Light", EditorStyles.boldLabel);
                MaterialProperty mainLightOffset = FindProperty("_Main_Light_Offset", properties);
                MaterialProperty mainLightSmoothness = FindProperty("_Main_Light_Smoothness", properties);

                materialEditor.FloatProperty(mainLightOffset, "Main Light Offset");
                materialEditor.RangeProperty(mainLightSmoothness, "Main Light Smoothness");

                GUILayout.Space(8.0f);
                GUILayout.Label("Additional Lights", EditorStyles.boldLabel);
                MaterialProperty additionalLightsOffset     = FindProperty("_Additional_Lights_Offset", properties);
                MaterialProperty additionalLightsSmoothness = FindProperty("_Additional_Lights_Smoothness", properties);

                materialEditor.FloatProperty(additionalLightsOffset, "Additional Light Offset");
                materialEditor.RangeProperty(additionalLightsSmoothness, "Additional Light Smoothness");

                GUILayout.Space(8.0f);
                GUILayout.Label("Ambient Light", EditorStyles.boldLabel);
                MaterialProperty ambientLightColor = FindProperty("_Override_Ambient_Color", properties);
                MaterialProperty ambientLightMix = FindProperty("_Override_Ambient_Mix", properties);

                materialEditor.RangeProperty(ambientLightMix, "Override Ambient Mix");
                materialEditor.ColorProperty(ambientLightColor, "Override Ambient Color");
            }

            materialEditor.serializedObject.ApplyModifiedProperties();
        }
    }

}
