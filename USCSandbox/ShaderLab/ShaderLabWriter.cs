using AssetRipper.Primitives;
using System.Globalization;
using System.Collections.Generic;
using USCSandbox.Extras;
using USCSandbox.Processor;

namespace USCSandbox.ShaderLab
{
    public class ShaderLabWriter
    {
        private readonly StringBuilderIndented _sb = new StringBuilderIndented();

        private HlslWriter _hlslWriter;

        private List<string> _keywords = [];
        public ShaderFeatureFilterMode ShaderFeatureMode { get; set; } = ShaderFeatureFilterMode.Partial;
        
        public ShaderLabWriter(BlobManager blobManager, UnityVersion engVer, GPUPlatform platformId)
        {
            _hlslWriter = new HlslWriter(blobManager, engVer, platformId, 3);
        }
        
        public void SetKeywords(params IEnumerable<string> keywords)
        {
            _keywords = keywords.ToList();
        }
        
        public string Write(ShaderAst shader)
        {
            _sb.Clear();

            _sb.AppendLine($"Shader \"{shader.Name}\" {{");
            _sb.Indent();
            WriteProperties(shader.Properties);
            WriteSubShaders(shader.SubShaders);
            if (!string.IsNullOrEmpty(shader.FallbackName))
            {
                _sb.AppendLine($"Fallback \"{shader.FallbackName}\"");
            }
            _sb.Unindent();
            _sb.AppendLine("}");

            return _sb.ToString();
        }

        private void WriteProperties(List<PropertyAst> properties)
        {
            _sb.AppendLine("Properties {");
            _sb.Indent();
            foreach (var prop in properties)
            {
                _sb.Append("");
                foreach (var attr in prop.Attributes)
                {
                    _sb.AppendNoIndent($"[{attr}] ");
                }

                var flags = prop.Flags;
                if (flags.HasFlag(SerializedPropertyFlag.HideInInspector))
                    _sb.AppendNoIndent("[HideInInspector] ");
                if (flags.HasFlag(SerializedPropertyFlag.PerRendererData))
                    _sb.AppendNoIndent("[PerRendererData] ");
                if (flags.HasFlag(SerializedPropertyFlag.NoScaleOffset))
                    _sb.AppendNoIndent("[NoScaleOffset] ");
                if (flags.HasFlag(SerializedPropertyFlag.Normal))
                    _sb.AppendNoIndent("[Normal] ");
                if (flags.HasFlag(SerializedPropertyFlag.HDR))
                    _sb.AppendNoIndent("[HDR] ");
                if (flags.HasFlag(SerializedPropertyFlag.Gamma))
                    _sb.AppendNoIndent("[Gamma] ");

                var typeName = prop.Type switch
                {
                    SerializedPropertyType.Color => "Color",
                    SerializedPropertyType.Vector => "Vector",
                    SerializedPropertyType.Float => "Float",
                    SerializedPropertyType.Range => $"Range({prop.DefaultValues[1].ToString(CultureInfo.InvariantCulture)}, {prop.DefaultValues[2].ToString(CultureInfo.InvariantCulture)})",
                    SerializedPropertyType.Texture => prop.DefaultTextureDim switch
                    {
                        1 => "any",
                        2 => "2D",
                        3 => "3D",
                        4 => "Cube",
                        5 => "2DArray",
                        6 => "CubeArray",
                        _ => "any"
                    },
                    SerializedPropertyType.Int => "Int",
                    _ => "Float"
                };

                var value = prop.Type switch
                {
                    SerializedPropertyType.Color or SerializedPropertyType.Vector
                        => $"({prop.DefaultValues[0].ToString(CultureInfo.InvariantCulture)}, {prop.DefaultValues[1].ToString(CultureInfo.InvariantCulture)}, {prop.DefaultValues[2].ToString(CultureInfo.InvariantCulture)}, {prop.DefaultValues[3].ToString(CultureInfo.InvariantCulture)})",
                    SerializedPropertyType.Float or SerializedPropertyType.Range or SerializedPropertyType.Int
                        => prop.DefaultValues[0].ToString(CultureInfo.InvariantCulture),
                    SerializedPropertyType.Texture
                        => $"\"{prop.DefaultTextureName}\" {{}}",
                    _ => prop.DefaultValues[0].ToString(CultureInfo.InvariantCulture)
                };

                _sb.AppendNoIndent($"{prop.Name} (\"{prop.Description}\", {typeName}) = {value}\n");
            }
            _sb.Unindent();
            _sb.AppendLine("}");
        }

