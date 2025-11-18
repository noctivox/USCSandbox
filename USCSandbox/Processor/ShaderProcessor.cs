using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.Converter;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.DirectX;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;
using AssetRipper.Primitives;
using AssetsTools.NET;
using AssetsTools.NET.Extra.Decompressors.LZ4;
using System.Globalization;
using System.Text;
using USCSandbox.Extras;
using USCSandbox.ShaderLab;

namespace USCSandbox.Processor;

internal class ShaderProcessor
{
    public GPUPlatform PlatformId;
    public UnityVersion EngVer;
    public BlobManager BlobManager;
    
    private AssetTypeValueField _shaderBf;
    private StringBuilderIndented _sb;

    public ShaderProcessor(AssetTypeValueField shaderBf, UnityVersion engVer, GPUPlatform platformId)
    {
        EngVer = engVer;
        _shaderBf = shaderBf;
        PlatformId = platformId;
        _sb = new StringBuilderIndented();
    }
        
    public void Process()
    {
        var platforms = _shaderBf["platforms.Array"].Select(i => i.AsInt).ToList();
        var offsets = _shaderBf["offsets.Array"]
            .Select(i => i["Array"]
                .Select(j => j.AsUInt)
                .ToList()
            )
            .ToList();
        var compressedLengths = _shaderBf["compressedLengths.Array"]
            .Select(i => i["Array"]
                .Select(j => j.AsUInt)
                .ToList()
            )
            .ToList();
        var decompressedLengths = _shaderBf["decompressedLengths.Array"]
            .Select(i => i["Array"]
                .Select(j => j.AsUInt)
                .ToList()
            )
            .ToList();
        var compressedBlob = _shaderBf["compressedBlob.Array"].AsByteArray;


        var selectedIndex = platforms.IndexOf((int)PlatformId);
        var selectedOffsets = offsets[selectedIndex];
        var selectedCompressedLength = compressedLengths[selectedIndex];
        var selectedDecompressedLength = decompressedLengths[selectedIndex];

        using var ms = new MemoryStream(compressedBlob);
        var blobs = new List<byte[]>();
        for (int i = 0; i < selectedOffsets.Count; i++)
        {
            var decompressedBlob = new byte[selectedDecompressedLength[i]];
            var lz4Decoder = new Lz4DecoderStream(ms);
            ms.Seek((int)selectedOffsets[i], SeekOrigin.Begin);
            lz4Decoder.Read(decompressedBlob, 0, (int)selectedDecompressedLength[i]);
            blobs.Add(decompressedBlob);
            lz4Decoder.Dispose();
        }

        BlobManager = new BlobManager(blobs, EngVer);
    }
    
    public ShaderAst BuildAst()
    {
        var parsedForm = _shaderBf["m_ParsedForm"];
        var name = parsedForm["m_Name"].AsString;

        var shaderAst = new ShaderAst
        {
            Name = name,
            Properties = BuildPropertiesAst(parsedForm["m_PropInfo"]),
            SubShaders = BuildSubShadersAst(parsedForm),
            FallbackName = string.IsNullOrEmpty(parsedForm["m_FallbackName"].AsString) ? null : parsedForm["m_FallbackName"].AsString
        };

        return shaderAst;
    }

    private List<PropertyAst> BuildPropertiesAst(AssetTypeValueField propInfo)
    {
        var list = new List<PropertyAst>();
        var props = propInfo["m_Props.Array"];
        foreach (var prop in props)
        {
            var propertyAst = new PropertyAst();

            var attributes = prop["m_Attributes.Array"];
            foreach (var attribute in attributes)
            {
                propertyAst.Attributes.Add(attribute.AsString);
            }

            propertyAst.Flags = (SerializedPropertyFlag)prop["m_Flags"].AsUInt;
            propertyAst.Name = prop["m_Name"].AsString;
            propertyAst.Description = prop["m_Description"].AsString;
            propertyAst.Type = (SerializedPropertyType)prop["m_Type"].AsInt;
            propertyAst.DefaultValues = new float[]
            {
                prop["m_DefValue[0]"].AsFloat,
                prop["m_DefValue[1]"].AsFloat,
                prop["m_DefValue[2]"].AsFloat,
                prop["m_DefValue[3]"].AsFloat
            };
            propertyAst.DefaultTextureName = prop["m_DefTexture.m_DefaultName"].AsString;
            propertyAst.DefaultTextureDim = prop["m_DefTexture.m_TexDim"].AsInt;

            list.Add(propertyAst);
        }
        return list;
    }

