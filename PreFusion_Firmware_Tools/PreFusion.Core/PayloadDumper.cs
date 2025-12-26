using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.IO.Compression;

namespace Ext2Read.Core
{
    public class PayloadDumper
    {
        private const uint MAGIC = 0x55417243; // CrAU
        private const ulong MAJOR_PAYLOAD_VERSION_BRILLO = 2;

        public class PartitionInfo
        {
            public string Name { get; set; } = "";
            public long Size { get; set; }
            public List<Operation> Operations { get; set; } = new List<Operation>();
        }

        public class Operation
        {
            public int Type { get; set; } 
            public long DataOffset { get; set; }
            public long DataLength { get; set; }
            public List<Extent> DstExtents { get; set; } = new List<Extent>();

            public bool HasData => Type == 0 || Type == 1 || Type == 8 || Type == 9;
        }

        public struct Extent
        {
            public long StartBlock;
            public long NumBlocks;
        }

        public static async Task<List<PartitionInfo>> ParsePayloadAsync(string filePath)
        {
            var parts = new List<PartitionInfo>();
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                // 1. Header
                uint magic = br.ReadUInt32(); // LE
                // Magic is usually BE "CrAU" -> 0x43 0x72 0x41 0x55.
                // In LE uint read: 0x55417243.
                if (magic != MAGIC) throw new Exception("Invalid Payload magic (Not a CrAU file).");

                ulong version = br.ReadUInt64();
                ulong manifestLen = br.ReadUInt64();
                uint metadataSigLen = 0;

                if (version >= 2)
                {
                    metadataSigLen = br.ReadUInt32();
                }

                // 2. Read Manifest Protobuf
                byte[] manifestBytes = new byte[manifestLen];
                await fs.ReadAsync(manifestBytes, 0, (int)manifestLen);

                // 3. Parse Protobuf
                var reader = new MinimalProtobufReader(manifestBytes);
                // Manifest ID=1 -> PartitionUpdate
                // We assume top level tags.
                
                while (!reader.IsEOF())
                {
                    int tag = reader.ReadTag();
                    int field = tag >> 3;
                    int wire = tag & 7;

                    if (field == 1) // partitions (repeated)
                    {
                        var pData = reader.ReadBytes(); // Nested message is bytes or length-delimited
                        var part = ParsePartitionUpdate(pData);
                        parts.Add(part);
                    }
                    else
                    {
                        reader.SkipField(wire);
                    }
                }
            }
            return parts;
        }
        
