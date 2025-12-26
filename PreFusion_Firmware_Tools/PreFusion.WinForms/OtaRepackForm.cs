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

            var lblInput = new Label { Text = "Input :", Location = new System.Drawing.Point(12, 15), AutoSize = true };
            txtInputFile = new TextBox { Location = new System.Drawing.Point(15, 35), Width = 400, ReadOnly = true, PlaceholderText = "Select File or Folder..." };
            btnBrowseInput = new Button { Text = "File...", Location = new System.Drawing.Point(420, 33), Width = 60 };
            btnBrowseInput.Click += BtnBrowseInput_Click;

            var btnBrowseFolder = new Button { Text = "Folder...", Location = new System.Drawing.Point(485, 33), Width = 60 };
            btnBrowseFolder.Click += BtnBrowseFolder_Click;
            this.Controls.Add(btnBrowseFolder);

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

        private void BtnBrowseFolder_Click(object? sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtInputFile.Text = fbd.SelectedPath;
                    SetDefaultOutput(fbd.SelectedPath);
                }
            }
        }

        private void BtnBrowseInput_Click(object? sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog { Filter = "Disk Images (*.img)|*.img|All Files (*.*)|*.*", Multiselect = true })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    if (ofd.FileNames.Length > 1)
                    {
                        txtInputFile.Text = string.Join(";", ofd.FileNames);
                        SetDefaultOutput(Path.GetDirectoryName(ofd.FileNames[0])!);
                    }
                    else
                    {
                        txtInputFile.Text = ofd.FileName;
                        SetDefaultOutput(Path.GetDirectoryName(ofd.FileName)!);
                    }
                }
            }
        }

        private void SetDefaultOutput(string path)
        {
            if (string.IsNullOrEmpty(txtOutputDir.Text))
            {
                txtOutputDir.Text = path;
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
            string input = txtInputFile.Text;
            string outputDir = txtOutputDir.Text;

            if (string.IsNullOrEmpty(input))
            {
                MessageBox.Show("Please select valid input(s).");
                return;
            }
            if (!Directory.Exists(outputDir))
            {
                MessageBox.Show("Please select a valid output directory.");
                return;
            }

            btnRepack.Enabled = false;
            txtLog.AppendText($"Starting batch Repack...\r\n");
            
            // Collect files
            var filesToProcess = new System.Collections.Generic.List<string>();

            if (Directory.Exists(input))
            {
                // Is Folder
                filesToProcess.AddRange(Directory.GetFiles(input, "*.img"));
            }
            else if (input.Contains(";"))
            {
                // Multiple files
                filesToProcess.AddRange(input.Split(';'));
            }
            else if (File.Exists(input))
            {
                // Single File
                filesToProcess.Add(input);
            }

            if (filesToProcess.Count == 0)
            {
                MessageBox.Show("No .img files found to repack.");
                btnRepack.Enabled = true;
                return;
            }

            int count = 0;
            int total = filesToProcess.Count;

            foreach (var inputFile in filesToProcess)
            {
                count++;
                lblStatus.Text = $"Processing {count}/{total}: {Path.GetFileName(inputFile)}";
                txtLog.AppendText($"[{count}/{total}] Repacking {Path.GetFileName(inputFile)}...\r\n");
                progressBar.Value = 0;

                try
                {
                    var progress = new Progress<float>(p => 
                    {
                        int val = (int)(p * 100);
                        if (val > 100) val = 100;
                        progressBar.Value = val;
                    });

                    // Don't stop batch on single error
                    var result = await Task.Run(() => OtaRepacker.RepackImageAsync(inputFile, outputDir, chkCompress.Checked, progress));

                    txtLog.AppendText($"  -> Dat: {Path.GetFileName(result.NewDatPath)}\r\n");
                    txtLog.AppendText($"  -> Transfer: {result.TotalBlocks} blocks\r\n");

                    if (chkCleanup.Checked && chkCompress.Checked)
                    {
                        string rawDat = result.NewDatPath.Replace(".br", "");
                        if (File.Exists(rawDat) && rawDat != result.NewDatPath)
                        {
                             // Ensure we don't delete if we didn't compress (i.e. NewDatPath IS rawDat)
                             // Logic inside Repacker returns the final path.
                             // result.NewDatPath is .br if compressed.
                             File.Delete(rawDat);
                             txtLog.AppendText($"  -> Cleaned .dat\r\n");
                        }
                    }
                }
                catch (Exception ex)
                {
                    txtLog.AppendText($"ERROR processing {Path.GetFileName(inputFile)}: {ex.Message}\r\n");
                }
            }

            lblStatus.Text = "Batch Repack Complete.";
            progressBar.Value = 100;
            MessageBox.Show($"Batch processing complete.\nProcessed {total} files.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            
            btnRepack.Enabled = true;
        }
    }
}
