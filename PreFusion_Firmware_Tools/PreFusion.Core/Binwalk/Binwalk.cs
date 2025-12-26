using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Ext2Read.Core.Binwalk
{
    public class Signature
    {
        public string Name { get; set; } = "";
        public byte[] Magic { get; set; } = Array.Empty<byte>();
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
        public string Description { get; set; } = "";
    }

    public class EntropyResult
    {
        public long Offset { get; set; }
        public double Entropy { get; set; } // 0.0 to 8.0
    }

    public class StringResult
    {
        public long Offset { get; set; }
        public string Text { get; set; } = "";
    }

    public static class Scanner
    {
        public static List<Signature> DefaultSignatures = new List<Signature>
        {
            // Compression
            new Signature { Name = "GZIP", Magic = new byte[] { 0x1F, 0x8B, 0x08 } },
            new Signature { Name = "LZMA", Magic = new byte[] { 0x5D, 0x00, 0x00, 0x80, 0x00 } }, // Classic LZMA
            new Signature { Name = "XZ", Magic = new byte[] { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00 } },
            new Signature { Name = "Zip Archive", Magic = new byte[] { 0x50, 0x4B, 0x03, 0x04 } },
            new Signature { Name = "BZIP2", Magic = new byte[] { 0x42, 0x5A, 0x68 } },
            new Signature { Name = "Zlib", Magic = new byte[] { 0x78, 0x9C } }, 

            // Filesystems
            new Signature { Name = "SquashFS (BE)", Magic = new byte[] { 0x73, 0x71, 0x73, 0x68 } },
            new Signature { Name = "SquashFS (LE)", Magic = new byte[] { 0x68, 0x73, 0x71, 0x73 } },
            new Signature { Name = "CramFS (LE)", Magic = new byte[] { 0x45, 0x3D, 0xCD, 0x28 } },
            new Signature { Name = "CramFS (BE)", Magic = new byte[] { 0x28, 0xCD, 0x3D, 0x45 } },
            new Signature { Name = "Ext2/3/4", Magic = new byte[] { 0x53, 0xEF }, MagicOffset = 0x38 }, 
            new Signature { Name = "JFFS2 (BE)", Magic = new byte[] { 0x19, 0x85 } },
            new Signature { Name = "JFFS2 (LE)", Magic = new byte[] { 0x85, 0x19 } },
            new Signature { Name = "UBI", Magic = new byte[] { 0x55, 0x42, 0x49, 0x23 } }, 
            
            // Boot / Firmware
            new Signature { Name = "Android Boot Img", Magic = new byte[] { 0x41, 0x4E, 0x44, 0x52, 0x4F, 0x49, 0x44, 0x21 } },
            new Signature { Name = "U-Boot Legacy", Magic = new byte[] { 0x27, 0x05, 0x19, 0x56 } },
            new Signature { Name = "U-Boot FIT", Magic = new byte[] { 0xD0, 0x0D, 0xFE, 0xED } }, 
            new Signature { Name = "TRX Header", Magic = new byte[] { 0x48, 0x44, 0x52, 0x30 } },

            // Opcodes (Simplified)
            new Signature { Name = "ARM LE Loop/Branch", Magic = new byte[] { 0xFE, 0xFF, 0xFF, 0xEA } }, 
            new Signature { Name = "ARM BE Loop/Branch", Magic = new byte[] { 0xEA, 0xFF, 0xFF, 0xFE } },
        };

        public static async Task<List<ScanResult>> SearchCustomAsync(string file, byte[] pattern, string description, IProgress<float>? progress = null)
        {
             var results = new List<ScanResult>();
             if (!File.Exists(file) || pattern == null || pattern.Length == 0) return results;
             
             // Wrap single signature
             var sigs = new List<Signature> { new Signature { Name = description, Magic = pattern } };
             return await ScanAsync(file, progress, sigs);
        }
        
        // Overload scan to take custom signatures
        public static async Task<List<ScanResult>> ScanAsync(string file, IProgress<float>? progress = null, List<Signature>? signatures = null)
        {
            var results = new List<ScanResult>();
            if (!File.Exists(file)) return results;

            var sigs = signatures ?? DefaultSignatures;

            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] buffer = new byte[4096 * 4]; // 16KB window
                int overlap = 1024; // Check max magic length
                long length = fs.Length;
                long pos = 0;
                int read;

                while ((read = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    // Scan buffer
                    for (int i = 0; i < read; i++)
                    {
                        foreach (var sig in sigs)
                        {
                            // Check bounds
                            if (i + sig.Magic.Length > read) continue;

                            // Optimization: Check first byte
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
                                // Adjust for MagicOffset
                                long realOffset = pos + i - sig.MagicOffset;
                                if (realOffset < 0) continue;

                                results.Add(new ScanResult 
                                { 
                                    Offset = realOffset, 
                                    Description = sig.Name 
                                });
                            }
                        }
                    }

                    // Progress
                    if (progress != null) progress.Report((float)pos / length);

                    // Move window
                    pos += read;
                    
                    // Handle overlap if not end of file (Rewind for next block to check boundary)
                    // Simple approach: Rewind file pointer
                    if (pos < length && overlap > 0)
                    {
                        fs.Seek(-overlap, SeekOrigin.Current);
                        pos -= overlap;
                    }
                }
            }
            return results;
        }

        public static async Task<List<EntropyResult>> CalculateEntropyAsync(string file, int blockSize = 1024, IProgress<float>? progress = null)
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

        public static async Task<List<StringResult>> ExtractStringsAsync(string file, int minLen = 4, IProgress<float>? progress = null)
        {
            var results = new List<StringResult>();
            if (!File.Exists(file)) return results;

            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] buffer = new byte[1024 * 64]; // 64KB buffer
                long length = fs.Length;
                long totalRead = 0;
                int bytesRead;

                // State
                long currentStringOffset = -1;
                var currentString = new StringBuilder();

                while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < bytesRead; i++)
                    {
                        byte b = buffer[i];
                        char c = (char)b;

                        // Printable ASCII check (32-126) + Tab/Newline? usually just 32-126
                        bool isPrintable = (b >= 32 && b <= 126);

                        if (isPrintable)
                        {
                            if (currentString.Length == 0) currentStringOffset = totalRead + i;
                            currentString.Append(c);
                        }
                        else
                        {
                            if (currentString.Length >= minLen)
                            {
                                results.Add(new StringResult 
                                { 
                                    Offset = currentStringOffset, 
                                    Text = currentString.ToString() 
                                });
                            }
                            currentString.Clear();
                        }
                    }

                    totalRead += bytesRead;
                    if (progress != null) progress.Report((float)totalRead / length);
                }
                
                // Final flush
                if (currentString.Length >= minLen)
                {
                    results.Add(new StringResult 
                    { 
                        Offset = currentStringOffset, 
                        Text = currentString.ToString() 
                    });
                }
            }
            return results;
        }
        public static List<Signature> LoadSignatures(string path)
        {
            var sigs = new List<Signature>();
            if (!File.Exists(path)) return sigs;

            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith("#") || line.Trim().StartsWith("//")) continue;

                var parts = line.Split(';');
                if (parts.Length < 2) continue;

                try
                {
                    string name = parts[0].Trim();
                    string hex = parts[1].Trim().Replace(" ", "").Replace("0x", "");
                    
                    if (hex.Length % 2 != 0) continue; // Invalid hex

                    byte[] magic = new byte[hex.Length / 2];
                    for (int i = 0; i < hex.Length; i += 2)
                        magic[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);

                    int offset = 0;
                    if (parts.Length > 2)
                    {
                        int.TryParse(parts[2].Trim(), out offset);
                    }

                    sigs.Add(new Signature { Name = name, Magic = magic, MagicOffset = offset });
                }
                catch
                {
                    // Ignore bad lines
                }
            }
            return sigs;
        }
    }
}
