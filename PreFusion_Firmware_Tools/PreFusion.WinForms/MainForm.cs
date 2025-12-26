using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Ext2Read.Core;

namespace Ext2Read.WinForms
{
    public partial class MainForm : Form
    {
        private MenuStrip menuStrip1;
        private ToolStripMenuItem fileToolStripMenuItem;
        private ToolStripMenuItem openImageToolStripMenuItem;
        private ToolStripMenuItem closeImageToolStripMenuItem;
        private ToolStripMenuItem rescanToolStripMenuItem;
        private ToolStripMenuItem toolsToolStripMenuItem;
        private ToolStripMenuItem convertSparseToolStripMenuItem;
        private ToolStripMenuItem otaUnpackerToolStripMenuItem;
        private ToolStripMenuItem analyzeFirmwareToolStripMenuItem;
        private ToolStripMenuItem autoScanToolStripMenuItem;
        private ToolStripMenuItem helpToolStripMenuItem;
        private ToolStripMenuItem aboutToolStripMenuItem;
        private ToolStripMenuItem exitToolStripMenuItem;
        private SplitContainer splitContainer1;
        private TreeView treeView1;
        private ListView listView1;
        private ImageList imageList1;
        private ContextMenuStrip contextMenuStrip1;
        private ToolStripMenuItem saveAsToolStripMenuItem;
        private ToolStripMenuItem copyNameToolStripMenuItem;
        private ToolStrip toolStrip1;
        private ToolStripLabel searchLabel;
        private ToolStripTextBox searchTextBox;
        private ToolStripButton searchButton;
        private DiskManager _diskManager;
        private List<Ext2FileSystem> _fileSystems = new List<Ext2FileSystem>();

        public MainForm()
        {
            InitializeComponent();
            InitializeContextMenu();
            InitializeSearchStrip();
            InitializeCloseImageMenu();
            _diskManager = new DiskManager();
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            // Sync UI with Settings
            autoScanToolStripMenuItem.Checked = AppSettings.Instance.AutoScanOnStartup;

            if (AppSettings.Instance.AutoScanOnStartup)
            {
                await ScanDisksAsync();
            }
        }