        private void WriteSubShaders(List<SubShaderAst> subShaders)
        {
            foreach (var sub in subShaders)
            {
                _sb.AppendLine("SubShader {");
                _sb.Indent();

                if (sub.Tags.Count > 0)
                {
                    _sb.AppendLine("Tags {");
                    _sb.Indent();
                    foreach (var tag in sub.Tags)
                    {
                        _sb.AppendLine($"\"{tag.Key}\"=\"{tag.Value}\"");
                    }
                    _sb.Unindent();
                    _sb.AppendLine("}");
                }

                if (sub.Lod != 0)
                {
                    _sb.AppendLine($"LOD {sub.Lod}");
                }

                WritePasses(sub.Passes);

                _sb.Unindent();
                _sb.AppendLine("}");
            }
        }

        private void WritePasses(List<PassAst> passes)
        {
            foreach (var pass in passes)
            {
                if (pass.IsUsePass && !string.IsNullOrEmpty(pass.UsePassName))
                {
                    _sb.AppendLine($"UsePass \"{pass.UsePassName}\"");
                    continue;
                }

                _sb.AppendLine("Pass {");
                _sb.Indent();

                if (pass.State != null)
                {
                    WritePassState(pass.State);
                }

                var cgBody = pass.WriteBody(_hlslWriter, _keywords, ShaderFeatureMode);
                if (!string.IsNullOrEmpty(cgBody))
                {
                    _sb.AppendLine("CGPROGRAM");
                    _sb.AppendNoIndent(cgBody);
                    _sb.AppendLine("ENDCG");
                    _sb.AppendLine("");
                }

                _sb.Unindent();
                _sb.AppendLine("}");
            }
        }

