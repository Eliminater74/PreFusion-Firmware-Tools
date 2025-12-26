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

            List<ulong> dataBlocks = GetDataBlocks(inode, debug);
            debug.AppendLine($"Resolved {dataBlocks.Count} physical blocks.");

            for (int i = 0; i < dataBlocks.Count && i < nBlocks; i++)
            {
                ulong blockNum = dataBlocks[i];
                if (blockNum == 0)
                {
                    debug.AppendLine($"Block[{i}] is 0 (Sparse). Skipping.");
                    continue;
                }

                debug.AppendLine($"Reading Block[{i}] @ {blockNum}");
                byte[] block = _partition.ReadBlock(blockNum, _blockSize);
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

                            // Read Inode for metadata (Size, Permissions, etc.)
                            EXT2_INODE childInode = ReadInode(entry.inode);
                            long size = childInode.i_size | ((long)childInode.i_size_high << 32);
                            DateTime mtime = DateTimeOffset.FromUnixTimeSeconds(childInode.i_mtime).LocalDateTime;

                            files.Add(new Ext2FileEntry
                            {
                                Name = name,
                                InodeNum = entry.inode,
                                IsDirectory = isDir,
                                Size = size,
                                Mode = childInode.i_mode,
                                Uid = childInode.i_uid,
                                Gid = childInode.i_gid,
                                ModifiedTime = mtime
                            });
                        }
                    }
                    offset += entry.rec_len;
                }
            }
            LastDebugMessage = debug.ToString();
            return files;
        }

        private List<ulong> GetDataBlocks(EXT2_INODE inode, StringBuilder debug = null)
        {
            var blocks = new List<ulong>();

            // Check for Extents
            if ((inode.i_flags & Ext2Constants.EXT4_EXTENTS_FL) != 0)
            {
                if (debug != null) debug.AppendLine("Inode uses Extents.");
                // Convert i_block array to byte array
                byte[] i_block_bytes = new byte[60];
                Buffer.BlockCopy(inode.i_block, 0, i_block_bytes, 0, 60);

                // Parse Extent Header at offset 0
                var header = BytesToStruct<EXT4_EXTENT_HEADER>(i_block_bytes, 0);
                if (header.eh_magic == Ext2Constants.EXT4_EXTENT_HEADER_MAGIC)
                {
                    WalkExtentNode(i_block_bytes, 0, blocks, debug);
                }
                else
                {
                    if (debug != null) debug.AppendLine($"Invalid Extent Magic: {header.eh_magic:X4}");
                }
            }
            else
            {
                if (debug != null) debug.AppendLine("Inode uses Direct Blocks.");
                // Direct blocks 0-11
                for (int i = 0; i < 12; i++)
                {
                    if (inode.i_block[i] != 0) blocks.Add(inode.i_block[i]);
                    else blocks.Add(0); // Sparse
                }
                // Indirect blocks omitted for now
            }
            return blocks;
        }

        public void ReadFile(uint inodeNum, System.IO.Stream outputStream)
        {
            var inode = ReadInode(inodeNum);
            // Support 64-bit file size
            long size = inode.i_size | ((long)inode.i_size_high << 32);

            // Check if it's a regular file or link. 
            // If it's a directory, we shouldn't really "read" it as a stream, but user might want to debug.

            List<ulong> blocks = GetDataBlocks(inode);
            long remaining = size;

            foreach (ulong blockNum in blocks)
            {
                if (remaining <= 0) break;

                int toRead = (int)Math.Min(_blockSize, remaining);

                if (blockNum == 0) // Sparse
                {
                    // Write zeros efficiently
                    // For very large sparse holes, allocating buffer might be slow, but _blockSize is small (4k usually).
                    byte[] zeros = new byte[toRead];
                    outputStream.Write(zeros, 0, toRead);
                }
                else
                {
                    byte[] data = _partition.ReadBlock(blockNum, _blockSize);
                    if (data != null)
                    {
                        outputStream.Write(data, 0, toRead);
                    }
                    else
                    {
                        // Read error, fill with zeros or throw?
                        // Filling with zeros to preserve offset
                        byte[] zeros = new byte[toRead];
                        outputStream.Write(zeros, 0, toRead);
                    }
                }
                remaining -= toRead;
            }
        }


        public List<Ext2FileEntry> SearchFiles(uint dirInodeNum, string searchQuery, string currentPath = "")
        {
            var results = new List<Ext2FileEntry>();
            string query = searchQuery.ToLower();

            // List current directory
            var files = ListDirectory(dirInodeNum);

            foreach (var file in files)
            {
                // Update FullPath
                if (string.IsNullOrEmpty(currentPath)) file.FullPath = "/" + file.Name;
                else file.FullPath = currentPath.TrimEnd('/') + "/" + file.Name;

                // Check match
                if (file.Name.ToLower().Contains(query))
                {
                    results.Add(file);
                }

                // Recurse if directory (skip . and .. which ListDirectory already filters)
                if (file.IsDirectory)
                {
                    // Avoid deep recursion stack overflow? 32k? 
                    // Ext2 can be deep. iterative might be safer but recursive is easier for now.
                    // C# limits recursion.
                    // But ListDirectory filters . and .. so we advance.
                    try
                    {
                        var childResults = SearchFiles(file.InodeNum, searchQuery, file.FullPath);
                        results.AddRange(childResults);
                    }
                    catch (Exception) { /* Skip errors in deep recursion or perm issues */ }
                }
            }
            return results;
        }

        private void WalkExtentNode(byte[] data, int offset, List<ulong> blocks, StringBuilder debug)
        {
            var header = BytesToStruct<EXT4_EXTENT_HEADER>(data, offset);

            // Entries start after header (12 bytes)
            int entryOffset = offset + 12;

            for (int i = 0; i < header.eh_entries; i++)
            {
                if (header.eh_depth == 0) // Leaf
                {
                    var extent = BytesToStruct<EXT4_EXTENT>(data, entryOffset);
                    ulong startBlock = ((ulong)extent.ee_start_hi << 32) | extent.ee_start_lo;
                    if (debug != null) debug.AppendLine($"Extent: Logical={extent.ee_block}, Len={extent.ee_len}, Phys={startBlock}");

                    for (int b = 0; b < extent.ee_len; b++)
                    {
                        blocks.Add(startBlock + (ulong)b);
                    }
                }
                else // Index
                {
                    var idx = BytesToStruct<EXT4_EXTENT_IDX>(data, entryOffset);
                    ulong leafBlock = ((ulong)idx.ei_leaf_hi << 32) | idx.ei_leaf_lo;
                    if (debug != null) debug.AppendLine($"Extent Index pointing to block {leafBlock}");

                    // Read index block
                    byte[] indexBlockData = _partition.ReadBlock(leafBlock, _blockSize);
                    if (indexBlockData != null)
                    {
                        WalkExtentNode(indexBlockData, 0, blocks, debug);
                    }
                }
                entryOffset += 12; // Both Extent and Index are 12 bytes
            }
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
        public long Size { get; set; }
        public uint Mode { get; set; }
        public uint Uid { get; set; }
        public uint Gid { get; set; }
        public DateTime ModifiedTime { get; set; }
        public string FullPath { get; set; }
    }
}