    private List<SubShaderAst> BuildSubShadersAst(AssetTypeValueField parsedForm)
    {
        var list = new List<SubShaderAst>();
        var subshaders = parsedForm["m_SubShaders.Array"];
        foreach (var subshader in subshaders)
        {
            var subAst = new SubShaderAst();

            var tags = subshader["m_Tags"]["tags.Array"];
            foreach (var tag in tags)
            {
                subAst.Tags.Add(new TagAst { Key = tag["first"].AsString, Value = tag["second"].AsString });
            }

            subAst.Lod = subshader["m_LOD"].AsInt;

            subAst.Passes = BuildPassesAst(subshader);

            list.Add(subAst);
        }
        return list;
    }

    private List<PassAst> BuildPassesAst(AssetTypeValueField subshader)
    {
        var list = new List<PassAst>();
        var passes = subshader["m_Passes.Array"];
        foreach (var pass in passes)
        {
            var usePassName = pass["m_UseName"].AsString;
            if (!string.IsNullOrEmpty(usePassName))
            {
                list.Add(new PassAst { IsUsePass = true, UsePassName = usePassName });
                continue;
            }

            var passAst = new PassAst { IsUsePass = false };
            passAst.State = BuildPassStateAst(pass["m_State"]);

            var nameTable = pass["m_NameIndices.Array"]
                .ToDictionary(ni => ni["second"].AsInt, ni => ni["first"].AsString);

            var vertInfo = new SerializedProgramInfo(pass["progVertex"], nameTable);
            var fragInfo = new SerializedProgramInfo(pass["progFragment"], nameTable);

            var vertProgInfos = vertInfo.GetVertexProgramForPlatform(PlatformId);
            var fragProgInfos = fragInfo.GetFragmentProgramForPlatform(PlatformId);

            List<ShaderProgramBasket> baskets = [];
            for (var i = 0; i < vertProgInfos.Count; i++)
            {
                var parameterBlobIndex = vertInfo.ParameterBlobIndices.Count > 0 ? (int)vertInfo.ParameterBlobIndices[i] : -1;
                var subProg = TryGetSubProgram((int) vertProgInfos[i].BlobIndex);
                baskets.Add(new ShaderProgramBasket(
                    vertInfo,
                    vertProgInfos[i],
                    parameterBlobIndex,
                    subProg
                ));
            }
            for (var i = 0; i < fragProgInfos.Count; i++)
            {
                var parameterBlobIndex = fragInfo.ParameterBlobIndices.Count > 0 ? (int)fragInfo.ParameterBlobIndices[i] : -1;
                var subProg = TryGetSubProgram((int) fragProgInfos[i].BlobIndex);
                baskets.Add(new ShaderProgramBasket(
                    fragInfo,
                    fragProgInfos[i],
                    parameterBlobIndex,
                    subProg
                ));
            }
            
            passAst.Baskets = baskets;

            list.Add(passAst);
        }
        return list;
    }

