using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Ext2Read.Core
{
    public class DiskManager : IDisposable
    {
        private List<DiskAccess> _openedDisks = new List<DiskAccess>();
        const int MAX_DRIVES = 16;

        public List<Ext2Partition> ScanSystem()
        {
            var partitions = new List<Ext2Partition>();
            var localDisks = new List<DiskAccess>();
            object lockObj = new object();

            System.Threading.Tasks.Parallel.For(0, MAX_DRIVES, i =>
            {
                string path = $"\\\\.\\PhysicalDrive{i}";
                var disk = new DiskAccess();

                if (disk.Open(path))
                {
                    lock (lockObj)
                    {
                        localDisks.Add(disk);
                    }

                    var drivePartitions = ScanPartitions(disk, i);

                    lock (lockObj)
                    {
                        partitions.AddRange(drivePartitions);
                    }
                }
            });

            _openedDisks.AddRange(localDisks);
            return partitions;
        }

        public List<Ext2Partition> ScanImage(string path)
        {
            var partitions = new List<Ext2Partition>();
            var disk = new DiskAccess();

            // Try to open the file
            if (!disk.Open(path))
            {
                return partitions;
            }

            _openedDisks.Add(disk);

            // 1. Try to scan as a full disk (MBR/GPT)
            var drivePartitions = ScanPartitions(disk, -1); // -1 indicating image file
            if (drivePartitions.Count > 0)
            {
                partitions.AddRange(drivePartitions);
                // Also update names to reflect image
                foreach (var p in partitions)
                {
                    // p.Name setter is private/not exposed? We might need to adjust or just leave generic
                    // For now, simpler to just accept generic names or refactor Ext2Partition to have settable Name
                }
            }
            else
            {
                // 2. If no partitions found, maybe it's a raw loopback partition (just the FS)
                // Check superblock at 1024 bytes
                byte[] sbData = disk.ReadSector(2, 2, PartitionConstants.SECTOR_SIZE); // 1024 bytes offset
                if (sbData != null)
                {
                    var sb = BytesToStruct<EXT2_SUPER_BLOCK>(sbData, 0);
                    if (sb.s_magic == Ext2Constants.EXT2_SUPER_MAGIC)
                    {
                        // It's a raw ext2 image
                        // Use file size for sector count? Or from superblock
                        long fileSize = new FileInfo(path).Length;
                        ulong sectors = (ulong)(fileSize / PartitionConstants.SECTOR_SIZE);

                        partitions.Add(new Ext2Partition(disk, 0, sectors, PartitionConstants.SECTOR_SIZE, $"Image: {System.IO.Path.GetFileName(path)}"));
                    }
                }
            }

            return partitions;
        }

        private List<Ext2Partition> ScanPartitions(DiskAccess disk, int diskIndex)
        {
            var parts = new List<Ext2Partition>();
            byte[] mbr = disk.ReadSector(0, 1, PartitionConstants.SECTOR_SIZE);

            if (mbr == null || mbr.Length < 512) return parts;
            if (mbr[510] != 0x55 || mbr[511] != 0xAA) return parts; // Invalid MBR signature

            // Check primary partitions
            for (int i = 0; i < 4; i++)
            {
                int offset = 446 + (i * 16);
                var part = BytesToStruct<MBRpartition>(mbr, offset);

                if (part.sys_ind == 0) continue;

                if (part.sys_ind == PartitionConstants.EXT2_PARTITION_ID)
                {
                    parts.Add(new Ext2Partition(disk, part.start_sect, part.nr_sects, PartitionConstants.SECTOR_SIZE, $"Disk {diskIndex} Part {i + 1}"));
                }
                else if (part.sys_ind == PartitionConstants.GPT_PROTECTIVE_MBR_ID)
                {
                    parts.AddRange(ScanGPT(disk, part.start_sect, diskIndex));
                }
                else if (part.sys_ind == 0x05 || part.sys_ind == 0x0F) // Extended
                {
                    // Scan EBR
                    parts.AddRange(ScanEBR(disk, part.start_sect, diskIndex));
                }
            }

            return parts;
        }

        private List<Ext2Partition> ScanEBR(DiskAccess disk, uint baseSector, int diskIndex)
        {
            var parts = new List<Ext2Partition>();
            uint nextEbr = 0; // Relative to baseSector
            uint currentBase = baseSector;
            int logicalIndex = 5;

            while (true)
            {
                byte[] ebr = disk.ReadSector(currentBase + nextEbr, 1, PartitionConstants.SECTOR_SIZE);
                if (ebr == null || ebr[510] != 0x55 || ebr[511] != 0xAA) break;

                var part1 = BytesToStruct<MBRpartition>(ebr, 446); // First entry: the logical partition
                var part2 = BytesToStruct<MBRpartition>(ebr, 462); // Second entry: link to next EBR

                if (part1.sys_ind == PartitionConstants.EXT2_PARTITION_ID)
                {
                    parts.Add(new Ext2Partition(disk, currentBase + nextEbr + part1.start_sect, part1.nr_sects, PartitionConstants.SECTOR_SIZE, $"Disk {diskIndex} Part {logicalIndex}"));
                }

                if (part2.sys_ind == 0) break; // No more logical partitions

                nextEbr = part2.start_sect; // Pointer to next EBR is relative to Extended Partition Base (baseSector) OR current EBR? 
                                            // Standard: relative to Ext Partition Start.
                                            // Wait, C++ code says:
                                            // nextPart = base; (this is extended part start)
                                            // ebr2 = get_start_sect(part1 which is link);
                                            // nextPart = (ebr2 + ebrBase);
                                            // Actually standard EBR:
                                            // Entry 1: Offset from current EBR.
                                            // Entry 2: Offset from Extended Partition Start.

                // Let's refine based on spec:
                // First entry points to the actual volume, StartSector is relative to CURRENT EBR sector.
                // Second entry points to NEXT EBR, StartSector is relative to EXTENDED PARTITION START.

                // C++ Logic:
                // partition = new Ext2Partition(..., part->start_sect + ebrBase + ebr2, ...); 
                // ebrBase is extended partition start. ebr2 is accumulated offset?

                // Let's stick to standard interpretation:
                // Entry 1 (Logical): start relative to CURRENT EBR.
                // Entry 2 (Next): start relative to PRIMARY EXTENDED START.

                // Correct logic:
                // logical_data_start = current_ebr_sector + part1.start_sect;
                // next_ebr_sector = baseSector + part2.start_sect; 

                // Correction for this code:
                // My part1 definition above was generic MBR partition.
                // part1.start_sect is relative to the sector passing to ReadSector (which is currentBase + nextEbr).

                // WAIT! MBR entries are LBA relative to "something".
                // In EBR:
                // Entry 1 start is relative to the EBR sector itself.
                // Entry 2 start is relative to the Extended Partition start.

                // Correct implementation:
                parts.Add(new Ext2Partition(disk, (currentBase + nextEbr) + part1.start_sect, part1.nr_sects, PartitionConstants.SECTOR_SIZE, $"Disk {diskIndex} Part {logicalIndex}"));

                logicalIndex++;

                nextEbr = part2.start_sect; // This is relative to baseSector (Extended Start)
                                            // currentBase is baseSector. So just:
                                            // next loop reads baseSector + nextEbr.
            }
            return parts;
        }

        private List<Ext2Partition> ScanGPT(DiskAccess disk, uint protectiveMbrStart, int diskIndex)
        {
            var parts = new List<Ext2Partition>();
            // Header is at LBA 1 (or protectiveMbrStart + 1)
            byte[] headerBytes = disk.ReadSector(1, 1, PartitionConstants.SECTOR_SIZE);
            if (headerBytes == null) return parts;

            var header = BytesToStruct<GPTHeader>(headerBytes, 0);
            if (header.signature != 0x5452415020494645) return parts; // "EFI PART"

            // Read partition table
            // header.partition_lba
            // header.num_partitions
            // header.entry_size

            // We need to read sectors covering num_partitions * entry_size
            ulong tableLba = header.partition_lba;
            uint totalBytes = header.num_partitions * header.entry_size;
            int sectorsToRead = (int)((totalBytes + PartitionConstants.SECTOR_SIZE - 1) / PartitionConstants.SECTOR_SIZE);

            byte[] tableBytes = disk.ReadSector(tableLba, sectorsToRead, PartitionConstants.SECTOR_SIZE);
            if (tableBytes == null) return parts;

            for (uint i = 0; i < header.num_partitions; i++)
            {
                int offset = (int)(i * header.entry_size);
                if (offset + Marshal.SizeOf(typeof(GPTPartition)) > tableBytes.Length) break;

                var entry = BytesToStruct<GPTPartition>(tableBytes, offset);

                // Check if unused (GUID type 0)
                bool allZero = true;
                foreach (var b in entry.type_guid) if (b != 0) allZero = false;
                if (allZero) continue;

                // Check if Linux Filesystem GUID
                // 0FC63DAF-8483-4772-8E79-3D69D8477DE4
                var linuxFsGuid = new Guid("0FC63DAF-8483-4772-8E79-3D69D8477DE4");
                var partGuid = new Guid(entry.type_guid);

                if (partGuid == linuxFsGuid)
                {
                    parts.Add(new Ext2Partition(disk, entry.first_lba, entry.last_lba - entry.first_lba, PartitionConstants.SECTOR_SIZE, $"Disk {diskIndex} GPT Part {i}"));
                }
            }

            return parts;
        }

        private static T BytesToStruct<T>(byte[] bytes, int offset) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(bytes, offset, ptr, size);
                return (T)Marshal.PtrToStructure(ptr, typeof(T));
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public void Dispose()
        {
            foreach (var disk in _openedDisks)
            {
                disk.Dispose();
            }
            _openedDisks.Clear();
        }
    }
}
