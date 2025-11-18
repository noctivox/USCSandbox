namespace USCSandbox.Processor
{
    public class ShaderProgramBasket
    {
        public SerializedProgramInfo ProgramInfo;
        public SerializedSubProgramInfo SubProgramInfo;
        public int ParameterBlobIndex;
        public ShaderSubProgram? SubProg { get; init; }

        public ShaderProgramBasket(
            SerializedProgramInfo programInfo, 
            SerializedSubProgramInfo subProgramInfo, 
            int parameterBlobIndex,
            ShaderSubProgram? subProg
        )
        {
            ProgramInfo = programInfo;
            SubProgramInfo = subProgramInfo;
            ParameterBlobIndex = parameterBlobIndex;
            SubProg = subProg;
        }
    }
}
