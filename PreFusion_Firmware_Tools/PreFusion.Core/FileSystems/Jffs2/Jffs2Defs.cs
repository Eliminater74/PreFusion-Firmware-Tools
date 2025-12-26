using System.Runtime.InteropServices;

namespace PreFusion.Core.FileSystems.Jffs2
{
    // detailed definitions from: https://github.com/torvalds/linux/blob/master/include/uapi/linux/jffs2.h

    public static class Jffs2Constants
    {
        public const ushort JFFS2_MAGIC_BITMASK = 0x1985;
        public const ushort JFFS2_OLD_MAGIC_BITMASK = 0x1984;
        public const ushort JFFS2_COMPR_TRUNC = 0x01; // Support for truncation (unused)
        
        // NodeType types
        public const ushort JFFS2_NODETYPE_DIRENT = 0xE001;
        public const ushort JFFS2_NODETYPE_INODE = 0xE002;
        public const ushort JFFS2_NODETYPE_CLEANMARKER = 0xE003;
        public const ushort JFFS2_NODETYPE_PADDING = 0xE004;
        public const ushort JFFS2_NODETYPE_SUMMARY = 0xE005;
        public const ushort JFFS2_NODETYPE_XATTR = 0xE006;
        public const ushort JFFS2_NODETYPE_XREF = 0xE007;

        // Compression types
        public const byte JFFS2_COMPR_NONE = 0x00;
        public const byte JFFS2_COMPR_ZERO = 0x01;
        public const byte JFFS2_COMPR_RTIME = 0x02;
        public const byte JFFS2_COMPR_RUBINMIPS = 0x03;
        public const byte JFFS2_COMPR_COPY = 0x04;
        public const byte JFFS2_COMPR_DYNRUBIN = 0x05;
        public const byte JFFS2_COMPR_ZLIB = 0x06;
        public const byte JFFS2_COMPR_LZO = 0x07;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Jffs2UnknownNode
    {
        public ushort Magic;
        public ushort NodeType;
        public uint Totlen;
        public uint HdrCrc;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Jffs2RawDirent
    {
        public ushort Magic;
        public ushort NodeType;
        public uint Totlen;
        public uint HdrCrc;
        public uint Pino;
        public uint Version;
        public uint Ino;
        public uint Mctime;
        public byte Nsize;
        public byte Type;
        public byte Unused; // node_crc
        public uint NodeCrc;
        public uint NameCrc;
        // char name[0] follows
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Jffs2RawInode
    {
        public ushort Magic;
        public ushort NodeType;
        public uint Totlen;
        public uint HdrCrc;
        public uint Ino;
        public uint Version;
        public uint Mode;
        public ushort Uid;
        public ushort Gid;
        public uint ISize;
        public uint Atime;
        public uint Mtime;
        public uint Ctime;
        public uint Offset;
        public uint Csize;
        public uint Dsize;
        public byte Compr;
        public byte UserCompr;
        public ushort Flags;
        public uint DataCrc;
        public uint NodeCrc;
        // data[0] follows
    }
}
