using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices; // Fix Marshal error

namespace PreFusion.Core.FileSystems.Jffs2
{
    public class Jffs2Entry
    {
        public uint Ino { get; set; }
        public string Name { get; set; }
        public bool IsDirectory { get; set; }
        public uint ParentIno { get; set; }
        public uint FileSize { get; set; }
        public uint Mode { get; set; }
        public List<Jffs2Node> Nodes { get; set; } = new List<Jffs2Node>();
        public List<Jffs2Entry> Children { get; set; } = new List<Jffs2Entry>();

        // Helpers to get metadata from the latest Inode
        public Jffs2RawInode? LatestInode => Nodes.Where(n => n.Inode.HasValue).OrderByDescending(n => n.Inode.Value.Version).Select(n => n.Inode).FirstOrDefault();

        public uint Uid => LatestInode?.Uid ?? 0;
        public uint Gid => LatestInode?.Gid ?? 0;
        public DateTime ModifiedTime 
        {
            get
            {
                var val = LatestInode?.Mtime ?? 0;
                return DateTimeOffset.FromUnixTimeSeconds(val).LocalDateTime;
            }
        }
    }

    public class Jffs2FileSystem
    {
        private Jffs2Scanner _scanner;
        private List<Jffs2Node> _allNodes;
        
        public Jffs2Entry Root { get; private set; }
        public Dictionary<uint, Jffs2Entry> InodeMap { get; private set; } = new Dictionary<uint, Jffs2Entry>();
        public string VolumeName { get; set; } = "JFFS2 Image";

        public Jffs2FileSystem(System.IO.Stream stream)
        {
            _scanner = new Jffs2Scanner(stream);
            _allNodes = _scanner.Scan();
            BuildTree();
        }

        private void BuildTree()
        {
            // JFFS2 is log-structured. Multiple nodes can share the same Ino (versioning).
            // We need to group by Ino.
            
            // 1. Identify all unique files/dirs from Dirent nodes
            // Dirents link a Name to an Ino and a ParentIno.
            var direntNodes = _allNodes.Where(n => n.Dirent.HasValue).OrderBy(n => n.Dirent.Value.Version); // Higher version overwrites?
            
            // Actually, newer versions of dirents with same name replace older ones?
            // "If a directory entry is written with version V, it obsoletes any older directory entry with same name in same directory."

            // Let's just collect all latest dirents.
            // Map (ParentIno, Name) -> Dirent
            
            // InodeMap: Ino -> Entry
            InodeMap[1] = new Jffs2Entry { Ino = 1, Name = "", IsDirectory = true }; // Root is always 1

            foreach (var node in direntNodes)
            {
                var d = node.Dirent.Value;
                if (d.Ino == 0) 
                {
                    // Unlink / Deletion
                    // TODO: Handle unlink
                    continue; 
                }

                if (!InodeMap.ContainsKey(d.Ino))
                {
                    InodeMap[d.Ino] = new Jffs2Entry 
                    { 
                        Ino = d.Ino, 
                        Name = node.Name, 
                        ParentIno = d.Pino,
                        IsDirectory = d.Type == 4 // DT_DIR
                    };
                }
                else
                {
                    // Update existing (maybe new name or parent?)
                    var entry = InodeMap[d.Ino];
                    entry.Name = node.Name;
                    entry.ParentIno = d.Pino;
                }
            }

            // 2. Associate Data Inodes
            var inodeNodes = _allNodes.Where(n => n.Inode.HasValue);
            foreach (var node in inodeNodes)
            {
                var i = node.Inode.Value;
                if (InodeMap.TryGetValue(i.Ino, out var entry))
                {
                    entry.Nodes.Add(node);
                    entry.Mode = i.Mode;
                    entry.FileSize = Math.Max(entry.FileSize, i.ISize);
                }
                // Else orphan inode? Or file without a dirent (shouldn't happen for valid files)
            }

            // 3. Build Hierarchy
            Root = InodeMap[1];
            foreach (var kvp in InodeMap)
            {
                var entry = kvp.Value;
                if (entry.Ino == 1) continue;

                if (InodeMap.TryGetValue(entry.ParentIno, out var parent))
                {
                    parent.Children.Add(entry);
                }
                // else orphan
            }
        }

        public void ReadFile(Jffs2Entry entry, System.IO.Stream output)
        {
            if (entry.IsDirectory) throw new InvalidOperationException("Cannot read a directory.");

            // Data nodes are those with Jffs2RawInode
            var dataNodes = entry.Nodes
                .Where(n => n.Inode.HasValue)
                .OrderBy(n => n.Inode.Value.Offset) // Sort by logical offset
                .ToList();

            // Note: JFFS2 nodes can overlap. Later version nodes obsolete earlier ones.
            // But simple implementation: Just process in version order?
            // Correct way: Map logical ranges (0-4096, 4096-8192) to nodes.
            // If multiple nodes cover same range, use highest version.
            
            // Simplified approach for now:
            // Iterate all nodes, decompress into a buffer, write to buffer.
            // Since we might have holes, we Initialize buffer with 0.
            
            byte[] fileBuffer = new byte[entry.FileSize];
            
            foreach (var node in dataNodes)
            {
                // Read Compressed Data
                var inode = node.Inode.Value;
                
                // Where is the data?
                // It follows the header (68 bytes for Inode)
                // In Jffs2Scanner, we didn't store the data in Jffs2Node, only the Offset to the node start.
                
                _scanner.Stream.Position = node.Offset + Marshal.SizeOf(typeof(Jffs2RawInode));
                
                // Read Csize bytes
                byte[] compressedData = new byte[inode.Csize];
                _scanner.Stream.Read(compressedData, 0, (int)inode.Csize);
                
                // Decompress
                byte[] uncompressedData;
                
                try
                {
                    uncompressedData = Jffs2Compression.Decompress(compressedData, inode.Dsize, inode.Compr);
                }
                catch (Exception ex)
                {
                    // Log error? Fill with 0?
                    // For now, continue
                    continue;
                }

                // Write to fileBuffer at inode.Offset
                if (inode.Offset + uncompressedData.Length <= fileBuffer.Length)
                {
                    Array.Copy(uncompressedData, 0, fileBuffer, (long)inode.Offset, uncompressedData.Length);
                }
                else
                {
                    // Truncated or bad size?
                    // Clip it
                    int len = (int)(fileBuffer.Length - inode.Offset);
                    if (len > 0)
                        Array.Copy(uncompressedData, 0, fileBuffer, (long)inode.Offset, len);
                }
            }
            
            output.Write(fileBuffer, 0, fileBuffer.Length);
        }
    }
}
