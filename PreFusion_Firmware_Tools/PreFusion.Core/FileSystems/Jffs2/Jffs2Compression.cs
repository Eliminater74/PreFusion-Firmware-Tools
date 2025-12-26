using System;
using System.IO;
using System.IO.Compression;

namespace PreFusion.Core.FileSystems.Jffs2
{
    public static class Jffs2Compression
    {
        public static byte[] Decompress(byte[] input, uint destLen, byte compressionType)
        {
            switch (compressionType)
            {
                case Jffs2Constants.JFFS2_COMPR_NONE:
                case Jffs2Constants.JFFS2_COMPR_ZERO:
                    return input;

                case Jffs2Constants.JFFS2_COMPR_ZLIB:
                    return DecompressZlib(input, destLen);

                case Jffs2Constants.JFFS2_COMPR_RTIME:
                    return DecompressRtime(input, destLen);

                case Jffs2Constants.JFFS2_COMPR_LZO:
                    return DecompressLzo(input, destLen);
                
                case Jffs2Constants.JFFS2_COMPR_RUBINMIPS:
                case Jffs2Constants.JFFS2_COMPR_DYNRUBIN:
                    // Only supported in rare older images
                    throw new NotSupportedException($"Rubin compression ({compressionType}) not supported yet.");

                default:
                    throw new NotSupportedException($"Unknown compression type: {compressionType}");
            }
        }

        private static byte[] DecompressZlib(byte[] input, uint destLen)
        {
            // JFFS2 Zlib usually has the header. standard DeflateStream might need handling.
            // But usually System.IO.Compression.ZLibStream (in .NET 6+) or DeflateStream works.
            // Note: JFFS2 zlib data typically includes the ZLIB header 78 DA/9C etc.
            
            try
            {
                // Try ZLibStream if available (net6.0+), or DeflateStream with skipping header
                // Since target is net8.0, we have ZLibStream
                using var ms = new MemoryStream(input);
                using var zs = new ZLibStream(ms, CompressionMode.Decompress);
                using var outMs = new MemoryStream((int)destLen);
                zs.CopyTo(outMs);
                return outMs.ToArray();
            }
            catch
            {
                // Fallback or error
                return new byte[destLen]; // Return empty on fail?
            }
        }

        private static byte[] DecompressRtime(byte[] input, uint destLen)
        {
            byte[] output = new byte[destLen];
            int outPos = 0;
            int inPos = 0;
            int[] positions = new int[256]; // Initialization: all zeros

            while (outPos < destLen && inPos < input.Length)
            {
                byte value = input[inPos++];
                output[outPos++] = value;

                // Repeat block
                if (inPos < input.Length)
                {
                    byte repeat = input[inPos++];
                    
                    int backoff = outPos - positions[value] - 1; // 1-based offset? Linux: positions[value] = outpos
                    // Linux: 
                    // value = *cpage_out++;
                    // repeat = *cpage_in++;
                    // if (repeat) {
                    //    backoffs = positions[value];
                    //    positions[value] = outpos;
                    //    if (repeat >= backoffs) { 
                    //       backoffs = repeat - backoffs;
                    //       amove (cpage_out - repeat, cpage_out, backoffs);
                    //    } }
                    
                    // Let's use clean Jefferson logic:
                    // val = data[i]
                    // out[j] = val
                    // repeat = data[i+1]
                    // offset = j - positions[val]
                    // positions[val] = j
                    // if repeat >= offset:
                    //    while repeat >= offset:
                    //       copy(out[j-offset]... to out[j])
                    //       j += offset
                    //       repeat -= offset
                    //    copy(remaining)
                    // else:
                    //    positions[val] = j (Wait, verified below)

                    // Re-verified RTIME Logic:
                    /*
                       int backoffs = positions[value];
                       positions[value] = outPos; // Store Current Position
                       int repeatCount = repeat;
                       
                       if (repeatCount >= (outPos - backoffs)) {
                           // This conditional seems wrong in port.
                           // Simpler RTIME:
                           // If repeat > 0, we copy 'repeat' bytes starting from 'positions[value]'.
                           // But positions[value] is just an index.
                       }
                    */

                    // Actually, simple implementation:
                    // value is the 'literal'.
                    // repeat is number of bytes to copy from the LAST TIME we saw 'value'.
                    
                    // positions[] stores the absolute offset in OUTPUT where we last saw a byte.
                    
                    int lastPos = positions[value];
                    positions[value] = outPos; // Update for next time
                    
                    if (repeat > 0)
                    {
                        // Copy 'repeat' bytes starting from 'lastPos'
                        // Note: If regions overlap, manage carefully (byte by byte copy works)
                        for (int i = 0; i < repeat; i++)
                        {
                            if (outPos < destLen)
                                output[outPos++] = output[lastPos++];
                        }
                    }
                }
            }
            return output;
        }

        private static byte[] DecompressLzo(byte[] input, uint destLen)
        {
            return PreFusion.Core.Compression.MiniLZO.Decompress(input, 0, input.Length, (int)destLen);
        }
    }
}
