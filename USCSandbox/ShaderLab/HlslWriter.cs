using System.Text;
using System.IO;
using System.Linq;
using AssetRipper.Primitives;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.Converter;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.DirectX;
using USCSandbox.Processor;

namespace USCSandbox.ShaderLab;

public enum ShaderFeatureFilterMode
{
    None,
    Partial,
    Exact
}

public class HlslWriter
{
    private readonly BlobManager _blobManager;
    private readonly UnityVersion _engVer;
    private readonly GPUPlatform _platformId;
    private readonly int _depth;

    public HlslWriter(BlobManager blobManager, UnityVersion engVer, GPUPlatform platformId, int depth)
    {
        _blobManager = blobManager;
        _engVer = engVer;
        _platformId = platformId;
        _depth = depth;
    }

    public string GeneratePassCgBlock(IEnumerable<ShaderProgramBasket> baskets, IEnumerable<string>? keywords = null, ShaderFeatureFilterMode shaderFeatureMode = ShaderFeatureFilterMode.Partial)
    {
        var defineSb = new StringBuilder();
        var passSb = new StringBuilder();

        defineSb.AppendLine(new string(' ', _depth * 4));
        passSb.AppendLine(new string(' ', _depth * 4));
        var basketsInfo = baskets
            .Where(x => x != null)
            .OrderBy(x => x.SubProg.GetProgramType(_engVer))
            .ThenByDescending(x => x.SubProg.GlobalKeywords.Concat(x.SubProg.LocalKeywords).Count())
            .ToList();

        if (basketsInfo.Count == 0)
        {
            return "";
        }

        var keywordList = keywords?.ToList() ?? new List<string>(0);

        var subPrograms = basketsInfo.Select(x => x.SubProg).ToList();
        ShaderGpuProgramType[] vertTypes = [ShaderGpuProgramType.DX11VertexSM40, ShaderGpuProgramType.DX11VertexSM50, ShaderGpuProgramType.ConsoleVS];
        ShaderGpuProgramType[] fragTypes = [ShaderGpuProgramType.DX11PixelSM40, ShaderGpuProgramType.DX11PixelSM50, ShaderGpuProgramType.ConsoleFS];
        var firstVert = subPrograms.FirstOrDefault(x => vertTypes.Contains(x.GetProgramType(_engVer)));
        var firstFrag = subPrograms.FirstOrDefault(x => fragTypes.Contains(x.GetProgramType(_engVer)));

        if (firstVert is not null)
        {
            defineSb.Append(new string(' ', _depth * 4));
            defineSb.AppendLine("#pragma vertex vert");
        }

        if (firstFrag is not null)
        {
            defineSb.Append(new string(' ', _depth * 4));
            defineSb.AppendLine("#pragma fragment frag");
        }

        defineSb.AppendLine();

        var allKeywordsCombinations = subPrograms.Select(x => x.GlobalKeywords.Concat(x.LocalKeywords).Order()).ToList();
        HashSet<string> allUniqueKeywords = allKeywordsCombinations.SelectMany(x => x).ToHashSet();
        int leastKeywordsAmount = allKeywordsCombinations.Min(x => x.Count());
        List<string> mandatoryKeywords = [];

        List<string> optionalKeywords = new List<string>();
        foreach (string keyword in allUniqueKeywords)
        {
            if (allKeywordsCombinations.All(x => x.Contains(keyword)))
            {
                defineSb.Append(new string(' ', _depth * 4));
                defineSb.AppendLine($"#pragma multi_compile {keyword}");
                mandatoryKeywords.Add(keyword);
            }
            else
            {
                optionalKeywords.Add(keyword);
            }
        }

        if (leastKeywordsAmount > mandatoryKeywords.Count)
        {
            var leastAmountCombinations = allKeywordsCombinations
                .Where(x => x.Count() == leastKeywordsAmount)
                .Select(x => x.Except(mandatoryKeywords).ToList())
                .ToList();
            int multiCompileKeywordIndex = 0;
            while (multiCompileKeywordIndex < leastKeywordsAmount - mandatoryKeywords.Count)
            {
                var multiCompileKeywords = leastAmountCombinations
                    .Select(x => x[multiCompileKeywordIndex])
                    .ToHashSet();

                defineSb.Append(new string(' ', _depth * 4));
                defineSb.AppendLine($"#pragma multi_compile {string.Join(" ", multiCompileKeywords)}");
                optionalKeywords = optionalKeywords.Except(multiCompileKeywords).ToList();
                multiCompileKeywordIndex++;
            }
        }

        foreach (string keyword in optionalKeywords)
        {
            defineSb.Append(new string(' ', _depth * 4));
            defineSb.AppendLine($"#pragma shader_feature {keyword}");
        }

        bool encounterdVert = false;
        bool encounterdFrag = false;

        var declaredCBufs = subPrograms
            .Select(x => String.Join("-", x.GlobalKeywords.Concat(x.LocalKeywords).Order()))
            .Distinct()
            .ToDictionary(x => x, _ => new HashSet<string>());

        var lastVertext = basketsInfo.LastOrDefault(x => vertTypes.Contains(x.SubProg.GetProgramType(_engVer)));
        var lastFragment = basketsInfo.LastOrDefault(x => fragTypes.Contains(x.SubProg.GetProgramType(_engVer)));

        bool noVertexVariants = subPrograms.Count(x => vertTypes.Contains(x.GetProgramType(_engVer))) <= 1;
        bool noFragmentVariants = subPrograms.Count(x => fragTypes.Contains(x.GetProgramType(_engVer))) <= 1;

        var passDefinedKeywords = new HashSet<string>(allUniqueKeywords);
        var shaderFeatureKeywordsSet = new HashSet<string>(optionalKeywords);
        var multiCompileKeywordsSet = new HashSet<string>(mandatoryKeywords);

        bool MatchesFilter(ShaderSubProgram sp)
        {
            var progKeywords = sp.GlobalKeywords.Concat(sp.LocalKeywords).ToHashSet();
            var progMulti = new HashSet<string>(progKeywords.Intersect(multiCompileKeywordsSet));
            var progFeature = new HashSet<string>(progKeywords.Intersect(shaderFeatureKeywordsSet));

            var effMulti = new HashSet<string>(keywordList.Intersect(multiCompileKeywordsSet));
            var effFeature = new HashSet<string>(keywordList.Intersect(shaderFeatureKeywordsSet));

            bool multiOk = progMulti.SetEquals(effMulti);

            bool featureOk = true; 
            if (effFeature.Count > 0)
            {
                var intersectCount = progFeature.Intersect(effFeature).Count();
                featureOk = shaderFeatureMode switch
                {
                    ShaderFeatureFilterMode.None => true,
                    ShaderFeatureFilterMode.Partial => intersectCount > 0,
                    ShaderFeatureFilterMode.Exact => progFeature.SetEquals(effFeature),
                    _ => true
                };
            }

            if (multiOk && featureOk)
            {
                Logger.Info($"Matched: {string.Join(" && ", progMulti)}, {string.Join(" && ", progFeature)}");
            }

            return multiOk && featureOk;
        }

        var filteredBasketsInfo = keywordList.Count == 0
            ? basketsInfo
            : basketsInfo.Where(b => MatchesFilter(b.SubProg)).ToList();

        if (filteredBasketsInfo.Count == 0)
        {
            filteredBasketsInfo = basketsInfo;
        }

        var filteredSubPrograms = filteredBasketsInfo.Select(x => x.SubProg).ToList();
        var lastVertexFiltered = filteredBasketsInfo.LastOrDefault(x => vertTypes.Contains(x.SubProg.GetProgramType(_engVer)));
        var lastFragmentFiltered = filteredBasketsInfo.LastOrDefault(x => fragTypes.Contains(x.SubProg.GetProgramType(_engVer)));
        bool noVertexVariantsFiltered = filteredSubPrograms.Count(x => vertTypes.Contains(x.GetProgramType(_engVer))) <= 1;
        bool noFragmentVariantsFiltered = filteredSubPrograms.Count(x => fragTypes.Contains(x.GetProgramType(_engVer))) <= 1;

        foreach (var basket in filteredBasketsInfo)
        {
            var structSb = new StringBuilder();
            var cbufferSb = new StringBuilder();
            var texSb = new StringBuilder();
            var memeSb = new StringBuilder();
            var codeSb = new StringBuilder();

            var progInfo = basket.ProgramInfo;
            var subProgInfo = basket.SubProgramInfo;
            var index = basket.ParameterBlobIndex;

            var subProg = basket.SubProg ?? _blobManager.GetShaderSubProgram((int)subProgInfo.BlobIndex);
            
            ShaderParams param;
            if (index != -1)
            {
                param = _blobManager.GetShaderParams(index);
            }
            else
            {
                param = subProg.ShaderParams;
            }

            param.CombineCommon(progInfo);

            var programType = subProg.GetProgramType(_engVer);
            var graphicApi = _platformId;

            var progKeywords = subProg.GlobalKeywords.Concat(subProg.LocalKeywords).Order().ToArray();

            if ((!noVertexVariantsFiltered && basket == lastVertexFiltered) || (!noFragmentVariantsFiltered && basket == lastFragmentFiltered))
            {
                structSb.AppendLine();
                structSb.Append(new string(' ', _depth * 4));
                structSb.Append("#else");
            }
            else if ((!noVertexVariantsFiltered && programType == ShaderGpuProgramType.DX11VertexSM40)
                     || (!noFragmentVariantsFiltered && programType == ShaderGpuProgramType.DX11PixelSM40)
                     || (!noVertexVariantsFiltered && programType == ShaderGpuProgramType.DX11VertexSM50)
                     || (!noFragmentVariantsFiltered && programType == ShaderGpuProgramType.DX11PixelSM50)
            )
            {
                string preprocessorDirective;
                if ((!encounterdVert && programType == ShaderGpuProgramType.DX11VertexSM40)
                    || (!encounterdFrag && programType == ShaderGpuProgramType.DX11PixelSM40)
                    || (!noVertexVariantsFiltered && programType == ShaderGpuProgramType.DX11VertexSM50)
                    || (!noFragmentVariantsFiltered && programType == ShaderGpuProgramType.DX11PixelSM50)
                )
                {
                    preprocessorDirective = "if";
                    if (programType is ShaderGpuProgramType.DX11VertexSM40 or ShaderGpuProgramType.DX11VertexSM50)
                    {
                        encounterdVert = true;
                    }
                    else
                    {
                        encounterdFrag = true;
                        structSb.Append(new string(' ', _depth * 4));
                        structSb.AppendLine("#endif");
                        structSb.AppendLine();
                    }
                }
                else
                {
                    preprocessorDirective = "elif";
                }

                structSb.AppendLine();
                structSb.Append(new string(' ', _depth * 4));
                structSb.Append($"#{preprocessorDirective} {string.Join(" && ", progKeywords)}");
            }

            cbufferSb.Append(new string(' ', _depth * 4));
            cbufferSb.AppendLine($"// CBs for {programType}");

            foreach (ConstantBuffer cbuffer in param.ConstantBuffers)
            {
                cbufferSb.Append(WritePassCBuffer(param, declaredCBufs[string.Join("-", progKeywords)], cbuffer, _depth));
            }

            texSb.Append(new string(' ', _depth * 4));
            texSb.AppendLine($"// Textures for {programType}");

            texSb.Append(WritePassTextures(param, declaredCBufs[string.Join("-", progKeywords)], _depth));

            switch (programType)
            {
                case ShaderGpuProgramType.DX11VertexSM40:
                case ShaderGpuProgramType.DX11PixelSM40:
                case ShaderGpuProgramType.DX11VertexSM50:
                case ShaderGpuProgramType.DX11PixelSM50:
                {
                    var conv = new USCShaderConverter();
                    conv.LoadDirectXCompiledShader(new MemoryStream(subProg.ProgramData), graphicApi, _engVer);
                    conv.ConvertDxShaderToUShaderProgram();
                    conv.ApplyMetadataToProgram(subProg, param, _engVer);

                    UShaderFunctionToHLSL hlslConverter = new UShaderFunctionToHLSL(conv.ShaderProgram!, _depth);

                    structSb.AppendLine();
                    codeSb.AppendLine();

                    structSb.Append(hlslConverter.WriteStruct());
                    structSb.AppendLine();
                    codeSb.Append(hlslConverter.WriteFunction());

                    break;
                }
                case ShaderGpuProgramType.ConsoleVS:
                case ShaderGpuProgramType.ConsoleFS:
                {
                    var conv = new USCShaderConverter();
                    conv.LoadUnityNvnShader(new MemoryStream(subProg.ProgramData), graphicApi, _engVer);
                    conv.ConvertNvnShaderToUShaderProgram(programType);
                    conv.ApplyMetadataToProgram(subProg, param, _engVer);

                    UShaderFunctionToHLSL hlslConverter = new UShaderFunctionToHLSL(conv.ShaderProgram!, _depth);
                    if ((!encounterdVert && programType == ShaderGpuProgramType.ConsoleVS)
                        || (!encounterdFrag && programType == ShaderGpuProgramType.ConsoleFS))
                    {
                        structSb.Append(hlslConverter.WriteStruct());
                        structSb.AppendLine();
                        if (programType == ShaderGpuProgramType.ConsoleVS)
                            encounterdVert = true;
                        else
                            encounterdFrag = true;
                    }

                    codeSb.Append(new string(' ', _depth * 4));
                    codeSb.AppendLine("// Keywords: " + string.Join(", ", progKeywords));
                    codeSb.Append(hlslConverter.WriteFunction());

                    break;
                }
            }
            passSb.Append(structSb.ToString());
            passSb.Append(cbufferSb.ToString());
            passSb.Append(texSb.ToString());
            passSb.Append(memeSb.ToString());
            passSb.Append(codeSb.ToString());
        }

        if (!noFragmentVariants)
        {
            passSb.Append(new string(' ', _depth * 4));
            passSb.AppendLine("#endif");
        }

        var sb = new StringBuilder();
        sb.Append(defineSb.ToString());
        sb.Append(passSb.ToString());
        return sb.ToString();
    }

