using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Linq;
using USCSandbox.Processor;

namespace USCSandbox.ShaderLab
{
    public class ShaderAst
    {
        public string Name { get; set; } = string.Empty;
        public List<PropertyAst> Properties { get; set; } = new List<PropertyAst>();
        public List<SubShaderAst> SubShaders { get; set; } = new List<SubShaderAst>();
        public string? FallbackName { get; set; }
    }

    public class PropertyAst
    {
        public List<string> Attributes { get; set; } = new List<string>();
        public SerializedPropertyFlag Flags { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public SerializedPropertyType Type { get; set; }
        public float[] DefaultValues { get; set; } = new float[4];
        public string DefaultTextureName { get; set; } = string.Empty;
        public int DefaultTextureDim { get; set; }
    }

    public class TagAst
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class SubShaderAst
    {
        public int Lod { get; set; }
        public List<TagAst> Tags { get; set; } = new List<TagAst>();
        public List<PassAst> Passes { get; set; } = new List<PassAst>();
    }

    public class PassAst
    {
        public bool IsUsePass { get; set; }
        public string? UsePassName { get; set; }
        public PassStateAst? State { get; set; }
        
        [JsonIgnore]
        public List<ShaderProgramBasket> Baskets { get; set; } = new List<ShaderProgramBasket>();

        public string? WriteBody(HlslWriter hlslWriter, IEnumerable<string> keywords, ShaderFeatureFilterMode shaderFeatureMode)
        {
            return hlslWriter.GeneratePassCgBlock(Baskets, keywords, shaderFeatureMode);
        }
    }

    public class PassStateAst
    {
        public string Name { get; set; } = string.Empty;
        public int Lod { get; set; }
        public bool RtSeparateBlend { get; set; }
        public List<RtBlendAst> RtBlends { get; set; } = new List<RtBlendAst>();

        public float AlphaToMask { get; set; }
        public ZClip ZClip { get; set; }
        public ZTest ZTest { get; set; }
        public ZWrite ZWrite { get; set; }
        public CullMode CullMode { get; set; }
        public float OffsetFactor { get; set; }
        public float OffsetUnits { get; set; }

        public float StencilRef { get; set; }
        public float StencilReadMask { get; set; }
        public float StencilWriteMask { get; set; }
        public StencilOp StencilOpPass { get; set; }
        public StencilOp StencilOpFail { get; set; }
        public StencilOp StencilOpZFail { get; set; }
        public StencilComp StencilOpComp { get; set; }
        public StencilOp StencilOpFrontPass { get; set; }
        public StencilOp StencilOpFrontFail { get; set; }
        public StencilOp StencilOpFrontZFail { get; set; }
        public StencilComp StencilOpFrontComp { get; set; }
        public StencilOp StencilOpBackPass { get; set; }
        public StencilOp StencilOpBackFail { get; set; }
        public StencilOp StencilOpBackZFail { get; set; }
        public StencilComp StencilOpBackComp { get; set; }

        public FogMode FogMode { get; set; }
        public float FogColorX { get; set; }
        public float FogColorY { get; set; }
        public float FogColorZ { get; set; }
        public float FogColorW { get; set; }
        public float FogDensity { get; set; }
        public float FogStart { get; set; }
        public float FogEnd { get; set; }

        public bool LightingOn { get; set; }

        public List<TagAst> Tags { get; set; } = new List<TagAst>();
    }

    public class RtBlendAst
    {
        public int Index { get; set; } = -1;
        public BlendMode SrcBlend { get; set; }
        public BlendMode DestBlend { get; set; }
        public BlendMode SrcBlendAlpha { get; set; }
        public BlendMode DestBlendAlpha { get; set; }
        public BlendOp BlendOp { get; set; }
        public BlendOp BlendOpAlpha { get; set; }
        public ColorWriteMask ColorMask { get; set; }
    }
}