    private PassStateAst BuildPassStateAst(AssetTypeValueField state)
    {
        var passState = new PassStateAst();

        passState.Name = state["m_Name"].AsString;
        passState.Lod = state["m_LOD"].AsInt;

        passState.RtSeparateBlend = state["rtSeparateBlend"].AsBool;
        if (passState.RtSeparateBlend)
        {
            for (var i = 0; i < 8; i++)
            {
                passState.RtBlends.Add(BuildRtBlendAst(state[$"rtBlend{i}"], i));
            }
        }
        else
        {
            passState.RtBlends.Add(BuildRtBlendAst(state["rtBlend0"], 0));
        }

        passState.AlphaToMask = state["alphaToMask.val"].AsFloat;
        passState.ZClip = (ZClip)(int)state["zClip.val"].AsFloat;
        passState.ZTest = (ZTest)(int)state["zTest.val"].AsFloat;
        passState.ZWrite = (ZWrite)(int)state["zWrite.val"].AsFloat;
        passState.CullMode = (CullMode)(int)state["culling.val"].AsFloat;
        passState.OffsetFactor = state["offsetFactor.val"].AsFloat;
        passState.OffsetUnits = state["offsetUnits.val"].AsFloat;
        passState.StencilRef = state["stencilRef.val"].AsFloat;
        passState.StencilReadMask = state["stencilReadMask.val"].AsFloat;
        passState.StencilWriteMask = state["stencilWriteMask.val"].AsFloat;
        passState.StencilOpPass = (StencilOp)(int)state["stencilOp.pass.val"].AsFloat;
        passState.StencilOpFail = (StencilOp)(int)state["stencilOp.fail.val"].AsFloat;
        passState.StencilOpZFail = (StencilOp)(int)state["stencilOp.zFail.val"].AsFloat;
        passState.StencilOpComp = (StencilComp)(int)state["stencilOp.comp.val"].AsFloat;
        passState.StencilOpFrontPass = (StencilOp)(int)state["stencilOpFront.pass.val"].AsFloat;
        passState.StencilOpFrontFail = (StencilOp)(int)state["stencilOpFront.fail.val"].AsFloat;
        passState.StencilOpFrontZFail = (StencilOp)(int)state["stencilOpFront.zFail.val"].AsFloat;
        passState.StencilOpFrontComp = (StencilComp)(int)state["stencilOpFront.comp.val"].AsFloat;
        passState.StencilOpBackPass = (StencilOp)(int)state["stencilOpBack.pass.val"].AsFloat;
        passState.StencilOpBackFail = (StencilOp)(int)state["stencilOpBack.fail.val"].AsFloat;
        passState.StencilOpBackZFail = (StencilOp)(int)state["stencilOpBack.zFail.val"].AsFloat;
        passState.StencilOpBackComp = (StencilComp)(int)state["stencilOpBack.comp.val"].AsFloat;
        passState.FogMode = (FogMode)(int)state["fogMode"].AsFloat;
        passState.FogColorX = state["fogColor.x.val"].AsFloat;
        passState.FogColorY = state["fogColor.y.val"].AsFloat;
        passState.FogColorZ = state["fogColor.z.val"].AsFloat;
        passState.FogColorW = state["fogColor.w.val"].AsFloat;
        passState.FogDensity = state["fogDensity.val"].AsFloat;
        passState.FogStart = state["fogStart.val"].AsFloat;
        passState.FogEnd = state["fogEnd.val"].AsFloat;

        passState.LightingOn = state["lighting"].AsBool;

        var tags = state["m_Tags"]["tags.Array"];
        foreach (var tag in tags)
        {
            passState.Tags.Add(new TagAst { Key = tag["first"].AsString, Value = tag["second"].AsString });
        }

        return passState;
    }

    private RtBlendAst BuildRtBlendAst(AssetTypeValueField rtBlend, int index)
    {
        return new RtBlendAst
        {
            Index = index,
            SrcBlend = (BlendMode)(int)rtBlend["srcBlend.val"].AsFloat,
            DestBlend = (BlendMode)(int)rtBlend["destBlend.val"].AsFloat,
            SrcBlendAlpha = (BlendMode)(int)rtBlend["srcBlendAlpha.val"].AsFloat,
            DestBlendAlpha = (BlendMode)(int)rtBlend["destBlendAlpha.val"].AsFloat,
            BlendOp = (BlendOp)(int)rtBlend["blendOp.val"].AsFloat,
            BlendOpAlpha = (BlendOp)(int)rtBlend["blendOpAlpha.val"].AsFloat,
            ColorMask = (ColorWriteMask)(int)rtBlend["colMask.val"].AsFloat
        };
    }
    
    private ShaderSubProgram? TryGetSubProgram(int blobIndex) {
        try
        {
            var subProg = BlobManager.GetShaderSubProgram(blobIndex);
            return subProg;
        } 
        catch (Exception e)
        {
            Logger.Error($"Error when parsing shader sub program {blobIndex}: {e}");
        }
        return null;
    }
}