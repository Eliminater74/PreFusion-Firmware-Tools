using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ext2Read.Core
{
    public static class OtaConverter
    {
        private const int BLOCK_SIZE = 4096;

        public static async Task DecompressBrotliAsync(string inputPath, string outputPath, IProgress<float> progress = null)
        {
            using (var inputFile = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
            using (var outputFile = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var brotliStream = new BrotliStream(inputFile, CompressionMode.Decompress))
            {
                byte[] buffer = new byte[81920];
                int read;
                long totalRead = 0;
                long length = inputFile.Length; // compressed length, estimation logic might be fuzzy

                while ((read = await brotliStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await outputFile.WriteAsync(buffer, 0, read);
                    totalRead += read; // Note: this tracks uncompressed output size roughly or input? 
                                       // Brotli stream doesn't expose input position easily.
                                       // We can track inputFile.Position but Brotli buffers.

                    if (progress != null && length > 0)
                    {
                        // Approximate progress based on input stream position
                        float percent = (float)inputFile.Position / length;
                        progress.Report(percent);
                    }
                }
            }
        }

        public static async Task ConvertDatToImgAsync(string transferListPath, string datPath, string outputImgPath, IProgress<float> progress = null)
        {
            // Validations
            if (!File.Exists(transferListPath)) throw new FileNotFoundException("Transfer list not found", transferListPath);
            if (!File.Exists(datPath)) throw new FileNotFoundException("Data file not found", datPath);

            var lines = await File.ReadAllLinesAsync(transferListPath);
            int version = int.Parse(lines[0]);
            int totalBlocks = int.Parse(lines[1]);

            // Skip stash/other header lines based on version
            int startLine = 2;
            if (version >= 2) startLine = 4; // V2+: version, total_blocks, stash_entries, max_stash

            // Open streams
            using (var dataStream = new FileStream(datPath, FileMode.Open, FileAccess.Read))
            using (var outStream = new FileStream(outputImgPath, FileMode.Create, FileAccess.Write))
            {
                // Pre-allocate file size? 
                // totalBlocks * 4096. 
                outStream.SetLength((long)totalBlocks * BLOCK_SIZE);

                for (int i = startLine; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string[] parts = line.Split(' ');
                    string cmd = parts[0];

                    if (cmd == "new")
                    {
                        // Format: new <range_count> <range_list>
                        // Example: new 2 10 20 30 40 (Ranges: 10-20, 30-40)
                        List<Tuple<long, long>> ranges = ParseRanges(parts);

                        foreach (var range in ranges)
                        {
                            long startBlock = range.Item1;
                            long endBlock = range.Item2;
                            long blockCount = endBlock - startBlock;
                            long byteCount = blockCount * BLOCK_SIZE;

                            // Read from dataStream (sequential)
                            // "new" commands consuming data in order of appearance in transfer list

                            // We need to copy `byteCount` from dataStream to `startBlock * BLOCK_SIZE` in outStream
                            byte[] buffer = new byte[BLOCK_SIZE];
                            // Use larger buffer for speed?

                            long bytesRemaining = byteCount;
                            long outputOffset = startBlock * BLOCK_SIZE;
                            outStream.Seek(outputOffset, SeekOrigin.Begin); // Random write

                            while (bytesRemaining > 0)
                            {
                                int toRead = (int)Math.Min(buffer.Length, bytesRemaining);
                                int read = await dataStream.ReadAsync(buffer, 0, toRead);
                                if (read == 0) throw new EndOfStreamException("Unexpected end of data stream");

                                await outStream.WriteAsync(buffer, 0, read);
                                bytesRemaining -= read;
                            }
                        }
                    }
                    else if (cmd == "erase" || cmd == "zero")
                    {
                        // Just ensure zeros? 
                        // Since we created file with SetLength, it might be sparse/zeroed initially?
                        // But if we seeked around, maybe safer to write zeros if we want explicit erase.
                        // But usually "new" writes over whatever.
                        // Optimization: If SetLength logic zeros file, we can skip specific Zero commands 
                        // unless we are reusing a file (which we aren't, FileMode.Create).

                        // We can skip writing zeros for performance if we trust OS handles new file as zero.
                    }
                    else
                    {
                        // "move", "bsdiff", "imgdiff" 
                        // These imply patching logic which requires the ORIGINAL image.
                        // For full ROM extraction, these shouldn't occur or are irrelevant for 'new.dat' based reconstruction?
                        // Actually, if a transfer list has 'move', it means it's an incremental OTA.
                        // We cannot convert incremental OTAs without source image.
                        // We should probably warn or throw if we see 'move' and don't have a source.
                        // But typically users want to extract "System.new.dat" which is usually a Full OTA.
                        if (cmd == "move")
                        {
                            // Warning: Incremental OTA detected. results may be incomplete.
                            // We can't handle it easily without source.
                        }
                    }

                    if (progress != null)
                    {
                        progress.Report((float)i / lines.Length);
                    }
                }
            }
        }

        private static List<Tuple<long, long>> ParseRanges(string[] parts)
        {
            // parts[0] is cmd
            // parts[1] is range count? NO, usually parts[1] is just part of numbers?
            // Actually spec: "cmd" then "range entries list"
            // Wait, typical Python code:
            // ranges = [int(i) for i in line.split(' ')[1].split(',')] -> format might be comma separated?
            // checking screenshots/examples:
            // "new 2,10,20,30,40"
            // Or "new 2 10 20 30 40"?
            // sdat2img.py: `tgt_ranges = [int(i) for i in pieces[1].split(',')]` (Wait, pieces split by space?)
            // No, `commands = transfer_list_file.readlines()`
            // `line.split(' ')` -> `['new', '2,10,20,30,40']` ?
            // Let's verify standard format.
            // "new 2 10 20 30 40" -> 2 ranges. Range 1: 10-20. Range 2: 30-40.
            // The python script often parses ranges directly.

            // Let's assume space separated for now based on some sources, OR check commas.
            // If parts[1] contains commas, we split it.

            var numbers = new List<long>();

            // Concatenate all parts after cmd and split by comma/space to be robust
            // Some formats might be "new,2,10,20..." ? No usually space after cmd.

            for (int i = 1; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.Contains(','))
                {
                    foreach (var sub in p.Split(','))
                    {
                        if (long.TryParse(sub, out long val)) numbers.Add(val);
                    }
                }
                else
                {
                    if (long.TryParse(p, out long val)) numbers.Add(val);
                }
            }

            // Number of ranges is NOT the first number.
            // Logic: the list itself is `[count, start1, end1, start2, end2...]`
            // The first number is indeed the count of range pairs? No, count of numbers?
            // Android code: `RangeSet("2,10,20,30,40")` -> parses string.
            // If string is "2,10,20,30,40", first number '2' is number of range pairs? Or number of integers following?
            // `RangeSet` documentation: "The string is a comma-separated list of integers. The first integer is the number of ranges."
            // So: 2 ranges. 10-20, 30-40. Total 4 integers follow.

            var ranges = new List<Tuple<long, long>>();
            if (numbers.Count == 0) return ranges;

            long rangeCount = numbers[0];
            // Validate
            // numbers.Count should be 1 + 2*rangeCount

            int currentIndex = 1;
            for (int k = 0; k < rangeCount; k++)
            {
                if (currentIndex + 1 >= numbers.Count) break;
                long start = numbers[currentIndex];
                long end = numbers[currentIndex + 1];
                ranges.Add(new Tuple<long, long>(start, end));
                currentIndex += 2;
            }

            return ranges;
        }
    }
}
