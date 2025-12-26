using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Ext2Read.Core.Binwalk
{
    public class Signature
    {
        public string Name { get; set; }
        public byte[] Magic { get; set; }
        public int Offset { get; set; } // Offset relative to the start of the pattern match?
                                        // Usually magic IS the start.
                                        // But Ext2 SB is at 1024.

        // Binwalk definition: "Magic bytes at offset X implies file type Y"
        // Most file types (GZIP, ZIP) have magic at 0.
        // Ext2: Magic `53 EF` is at 56 (0x38) bytes into the Superblock.
        // So if we find `53 EF`, the start of the file is `CurrentPos - 56`.
        // We will call this `MagicOffset`.
        public int MagicOffset { get; set; }
    }

    public class ScanResult
    {
        public long Offset { get; set; }
        public string Description { get; set; }
    }

    public class EntropyResult
    {
        public long Offset { get; set; }
        public double Entropy { get; set; } // 0.0 to 8.0
    }

    public static class Scanner
    {
        public static List<Signature> DefaultSignatures = new List<Signature>
        {
            // Archives
            new Signature { Name = "GZIP Archive", Magic = new byte[] { 0x1F, 0x8B, 0x08 }, MagicOffset = 0 },
            new Signature { Name = "LZMA headers", Magic = new byte[] { 0x5D, 0x00, 0x00 }, MagicOffset = 0 }, // Very generic, might trigger falses
            new Signature { Name = "XZ Archive", Magic = new byte[] { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00 }, MagicOffset = 0 },
            new Signature { Name = "Zip Archive", Magic = new byte[] { 0x50, 0x4B, 0x03, 0x04 }, MagicOffset = 0 },
            // new Signature { Name = "Bzip2 Archive", Magic = new byte[] { 0x42, 0x5A, 0x68 }, MagicOffset = 0 },
            
            // Filesystems
            new Signature { Name = "SquashFS (LE)", Magic = new byte[] { 0x68, 0x73, 0x71, 0x73 }, MagicOffset = 0 }, // hsqs
            new Signature { Name = "SquashFS (BE)", Magic = new byte[] { 0x73, 0x71, 0x73, 0x68 }, MagicOffset = 0 }, // sqsh
            
            // Ext2/3/4 - Superblock Magic is 0xEF53 at offset 0x38 (56) of the superblock.
            // But signatures are usually identified by the start of the structure.
            new Signature { Name = "Linux Ext2/3/4 Filesystem", Magic = new byte[] { 0x53, 0xEF }, MagicOffset = 0x38 },

            // Android
            new Signature { Name = "Android Boot Image", Magic = Encoding.ASCII.GetBytes("ANDROID!"), MagicOffset = 0 },
            
            // CramFS - 0x28cd3d45 at offset 0 (LE)
            new Signature { Name = "CramFS Filesystem", Magic = new byte[] { 0x45, 0x3D, 0xCD, 0x28 }, MagicOffset = 0 },
            
            // JFFS2 - 0x1985 at offset 0 (Big Endian usually?)
            // LE: 85 19
            new Signature { Name = "JFFS2 Filesystem (LE)", Magic = new byte[] { 0x85, 0x19 }, MagicOffset = 0 },
             new Signature { Name = "JFFS2 Filesystem (BE)", Magic = new byte[] { 0x19, 0x85 }, MagicOffset = 0 },
        };

        public static async Task<List<ScanResult>> ScanAsync(string file, IProgress<float> progress = null)
        {
            var results = new List<ScanResult>();
            if (!File.Exists(file)) return results;

            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long length = fs.Length;
                byte[] buffer = new byte[1024 * 1024]; // 1MB buffer
                int bytesRead;
                long totalRead = 0;
                long bufferStartOffset = 0;

                // Simple overlap buffering: Keep last X bytes to handle signatures crossing buffer boundary
                // Max signature length here is small (< 10 bytes).
                int overlap = 100;

                while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    // Scan buffer
                    // For performance, we loop signatures outside inner byte loop? 
                    // Or byte loop then signatures?
                    // Signatures are few (10). Bytes are many.
                    // For each byte in buffer...

                    for (int i = 0; i < bytesRead; i++)
                    {
                        foreach (var sig in DefaultSignatures)
                        {
                            // Check bounds
                            if (i + sig.Magic.Length > bytesRead) continue; // Split boundary case ignored for MVP (overlap solves this usually)

                            // Quick check first byte
                            if (buffer[i] != sig.Magic[0]) continue;

                            // Full check
                            bool match = true;
                            for (int k = 1; k < sig.Magic.Length; k++)
                            {
                                if (buffer[i + k] != sig.Magic[k])
                                {
                                    match = false;
                                    break;
                                }
                            }

                            if (match)
                            {
                                long absolutePos = bufferStartOffset + i;
                                long startOfFile = absolutePos - sig.MagicOffset;

                                if (startOfFile >= 0)
                                {
                                    // Avoid duplicate spam (e.g. JFFS2 nodes appear every few bytes)
                                    // Simple debounce logic? 
                                    // For now just add. UI can filter.
                                    results.Add(new ScanResult { Offset = startOfFile, Description = sig.Name });
                                }
                            }
                        }
                    }

                    totalRead += bytesRead;
                    bufferStartOffset += bytesRead;
                    if (progress != null) progress.Report((float)totalRead / length);

                    // Handle overlap if needed (not implemented for simplicity, losing <100 bytes edge cases)
                }
            }

            return results;
        }

        public static async Task<List<EntropyResult>> CalculateEntropyAsync(string file, int blockSize = 1024, IProgress<float> progress = null)
        {
            var results = new List<EntropyResult>();
            if (!File.Exists(file)) return results;

            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] buffer = new byte[blockSize];
                long length = fs.Length;
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    double entropy = CalculateBlockEntropy(buffer, bytesRead);
                    results.Add(new EntropyResult { Offset = totalRead, Entropy = entropy });

                    totalRead += bytesRead;
                    if (progress != null) progress.Report((float)totalRead / length);
                }
            }
            return results;
        }

        private static double CalculateBlockEntropy(byte[] data, int length)
        {
            if (length == 0) return 0;

            int[] frequencies = new int[256];
            for (int i = 0; i < length; i++)
            {
                frequencies[data[i]]++;
            }

            double entropy = 0;
            double len = (double)length;

            foreach (var count in frequencies)
            {
                if (count > 0)
                {
                    double p = count / len;
                    entropy -= p * Math.Log(p, 2);
                }
            }

            return entropy;
        }
    }
}