        private async System.Threading.Tasks.Task ScanDisksAsync()
        {
            treeView1.Nodes.Clear();
            _fileSystems.Clear();

            // Show loading state
            var loadingNode = new TreeNode("Scanning for Linux partitions...");
            treeView1.Nodes.Add(loadingNode);
            treeView1.Enabled = false;

            try
            {
                // Run heavy scan on background thread
                var partitions = await System.Threading.Tasks.Task.Run(() => _diskManager.ScanSystem());

                treeView1.Nodes.Remove(loadingNode);
                treeView1.Enabled = true;

                if (partitions.Count == 0)
                {
                    MessageBox.Show("No Linux Ext2/3/4 partitions found.", "Ext2Read", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                foreach (var part in partitions)
                {
                    // Mount on background too if it takes time, but usually fast. 
                    // Let's keep mount explicit to ensure thread safety if FileSystem object is not thread safe yet.
                    // Doing Mount here on UI thread is fine as it's just reading Superblock (1 seek/read).
                    var fs = new Ext2FileSystem(part);
                    if (fs.Mount())
                    {
                        _fileSystems.Add(fs);
                        TreeNode node = new TreeNode($"{part.Name} ({fs.VolumeName})");
                        node.Tag = new NodeData { FileSystem = fs, Inode = 2 }; // Root inode is 2
                        node.ImageIndex = 0; // Drive icon
                        node.SelectedImageIndex = 0;
                        treeView1.Nodes.Add(node);

                        // Add dummy node to allow expansion
                        node.Nodes.Add("Loading...");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scanning disks: {ex.Message}\nMake sure you are running as Administrator.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                treeView1.Enabled = true;
            }
        }

        private async void treeView1_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Text == "Loading...")
            {
                // Don't clear immediately, let "Loading..." stay while we fetch
                // e.Node.Nodes.Clear(); 
                // Creating a separate method to avoid async void issues if possible, but for event handlers async void is standard.
                e.Cancel = true; // Cancel the expand so we can handle it manually after load? 
                                 // No, standard pattern: let it expand showing "Loading...", then populate.
                e.Cancel = false;

                await LoadDirectoryAsync(e.Node);
            }
        }

        private async System.Threading.Tasks.Task LoadDirectoryAsync(TreeNode parentNode)
        {
            var data = parentNode.Tag as NodeData;
            if (data == null) return;

            try
            {
                var files = await System.Threading.Tasks.Task.Run(() => data.FileSystem.ListDirectory(data.Inode));

                // Back on UI thread
                parentNode.Nodes.Clear(); // Remove "Loading..."


                foreach (var file in files)
                {
                    if (file.IsDirectory)
                    {
                        TreeNode node = new TreeNode(file.Name);
                        node.Tag = new NodeData { FileSystem = data.FileSystem, Inode = file.InodeNum };
                        node.ImageIndex = 1; // Folder icon
                        node.SelectedImageIndex = 1;
                        parentNode.Nodes.Add(node);

                        // Add dummy for expansion
                        node.Nodes.Add("Loading...");
                    }
                }
            }
            catch (Exception ex)
            {
                parentNode.Nodes.Clear();
                parentNode.Nodes.Add(new TreeNode("Error: " + ex.Message));
            }
        }

        private async void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            listView1.Items.Clear();
            var data = e.Node.Tag as NodeData;
            if (data == null) return;

            // Show loading in ListView
            listView1.Items.Add(new ListViewItem("Loading items..."));

            try
            {
                var files = await System.Threading.Tasks.Task.Run(() => data.FileSystem.ListDirectory(data.Inode));

                listView1.Items.Clear(); // Clear "Loading..."

                // Optimizing ListView population with BeginUpdate/EndUpdate for massive folders
                listView1.BeginUpdate();
                foreach (var file in files)
                {
                    ListViewItem item = new ListViewItem(file.Name);
                    item.Tag = new NodeData { FileSystem = data.FileSystem, Inode = file.InodeNum, Name = file.Name };
                    item.ImageIndex = file.IsDirectory ? 1 : 2; // Folder or File

                    // Size
                    item.SubItems.Add(file.IsDirectory ? "" : FormatBytes(file.Size));

                    // Type
                    if (file.IsDirectory) item.SubItems.Add("Directory");
                    else item.SubItems.Add("File");

                    // Date
                    item.SubItems.Add(file.ModifiedTime.ToString("g"));

                    // Permissions
                    item.SubItems.Add(FormatPermissions(file.Mode));

                    // Owner
                    item.SubItems.Add($"{file.Uid}/{file.Gid}");

                    listView1.Items.Add(item);
                }
                listView1.EndUpdate();
            }
            catch (Exception ex)
            {
                listView1.Items.Clear();
                MessageBox.Show($"Error reading directory: {ex.Message}");
            }
        }

        private async void openImageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Disk Images (*.img;*.iso;*.bin)|*.img;*.iso;*.bin|All files (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    string fileToOpen = ofd.FileName;

                    // Check for Android Sparse Image
                    if (SparseConverter.IsSparseImage(fileToOpen))
                    {
                        var result = MessageBox.Show(
                            "This appears to be an Android Sparse Image. It must be converted to a raw image to be read.\n\nDo you want to convert it now?",
                            "Sparse Image Detected",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);

                        if (result == DialogResult.Yes)
                        {
                            using (SaveFileDialog sfd = new SaveFileDialog())
                            {
                                sfd.Filter = "Raw Disk Image (*.img)|*.img";
                                sfd.FileName = Path.GetFileNameWithoutExtension(fileToOpen) + "_raw.img";
                                if (sfd.ShowDialog() == DialogResult.OK)
                                {
                                    // Run conversion
                                    // TODO: Show proper progress dialog. For now, using title bar or specialized node
                                    var loadingNode = new TreeNode("Converting sparse image... Please wait.");
                                    treeView1.Nodes.Add(loadingNode);
                                    treeView1.Enabled = false;

                                    try
                                    {
                                        await System.Threading.Tasks.Task.Run(() =>
                                            SparseConverter.Convert(fileToOpen, sfd.FileName, null));

                                        MessageBox.Show("Conversion complete!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                        fileToOpen = sfd.FileName; // Switch to opening the new file
                                    }
                                    catch (Exception ex)
                                    {
                                        MessageBox.Show($"Conversion failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                        treeView1.Nodes.Remove(loadingNode);
                                        treeView1.Enabled = true;
                                        return;
                                    }
                                    finally
                                    {
                                        treeView1.Nodes.Remove(loadingNode);
                                        treeView1.Enabled = true;
                                    }
                                }
                                else
                                {
                                    return; // User cancelled save
                                }
                            }
                        }
                        else
                        {
                            return; // User cancelled conversion
                        }
                    }

                    try
                    {
                        var partitions = await System.Threading.Tasks.Task.Run(() => _diskManager.ScanImage(fileToOpen));

                        if (partitions.Count == 0)
                        {
                            MessageBox.Show("No Linux Ext2/3/4 partitions or filesystems found in this image.\n\nThe image might be:\n- Encrypted\n- Using a feature not supported by Ext2Read\n- Not a valid disk image", "No Partitions Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        foreach (var part in partitions)
                        {
                            var fs = new Ext2FileSystem(part);
                            if (fs.Mount())
                            {
                                _fileSystems.Add(fs);
                                TreeNode node = new TreeNode($"{part.Name} ({fs.VolumeName})");
                                node.Tag = new NodeData { FileSystem = fs, Inode = 2 };
                                node.ImageIndex = 0;
                                node.SelectedImageIndex = 0;
                                treeView1.Nodes.Add(node);
                                node.Nodes.Add("Loading...");
                                node.Expand(); // Auto expand images
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error opening image: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void autoScanToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AppSettings.Instance.AutoScanOnStartup = !AppSettings.Instance.AutoScanOnStartup;
            autoScanToolStripMenuItem.Checked = AppSettings.Instance.AutoScanOnStartup;
            AppSettings.Instance.Save();
        }

        private async void rescanToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await ScanDisksAsync();
        }

        private async void convertSparseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Android Sparse Img to Convert";
                ofd.Filter = "Sparse Images (*.img;*.simg)|*.img;*.simg|All files (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    if (!SparseConverter.IsSparseImage(ofd.FileName))
                    {
                        MessageBox.Show("The selected file does not appear to be a valid Android Sparse Image.", "Invalid Image", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    using (SaveFileDialog sfd = new SaveFileDialog())
                    {
                        sfd.Filter = "Raw Disk Image (*.img)|*.img";
                        sfd.FileName = Path.GetFileNameWithoutExtension(ofd.FileName) + "_raw.img";
                        if (sfd.ShowDialog() == DialogResult.OK)
                        {
                            var loadingNode = new TreeNode("Converting sparse image...");
                            treeView1.Nodes.Add(loadingNode);
                            treeView1.Enabled = false;

                            try
                            {
                                await System.Threading.Tasks.Task.Run(() =>
                                    SparseConverter.Convert(ofd.FileName, sfd.FileName, null));

                                MessageBox.Show("Conversion complete! You can now open the raw image.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Conversion failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                            finally
                            {
                                treeView1.Nodes.Remove(loadingNode);
                                treeView1.Enabled = true;
                            }
                        }
                    }
                }
            }
        }

        private void InitializeComponent()
        {
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openImageToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.rescanToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.convertSparseToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.otaUnpackerToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.analyzeFirmwareToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.autoScanToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.helpToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.aboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.treeView1 = new System.Windows.Forms.TreeView();
            this.listView1 = new System.Windows.Forms.ListView();
            this.imageList1 = new System.Windows.Forms.ImageList();

            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.menuStrip1.SuspendLayout();
            this.SuspendLayout();

            // 
            // menuStrip1
            // 
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem,
            this.toolsToolStripMenuItem,
            this.helpToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(800, 24);
            this.menuStrip1.TabIndex = 1;
            this.menuStrip1.Text = "menuStrip1";

            // 
            // fileToolStripMenuItem
            // 
            this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.openImageToolStripMenuItem,
            this.rescanToolStripMenuItem,
            this.exitToolStripMenuItem});
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new System.Drawing.Size(37, 20);
            this.fileToolStripMenuItem.Text = "&File";

            // 
            // openImageToolStripMenuItem
            // 
            this.openImageToolStripMenuItem.Name = "openImageToolStripMenuItem";
            this.openImageToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.openImageToolStripMenuItem.Text = "&Open Image...";
            this.openImageToolStripMenuItem.Click += new System.EventHandler(this.openImageToolStripMenuItem_Click);

            // 
            // rescanToolStripMenuItem
            // 
            this.rescanToolStripMenuItem.Name = "rescanToolStripMenuItem";
            this.rescanToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.rescanToolStripMenuItem.Text = "&Rescan Drives";
            this.rescanToolStripMenuItem.Click += new System.EventHandler(this.rescanToolStripMenuItem_Click);

            // 
            // exitToolStripMenuItem
            // 
            this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            this.exitToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.exitToolStripMenuItem.Text = "E&xit";
            this.exitToolStripMenuItem.Click += (s, e) => Close();

            // 
            // toolsToolStripMenuItem
            // 
            this.toolsToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.convertSparseToolStripMenuItem,
            this.otaUnpackerToolStripMenuItem,
            this.analyzeFirmwareToolStripMenuItem,
            this.autoScanToolStripMenuItem});
            this.toolsToolStripMenuItem.Name = "toolsToolStripMenuItem";
            this.toolsToolStripMenuItem.Size = new System.Drawing.Size(46, 20);
            this.toolsToolStripMenuItem.Text = "&Tools";

            // 
            // convertSparseToolStripMenuItem
            // 
            this.convertSparseToolStripMenuItem.Name = "convertSparseToolStripMenuItem";
            this.convertSparseToolStripMenuItem.Size = new System.Drawing.Size(235, 22);
            this.convertSparseToolStripMenuItem.Text = "&Convert Android Sparse Image";
            this.convertSparseToolStripMenuItem.Click += new System.EventHandler(this.convertSparseToolStripMenuItem_Click);

            // 
            // otaUnpackerToolStripMenuItem
            // 
            this.otaUnpackerToolStripMenuItem.Name = "otaUnpackerToolStripMenuItem";
            this.otaUnpackerToolStripMenuItem.Size = new System.Drawing.Size(262, 22);
            this.otaUnpackerToolStripMenuItem.Text = "Unpack Android OTA...";
            this.otaUnpackerToolStripMenuItem.Click += new System.EventHandler(this.otaUnpackerToolStripMenuItem_Click);

            // 
            // otaUnpackerToolStripMenuItem
            // 
            this.otaUnpackerToolStripMenuItem.Name = "otaUnpackerToolStripMenuItem";
            this.otaUnpackerToolStripMenuItem.Size = new System.Drawing.Size(262, 22);
            this.otaUnpackerToolStripMenuItem.Text = "Unpack Android OTA...";
            this.otaUnpackerToolStripMenuItem.Click += new System.EventHandler(this.otaUnpackerToolStripMenuItem_Click);

            // 
            // analyzeFirmwareToolStripMenuItem
            // 
            this.analyzeFirmwareToolStripMenuItem.Name = "analyzeFirmwareToolStripMenuItem";
            this.analyzeFirmwareToolStripMenuItem.Size = new System.Drawing.Size(262, 22);
            this.analyzeFirmwareToolStripMenuItem.Text = "Analyze Firmware (BinWalk)";
            this.analyzeFirmwareToolStripMenuItem.Click += new System.EventHandler(this.analyzeFirmwareToolStripMenuItem_Click);

            // 
            // autoScanToolStripMenuItem
            // 
            this.autoScanToolStripMenuItem.Name = "autoScanToolStripMenuItem";
            this.autoScanToolStripMenuItem.Size = new System.Drawing.Size(235, 22);
            this.autoScanToolStripMenuItem.Text = "&Auto Scan Physical Drives";
            this.autoScanToolStripMenuItem.CheckOnClick = true;
            this.autoScanToolStripMenuItem.Click += new System.EventHandler(this.autoScanToolStripMenuItem_Click);

            // 
            // helpToolStripMenuItem
            // 
            this.helpToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.aboutToolStripMenuItem});
            this.helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            this.helpToolStripMenuItem.Size = new System.Drawing.Size(44, 20);
            this.helpToolStripMenuItem.Text = "&Help";

            // 
            // aboutToolStripMenuItem
            // 
            this.aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            this.aboutToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.aboutToolStripMenuItem.Text = "&About PreFusion Firmware Tools";
            this.aboutToolStripMenuItem.Click += new System.EventHandler(this.aboutToolStripMenuItem_Click);

            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 24); // Adjust for menu
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.treeView1);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.listView1);
            this.splitContainer1.Size = new System.Drawing.Size(800, 450);
            this.splitContainer1.SplitterDistance = 266;
            this.splitContainer1.TabIndex = 0;

