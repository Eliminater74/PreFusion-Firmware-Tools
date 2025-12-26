using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Ext2Read.Core;

namespace Ext2Read.WinForms
{
    public class PayloadUnpackerForm : Form
    {
        private TextBox txtInputFile;
        private Button btnBrowseInput;
        private TextBox txtOutputDir;
        private Button btnBrowseOutput;
        private Button btnUnpack;
        private ProgressBar progressBar;
        private Label lblStatus;
        private TextBox txtLog;

        public PayloadUnpackerForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Android Payload.bin Unpacker";
            this.Size = new System.Drawing.Size(600, 400);
            this.StartPosition = FormStartPosition.CenterParent;

            var lblInput = new Label { Text = "Input Payload (.bin or .zip):", Location = new System.Drawing.Point(12, 15), AutoSize = true };
            txtInputFile = new TextBox { Location = new System.Drawing.Point(15, 35), Width = 450, ReadOnly = true };
            btnBrowseInput = new Button { Text = "...", Location = new System.Drawing.Point(475, 33), Width = 40 };
            btnBrowseInput.Click += BtnBrowseInput_Click;

            var lblOutput = new Label { Text = "Output Directory:", Location = new System.Drawing.Point(12, 70), AutoSize = true };
            txtOutputDir = new TextBox { Location = new System.Drawing.Point(15, 90), Width = 450, ReadOnly = true };
            btnBrowseOutput = new Button { Text = "...", Location = new System.Drawing.Point(475, 88), Width = 40 };
            btnBrowseOutput.Click += BtnBrowseOutput_Click;

            btnUnpack = new Button { Text = "Unpack Payload", Location = new System.Drawing.Point(420, 130), Width = 120, Height = 30 };
            btnUnpack.Click += BtnUnpack_Click;

            lblStatus = new Label { Text = "Ready", Location = new System.Drawing.Point(12, 160), AutoSize = true };
            progressBar = new ProgressBar { Location = new System.Drawing.Point(15, 180), Width = 550, Height = 20 };

            txtLog = new TextBox { Location = new System.Drawing.Point(15, 210), Width = 550, Height = 140, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true };

            this.Controls.Add(lblInput);
            this.Controls.Add(txtInputFile);
            this.Controls.Add(btnBrowseInput);
            this.Controls.Add(lblOutput);
            this.Controls.Add(txtOutputDir);
            this.Controls.Add(btnBrowseOutput);
            this.Controls.Add(btnUnpack);
            this.Controls.Add(lblStatus);
            this.Controls.Add(progressBar);
            this.Controls.Add(txtLog);
        }

        private void BtnBrowseInput_Click(object? sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog { Filter = "Payload Files (*.bin;*.zip)|*.bin;*.zip|All Files (*.*)|*.*" })
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

        private async void BtnUnpack_Click(object? sender, EventArgs e)
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

            btnUnpack.Enabled = false;
            txtLog.AppendText($"Starting unpack of {Path.GetFileName(inputFile)}...\r\n");
            lblStatus.Text = "Parsing Payload...";
            progressBar.Value = 0;

            try
            {
                var progress = new Progress<float>(p => 
                {
                    int val = (int)(p * 100);
                    if (val > 100) val = 100;
                    progressBar.Value = val;
                    // lblStatus.Text = $"Processing... {val}%"; // Avoid too many updates
                });

                await Task.Run(() => PayloadDumper.ExtractPayloadAsync(inputFile, outputDir, progress));

                lblStatus.Text = "Unpack Complete.";
                progressBar.Value = 100;
                MessageBox.Show("Payload Unpacked Successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtLog.AppendText("Done.\r\n");
            }
            catch (Exception ex)
            {
                txtLog.AppendText($"Error: {ex.Message}\r\n");
                lblStatus.Text = "Error.";
                MessageBox.Show($"Error during unpack: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnUnpack.Enabled = true;
            }
        }
    }
}