    private string WritePassCBuffer(
        ShaderParams shaderParams, HashSet<string> declaredCBufs,
        ConstantBuffer? cbuffer, int depth)
    {
        StringBuilder sb = new StringBuilder();
        if (cbuffer != null)
        {
            bool nonGlobalCbuffer = cbuffer.Name != "$Globals";
            int cbufferIndex = shaderParams.ConstantBuffers.IndexOf(cbuffer);

            bool wroteCbufferHeaderYet = false;

            char[] chars = new char[] { 'x', 'y', 'z', 'w' };
            List<ConstantBufferParameter> allParams = cbuffer.CBParams;
            foreach (ConstantBufferParameter param in allParams)
            {
                string typeName = DXShaderNamingUtils.GetConstantBufferParamTypeName(param);
                string name = param.ParamName;

                if (UnityShaderConstants.INCLUDED_UNITY_PROP_NAMES.Contains(name))
                {
                    continue;
                }

                if (!wroteCbufferHeaderYet && nonGlobalCbuffer)
                {
                    sb.Append(new string(' ', depth * 4));
                    sb.AppendLine($"// CBUFFER_START({cbuffer.Name}) // {cbufferIndex}");
                    depth++;
                }

                if (!declaredCBufs.Contains(name))
                {
                    if (param.ArraySize > 0)
                    {
                        sb.Append(new string(' ', depth * 4));
                        if (nonGlobalCbuffer)
                            sb.Append("// ");
                        sb.AppendLine($"{typeName} {name}[{param.ArraySize}]; // {param.Index} (starting at cb{cbufferIndex}[{param.Index / 16}].{chars[param.Index % 16 / 4]})");
                    }
                    else
                    {
                        sb.Append(new string(' ', depth * 4));
                        if (nonGlobalCbuffer && !cbuffer.Name.StartsWith("UnityPerDrawSprite"))
                            sb.Append("// ");
                        sb.AppendLine($"{typeName} {name}; // {param.Index} (starting at cb{cbufferIndex}[{param.Index / 16}].{chars[param.Index % 16 / 4]})");
                    }
                    declaredCBufs.Add(name);
                }

                if (!wroteCbufferHeaderYet && nonGlobalCbuffer)
                {
                    depth--;
                    sb.Append(new string(' ', depth * 4));
                    sb.AppendLine("// CBUFFER_END");
                    wroteCbufferHeaderYet = true;
                }
            }
        }
        return sb.ToString();
    }

