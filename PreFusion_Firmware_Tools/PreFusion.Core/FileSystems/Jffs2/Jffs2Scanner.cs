using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PreFusion.Core.FileSystems.Jffs2
{
    public class Jffs2Node
    {
        public Jffs2RawInode? Inode { get; set; }
        public Jffs2RawDirent? Dirent { get; set; }
        public long Offset { get; set; }
        public string Name { get; set; }
    }

    public class Jffs2Scanner
    {
        private Stream _stream;
        public Stream Stream => _stream; // Expose for FileSystem
        private BinaryReader _reader;
        private bool _isBigEndian;

        public Jffs2Scanner(Stream stream)
        {
            _stream = stream;
            _reader = new BinaryReader(stream);
            DetectEndianness();
        }

        private void DetectEndianness()
        {
            // Simple heuristic check at the start or search for first magic
            long start = _stream.Position;
            byte[] buf = new byte[2];
            _stream.Read(buf, 0, 2);
            ushort magic = BitConverter.ToUInt16(buf, 0);
            
            if (magic == Jffs2Constants.JFFS2_MAGIC_BITMASK)
            {
                _isBigEndian = false;
            }
            else if (magic == 0x8519) // Big Endian representation
            {
                _isBigEndian = true;
            }
            // else scan forward? for now assume Little Endian or standard
            _stream.Position = start;
        }

        public List<Jffs2Node> Scan()
        {
            var nodes = new List<Jffs2Node>();
            long length = _stream.Length;

            // Simple byte-by-byte or block scan for 0x1985
            // Doing a buffered scan for performance
            byte[] buffer = new byte[4096];
            
            while (_stream.Position < length - 4)
            {
                long currentPos = _stream.Position;
                
                // Align to 4 bytes? JFFS2 nodes are 4-byte aligned usually, but padding nodes exist.
                // Let's try reading a UInt16 magic.
                if (currentPos % 4 != 0)
                {
                    long padding = 4 - (currentPos % 4);
                    if (_stream.Position + padding > length) break;
                    _stream.Seek(padding, SeekOrigin.Current);
                    currentPos = _stream.Position;
                }

                if (currentPos >= length) break;

                ushort magic = ReadUInt16();

                if (magic == Jffs2Constants.JFFS2_MAGIC_BITMASK)
                {
                    // Found a node
                    ushort nodeType = ReadUInt16();
                    uint totlen = ReadUInt32();
                    uint hdrCrc = ReadUInt32();

                    // Verify Header CRC? (Skip for speed for now, or implement later)

                    // Seek back to start of node to read full struct
                    _stream.Position = currentPos;

                    if (nodeType == Jffs2Constants.JFFS2_NODETYPE_INODE)
                    {
                        var inode = ReadStruct<Jffs2RawInode>();
                        nodes.Add(new Jffs2Node { Inode = inode, Offset = currentPos });
                    }
                    else if (nodeType == Jffs2Constants.JFFS2_NODETYPE_DIRENT)
                    {
                        var dirent = ReadStruct<Jffs2RawDirent>();
                        
                        // Read Name
                        int nameLen = dirent.Nsize;
                        byte[] nameBytes = _reader.ReadBytes(nameLen);
                        string name = Encoding.UTF8.GetString(nameBytes);

                        nodes.Add(new Jffs2Node { Dirent = dirent, Offset = currentPos, Name = name });
                    }

                    // Jump to end of node
                    // Pad to 4 bytes
                    long nextPos = currentPos + totlen;
                    nextPos = (nextPos + 3) & ~3;
                    
                    if (nextPos <= currentPos) // prevent infinite loop if totlen is 0 or bad
                        nextPos = currentPos + 4;
                        
                    if (nextPos > length) break;
                    
                    _stream.Position = nextPos;
                }
                else
                {
                    // Scan forward for magic byte 0x85 0x19 (LE) -> 0x1985
                    // But we read as ushort.
                    // If mismatch, skip 4 bytes
                     _stream.Seek(2, SeekOrigin.Current); // We read 2 already (magic), read 2 more (Type) effectively skipping 4
                }
            }

            return nodes;
        }

        private ushort ReadUInt16()
        {
            var val = _reader.ReadUInt16();
            if (_isBigEndian) return (ushort)((val << 8) | (val >> 8));
            return val;
        }

        private uint ReadUInt32()
        {
            var val = _reader.ReadUInt32();
            if (_isBigEndian) 
            {
                return ((val & 0x000000FF) << 24) |
                       ((val & 0x0000FF00) << 8) |
                       ((val & 0x00FF0000) >> 8) |
                       ((val & 0xFF000000) >> 24);
            }
            return val;
        }

        private T ReadStruct<T>() where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] bytes = _reader.ReadBytes(size);
            
            // Handle Endianness for fields? 
            // For complex structs, standard Marshal won't flip fields automatically.
            // We might need to manually flip fields after reading if BigEndian.
            // For now, assuming LE, but JFFS2 handles Endianness.
            // TODO: Implement reflection-based field swapping if BE detected.

            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            T structure = (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            handle.Free();

            return structure;
        }
    }
}
