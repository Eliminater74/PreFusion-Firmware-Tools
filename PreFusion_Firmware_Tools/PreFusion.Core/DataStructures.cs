using System;
using System.Runtime.InteropServices;

namespace Ext2Read.Core
{
    public static class Ext2Constants
    {
        public const ushort EXT2_SUPER_MAGIC = 0xEF53;
        public const int EXT2_MIN_BLOCK_SIZE = 1024;
        public const int EXT2_MAX_BLOCK_SIZE = 4096;
        public const int EXT2_N_BLOCKS = 15;
        public const int EXT2_NAME_LEN = 255;

        // Inode Modes
        public const ushort S_IFMT = 0xF000;
        public const ushort S_IFDIR = 0x4000;
        public const ushort S_IFREG = 0x8000;

        // Features
        public const uint EXT4_EXTENTS_FL = 0x80000;
        public const ushort EXT4_EXTENT_HEADER_MAGIC = 0xF30A;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EXT2_SUPER_BLOCK
    {
        public uint s_inodes_count;      /* total no of inodes */
        public uint s_blocks_count;      /* total no of blocks */
        public uint s_r_blocks_count;    /* total no of blocks reserved for exclusive use of superuser */
        public uint s_free_blocks_count; /* total no of free blocks */
        public uint s_free_inodes_count; /* total no of free inodes */
        public uint s_first_data_block;  /* position of the first data block */
        public uint s_log_block_size;    /* used to compute logical block size in bytes */
        public uint s_log_frag_size;     /* used to compute logical fragment size */
        public uint s_blocks_per_group;  /* total number of blocks contained in the group */
        public uint s_frags_per_group;   /* total number of fragments in a group */
        public uint s_inodes_per_group;  /* number of inodes in a group */
        public uint s_mtime;             /* time at which the last mount was performed */
        public uint s_wtime;             /* time at which the last write was performed */
        public ushort s_mnt_count;       /* number of time the fs system has been mounted in r/w mode without having checked */
        public ushort s_max_mnt_count;   /* the max no of times the fs can be mounted in r/w mode before a check must be done */
        public ushort s_magic;           /* a number that identifies the fs (eg. 0xef53 for ext2) */
        public ushort s_state;           /* gives the state of fs (eg. 0x001 is Unmounted cleanly) */
        public ushort s_pad;             /* unused */
        public ushort s_minor_rev_level;
        public uint s_lastcheck;         /* the time of last check performed */
        public uint s_checkinterval;     /* the max possible time between checks on the fs */
        public uint s_creator_os;        /* os */
        public uint s_rev_level;         /* Revision level */
        public ushort s_def_resuid;      /* default uid for reserved blocks */
        public ushort s_def_regid;       /* default gid for reserved blocks */

        /* for EXT2_DYNAMIC_REV superblocks only */
        public uint s_first_ino;         /* First non-reserved inode */
        public ushort s_inode_size;      /* size of inode structure */
        public ushort s_block_group_nr;  /* block group # of this superblock */
        public uint s_feature_compat;    /* compatible feature set */
        public uint s_feature_incompat;  /* incompatible feature set */
        public uint s_feature_ro_compat; /* readonly-compatible feature set */

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] s_uuid;          /* 128-bit uuid for volume */

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string s_volume_name;     /* volume name */

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string s_last_mounted;    /* directory where last mounted */

        public uint s_algorithm_usage_bitmap; /* For compression */
        public byte s_prealloc_blocks;   /* Nr of blocks to try to preallocate*/
        public byte s_prealloc_dir_blocks; /* Nr to preallocate for dirs */
        public ushort s_padding1;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 204)]
        public uint[] s_reserved;        /* unused */
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EXT2_GROUP_DESC
    {
        public uint bg_block_bitmap;      /* points to the blocks bitmap for the group */
        public uint bg_inode_bitmap;      /* points to the inodes bitmap for the group */
        public uint bg_inode_table;       /* points to the inode table first block */
        public ushort bg_free_blocks_count; /* number of free blocks in the group */
        public ushort bg_free_inodes_count; /* number of free inodes in the */
        public ushort bg_used_dirs_count;   /* number of inodes allocated to directories */
        public ushort bg_pad;             /* padding */

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public uint[] bg_reserved;        /* reserved */
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EXT2_INODE
    {
        public ushort i_mode;        /* File mode */
        public ushort i_uid;         /* Low 16 bits of Owner Uid */
        public uint i_size;          /* Size in bytes */
        public uint i_atime;         /* Access time */
        public uint i_ctime;         /* Creation time */
        public uint i_mtime;         /* Modification time */
        public uint i_dtime;         /* Deletion Time */
        public ushort i_gid;         /* Low 16 bits of Group Id */
        public ushort i_links_count; /* Links count */
        public uint i_blocks;        /* Blocks count */
        public uint i_flags;         /* File flags */

        public uint osd1;            /* OS dependent 1 */

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 15)] // EXT2_N_BLOCKS
        public uint[] i_block;       /* Pointers to blocks */

        public uint i_generation;    /* File version (for NFS) */
        public uint i_file_acl;      /* File ACL */
        public uint i_size_high;     /* This is used store the high 32 bit of file size in large files */
        public uint i_faddr;         /* Fragment address */

        public ushort l_i_blocks_hi; /* High 16 bits of block count */
        public ushort l_i_file_acl_high;
        public ushort l_i_uid_high;  /* these 2 fields */
        public ushort l_i_gid_high;  /* were reserved2[0] */
        public ushort l_i_checksum_lo; /* crc32c(uuid+inum+inode) LE */
        public ushort l_i_reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EXT2_DIR_ENTRY
    {
        public uint inode;          /* Inode number */
        public ushort rec_len;      /* Directory entry length */
        public byte name_len;       /* Name length */
        public byte filetype;       /* File type */
        // Name follows immediately, variable length
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EXT4_EXTENT_HEADER
    {
        public ushort eh_magic;      /* probable magic number - 0xF30A */
        public ushort eh_entries;    /* number of valid entries */
        public ushort eh_max;        /* capacity of store in entries */
        public ushort eh_depth;      /* has tree real underlying blocks? */
        public uint eh_generation;   /* generation of the tree */
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EXT4_EXTENT
    {
        public uint ee_block;       /* first logical block extent covers */
        public ushort ee_len;       /* number of blocks covered by extent */
        public ushort ee_start_hi;  /* high 16 bits of physical block */
        public uint ee_start_lo;    /* low 32 bits of physical block */
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EXT4_EXTENT_IDX
    {
        public uint ei_block;       /* index covers logical blocks from 'block' */
        public uint ei_leaf_lo;     /* pointer to the physical block of the next level. leaf or next index */
        public ushort ei_leaf_hi;   /* high 16 bits of physical block */
        public ushort ei_unused;
    }
}
