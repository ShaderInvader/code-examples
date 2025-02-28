#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Outlines.Runtime
{
    [System.Serializable]
    public class OutlinePropertyData
    {
        public string name;

        public Color outlineColor;
        public Color altOutlineColor;
        public Texture2D outlineTexture;

        public Color overrideOutlineColor;
        public Color altOverrideOutlineColor;

        public float outlineSize = 0.1f;
        public float overrideOutlineSize = 0.1f;
    }

    [ExecuteAlways]
    public class OutlinePropertyController : MonoBehaviour
    {
        #if ODIN_INSPECTOR
        // ReSharper disable once ArrangeAttributes
        [DetailedInfoBox(
            "How to use outline property controller",
            "<b>Some important facts about the property controller:</b>\n"
            + "Outline Color and Alt Outline Color are immutable. They can be only set from the inspector and cannot be modified from code.\n"
            + "Highlight Outline Color and Low Hp Highlight Outline Color are mutable. They can be set from both the inspector and the code if needed.\n"
            + "The Low Hp versions can be used to change outline color based on circumstances, not only when player has low hp.\n"
            + "The blending between Highlight and Low Hp can be controlled in two ways:\n"
            + " - On a per-component basis using sliders\n"
            + " - Globally through static variables.\n"
            + "The blending uses Max() operation, so if the component has a value of 0.1 and global value is 0.5, the global one is used."
            + "The same applies to the Size and Low Hp Size properties.")]
        #endif

        [FormerlySerializedAs("OutlineTexture")]
        [SerializeField]
        private Texture2D outlineTexture;

        #if ODIN_INSPECTOR
        [FoldoutGroup("Base Color")]
        #else
        [Header("Base Outline Color")]
        #endif
        [FormerlySerializedAs("OutlineColor")]
        [SerializeField]
        private Color outlineColor = Color.black;

        #if ODIN_INSPECTOR
        [FoldoutGroup("Base Color")]
        #endif
        [FormerlySerializedAs("altOutlineColor"),FormerlySerializedAs("alternateOutlineColor")]
        [SerializeField]
        private Color lowHpOutlineColor = Color.black;

        #if ODIN_INSPECTOR
        [FoldoutGroup("Highlight Color")]
        #else
        [Header("Highlight Outline Color")]
        #endif
        [FormerlySerializedAs("overrideOutlineColor"),FormerlySerializedAs("OverrideColor")]
        [SerializeField]
        private Color highlightOutlineColor = Color.black;

        #if ODIN_INSPECTOR
        [FoldoutGroup("Highlight Color")]
        #endif
        [FormerlySerializedAs("altOverrideOutlineColor"),FormerlySerializedAs("AlternateOverrideColor")]
        [SerializeField]
        private Color lowHpHighlightOutlineColor = Color.black;

        #if ODIN_INSPECTOR
        [FoldoutGroup("Size")]
        #else
        [Header("Outline Size")]
        #endif
        [FormerlySerializedAs("OutlineSize")]
        [SerializeField]
        private float outlineSize = 0.1f;

        #if ODIN_INSPECTOR
        [FoldoutGroup("Size")]
        #endif
        [FormerlySerializedAs("overrideOutlineSize"),SerializeField]
        private float lowHpOutlineSize = 0.2f;

        [Header("Mixing Control")]
        [FormerlySerializedAs("AlternateColorMix"),FormerlySerializedAs("AlternateMix")]
        [Range(0.0f, 1.0f)]
        public float LowHpColorMix;

        [FormerlySerializedAs("OverrideColorMix"),FormerlySerializedAs("OverrideMix")]
        [Range(0.0f, 1.0f)]
        public float HighlightColorMix;

        [FormerlySerializedAs("OverrideSizeMix"),Range(0.0f, 1.0f)]
        public float LowHpSizeMix;

        public Color OutlineColor => outlineColor;

        public Color LowHpOutlineColor => lowHpOutlineColor;

        public Texture2D OutlineTexture { get => outlineTexture; set => outlineTexture = value; }

        public Color HighlightOutlineColor { get => highlightOutlineColor; set => highlightOutlineColor = value; }

        public Color LowHpHighlightOutlineColor { get => lowHpHighlightOutlineColor; set => lowHpHighlightOutlineColor = value; }

        public float OutlineSize => outlineSize;

        public float LowHpOutlineSize { get => lowHpOutlineSize; set => lowHpOutlineSize = value; }

        public static float                 GlobalSizeMix;
        public static float                 GlobalHighlightColorMix;
        public static float                 GlobalLowHpColorMix;
        private       Renderer              _renderer;

        [SerializeField]
        [HideInInspector]
        private int _originalLayer;

        private static readonly int OutlineSizeProperty       = Shader.PropertyToID("_OutlineSize");
        private static readonly int OutlineColorProperty      = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineTextureProperty    = Shader.PropertyToID("_OutlineTexture");
        private static readonly int OutlineTextureMixProperty = Shader.PropertyToID("_OutlineTextureMix");


        private MaterialPropertyBlock _propertyBlock;

        public void ResetLayer()
        {
            gameObject.layer = _originalLayer;
        }

        public void Initialize(float startOutlineSize)
        {
            outlineSize = startOutlineSize;
        }

        private void Start()
        {
            _renderer = GetComponent<Renderer>();
        }

        private void OnValidate()
        {
            _originalLayer = gameObject.layer;
        }

        private void OnWillRenderObject()
        {
            #if UNITY_EDITOR
			if(!_renderer)
				_renderer = GetComponent<Renderer>();
            #endif

            if (_renderer)
            {
                if (_propertyBlock == null)
                    _propertyBlock = new();
                
                //propertyBlock.Clear();

                if (_renderer.HasPropertyBlock())
                {
                    _renderer.GetPropertyBlock(_propertyBlock);
                }

                float size = Mathf.Lerp(outlineSize,
                                        lowHpOutlineSize,
                                        Mathf.Max(LowHpSizeMix, OutlinePropertyController.GlobalSizeMix));

                Color baseColor = Color.Lerp(outlineColor,
                                             lowHpOutlineColor,
                                             Mathf.Max(LowHpColorMix, OutlinePropertyController.GlobalLowHpColorMix));

                Color highlightColor = Color.Lerp(highlightOutlineColor,
                                                 lowHpHighlightOutlineColor,
                                                 Mathf.Max(LowHpColorMix, OutlinePropertyController.GlobalLowHpColorMix));

                Color color = Color.Lerp(baseColor,
                                         highlightColor,
                                         Mathf.Max(HighlightColorMix, OutlinePropertyController.GlobalHighlightColorMix));

                _propertyBlock.SetFloat(OutlinePropertyController.OutlineSizeProperty, size);
                _propertyBlock.SetColor(OutlinePropertyController.OutlineColorProperty, color);
                _propertyBlock.SetFloat(OutlinePropertyController.OutlineTextureMixProperty,
                                       Mathf.Max(HighlightColorMix, OutlinePropertyController.GlobalHighlightColorMix));
                _propertyBlock.SetTexture(OutlinePropertyController.OutlineTextureProperty,
                                          outlineTexture ? outlineTexture : Texture2D.whiteTexture);

                _renderer.SetPropertyBlock(_propertyBlock);
            }
            else
            {
                Debug.LogWarning("Renderer not found!");
            }
        }
    }
}
