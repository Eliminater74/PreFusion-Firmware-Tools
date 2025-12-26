using System;

namespace Ext2Read.Core
{
    public class Ext2Partition
    {
        public ulong StartSector { get; private set; }
        public ulong SectorCount { get; private set; }
        public int SectorSize { get; private set; }
        public string Name { get; private set; }
        public DiskAccess Disk { get; private set; }

        public Ext2Partition(DiskAccess disk, ulong startSector, ulong sectorCount, int sectorSize, string name)
        {
            Disk = disk;
            StartSector = startSector;
            SectorCount = sectorCount;
            SectorSize = sectorSize;
            Name = name;
        }

        public byte[] ReadBlock(ulong blockNumber, int blockSize)
        {
            // Calculate absolute sector
            ulong absoluteSector = StartSector + (blockNumber * (ulong)blockSize / (ulong)SectorSize);
            int sectorsPerBlock = blockSize / SectorSize;

            return Disk.ReadSector(absoluteSector, sectorsPerBlock, SectorSize);
        }

        public byte[] ReadSectors(ulong partitionRelativeSector, int count)
        {
            return Disk.ReadSector(StartSector + partitionRelativeSector, count, SectorSize);
        }
    }
}