        public static async Task ExtractPayloadAsync(string inputPath, string outputDir, IProgress<float>? progress = null)
        {
             // Check if Zip
             string payloadPath = inputPath;
             string tempPayload = "";

             if (Path.GetExtension(inputPath).ToLower() == ".zip")
             {
                 // Extract payload.bin from zip
                 using (var archive = System.IO.Compression.ZipFile.OpenRead(inputPath))
                 {
                     var entry = archive.GetEntry("payload.bin");
                     if (entry == null) throw new FileNotFoundException("payload.bin not found inside the zip file.");
                     
                     tempPayload = Path.Combine(Path.GetTempPath(), "payload_" + Guid.NewGuid() + ".bin");
                     entry.ExtractToFile(tempPayload);
                     payloadPath = tempPayload;
                 }
             }

             try
             {
                 var partitions = await ParsePayloadAsync(payloadPath);
                 long totalOps = 0;
             foreach(var p in partitions) totalOps += p.Operations.Count;
             long currentOp = 0;

             using (var fsPayload = new FileStream(payloadPath, FileMode.Open, FileAccess.Read))
             {
                 // Need to re-read header to skip manifest properly to find data start? 
                 // Actually data_offset in operations is absolute or relative?
                 // Usually relative to the end of manifest? Or absolute?
                 // Android doc: "Data is located at: `payload_header + manifest + signatures + data_offset`"
                 // So we need to calculate `dataStart`.
                 
                 fsPayload.Seek(0, SeekOrigin.Begin);
                 var br = new BinaryReader(fsPayload);
                 br.ReadUInt32(); // Magic
                 ulong version = br.ReadUInt64();
                 ulong manifestLen = br.ReadUInt64();
                 uint metaLen = 0;
                 if (version >= 2) metaLen = br.ReadUInt32();
                 
                 long dataStart = 4 + 8 + 8 + (version >= 2 ? 4 : 0) + (long)manifestLen + metaLen;
                 // Note: Signatures might be padding? 
                 // Actually: Manifest includes signature blob?
                 // Specification says:
                 // 1. Magic (4)
                 // 2. Version (8)
                 // 3. Manifest Size (8)
                 // 4. [Metadata Sig Size (4)] (v2+)
                 // = Header Size
                 //
                 // Then Manifest (Manifest Size bytes)
                 // Then Metadata Signature (Metadata Sig Size bytes)
                 // Then Data.
                 
                 dataStart += metaLen; 

                 foreach (var part in partitions)
                 {
                     string outPath = Path.Combine(outputDir, part.Name + ".img");
                     using (var fsOut = new FileStream(outPath, FileMode.Create, FileAccess.Write))
                     {
                         foreach (var op in part.Operations)
                         {
                             // Operation Type:
                             // 0 = REPLACE (Raw)
                             // 1 = REPLACE_BZ (Bzip2)
                             // 2 = MOVE
                             // 3 = BSDIFF
                             // 4 = SOURCE_COPY
                             // 5 = SOURCE_BSDIFF
                             // 6 = ZERO
                             // 7 = DISCARD
                             // 8 = REPLACE_XZ (LZMA)
                             // 9 = BROTLI_BSDIFF
                             
                             // We are focusing on FULL payload (REPLACE/REPLACE_XZ/REPLACE_BZ).
                             // Incremental stuff (MOVE/BSDIFF) is for OTA patching which needs source.
                             // We assume "Full OTA".
                             
                             if (op.Type == 6) // ZERO
                             {
                                 // Fill 0
                                 // We just seek? 
                                 // Sparse output would be better.
                             }
                             else if (op.HasData) 
                             {
                                 // Seek to data
                                 fsPayload.Seek(dataStart + op.DataOffset, SeekOrigin.Begin);
                                 byte[] data = new byte[op.DataLength];
                                 await fsPayload.ReadAsync(data, 0, data.Length);
                                 
                                 byte[] decoded = data;
                                 
                                 if (op.Type == 8) // REPLACE_XZ
                                 {
                                     decoded = DecompressXZ(data);
                                 }
                                 else if (op.Type == 1) // BZIP2
                                 {
                                     // TODO: Bzip2 support. For now, pass raw? Or throw?
                                     // throw new NotSupportedException("Bzip2 not supported");
                                 }

                                 // Write to Extents
                                 int bufferOffset = 0;
                                 foreach (var ext in op.DstExtents)
                                 {
                                     long seekPos = ext.StartBlock * 4096;
                                     long len = ext.NumBlocks * 4096;
                                     
                                     if (bufferOffset + len > decoded.Length)
                                     {
                                         // Warning: Data shortage? 
                                         len = decoded.Length - bufferOffset;
                                     }

                                     fsOut.Seek(seekPos, SeekOrigin.Begin);
                                     await fsOut.WriteAsync(decoded, bufferOffset, (int)len);
                                     bufferOffset += (int)len;
                                 }
                             }

                             currentOp++;
                             progress?.Report((float)currentOp / totalOps);
                         }
                     }
                 }
                 } // Close using fsPayload
             } // Close try
             finally
             {
                 if (!string.IsNullOrEmpty(tempPayload) && File.Exists(tempPayload))
                 {
                     File.Delete(tempPayload);
                 }
             }
        }

        private static PartitionInfo ParsePartitionUpdate(byte[] data)
        {
            var p = new PartitionInfo();
            var reader = new MinimalProtobufReader(data);

            while (!reader.IsEOF())
            {
                int tag = reader.ReadTag();
                int field = tag >> 3;
                int wire = tag & 7;

                switch (field)
                {
                    case 1: // partition_name (string)
                        p.Name = reader.ReadString();
                        break;
                    case 5: // new_partition_info (message)
                        var infoBytes = reader.ReadBytes();
                        p.Size = ParsePartitionInfo(infoBytes);
                        break;
                    case 6: // operations (repeated)
                        var opBytes = reader.ReadBytes();
                        p.Operations.Add(ParseOperation(opBytes));
                        break;
                    default:
                        reader.SkipField(wire);
                        break;
                }
            }
            return p;
        }

        private static long ParsePartitionInfo(byte[] data)
        {
            long size = 0;
            var reader = new MinimalProtobufReader(data);
            while (!reader.IsEOF())
            {
                int tag = reader.ReadTag();
                int field = tag >> 3;
                int wire = tag & 7;

                if (field == 1) // size check
                {
                   size = (long)reader.ReadVarint();
                }
                else
                {
                    reader.SkipField(wire);
                }
            }
            return size;
        }

