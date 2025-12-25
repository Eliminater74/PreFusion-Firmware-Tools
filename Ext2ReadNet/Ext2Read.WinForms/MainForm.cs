using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Ext2Read.Core;

namespace Ext2Read.WinForms
{
    public partial class MainForm : Form
    {
        private SplitContainer splitContainer1;
        private TreeView treeView1;
        private ListView listView1;
        private ImageList imageList1;
        private DiskManager _diskManager;
        private List<Ext2FileSystem> _fileSystems = new List<Ext2FileSystem>();

        public MainForm()
        {
            InitializeComponent();
            _diskManager = new DiskManager();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            ScanDisks();
        }

        private void ScanDisks()
        {
            treeView1.Nodes.Clear();
            _fileSystems.Clear();

            try
            {
                var partitions = _diskManager.ScanSystem();
                if (partitions.Count == 0)
                {
                    MessageBox.Show("No Linux Ext2/3/4 partitions found.", "Ext2Read", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                foreach (var part in partitions)
                {
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
        }

        private void treeView1_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Text == "Loading...")
            {
                e.Node.Nodes.Clear();
                LoadDirectory(e.Node);
            }
        }

        private void LoadDirectory(TreeNode parentNode)
        {
            var data = parentNode.Tag as NodeData;
            if (data == null) return;

            try
            {
                var files = data.FileSystem.ListDirectory(data.Inode);
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
                MessageBox.Show($"Error reading directory: {ex.Message}");
            }
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            listView1.Items.Clear();
            var data = e.Node.Tag as NodeData;
            if (data == null) return;

            try
            {
                var files = data.FileSystem.ListDirectory(data.Inode);
                foreach (var file in files)
                {
                    ListViewItem item = new ListViewItem(file.Name);
                    item.ImageIndex = file.IsDirectory ? 1 : 2; // Folder or File
                    if (file.IsDirectory) item.SubItems.Add("Directory");
                    else item.SubItems.Add("File"); // Could get size from Inode if ListDirectory returned it
                    
                    listView1.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                 MessageBox.Show($"Error reading directory: {ex.Message}");
            }
        }

        private void InitializeComponent()
        {
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.treeView1 = new System.Windows.Forms.TreeView();
            this.listView1 = new System.Windows.Forms.ListView();
            this.imageList1 = new System.Windows.Forms.ImageList();

            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.SuspendLayout();

            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 0);
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
            this.listView1.Columns.Add("Type", 100);
            this.listView1.SmallImageList = this.imageList1;

            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.splitContainer1);
            this.Name = "MainForm";
            this.Text = "Ext2Read .NET";
            this.Load += new System.EventHandler(this.MainForm_Load);
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
    }

    class NodeData
    {
        public Ext2FileSystem FileSystem { get; set; }
        public uint Inode { get; set; }
    }
}