    private string WritePassTextures(
        ShaderParams shaderParams, HashSet<string> declaredCBufs, int depth)
    {
        StringBuilder sb = new StringBuilder();
        foreach (TextureParameter param in shaderParams.TextureParameters)
        {
            string name = param.Name;
            if (!declaredCBufs.Contains(name) && !UnityShaderConstants.BUILTIN_TEXTURE_NAMES.Contains(name))
            {
                sb.Append(new string(' ', depth * 4));
                switch (param.Dim)
                {
                    case 2:
                        sb.AppendLine($"sampler2D {name}; // {param.Index}");
                        break;
                    case 3:
                        sb.AppendLine($"sampler3D {name}; // {param.Index}");
                        break;
                    case 4:
                        sb.AppendLine($"samplerCUBE {name}; // {param.Index}");
                        break;
                    case 5:
                        sb.AppendLine($"UNITY_DECLARE_TEX2DARRAY({name}); // {param.Index}");
                        break;
                    case 6:
                        sb.AppendLine($"UNITY_DECLARE_TEXCUBEARRAY({name}); // {param.Index}");
                        break;
                    default:
                        sb.AppendLine($"sampler2D {name}; // {param.Index} // Unsure of real type ({param.Dim})");
                        break;
                }
                declaredCBufs.Add(name);
            }
        }
        return sb.ToString();
    }
}
