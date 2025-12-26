using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Ext2Read.Core;

namespace Ext2Read.WinForms
{
    public class OtaRepackForm : Form
    {
        private TextBox txtInputFile;
        private Button btnBrowseInput;
        private TextBox txtOutputDir;
        private Button btnBrowseOutput;
        private CheckBox chkCompress;
        private CheckBox chkCleanup; // Delete intermediate .new.dat
        private Button btnRepack;
        private ProgressBar progressBar;
        private Label lblStatus;
        private TextBox txtLog;

        public OtaRepackForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Android OTA Repacker";
            this.Size = new System.Drawing.Size(600, 450);
            this.StartPosition = FormStartPosition.CenterParent;

            var lblInput = new Label { Text = "Input Image (.img):", Location = new System.Drawing.Point(12, 15), AutoSize = true };
            txtInputFile = new TextBox { Location = new System.Drawing.Point(15, 35), Width = 450, ReadOnly = true };
            btnBrowseInput = new Button { Text = "...", Location = new System.Drawing.Point(475, 33), Width = 40 };
            btnBrowseInput.Click += BtnBrowseInput_Click;

            var lblOutput = new Label { Text = "Output Directory:", Location = new System.Drawing.Point(12, 70), AutoSize = true };
            txtOutputDir = new TextBox { Location = new System.Drawing.Point(15, 90), Width = 450, ReadOnly = true };
            btnBrowseOutput = new Button { Text = "...", Location = new System.Drawing.Point(475, 88), Width = 40 };
            btnBrowseOutput.Click += BtnBrowseOutput_Click;

            chkCompress = new CheckBox { Text = "Compress Output (Brotli)", Location = new System.Drawing.Point(15, 125), Checked = true, AutoSize = true };
            chkCleanup = new CheckBox { Text = "Cleanup Intermediate Files (.dat)", Location = new System.Drawing.Point(200, 125), Checked = true, AutoSize = true };

            btnRepack = new Button { Text = "Repack Now", Location = new System.Drawing.Point(440, 120), Width = 100, Height = 30 };
            btnRepack.Click += BtnRepack_Click;

            lblStatus = new Label { Text = "Ready", Location = new System.Drawing.Point(12, 160), AutoSize = true };
            progressBar = new ProgressBar { Location = new System.Drawing.Point(15, 180), Width = 550, Height = 20 };

            txtLog = new TextBox { Location = new System.Drawing.Point(15, 210), Width = 550, Height = 180, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true };

            this.Controls.Add(lblInput);
            this.Controls.Add(txtInputFile);
            this.Controls.Add(btnBrowseInput);
            this.Controls.Add(lblOutput);
            this.Controls.Add(txtOutputDir);
            this.Controls.Add(btnBrowseOutput);
            this.Controls.Add(chkCompress);
            this.Controls.Add(chkCleanup);
            this.Controls.Add(btnRepack);
            this.Controls.Add(lblStatus);
            this.Controls.Add(progressBar);
            this.Controls.Add(txtLog);
        }

        private void BtnBrowseInput_Click(object? sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog { Filter = "Disk Images (*.img)|*.img|All Files (*.*)|*.*" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtInputFile.Text = ofd.FileName;
                    if (string.IsNullOrEmpty(txtOutputDir.Text))
                    {
                        txtOutputDir.Text = Path.GetDirectoryName(ofd.FileName);
                    }
                }
            }
        }

        private void BtnBrowseOutput_Click(object? sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtOutputDir.Text = fbd.SelectedPath;
                }
            }
        }

        private async void BtnRepack_Click(object? sender, EventArgs e)
        {
            string inputFile = txtInputFile.Text;
            string outputDir = txtOutputDir.Text;

            if (!File.Exists(inputFile))
            {
                MessageBox.Show("Please select a valid input file.");
                return;
            }
            if (!Directory.Exists(outputDir))
            {
                MessageBox.Show("Please select a valid output directory.");
                return;
            }

            btnRepack.Enabled = false;
            txtLog.AppendText($"Starting repack of {Path.GetFileName(inputFile)}...\r\n");
            lblStatus.Text = "Analyzing and Writing .new.dat...";
            progressBar.Value = 0;

            try
            {
                var progress = new Progress<float>(p => 
                {
                    // Scale 0-100
                    int val = (int)(p * 100);
                    if (val > 100) val = 100;
                    progressBar.Value = val;
                });

                var result = await Task.Run(() => OtaRepacker.RepackImageAsync(inputFile, outputDir, chkCompress.Checked, progress));

                txtLog.AppendText($"Generated: {Path.GetFileName(result.NewDatPath)}\r\n");
                txtLog.AppendText($"Generated: {Path.GetFileName(result.TransferListPath)}\r\n");
                txtLog.AppendText($"Total Blocks: {result.TotalBlocks}\r\n");

                if (chkCleanup.Checked && chkCompress.Checked)
                {
                    // If we compressed, result.NewDatPath is the .br file.
                    // The .new.dat file is the one without .br
                    string rawDat = result.NewDatPath.Replace(".br", "");
                    if (File.Exists(rawDat) && rawDat != result.NewDatPath)
                    {
                        File.Delete(rawDat);
                        txtLog.AppendText($"Cleaned up: {Path.GetFileName(rawDat)}\r\n");
                    }
                }

                lblStatus.Text = "Repack Complete.";
                MessageBox.Show("Repack completed successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                txtLog.AppendText($"Error: {ex.Message}\r\n");
                lblStatus.Text = "Error.";
                MessageBox.Show($"Error during repack: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnRepack.Enabled = true;
            }
        }
    }
}
