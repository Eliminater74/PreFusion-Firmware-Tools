using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ext2Read.Core
{
    public class OtaRepacker
    {
        private const int BLOCK_SIZE = 4096;

        public class RepackResult
        {
            public string NewDatPath { get; set; } = "";
            public string TransferListPath { get; set; } = "";
            public string PatchDatPath { get; set; } = "";
            public long TotalBlocks { get; set; }
        }

        public static async Task<RepackResult> RepackImageAsync(string imagePath, string outputDir, bool compress = true, IProgress<float>? progress = null)
        {
            if (!File.Exists(imagePath)) throw new FileNotFoundException("Image not found", imagePath);
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            string baseName = Path.GetFileNameWithoutExtension(imagePath);
            string newDatPath = Path.Combine(outputDir, $"{baseName}.new.dat");
            string transferPath = Path.Combine(outputDir, $"{baseName}.transfer.list");
            string patchPath = Path.Combine(outputDir, $"{baseName}.patch.dat");

            // Create empty patch.dat (required by some installers)
            File.WriteAllBytes(patchPath, Array.Empty<byte>()); // Header "BLOCK_IMAGE_UPDATE\0"? No, new.dat is usually raw.

            var dataRanges = new List<Range>();
            long totalBlocks = 0;

            // 1. Scan Image and Write .new.dat
            using (var fsInfo = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var fsDat = new FileStream(newDatPath, FileMode.Create, FileAccess.Write))
            {
                long length = fsInfo.Length;
                totalBlocks = (long)Math.Ceiling((double)length / BLOCK_SIZE);
                byte[] buffer = new byte[BLOCK_SIZE];
                int read;
                long currentBlock = 0;

                // Optimization: Buffer writes?
                // Also need to track ranges.
                long startBlock = -1;

                while ((read = await fsInfo.ReadAsync(buffer, 0, BLOCK_SIZE)) > 0)
                {
                    // Check if block is ALL zeros
                    bool isZero = true;
                    for (int i = 0; i < read; i++)
                    {
                        if (buffer[i] != 0)
                        {
                            isZero = false;
                            break;
                        }
                        // Handle partial block (padding is zero?)
                    }

                    if (!isZero)
                    {
                        // It is data
                        await fsDat.WriteAsync(buffer, 0, read);

                        if (startBlock == -1) startBlock = currentBlock; // Start range
                    }
                    else
                    {
                        // It is zero
                        if (startBlock != -1)
                        {
                            // Close range
                            dataRanges.Add(new Range(startBlock, currentBlock)); // Range is [start, end) usually
                            startBlock = -1;
                        }
                    }

                    currentBlock++;
                    if (progress != null && currentBlock % 1000 == 0) 
                        progress.Report((float)currentBlock / totalBlocks * 0.5f); // 0-50%
                }

                // Close final range
                if (startBlock != -1)
                {
                    dataRanges.Add(new Range(startBlock, currentBlock));
                }
            }

            // 2. Generate transfer.list
            // Header: 4
            // Count: totalBlocks
            // cmds...
            using (var sw = new StreamWriter(transferPath))
            {
                sw.WriteLine("4");
                sw.WriteLine(totalBlocks);
                sw.WriteLine("0"); // stash count?
                sw.WriteLine("0"); // limit?

                // ERASE command (optional, but good for cleanliness)? 
                // Usually `erase <ranges>` for all defined blocks not in new?
                // Or simply `new` overwrites. 
                // Let's just do `new` for data blocks.
                
                if (dataRanges.Count > 0)
                {
                    sw.WriteLine($"new 2 {RangesToString(dataRanges)}");
                }
            }

            // 3. Compress if requested
            string finalDatPath = newDatPath;
            if (compress)
            {
                string brPath = newDatPath + ".br";
                using (var src = new FileStream(newDatPath, FileMode.Open, FileAccess.Read))
                using (var dst = new FileStream(brPath, FileMode.Create))
                using (var brotli = new BrotliStream(dst, CompressionLevel.Optimal))
                {
                    await src.CopyToAsync(brotli);
                }
                
                // Return .br path
                finalDatPath = brPath;
                // Don't delete original yet, user might want to debug? 
                // Or delete if Cleanup requested. 
                // Method currently returns path.
            }

            return new RepackResult 
            { 
               NewDatPath = finalDatPath,
               TransferListPath = transferPath,
               PatchDatPath = patchPath,
               TotalBlocks = totalBlocks
            };
        }

        private static string RangesToString(List<Range> ranges)
        {
            var sb = new StringBuilder();
            sb.Append(ranges.Count);
            foreach (var r in ranges)
            {
                sb.Append($",{r.Start},{r.End}");
            }
            return sb.ToString();
        }

        public struct Range
        {
            public long Start;
            public long End; // Exclusive
            public Range(long s, long e) { Start = s; End = e; }
        }
    }
}
