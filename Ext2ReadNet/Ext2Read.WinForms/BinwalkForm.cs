using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Ext2Read.Core.Binwalk;

namespace Ext2Read.WinForms
{
    public class BinwalkForm : Form
    {
        private TextBox txtInputFile;
        private Button btnBrowse;
        private Button btnScan;
        private ListView lstResults;
        private ProgressBar progressBar;
        private Label lblStatus;
        private ContextMenuStrip contextMenu;

        public BinwalkForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Native Firmware Scanner (BinWalk)";
            this.Size = new System.Drawing.Size(700, 500);
            this.StartPosition = FormStartPosition.CenterParent;

            // Input
            var lblInput = new Label { Text = "Firmware File:", Location = new Point(10, 15), AutoSize = true };
            this.Controls.Add(lblInput);

            txtInputFile = new TextBox { Location = new Point(100, 12), Width = 460 };
            this.Controls.Add(txtInputFile);

            btnBrowse = new Button { Text = "Browse...", Location = new Point(570, 10) };
            btnBrowse.Click += BtnBrowse_Click;
            this.Controls.Add(btnBrowse);

            // Scan Button
            btnScan = new Button { Text = "Scan Firmware", Location = new Point(10, 45), Width = 150, Height = 30 };
            btnScan.Click += BtnScan_Click;
            this.Controls.Add(btnScan);

            // Progress
            progressBar = new ProgressBar { Location = new Point(170, 50), Width = 500, Height = 20 };
            this.Controls.Add(progressBar);

            // List View
            lstResults = new ListView
            {
                Location = new Point(10, 90),
                Width = 660,
                Height = 360,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            lstResults.Columns.Add("Decimal", 100);
            lstResults.Columns.Add("Hexadecimal", 100);
            lstResults.Columns.Add("Description", 400);
            this.Controls.Add(lstResults);

            // Context Menu
            contextMenu = new ContextMenuStrip();
            var extractItem = new ToolStripMenuItem("Extract from here...");
            extractItem.Click += ExtractItem_Click;
            contextMenu.Items.Add(extractItem);
            lstResults.ContextMenuStrip = contextMenu;

            // Status
            lblStatus = new Label { Text = "Ready", Location = new Point(10, 460), AutoSize = true };
            this.Controls.Add(lblStatus);
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtInputFile.Text = ofd.FileName;
                }
            }
        }

        private async void BtnScan_Click(object sender, EventArgs e)
        {
            string file = txtInputFile.Text;
            if (!File.Exists(file)) return;

            btnScan.Enabled = false;
            lstResults.Items.Clear();
            lblStatus.Text = "Scanning...";

            var progress = new Progress<float>(p => {
                progressBar.Value = (int)(p * 100);
            });

            try
            {
                var results = await Scanner.ScanAsync(file, progress);
                
                lstResults.BeginUpdate();
                foreach (var res in results)
                {
                    var item = new ListViewItem(res.Offset.ToString());
                    item.SubItems.Add("0x" + res.Offset.ToString("X"));
                    item.SubItems.Add(res.Description);
                    item.Tag = res;
                    lstResults.Items.Add(item);
                }
                lstResults.EndUpdate();
                
                lblStatus.Text = $"Scan complete. Found {results.Count} signatures.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
            finally
            {
                btnScan.Enabled = true;
                progressBar.Value = 0;
            }
        }

        private void ExtractItem_Click(object sender, EventArgs e)
        {
            if (lstResults.SelectedItems.Count == 0) return;
            var res = (ScanResult)lstResults.SelectedItems[0].Tag;

            using (var sfd = new SaveFileDialog())
            {
                sfd.FileName = $"extracted_0x{res.Offset:X}.bin";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        using (var fs = new FileStream(txtInputFile.Text, FileMode.Open, FileAccess.Read))
                        using (var outFs = new FileStream(sfd.FileName, FileMode.Create, FileAccess.Write))
                        {
                            fs.Seek(res.Offset, SeekOrigin.Begin);
                            fs.CopyTo(outFs); // Dump until end. 
                            // TODO: In future, dump until next signature, but "End of file" is safest default for manual analysis.
                        }
                        MessageBox.Show("Extracted successfully.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Extraction failed: " + ex.Message);
                    }
                }
            }
        }
    }
}