        private void WritePassState(PassStateAst state)
        {
            _sb.AppendLine($"Name \"{state.Name}\"");

            if (state.Lod != 0)
            {
                _sb.AppendLine($"LOD {state.Lod}");
            }

            if (state.RtSeparateBlend)
            {
                for (var i = 0; i < state.RtBlends.Count; i++)
                {
                    WritePassRtBlend(state.RtBlends[i], i);
                }
            }
            else if (state.RtBlends.Count > 0)
            {
                WritePassRtBlend(state.RtBlends[0], -1);
            }

            if (state.AlphaToMask > 0f)
            {
                _sb.AppendLine("AlphaToMask On");
            }
            if (state.ZClip == ZClip.On)
            {
                _sb.AppendLine("ZClip On");
            }
            if (state.ZTest != ZTest.None && state.ZTest != ZTest.LEqual)
            {
                _sb.AppendLine($"ZTest {state.ZTest}");
            }
            if (state.ZWrite != ZWrite.On)
            {
                _sb.AppendLine($"ZWrite {state.ZWrite}");
            }
            if (state.CullMode != CullMode.Back)
            {
                _sb.AppendLine($"Cull {state.CullMode}");
            }
            if (state.OffsetFactor != 0f || state.OffsetUnits != 0f)
            {
                _sb.AppendLine($"Offset {state.OffsetFactor}, {state.OffsetUnits}");
            }

            bool writeStencil = state.StencilRef != 0.0 || state.StencilReadMask != 255.0 || state.StencilWriteMask != 255.0
                                || !(state.StencilOpPass == StencilOp.Keep && state.StencilOpFail == StencilOp.Keep && state.StencilOpZFail == StencilOp.Keep && state.StencilOpComp == StencilComp.Always)
                                || !(state.StencilOpFrontPass == StencilOp.Keep && state.StencilOpFrontFail == StencilOp.Keep && state.StencilOpFrontZFail == StencilOp.Keep && state.StencilOpFrontComp == StencilComp.Always)
                                || !(state.StencilOpBackPass == StencilOp.Keep && state.StencilOpBackFail == StencilOp.Keep && state.StencilOpBackZFail == StencilOp.Keep && state.StencilOpBackComp == StencilComp.Always);
            if (writeStencil)
            {
                _sb.AppendLine("Stencil {");
                _sb.Indent();
                if (state.StencilRef != 0.0)
                {
                    _sb.AppendLine($"Ref {state.StencilRef}");
                }
                if (state.StencilReadMask != 255.0)
                {
                    _sb.AppendLine($"ReadMask {state.StencilReadMask}");
                }
                if (state.StencilWriteMask != 255.0)
                {
                    _sb.AppendLine($"WriteMask {state.StencilWriteMask}");
                }
                if (state.StencilOpPass != StencilOp.Keep || state.StencilOpFail != StencilOp.Keep || state.StencilOpZFail != StencilOp.Keep || (state.StencilOpComp != StencilComp.Always && state.StencilOpComp != StencilComp.Disabled))
                {
                    _sb.AppendLine($"Comp {state.StencilOpComp}");
                    _sb.AppendLine($"Pass {state.StencilOpPass}");
                    _sb.AppendLine($"Fail {state.StencilOpFail}");
                    _sb.AppendLine($"ZFail {state.StencilOpZFail}");
                }
                if (state.StencilOpFrontPass != StencilOp.Keep || state.StencilOpFrontFail != StencilOp.Keep || state.StencilOpFrontZFail != StencilOp.Keep || (state.StencilOpFrontComp != StencilComp.Always && state.StencilOpFrontComp != StencilComp.Disabled))
                {
                    _sb.AppendLine($"CompFront {state.StencilOpFrontComp}");
                    _sb.AppendLine($"PassFront {state.StencilOpFrontPass}");
                    _sb.AppendLine($"FailFront {state.StencilOpFrontFail}");
                    _sb.AppendLine($"ZFailFront {state.StencilOpFrontZFail}");
                }
                if (state.StencilOpBackPass != StencilOp.Keep || state.StencilOpBackFail != StencilOp.Keep || state.StencilOpBackZFail != StencilOp.Keep || (state.StencilOpBackComp != StencilComp.Always && state.StencilOpBackComp != StencilComp.Disabled))
                {
                    _sb.AppendLine($"CompBack {state.StencilOpBackComp}");
                    _sb.AppendLine($"PassBack {state.StencilOpBackPass}");
                    _sb.AppendLine($"FailBack {state.StencilOpBackFail}");
                    _sb.AppendLine($"ZFailBack {state.StencilOpBackZFail}");
                }
                _sb.Unindent();
                _sb.AppendLine("}");
            }

            bool writeFog = state.FogMode != FogMode.Unknown || state.FogDensity != 0.0 || state.FogStart != 0.0 || state.FogEnd != 0.0
                            || !(state.FogColorX == 0.0 && state.FogColorY == 0.0 && state.FogColorZ == 0.0 && state.FogColorW == 0.0);
            if (writeFog)
            {
                _sb.AppendLine("Fog {");
                _sb.Indent();
                if (state.FogMode != FogMode.Unknown)
                {
                    _sb.AppendLine($"Mode {state.FogMode}");
                }
                if (state.FogColorX != 0.0 || state.FogColorY != 0.0 || state.FogColorZ != 0.0 || state.FogColorW != 0.0)
                {
                    _sb.AppendLine($"Color ({state.FogColorX.ToString(CultureInfo.InvariantCulture)},{state.FogColorY.ToString(CultureInfo.InvariantCulture)},{state.FogColorZ.ToString(CultureInfo.InvariantCulture)},{state.FogColorW.ToString(CultureInfo.InvariantCulture)})");
                }
                if (state.FogDensity != 0.0)
                {
                    _sb.AppendLine($"Density {state.FogDensity.ToString(CultureInfo.InvariantCulture)}");
                }
                if (state.FogStart != 0.0 || state.FogEnd != 0.0)
                {
                    _sb.AppendLine($"Range {state.FogStart.ToString(CultureInfo.InvariantCulture)}, {state.FogEnd.ToString(CultureInfo.InvariantCulture)}");
                }
                _sb.Unindent();
                _sb.AppendLine("}");
            }

            if (state.LightingOn)
            {
                _sb.AppendLine("Lighting On");
            }

            if (state.Tags.Count > 0)
            {
                _sb.AppendLine("Tags {");
                _sb.Indent();
                foreach (var tag in state.Tags)
                {
                    _sb.AppendLine($"\"{tag.Key}\"=\"{tag.Value}\"");
                }
                _sb.Unindent();
                _sb.AppendLine("}");
            }
        }

