using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Ext2Read.WinForms; // Ensure namespace access

namespace Ext2Read.WinForms
{
    public class BinwalkWrapperForm : Form
    {
        private TextBox txtInputFile;
        private Button btnBrowseInput;
        private CheckBox chkExtract;
        private CheckBox chkRecursive;
        private CheckBox chkEntropy;
        private TextBox txtOutput;
        private TextBox txtBinwalkPath;
        private Button btnBrowseBinwalk;
        private Button btnRun;
        private Button btnClose;

        public BinwalkWrapperForm()
        {
            InitializeComponent();
            FindBinwalk();
        }

        private void InitializeComponent()
        {
            this.Text = "BinWalk Wrapper (Rust)";
            this.Size = new System.Drawing.Size(600, 500);
            this.StartPosition = FormStartPosition.CenterParent;

            int y = 20;

            // Binwalk Path
            var lblBinwalk = new Label { Text = "BinWalk Executable:", Location = new System.Drawing.Point(10, y), AutoSize = true };
            this.Controls.Add(lblBinwalk);
            
            txtBinwalkPath = new TextBox { Location = new System.Drawing.Point(120, y - 3), Width = 370 };
            this.Controls.Add(txtBinwalkPath);

            btnBrowseBinwalk = new Button { Text = "Browse...", Location = new System.Drawing.Point(500, y - 5) };
            btnBrowseBinwalk.Click += (s, e) => BrowseFile("Select binwalk.exe", "Executables (*.exe)|*.exe", txtBinwalkPath);
            this.Controls.Add(btnBrowseBinwalk);
            y += 40;

            // Input File
            var lblInput = new Label { Text = "File to Analyze:", Location = new System.Drawing.Point(10, y), AutoSize = true };
            this.Controls.Add(lblInput);

            txtInputFile = new TextBox { Location = new System.Drawing.Point(120, y - 3), Width = 370 };
            this.Controls.Add(txtInputFile);

            btnBrowseInput = new Button { Text = "Browse...", Location = new System.Drawing.Point(500, y - 5) };
            btnBrowseInput.Click += (s, e) => BrowseFile("Select File to Analyze", "All Files (*.*)|*.*", txtInputFile);
            this.Controls.Add(btnBrowseInput);
            y += 40;

            // Options Group
            var grpOptions = new GroupBox { Text = "Analysis Options", Location = new System.Drawing.Point(10, y), Size = new System.Drawing.Size(560, 80) };
            this.Controls.Add(grpOptions);

            chkExtract = new CheckBox { Text = "Extract (-e)", Location = new System.Drawing.Point(20, 30), Checked = true, AutoSize = true };
            grpOptions.Controls.Add(chkExtract);

            chkRecursive = new CheckBox { Text = "Recursive (-M)", Location = new System.Drawing.Point(150, 30), AutoSize = true };
            grpOptions.Controls.Add(chkRecursive);

            chkEntropy = new CheckBox { Text = "Entropy Graph (-E)", Location = new System.Drawing.Point(300, 30), AutoSize = true };
            grpOptions.Controls.Add(chkEntropy);
            
            y += 90;

            // Buttons
            btnRun = new Button { Text = "Run BinWalk", Location = new System.Drawing.Point(10, y), Width = 150, Height = 30 };
            btnRun.Click += BtnRun_Click;
            this.Controls.Add(btnRun);

            btnClose = new Button { Text = "Close", Location = new System.Drawing.Point(420, y), Width = 150, Height = 30 };
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);
            y += 40;

            // Output Log
            txtOutput = new TextBox { Location = new System.Drawing.Point(10, y), Width = 560, Height = 200, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Font = new System.Drawing.Font("Consolas", 9F) };
            this.Controls.Add(txtOutput);
        }

        private void FindBinwalk()
        {
            // Try relative path
            string relative = Path.Combine(Application.StartupPath, "Tools", "BinWalk", "binwalk.exe");
            if (File.Exists(relative))
            {
                txtBinwalkPath.Text = relative;
            }
            else
            {
                 // Try looking in TEMP if user placed it there?
                 // But typically apps shouldn't run from source dir TEMP.
                 txtBinwalkPath.Text = "";
                 Log("BinWalk executable not found automatically. Please locate binwalk.exe.");
            }
        }

        private void BrowseFile(string title, string filter, TextBox target)
        {
            using (var ofd = new OpenFileDialog { Title = title, Filter = filter })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    target.Text = ofd.FileName;
                }
            }
        }

        private void Log(string msg)
        {
            if (this.InvokeRequired) this.Invoke(new Action(() => Log(msg)));
            else
            {
                txtOutput.AppendText(msg + "\r\n");
                txtOutput.SelectionStart = txtOutput.Text.Length;
                txtOutput.ScrollToCaret();
            }
        }

        private async void BtnRun_Click(object sender, EventArgs e)
        {
            string binwalkExe = txtBinwalkPath.Text;
            string inputFile = txtInputFile.Text;

            if (string.IsNullOrWhiteSpace(binwalkExe) || !File.Exists(binwalkExe))
            {
                MessageBox.Show("Please select a valid binwalk.exe executable.\n\nYou may need to compile the Rust project or download a release.", "Executable Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(inputFile) || !File.Exists(inputFile))
            {
                MessageBox.Show("Please select a valid input file to analyze.", "Input Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnRun.Enabled = false;
            txtOutput.Clear();
            Log($"Running BinWalk on: {Path.GetFileName(inputFile)}...");

            try
            {
                // Build Arguments
                // binwalk.exe [OPTIONS] <FILE>
                string args = "";
                if (chkExtract.Checked) args += "-e ";
                if (chkRecursive.Checked) args += "-M ";
                if (chkEntropy.Checked) args += "-E ";
                
                args += $"\"{inputFile}\"";

                await Task.Run(() => RunProcess(binwalkExe, args));
            }
            catch (Exception ex)
            {
                Log($"Error: {ex.Message}");
            }
            finally
            {
                btnRun.Enabled = true;
                Log("Finished.");
            }
        }

        private void RunProcess(string exe, string args)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe)
            };

            using (var proc = new Process { StartInfo = startInfo })
            {
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) Log(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) Log("ERR: " + e.Data); };

                try 
                {
                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    proc.WaitForExit();
                }
                catch (Exception ex)
                {
                    Log($"Failed to start process: {ex.Message}"); // e.g. not a valid win32 app if they select a python script
                }
            }
        }
    }
}
