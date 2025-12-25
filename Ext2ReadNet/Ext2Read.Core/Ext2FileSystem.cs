using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Ext2Read.Core
{
    public class Ext2FileSystem
    {
        private Ext2Partition _partition;
        private EXT2_SUPER_BLOCK _superBlock;
        private EXT2_GROUP_DESC[] _groupDescriptors;
        private int _blockSize;
        private int _inodeSize;

        public string VolumeName => _superBlock.s_volume_name;
        public string LastDebugMessage { get; private set; }

        public Ext2FileSystem(Ext2Partition partition)
        {
            _partition = partition;
        }

        public bool Mount()
        {
            // Read Superblock (Wait, SB is always at offset 1024 bytes from start of partition)
            // If block size is 1024, it's block 1. If 2048/4096, it's block 0 with offset 1024.
            // Simplest: Read 1024 bytes at offset 1024.

            byte[] sbData = _partition.ReadSectors(2, 2); // 1024 bytes (2 sectors @ 512). 1024 offset = sector 2.
                                                          // Wait, offset 1024 from start of partition.
                                                          // Sector 0: 0-511
                                                          // Sector 1: 512-1023
                                                          // Sector 2: 1024-1535.
                                                          // So Superblock starts at Sector 2.

            if (sbData == null) return false;

            _superBlock = BytesToStruct<EXT2_SUPER_BLOCK>(sbData, 0);

            if (_superBlock.s_magic != Ext2Constants.EXT2_SUPER_MAGIC)
                return false;

            _blockSize = (int)(Ext2Constants.EXT2_MIN_BLOCK_SIZE << (int)_superBlock.s_log_block_size);

            _inodeSize = (_superBlock.s_rev_level == 0) ? 128 : _superBlock.s_inode_size;

            ReadGroupDescriptors();
            return true;
        }

        private void ReadGroupDescriptors()
        {
            int groupsCount = (int)((_superBlock.s_blocks_count - _superBlock.s_first_data_block + _superBlock.s_blocks_per_group - 1) / _superBlock.s_blocks_per_group);
            _groupDescriptors = new EXT2_GROUP_DESC[groupsCount];

            // GDT starts at block s_first_data_block + 1
            ulong gdtBlock = _superBlock.s_first_data_block + 1;

            // Calculate how many blocks GDT takes
            int descSize = Marshal.SizeOf(typeof(EXT2_GROUP_DESC));
            int descriptorsPerBlock = _blockSize / descSize;
            int gdtBlocks = (groupsCount + descriptorsPerBlock - 1) / descriptorsPerBlock;

            // Read all GDT blocks
            // Note: Simplification - reading block by block
            int currentGroup = 0;
            for (int i = 0; i < gdtBlocks; i++)
            {
                byte[] block = _partition.ReadBlock(gdtBlock + (ulong)i, _blockSize);
                for (int j = 0; j < descriptorsPerBlock && currentGroup < groupsCount; j++)
                {
                    _groupDescriptors[currentGroup] = BytesToStruct<EXT2_GROUP_DESC>(block, j * descSize);
                    currentGroup++;
                }
            }
        }

        public EXT2_INODE ReadInode(uint inodeNum)
        {
            if (inodeNum < 1) throw new ArgumentException("Invalid inode number");

            uint group = (inodeNum - 1) / _superBlock.s_inodes_per_group;
            uint index = (inodeNum - 1) % _superBlock.s_inodes_per_group;

            ulong inodeTableBlock = _groupDescriptors[group].bg_inode_table;

            ulong blockOffset = (ulong)((index * _inodeSize) / _blockSize);
            int byteOffset = (int)((index * _inodeSize) % _blockSize);

            byte[] block = _partition.ReadBlock(inodeTableBlock + blockOffset, _blockSize);
            return BytesToStruct<EXT2_INODE>(block, byteOffset);
        }

        public List<Ext2FileEntry> ListDirectory(uint dirInodeNum)
        {
            // Simplified: Assuming directory doesn't use htree index for now, just linear scan
            var files = new List<Ext2FileEntry>();
            var inode = ReadInode(dirInodeNum);

            if ((inode.i_mode & Ext2Constants.S_IFDIR) == 0) return files;

            // Iterate blocks
            int nBlocks = (int)(inode.i_size + _blockSize - 1) / _blockSize;
            StringBuilder debug = new StringBuilder();
            debug.AppendLine($"Listing Inode {dirInodeNum}: Size={inode.i_size}, Blocks={inode.i_blocks}, nBlocks={nBlocks}");

            for (int i = 0; i < 12 && i < nBlocks; i++) // Direct blocks
            {
                if (inode.i_block[i] == 0)
                {
                    debug.AppendLine($"Block[{i}] is 0. Ending.");
                    break;
                }

                debug.AppendLine($"Reading Block[{i}] @ {inode.i_block[i]}");
                byte[] block = _partition.ReadBlock(inode.i_block[i], _blockSize);
                if (block == null)
                {
                    debug.AppendLine("ReadBlock returned null.");
                    break;
                }

                int offset = 0;
                while (offset < _blockSize)
                {
                    // Check if we have enough bytes for fixed header (8 bytes)
                    if (offset + 8 > _blockSize)
                    {
                        debug.AppendLine($"Offset {offset} + 8 > BlockSize {_blockSize}. Break.");
                        break;
                    }

                    var entry = BytesToStruct<EXT2_DIR_ENTRY>(block, offset);
                    if (entry.rec_len == 0)
                    {
                        debug.AppendLine($"Offset {offset}: rec_len is 0. Break.");
                        break;
                    }

                    // Check if record length goes beyond block
                    if (offset + entry.rec_len > _blockSize)
                    {
                        debug.AppendLine($"Offset {offset}: rec_len {entry.rec_len} > remaining. Break.");
                        break;
                    }

                    if (entry.inode != 0)
                    {
                        // Check name length bounds
                        if (offset + 8 + entry.name_len > _blockSize)
                        {
                            debug.AppendLine($"Offset {offset}: Name len {entry.name_len} out of bounds. Break.");
                            break;
                        }

                        string name = Encoding.Default.GetString(block, offset + 8, entry.name_len);
                        debug.AppendLine($"Found: {name} (Inode {entry.inode})");

                        if (name != "." && name != "..")
                        {
                            // Get type info
                            bool isDir = entry.filetype == 2; // 2=DIR
                            files.Add(new Ext2FileEntry
                            {
                                Name = name,
                                InodeNum = entry.inode,
                                IsDirectory = isDir
                            });
                        }
                    }
                    offset += entry.rec_len;
                }
            }
            LastDebugMessage = debug.ToString();
            return files;
        }

        private static T BytesToStruct<T>(byte[] bytes, int offset) where T : struct
        {
            // Same as DiskManager helper, duplicating for self-containment or could move to Util
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
    }

    public class Ext2FileEntry
    {
        public string Name { get; set; }
        public uint InodeNum { get; set; }
        public bool IsDirectory { get; set; }
    }
}
