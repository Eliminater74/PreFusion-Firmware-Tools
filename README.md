# PreFusion Firmware Tools

**A comprehensive Windows toolkit for firmware analysis, reverse engineering, and extraction.**  
*Formerly known as Ext2Read .NET*

![PreFusion Banner](https://img.shields.io/badge/PreFusion-Firmware_Tools-blue?style=for-the-badge&logo=windows)
![License](https://img.shields.io/badge/License-GPLv2-green?style=for-the-badge)
![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey?style=for-the-badge)

## ⚠️ DISCLAIMER & LEGAL WARNING

**USAGE AT YOUR OWN RISK.**

PreFusion Firmware Tools is developed strictly for **educational and research purposes**. It is designed to assist security researchers, developers, and hobbyists in analyzing firmware packages to understand their structure and content.

1.  **No Liability**: The authors and contributors of this software are **not responsible** for any damage, data loss, or system instability caused by the use of this tool. Reverse engineering firmware can be risky and may void warranties.
2.  **Legal Compliance**: It is the user's sole responsibility to ensure that their use of this tool complies with all applicable logic, regulations, and Terms of Service agreements.
3.  **No Illegal Use**: This tool **must not** be used for any illegal activities, including but not limited to software piracy, copyright infringement, or malicious tampering.

**By downloading or using this software, you agree to these terms.**

---

## 📖 About the Project

**PreFusion Firmware Tools** started as a simple utility ("Ext2Read .NET") to help Windows users read Linux `Ext2/3/4` partitions. Over time, it grew into a powerful suite of tools designed to bridge the gap between Linux-centric firmware analysis and the Windows ecosystem.

Our goal is to provide a unified, native Windows GUI for tasks that typically require a Linux VM or complex command-line chains.

---

## 🚀 Key Features

### 1. 📂 Ext2/3/4 Filesystem Browser
-   **Read Linux Partitions**: Open and browse `ext2`, `ext3`, and `ext4` images or physical drives directly on Windows.
-   **LVM2 Support**: Automatically detects and mounts Logical Volume Manager (LVM2) volumes.
-   **File Extraction**: Drag-and-drop or batch extract files and folders from Linux images to your Windows desktop.

### 2. 🔍 Native Firmware Scanner (BinWalk Integration)
A C# native implementation of the popular `binwalk` analysis tool.
-   **Signature Scan**: Detects compression (GZIP, LZMA, XZ), filesystems (SquashFS, UBIFS, JFFS2), and bootloaders (U-Boot).
-   **Matryoshka Scan (-M)**: Recursively unpacks nested firmware layers (e.g., extracting a Zip inside a Gzip inside a firmware image) to find hidden data.
-   **Opcode Scan (-A)**: Scans for common ARM/MIPS instruction sequences to identify code segments.
-   **Entropy Analysis**: Visualizes file entropy (0.0 - 8.0) to identify encrypted or compressed regions.
-   **Raw Search (-R)**: Hex and Text pattern search across the entire binary.
-   **Carving**: Extract identified artifacts automatically.

### 3. 📱 Android OTA Utilities
-   **Payload.bin Unpacker**: Converts Android OTA `payload.bin` files into flashable images.
-   **Sparse Image Converter**: Converts Android sparse images (`.img`) to raw images readable by other tools.
-   **Brotli Decompression**: Native support for decompressing `.new.dat.br` files found in modern OTAs.

---

## 🛠️ How to Build

### Prerequisites
-   **Visual Studio 2022** (Community or Pro)
-   **.NET 8.0 SDK** (or later)

### Build Steps
1.  Clone the repository:
    ```powershell
    git clone https://github.com/Eliminater74/PreFusion-Firmware-Tools.git
    ```
2.  Open `PreFusionFirmwareTools.sln` in Visual Studio.
3.  Right-click the Solution and select **Restore NuGet Packages**.
4.  Set the build target to **Release** / **Any CPU**.
5.  Click **Build Solution** (Ctrl+Shift+B).
6.  The executable will be in `PreFusion.WinForms\bin\Release\net8.0-windows\PreFusion.WinForms.exe`.

---

## 🔮 Future Roadmap

We are constantly working to expand the toolkit's capabilities. Planned features include:

-   [ ] **JFFS2 Content Extraction**: Porting [jefferson](https://github.com/onekey-sec/jefferson) logic to allow browsing and extracting files from JFFS2 partitions.
-   [ ] **UBI/UBIFS Support**: Reader support for raw UBI NAND images.
-   [ ] **Hex Editor**: A built-in lightweight hex viewer/editor for quick patches.
-   [ ] **Plugin System**: Allow community written scripts for custom unpacking.

---

## 🤝 Contributing

Contributions are welcome! If you find a bug or want to suggest a feature, please open an Issue. Pull Requests are appreciated—please ensure your code follows the existing style and includes comments.

**Original Credits**:
-   Based on the original *Ext2Read* (GPLv2).
-   *BinWalk* concepts inspired by ReFirmLabs.
-   *sdat2img* logic ported from xpirt.
