# Ext2Read .NET

A C# Windows Forms port of the classic **Ext2Read** tool. This utility allows you to explore Linux Ext2, Ext3, and Ext4 partitions directly from Windows.

## Features

-   **View Linux Partitions**: Automatically detects Ext2/3/4 partitions on attached physical drives.
-   **Explore Files**: Browse standard directory structures.
-   **Read Support**: (In Progress) View and copy files from Linux partitions to Windows.
-   **LVM2 Support**: (Planned) Support for Logical Volume Manager partitions.

## Requirements

-   Windows 10/11
-   .NET 8.0 Runtime (or SDK to build)
-   **Administrator Privileges** (Required for raw disk access)

## Usage

1.  Run `Ext2Read.WinForms.exe` as Administrator.
2.  The application will automatically scan your drives.
3.  Expand the nodes in the left tree view to navigate folders.
4.  Select a folder to view its contents in the right pane.

## Development

This project was ported from the original C++/Qt source to C# .NET 8.0.

### Project Structure
-   **Ext2Read.Core**: Library containing raw disk I/O (P/Invoke), Ext2 struct definitions, and partition parsing logic.
-   **Ext2Read.WinForms**: Windows Forms UI application.

### Building
Open `Ext2ReadNet.sln` in Visual Studio 2022 and build the solution. Ensure you run Visual Studio as Administrator for debugging.