        private static Operation ParseOperation(byte[] data)
        {
            var op = new Operation();
            var reader = new MinimalProtobufReader(data);

            while (!reader.IsEOF())
            {
                int tag = reader.ReadTag();
                int field = tag >> 3;
                int wire = tag & 7;

                switch (field)
                {
                    case 1: // type (enum)
                        op.Type = (int)reader.ReadVarint();
                        break;
                    case 2: // data_offset
                        op.DataOffset = (long)reader.ReadVarint();
                        break;
                    case 3: // data_length
                        op.DataLength = (long)reader.ReadVarint();
                        break;
                    case 5: // dst_extents (repeated)
                        var extBytes = reader.ReadBytes();
                        op.DstExtents.Add(ParseExtent(extBytes));
                        break;
                    default:
                        reader.SkipField(wire);
                        break;
                }
            }
            return op;
        }

        private static byte[] DecompressXZ(byte[] compressed)
        {
            // Use 7za.exe via Process
            // Temp file strategy is robust.
            string tempIn = Path.GetTempFileName();
            string tempOut = Path.Combine(Path.GetDirectoryName(tempIn), Path.GetFileNameWithoutExtension(tempIn) + ".out");

            try
            {
                File.WriteAllBytes(tempIn, compressed);

                // Locate 7za.exe
                // Assumed to be in App Directory (Bin) or a known path
                string sevenZip = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7za.exe");
                if (!File.Exists(sevenZip))
                {
                    // Fallback to searching? or Check TEMP/bin
                     sevenZip = @"i:\GITHUB\Projects\PreFusion_Firmware_Tools\TEMP\atoto_firmware_downloader\bin\7za.exe"; // Hardcoded Dev path fallback
                     if (!File.Exists(sevenZip)) throw new FileNotFoundException("7za.exe not found.");
                }

                // 7za e -txz -so input > output (Streaming not easy with Process stdio sometimes)
                // Use file I/O
                // 7za e input -oOutput -y
                
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZip,
                    Arguments = $"e \"{tempIn}\" -o\"{Path.GetDirectoryName(tempOut)}\" -y",
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };
                
                // 7z extracts with original name if stored? No, XZ usually is single stream. 
                // Wait, 7z e input will extract to... what filename? 
                // XZ doesn't store filename usually.
                // If input is tmpE34.tmp, output might be tmpE34 (without extension) or something.
                
                // Better to use StdOut
                psi.Arguments = $"e -txz -so \"{tempIn}\"";
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;

                using (var p = System.Diagnostics.Process.Start(psi))
                using (var ms = new MemoryStream())
                {
                    p.StandardOutput.BaseStream.CopyTo(ms);
                    p.WaitForExit();
                    return ms.ToArray();
                }
            }
            finally
            {
                if (File.Exists(tempIn)) File.Delete(tempIn);
                if (File.Exists(tempOut)) File.Delete(tempOut);
            }
        }

        private static Extent ParseExtent(byte[] data)
        {
            var e = new Extent();
            var reader = new MinimalProtobufReader(data);
             while (!reader.IsEOF())
            {
                int tag = reader.ReadTag();
                int field = tag >> 3;
                int wire = tag & 7;

                if (field == 1) e.StartBlock = (long)reader.ReadVarint();
                else if (field == 2) e.NumBlocks = (long)reader.ReadVarint();
                else reader.SkipField(wire);
            }
            return e;
        }
    }

    // --- Minimal Reader Implementation ---
    class MinimalProtobufReader
    {
        private byte[] _buffer;
        private int _pos;

        public MinimalProtobufReader(byte[] buffer)
        {
            _buffer = buffer;
            _pos = 0;
        }

        public bool IsEOF() => _pos >= _buffer.Length;

        public int ReadTag() => (int)ReadVarint();

        public ulong ReadVarint()
        {
            ulong value = 0;
            int shift = 0;
            while (_pos < _buffer.Length)
            {
                byte b = _buffer[_pos++];
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            return value;
        }

        public byte[] ReadBytes()
        {
            ulong len = ReadVarint();
            byte[] data = new byte[len];
            Array.Copy(_buffer, _pos, data, 0, (int)len);
            _pos += (int)len;
            return data;
        }

        public string ReadString() => Encoding.UTF8.GetString(ReadBytes());

        public void SkipField(int wireType)
        {
            switch (wireType)
            {
                case 0: // Varint
                    ReadVarint();
                    break;
                case 1: // 64-bit
                    _pos += 8;
                    break;
                case 2: // Length-delimited
                    int len = (int)ReadVarint();
                    _pos += len;
                    break;
                case 5: // 32-bit
                    _pos += 4;
                    break;
                default: 
                    throw new Exception($"Unknown wire type: {wireType}");
            }
        }
    }
}
