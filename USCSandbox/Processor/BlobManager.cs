using AssetRipper.Primitives;
using AssetsTools.NET;

namespace USCSandbox.Processor
{
    public class BlobManager
    {
        private List<AssetsFileReader> _readers;
        private UnityVersion _engVer;
        
        public List<BlobEntry> Entries;

        public BlobManager(List<byte[]> blobs, UnityVersion engVer)
        {
            _readers = blobs.Select(blob => new AssetsFileReader(new MemoryStream(blob))).ToList();
            _engVer = engVer;

            var reader = _readers[0];
            var count = reader.ReadInt32();
            Entries = new List<BlobEntry>(count);
            for (var i = 0; i < count; i++)
            {
                Entries.Add(new BlobEntry(reader, engVer));
            }
        }

        public byte[] GetRawEntry(int index)
        {
            var entry = Entries[index];
            var reader = _readers[entry.Segment];
            reader.BaseStream.Position = Entries[index].Offset;
            return reader.ReadBytes(Entries[index].Length);
        }

        public ShaderParams GetShaderParams(int index)
        {
            var blobEntry = GetRawEntry(index);
            var r = new AssetsFileReader(new MemoryStream(blobEntry));
            return new ShaderParams(r, _engVer, true);
        }

        public ShaderSubProgram GetShaderSubProgram(int index)
        {
            var blobEntry = GetRawEntry(index);
            var r = new AssetsFileReader(new MemoryStream(blobEntry));
            return new ShaderSubProgram(r, _engVer);
        }
    }
}