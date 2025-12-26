using System;
using System.Runtime.InteropServices;

namespace Ext2Read.Core
{
    public static class PartitionConstants
    {
        public const int SECTOR_SIZE = 512;
        public const byte EXT2_PARTITION_ID = 0x83;
        public const byte LINUX_LVM_ID = 0x8E;
        public const byte GPT_PROTECTIVE_MBR_ID = 0xEE;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MBRpartition
    {
        public byte boot_ind;           /* 0x80 - active */
        public byte head;               /* starting head */
        public byte sector;             /* starting sector */
        public byte cyl;                /* starting cylinder */
        public byte sys_ind;            /* What partition type */
        public byte end_head;           /* end head */
        public byte end_sector;         /* end sector */
        public byte end_cyl;            /* end cylinder */
        public uint start_sect;         /* starting sector counting from 0 */
        public uint nr_sects;           /* nr of sectors in partition */
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GPTHeader
    {
        public ulong signature;
        public uint revision;
        public uint header_size;
        public uint header_crc32;
        public uint reserved1;
        public ulong current_lba;
        public ulong backup_lba;
        public ulong first_usable_lba;
        public ulong last_usable_lba;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] disk_guid;
        public ulong partition_lba;
        public uint num_partitions;
        public uint entry_size;
        public uint partition_crc32;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GPTPartition
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] type_guid;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] unique_guid;
        public ulong first_lba;
        public ulong last_lba;
        public ulong flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 72)]
        public string name;
    }
}