        private void WritePassRtBlend(RtBlendAst rtBlend, int index)
        {
            var srcBlend = rtBlend.SrcBlend;
            var destBlend = rtBlend.DestBlend;
            var srcBlendAlpha = rtBlend.SrcBlendAlpha;
            var destBlendAlpha = rtBlend.DestBlendAlpha;
            var blendOp = rtBlend.BlendOp;
            var blendOpAlpha = rtBlend.BlendOpAlpha;
            var colMask = rtBlend.ColorMask;

            if (srcBlend != BlendMode.One || destBlend != BlendMode.Zero || srcBlendAlpha != BlendMode.One || destBlendAlpha != BlendMode.Zero)
            {
                _sb.Append("");
                _sb.AppendNoIndent("Blend ");
                if (index != -1)
                {
                    _sb.AppendNoIndent($"{index} ");
                }
                _sb.AppendNoIndent($"{srcBlend} {destBlend}");
                if (srcBlendAlpha != BlendMode.One || destBlendAlpha != BlendMode.Zero)
                {
                    _sb.AppendNoIndent($", {srcBlendAlpha} {destBlendAlpha}");
                }
                _sb.AppendNoIndent("\n");
            }

            if (blendOp != BlendOp.Add || blendOpAlpha != BlendOp.Add)
            {
                _sb.Append("");
                _sb.AppendNoIndent("BlendOp ");
                if (index != -1)
                {
                    _sb.AppendNoIndent($"{index} ");
                }
                _sb.AppendNoIndent($"{blendOp}");
                if (blendOpAlpha != BlendOp.Add)
                {
                    _sb.AppendNoIndent($", {blendOpAlpha}");
                }
                _sb.AppendNoIndent("\n");
            }

            if (colMask != ColorWriteMask.All)
            {
                _sb.Append("");
                _sb.AppendNoIndent("ColorMask ");
                if (colMask == ColorWriteMask.None)
                {
                    _sb.AppendNoIndent("0");
                }
                else
                {
                    if ((colMask & ColorWriteMask.Red) == ColorWriteMask.Red)
                    {
                        _sb.AppendNoIndent("R");
                    }
                    if ((colMask & ColorWriteMask.Green) == ColorWriteMask.Green)
                    {
                        _sb.AppendNoIndent("G");
                    }
                    if ((colMask & ColorWriteMask.Blue) == ColorWriteMask.Blue)
                    {
                        _sb.AppendNoIndent("B");
                    }
                    if ((colMask & ColorWriteMask.Alpha) == ColorWriteMask.Alpha)
                    {
                        _sb.AppendNoIndent("A");
                    }
                }
                if (index != -1)
                {
                    _sb.AppendNoIndent($" {index}");
                }
                _sb.AppendNoIndent("\n");
            }
        }
    }
}