            // 
            // treeView1
            // 
            this.treeView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeView1.Location = new System.Drawing.Point(0, 0);
            this.treeView1.Name = "treeView1";
            this.treeView1.Size = new System.Drawing.Size(266, 450);
            this.treeView1.TabIndex = 0;
            this.treeView1.ImageList = this.imageList1;
            this.treeView1.BeforeExpand += new System.Windows.Forms.TreeViewCancelEventHandler(this.treeView1_BeforeExpand);
            this.treeView1.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeView1_AfterSelect);

            // 
            // listView1
            // 
            this.listView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listView1.Location = new System.Drawing.Point(0, 0);
            this.listView1.Name = "listView1";
            this.listView1.Size = new System.Drawing.Size(530, 450);
            this.listView1.TabIndex = 0;
            this.listView1.View = View.Details;
            this.listView1.Columns.Add("Name", 200);
            this.listView1.Columns.Add("Size", 100, HorizontalAlignment.Right);
            this.listView1.Columns.Add("Type", 80);
            this.listView1.Columns.Add("Date Modified", 140);
            this.listView1.Columns.Add("Permissions", 100);
            this.listView1.Columns.Add("Owner", 80);
            this.listView1.Columns.Add("Path", 300);
            this.listView1.SmallImageList = this.imageList1;

            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.menuStrip1);
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "MainForm";
            this.Text = "PreFusion Firmware Tools v1.0.0";
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            this.splitContainer1.ResumeLayout(false);
            this.ResumeLayout(false);

            // ImageList setup could be done here with resources, skipping for simple text MVP or using system icons later
            // Adding placeholder images
            // this.imageList1.Images.Add(...) 
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_diskManager != null) _diskManager.Dispose();
            }
            base.Dispose(disposing);
        }
        private void InitializeContextMenu()
        {
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip();
            this.saveAsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.copyNameToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();

            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.saveAsToolStripMenuItem,
            this.copyNameToolStripMenuItem});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(180, 48);

            // saveAsToolStripMenuItem
            this.saveAsToolStripMenuItem.Name = "saveAsToolStripMenuItem";
            this.saveAsToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.saveAsToolStripMenuItem.Text = "Save As...";
            this.saveAsToolStripMenuItem.Click += new System.EventHandler(this.saveAsToolStripMenuItem_Click);

            // copyNameToolStripMenuItem
            this.copyNameToolStripMenuItem.Name = "copyNameToolStripMenuItem";
            this.copyNameToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.copyNameToolStripMenuItem.Text = "Copy Filename";
            this.copyNameToolStripMenuItem.Click += new System.EventHandler(this.copyNameToolStripMenuItem_Click);

            // Bind to ListView
            this.listView1.ContextMenuStrip = this.contextMenuStrip1;
        }

        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.listView1.SelectedItems.Count == 0) return;

            var item = this.listView1.SelectedItems[0];
            var data = item.Tag as NodeData;
            if (data == null) return;

            // TODO: Check if directory? For now only files.
            // NodeData doesn't explicitly store isDirectory but we can check Icon or similar.
            // Or assume user knows what they are doing.
            // But reading a directory as file gives garbage.
            // We can check item.ImageIndex == 2 (File).

            if (item.ImageIndex == 1) // Dictionary
            {
                MessageBox.Show("Folder copying is not yet supported.", "Info");
                return;
            }

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.FileName = data.Name ?? item.Text;
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        using (var fs = new System.IO.FileStream(sfd.FileName, System.IO.FileMode.Create, System.IO.FileAccess.Write))
                        {
                            // Call FileSystem ReadFile
                            data.FileSystem.ReadFile(data.Inode, fs);
                        }
                        MessageBox.Show("File saved successfully!", "Success");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error saving file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void copyNameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.listView1.SelectedItems.Count == 0) return;
            var item = this.listView1.SelectedItems[0];
            Clipboard.SetText(item.Text);
        }

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }

        private string FormatPermissions(uint mode)
        {
            // Simple rwxr-xr-x format
            // mode is 16 bits.
            // 0x4000 = DIR, 0x8000 = REG.
            // Permission bits are lower 9 bits.

            char[] perms = new char[] { '-', '-', '-', '-', '-', '-', '-', '-', '-', '-' };

            if ((mode & Ext2Constants.S_IFDIR) == Ext2Constants.S_IFDIR) perms[0] = 'd';
            else if ((mode & Ext2Constants.S_IFREG) == Ext2Constants.S_IFREG) perms[0] = '-';
            else perms[0] = '?'; // Link, etc.

            // User
            if ((mode & 0x0100) != 0) perms[1] = 'r';
            if ((mode & 0x0080) != 0) perms[2] = 'w';
            if ((mode & 0x0040) != 0) perms[3] = 'x';

            // Group
            if ((mode & 0x0020) != 0) perms[4] = 'r';
            if ((mode & 0x0010) != 0) perms[5] = 'w';
            if ((mode & 0x0008) != 0) perms[6] = 'x';

            // Other
            if ((mode & 0x0004) != 0) perms[7] = 'r';
            if ((mode & 0x0002) != 0) perms[8] = 'w';
            if ((mode & 0x0001) != 0) perms[9] = 'x';

            return new string(perms);
        }

        private void InitializeSearchStrip()
        {
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.searchLabel = new System.Windows.Forms.ToolStripLabel();
            this.searchTextBox = new System.Windows.Forms.ToolStripTextBox();
            this.searchButton = new System.Windows.Forms.ToolStripButton();

            // toolStrip1
            this.toolStrip1.Dock = DockStyle.Top;
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.searchLabel,
            this.searchTextBox,
            this.searchButton});
            // Make sure it's below MenuStrip (MenuStrip is typically Top too, order matters in Controls.Add)
            // But we can just Add it.

            this.searchLabel.Text = "Search:";
            this.searchTextBox.Size = new System.Drawing.Size(200, 25);
            this.searchButton.Text = "Go";
            this.searchButton.Click += new System.EventHandler(this.searchButton_Click);

            this.Controls.Add(this.toolStrip1);
            // Ensure correct Z-order (ToolStrip below MenuStrip)
            this.toolStrip1.BringToFront(); // Actually we want it below Menu.
            // MenuStrip (if Dock=Top) should stay at top. 
            // In Windows Forms, last added Control with Dock=Top is at bottom of Top stack?
            // Actually, usually reverse order of addition.
            // MenuStrip was added early.
            // I'll assume Controls.Add works fine or I'll check.
            // Controls.setChildIndex can fix it.
            this.Controls.SetChildIndex(this.toolStrip1, 1);
        }

        private async void searchButton_Click(object sender, EventArgs e)
        {
            string query = searchTextBox.Text;
            if (string.IsNullOrWhiteSpace(query))
            {
                MessageBox.Show("Please enter a search term.");
                return;
            }

            if (treeView1.SelectedNode == null || treeView1.SelectedNode.Tag == null)
            {
                // Fallback to first Node if nothing selected (Root of first FS)
                if (treeView1.Nodes.Count > 0 && treeView1.Nodes[0].Tag != null)
                {
                    treeView1.SelectedNode = treeView1.Nodes[0];
                }
                else
                {
                    MessageBox.Show("Please select a partition or folder to search in.");
                    return;
                }
            }

            var data = treeView1.SelectedNode.Tag as NodeData;
            uint startInode = data.Inode;

            listView1.Items.Clear();
            listView1.Items.Add(new ListViewItem("Searching..."));
            searchButton.Enabled = false;

            try
            {
                var results = await System.Threading.Tasks.Task.Run(() => data.FileSystem.SearchFiles(startInode, query, ""));

                listView1.Items.Clear();
                listView1.BeginUpdate();
                foreach (var file in results)
                {
                    ListViewItem item = new ListViewItem(file.Name);
                    // Update Tag
                    item.Tag = new NodeData { FileSystem = data.FileSystem, Inode = file.InodeNum, Name = file.Name };
                    item.ImageIndex = file.IsDirectory ? 1 : 2;

                    item.SubItems.Add(file.IsDirectory ? "" : FormatBytes(file.Size));
                    item.SubItems.Add(file.IsDirectory ? "Directory" : "File");
                    item.SubItems.Add(file.ModifiedTime.ToString("g"));
                    item.SubItems.Add(FormatPermissions(file.Mode));
                    item.SubItems.Add($"{file.Uid}/{file.Gid}");
                    item.SubItems.Add(file.FullPath); // Added Path

                    listView1.Items.Add(item);
                }
                listView1.EndUpdate();
                MessageBox.Show($"Found {results.Count} items.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error searching: " + ex.Message);
            }
            finally
            {
                searchButton.Enabled = true;
            }
        }

        private void InitializeCloseImageMenu()
        {
            this.closeImageToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.closeImageToolStripMenuItem.Name = "closeImageToolStripMenuItem";
            this.closeImageToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.closeImageToolStripMenuItem.Text = "Close Image";
            this.closeImageToolStripMenuItem.Click += new System.EventHandler(this.closeImageToolStripMenuItem_Click);

            // Insert after Open Image (Index 0 is Open, Index 1 will be Close)
            // Need to verify index. usually:
            // 0: Open
            // 1: Rescan
            // 2: Exit
            // So Insert at 1.
            if (this.fileToolStripMenuItem.DropDownItems.Count > 0)
            {
                this.fileToolStripMenuItem.DropDownItems.Insert(1, this.closeImageToolStripMenuItem);
            }
            else
            {
                this.fileToolStripMenuItem.DropDownItems.Add(this.closeImageToolStripMenuItem);
            }
        }

        private async void closeImageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Clear UI
            treeView1.Nodes.Clear();
            listView1.Items.Clear();
            _fileSystems.Clear();

            // Release handles
            if (_diskManager != null)
            {
                _diskManager.Dispose();
            }
            _diskManager = new DiskManager();

            // Rescan physical drives if option enabled
            // Or just leave empty to let user decide?
            // "Close Image" implies returning to base state.
            // If AutoScan is on, base state includes physical drives.
            if (AppSettings.Instance.AutoScanOnStartup)
            {
                await ScanDisksAsync();
            }
        }

        private void otaUnpackerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var form = new OtaUnpackerForm())
            {
                form.ShowDialog(this);
            }
        }

        private void analyzeFirmwareToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var form = new BinwalkForm())
            {
                form.ShowDialog(this);
            }
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var about = new AboutBox())
            {
                about.ShowDialog(this);
            }
        }
    }

    class NodeData
    {
        public Ext2FileSystem FileSystem { get; set; }
        public uint Inode { get; set; }
        public string Name { get; set; }
    }
}
