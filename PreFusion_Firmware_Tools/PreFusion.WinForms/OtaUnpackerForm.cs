using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Ext2Read.Core;

namespace Ext2Read.WinForms
{
    public class OtaUnpackerForm : Form
    {
        private TextBox txtFolder;
        private Button btnBrowse;
        private CheckedListBox lstPartitions;
        private Button btnUnpack;
        private TextBox txtLog;
        private ProgressBar progressBar;
        private Label lblStatus;
        private Button btnClose;

        public OtaUnpackerForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Android OTA Unpacker";
            this.Size = new System.Drawing.Size(600, 500);
            this.StartPosition = FormStartPosition.CenterParent;

            var lblFolder = new Label { Text = "OTA Folder:", Location = new System.Drawing.Point(10, 15), AutoSize = true };
            this.Controls.Add(lblFolder);

            txtFolder = new TextBox { Location = new System.Drawing.Point(90, 12), Width = 400, ReadOnly = true };
            this.Controls.Add(txtFolder);

            btnBrowse = new Button { Text = "Browse...", Location = new System.Drawing.Point(500, 10) };
            btnBrowse.Click += BtnBrowse_Click;
            this.Controls.Add(btnBrowse);

            var lblParts = new Label { Text = "Detected Partitions:", Location = new System.Drawing.Point(10, 50), AutoSize = true };
            this.Controls.Add(lblParts);

            lstPartitions = new CheckedListBox { Location = new System.Drawing.Point(10, 70), Width = 560, Height = 150 };
            this.Controls.Add(lstPartitions);

            btnUnpack = new Button { Text = "Unpack Selected", Location = new System.Drawing.Point(10, 230), Width = 150, Height = 30 };
            btnUnpack.Click += BtnUnpack_Click;
            this.Controls.Add(btnUnpack);

            btnClose = new Button { Text = "Close", Location = new System.Drawing.Point(420, 230), Width = 150, Height = 30 };
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);

            progressBar = new ProgressBar { Location = new System.Drawing.Point(10, 270), Width = 560, Height = 20 };
            this.Controls.Add(progressBar);

            lblStatus = new Label { Text = "Ready", Location = new System.Drawing.Point(10, 295), AutoSize = true };
            this.Controls.Add(lblStatus);

            txtLog = new TextBox { Location = new System.Drawing.Point(10, 320), Width = 560, Height = 130, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
            this.Controls.Add(txtLog);
        }

        private void Log(string msg)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => Log(msg)));
                return;
            }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtFolder.Text = fbd.SelectedPath;
                    ScanFolder(fbd.SelectedPath);
                }
            }
        }

        private void ScanFolder(string path)
        {
            lstPartitions.Items.Clear();
            Log($"Scanning {path}...");

            try
            {
                var transfers = Directory.GetFiles(path, "*.transfer.list");
                foreach (var transferPath in transfers)
                {
                    string name = Path.GetFileName(transferPath).Replace(".transfer.list", "");
                    // Check for .new.dat or .new.dat.br
                    string datPath = Path.Combine(path, name + ".new.dat");
                    string brPath = Path.Combine(path, name + ".new.dat.br");

                    bool hasDat = File.Exists(datPath);
                    bool hasBr = File.Exists(brPath);

                    if (hasDat || hasBr)
                    {
                        var item = new PartitionItem { Name = name, TransferList = transferPath, DatPath = datPath, BrPath = brPath, HasBr = hasBr };
                        lstPartitions.Items.Add(item, true); // Checked by default
                        Log($"Found partition: {name} (Brotli: {hasBr})");
                    }
                }
                if (lstPartitions.Items.Count == 0) Log("No OTA partitions found.");
            }
            catch (Exception ex)
            {
                Log($"Error scanning: {ex.Message}");
            }
        }

        private async void BtnUnpack_Click(object sender, EventArgs e)
        {
            if (lstPartitions.CheckedItems.Count == 0) return;

            btnUnpack.Enabled = false;
            btnBrowse.Enabled = false;
            lstPartitions.Enabled = false;

            try
            {
                foreach (PartitionItem item in lstPartitions.CheckedItems)
                {
                    await ProcessPartition(item);
                }
                Log("All operations completed.");
                MessageBox.Show("Unpacking complete!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log($"Error: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnUnpack.Enabled = true;
                btnBrowse.Enabled = true;
                lstPartitions.Enabled = true;
                lblStatus.Text = "Ready";
                progressBar.Value = 0;
            }
        }

        private async System.Threading.Tasks.Task ProcessPartition(PartitionItem item)
        {
            string finalDatPath = item.DatPath;
            string outputImg = Path.ChangeExtension(item.TransferList, ".img");

            // Step 1: Decompress if necessary
            if (item.HasBr)
            {
                // Check if .dat already exists
                /* 
                 * Note: If .dat exists but .br also exists, user might want to force re-decompress?
                 * For now, if .dat missing, decompress. 
                 */
                if (!File.Exists(finalDatPath))
                {
                    Log($"Decompressing {Path.GetFileName(item.BrPath)}...");
                    lblStatus.Text = $"Decompressing {item.Name}...";
                    
                    var progress = new Progress<float>(p => {
                       // Brotli progress is fuzzy, assume 50% of total work for this item is brotli?
                       // Simple progress bar update
                       if (progressBar.InvokeRequired) progressBar.Invoke(new Action(() => progressBar.Value = (int)(p * 50)));
                       else progressBar.Value = (int)(p * 50);
                    });

                    await System.Threading.Tasks.Task.Run(() => OtaConverter.DecompressBrotliAsync(item.BrPath, finalDatPath, progress));
                    Log("Decompression done.");
                }
                else
                {
                    Log($"Skipping decompression for {item.Name} (.dat exists)");
                }
            }

            // Step 2: Convert to Img
            Log($"Converting {item.Name} to .img...");
            lblStatus.Text = $"Converting {item.Name}...";

            var convertProgress = new Progress<float>(p => {
                 int baseVal = item.HasBr ? 50 : 0;
                 int scale = item.HasBr ? 50 : 100;
                 int val = baseVal + (int)(p * scale);
                 if (progressBar.InvokeRequired) progressBar.Invoke(new Action(() => progressBar.Value = val));
                 else progressBar.Value = val;
            });

            await System.Threading.Tasks.Task.Run(() => OtaConverter.ConvertDatToImgAsync(item.TransferList, finalDatPath, outputImg, convertProgress));
            Log($"Created {Path.GetFileName(outputImg)}");
            progressBar.Value = 100;
        }

        private class PartitionItem
        {
            public string Name { get; set; }
            public string TransferList { get; set; }
            public string DatPath { get; set; }
            public string BrPath { get; set; }
            public bool HasBr { get; set; }

            public override string ToString()
            {
                return $"{Name} ({(HasBr ? "Brotli -> " : "")}.img)";
            }
        }
    }
}
