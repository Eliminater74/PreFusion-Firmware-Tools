using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Ext2Read.Core
{
    public static class SparseConverter
    {
        private const uint SPARSE_HEADER_MAGIC = 0xED26FF3A;
        private const ushort CHUNK_TYPE_RAW = 0xCAC1;
        private const ushort CHUNK_TYPE_FILL = 0xCAC2;
        private const ushort CHUNK_TYPE_DONT_CARE = 0xCAC3;
        private const ushort CHUNK_TYPE_CRC32 = 0xCAC4;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct SparseHeader
        {
            public uint magic;
            public ushort major_version;
            public ushort minor_version;
            public ushort file_header_sz;
            public ushort chunk_header_sz;
            public uint blk_sz;
            public uint total_blks;
            public uint total_chunks;
            public uint image_checksum;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct ChunkHeader
        {
            public ushort chunk_type;
            public ushort reserved1;
            public uint chunk_sz; // in blocks
            public uint total_sz; // in bytes (header + data)
        }

        public static bool IsSparseImage(string path)
        {
            if (!File.Exists(path)) return false;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Length < 28) return false;
                    using (var br = new BinaryReader(fs))
                    {
                        return br.ReadUInt32() == SPARSE_HEADER_MAGIC;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        public static void Convert(string inputPath, string outputPath, IProgress<int> progress = null)
        {
            using (var fsIn = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fsIn))
            using (var fsOut = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var bw = new BinaryWriter(fsOut))
            {
                // Read Header
                byte[] headerBytes = br.ReadBytes(Marshal.SizeOf(typeof(SparseHeader)));
                var header = BytesToStruct<SparseHeader>(headerBytes);

                if (header.magic != SPARSE_HEADER_MAGIC)
                    throw new InvalidDataException($"Invalid sparse image magic: 0x{header.magic:X8} (Expected 0x{SPARSE_HEADER_MAGIC:X8}). File might be raw image or encrypted.");

                // Check version (only support 1.0)
                if (header.major_version != 1 || header.minor_version != 0)
                    throw new InvalidDataException($"Unsupported sparse version {header.major_version}.{header.minor_version}");

                // Validate sizes
                if (header.file_header_sz < Marshal.SizeOf(typeof(SparseHeader)))
                    throw new InvalidDataException("Invalid file header size.");
                if (header.chunk_header_sz < Marshal.SizeOf(typeof(ChunkHeader)))
                    throw new InvalidDataException("Invalid chunk header size.");

                // Skip any remaining file header padding
                if (header.file_header_sz > headerBytes.Length)
                {
                    br.ReadBytes(header.file_header_sz - headerBytes.Length);
                }

                long totalBlocksWritten = 0;

                for (int i = 0; i < header.total_chunks; i++)
                {
                    if (progress != null)
                    {
                        int percent = (int)((i / (float)header.total_chunks) * 100);
                        progress.Report(percent);
                    }

                    // Read Chunk Header
                    long chunkStartPos = fsIn.Position;
                    byte[] chunkHeaderBytes = br.ReadBytes(Marshal.SizeOf(typeof(ChunkHeader)));
                    var chunk = BytesToStruct<ChunkHeader>(chunkHeaderBytes);

                    // Process Chunk
                    long dataSize = chunk.total_sz - header.chunk_header_sz;
                    long blockSizeBytes = (long)chunk.chunk_sz * header.blk_sz;

                    switch (chunk.chunk_type)
                    {
                        case CHUNK_TYPE_RAW:
                            {
                                // RAW: Read dataSize bytes from input, write to output
                                // dataSize should satisfy chunk_sz * blk_sz
                                if (dataSize != blockSizeBytes)
                                    throw new InvalidDataException($"RAW chunk size mismatch. Expected {blockSizeBytes}, got {dataSize}");

                                // Copy using buffer
                                const int bufferSize = 4096 * 1024; // 4MB buffer
                                byte[] buffer = new byte[bufferSize];
                                long remaining = dataSize;
                                while (remaining > 0)
                                {
                                    int toRead = (int)Math.Min(remaining, bufferSize);
                                    int read = br.Read(buffer, 0, toRead);
                                    if (read == 0) break;
                                    bw.Write(buffer, 0, read);
                                    remaining -= read;
                                }
                                break;
                            }
                        case CHUNK_TYPE_FILL:
                            {
                                // FILL: 4 bytes of fill data
                                if (dataSize != 4)
                                    throw new InvalidDataException("FILL chunk must have 4 bytes of data.");

                                uint fillValue = br.ReadUInt32();
                                // Write 'chunk.chunk_sz' blocks of 'fillValue'
                                // Optimize: Create a buffer of fill values

                                // Fill check: if fillValue is 0, we can just seek? 
                                // Only if file was pre-set to 0. Safest to write.

                                byte[] fillPattern = BitConverter.GetBytes(fillValue);
                                // Only need to write 'blockSizeBytes'

                                // Optimization: replicate pattern into larger buffer
                                const int bufferSize = 4096;
                                byte[] buffer = new byte[bufferSize];
                                for (int k = 0; k < bufferSize; k += 4)
                                {
                                    Array.Copy(fillPattern, 0, buffer, k, 4);
                                }

                                long remaining = blockSizeBytes;
                                while (remaining > 0)
                                {
                                    int toWrite = (int)Math.Min(remaining, bufferSize);
                                    bw.Write(buffer, 0, toWrite);
                                    remaining -= toWrite;
                                }
                                break;
                            }
                        case CHUNK_TYPE_DONT_CARE:
                            {
                                // DONT_CARE: Skip output. Just seek.
                                // Ensure file is extended.
                                // SafeFileHandle SetFilePointer? Or just stream Seek.
                                // If we seek past end of file on Write stream, it effectively extends (usually zero filled on NTFS).
                                fsOut.Seek(blockSizeBytes, SeekOrigin.Current);
                                break;
                            }
                        case CHUNK_TYPE_CRC32:
                            {
                                // CRC32: 4 bytes of CRC. Ignore.
                                br.ReadUInt32();
                                break;
                            }
                        default:
                            throw new InvalidDataException($"Unknown chunk type 0x{chunk.chunk_type:X4}");
                    }

                    // Skip any padding if chunk header sz > struct size
                    // Actually spec says total_sz includes header + data.
                    // We read header struct. 
                    // We read data based on type.
                    // We should align or seek to next chunk?
                    // The sparse format ensures chunks are aligned? 
                    // Wait, `total_sz` is the truth. 
                    // Calculate expected position of next chunk
                    long chunkEndPos = chunkStartPos + chunk.total_sz;
                    if (fsIn.Position != chunkEndPos)
                    {
                        // Reposition for next chunk if we didn't read exactly the right amount (e.g. padding)
                        fsIn.Seek(chunkEndPos, SeekOrigin.Begin);
                    }

                    totalBlocksWritten += chunk.chunk_sz;
                }

                // Final Check
                if (totalBlocksWritten != header.total_blks)
                {
                    // Warn? Or just accept.
                }

                // Ensure output file size is correct (in case last chunk was DONT_CARE)
                long expectedSize = (long)header.total_blks * header.blk_sz;
                if (fsOut.Length != expectedSize)
                {
                    fsOut.SetLength(expectedSize);
                }
            }
        }

        private static T BytesToStruct<T>(byte[] bytes) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(bytes, 0, ptr, size);
                return (T)Marshal.PtrToStructure(ptr, typeof(T));
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }
}
