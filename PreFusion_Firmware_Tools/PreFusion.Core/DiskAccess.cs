using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Ext2Read.Core
{
    public class DiskAccess :  IDisposable
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(
            SafeFileHandle hFile,
            [Out] byte[] lpBuffer,
            uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFilePointerEx(
            SafeFileHandle hFile,
            long liDistanceToMove,
            out long lpNewFilePointer,
            uint dwMoveMethod);

        private SafeFileHandle _handle;
        private const uint FILE_BEGIN = 0;

        public bool Open(string path)
        {
            _handle = CreateFile(
                path,
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            return !_handle.IsInvalid;
        }

        public void Close()
        {
            if (_handle != null && !_handle.IsInvalid)
            {
                _handle.Close();
            }
        }

        public byte[] ReadSector(ulong sector, int count, int sectorSize)
        {
            long offset = (long)(sector * (ulong)sectorSize);
            long newPtr;
            
            if (!SetFilePointerEx(_handle, offset, out newPtr, FILE_BEGIN))
            {
                return null;
            }

            uint bytesToRead = (uint)(count * sectorSize);
            byte[] buffer = new byte[bytesToRead];
            uint bytesRead;

            if (!ReadFile(_handle, buffer, bytesToRead, out bytesRead, IntPtr.Zero))
            {
                return null;
            }

            if (bytesRead < bytesToRead)
            {
                // Simple handle for short reads
                byte[] actualBuffer = new byte[bytesRead];
                Array.Copy(buffer, actualBuffer, bytesRead);
                return actualBuffer;
            }

            return buffer;
        }
        
         public byte[] ReadBytes(ulong offset, int count)
        {
            long newPtr;
            if (!SetFilePointerEx(_handle, (long)offset, out newPtr, FILE_BEGIN))
            {
                return null;
            }

            uint bytesToRead = (uint)count;
            byte[] buffer = new byte[bytesToRead];
            uint bytesRead;

            if (!ReadFile(_handle, buffer, bytesToRead, out bytesRead, IntPtr.Zero))
            {
                return null;
            }

            return buffer;
        }

        public void Dispose()
        {
            Close();
        }
    }
}
