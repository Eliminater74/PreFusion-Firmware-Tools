using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Ext2Read.Core.Binwalk;
using System.Linq;

namespace Ext2Read.WinForms
{
    public class BinwalkForm : Form
    {
        private TextBox txtInputFile;
        private Button btnBrowse;
        private TabControl tabControl;

        // Tab 3: Search
        private TextBox txtSearch;
        private RadioButton rdoText;
        private RadioButton rdoHex;
        private Button btnSearch;
        private ListView lstSearch;

        // Tab 1: Signatures
        private ListView lstResults;
        private Button btnScan;
        private Button btnLoadSigs; // New
        private Button btnExtractAll;
        private Button btnClear;
        private Button btnSaveLog;
        private CheckBox chkRecursive;
        private CheckBox chkOpcodes;
        private NumericUpDown numDepth;
        private ContextMenuStrip contextMenu;

        // Tab 2: Entropy
        private Button btnEntropy;
        private Panel pnlEntropy;
        private Label lblEntropyStatus;
        private List<EntropyResult> _entropyData;

        // Tab 4: Strings
        private ListView lstStrings;
        private NumericUpDown numMinLen;
        private TextBox txtStringsFilter;
        private Button btnStringsScan;
        private Button btnSaveStrings;
        private Label lblStringsStatus;
        private List<StringResult> _allStringData = new List<StringResult>();
        private List<StringResult> _filteredStringData = new List<StringResult>();
        private List<Signature> _customSignatures = new List<Signature>(); // New

        // Shared
        private ProgressBar progressBar;
        private Label lblStatus;

        public BinwalkForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Native Firmware Scanner (BinWalk)";
            this.Size = new System.Drawing.Size(800, 600);
            this.StartPosition = FormStartPosition.CenterParent;

            // Header
            var lblInput = new Label { Text = "Firmware File:", Location = new Point(10, 15), AutoSize = true };
            this.Controls.Add(lblInput);

            txtInputFile = new TextBox { Location = new Point(100, 12), Width = 560 };
            this.Controls.Add(txtInputFile);

            btnBrowse = new Button { Text = "Browse...", Location = new Point(670, 10) };
            btnBrowse.Click += BtnBrowse_Click;
            this.Controls.Add(btnBrowse);

            // Tabs
            tabControl = new TabControl { Location = new Point(10, 50), Width = 760, Height = 480 };
            this.Controls.Add(tabControl);

            // --- Tab 1: Signatures ---
            var tabSig = new TabPage("Signatures");
            tabControl.TabPages.Add(tabSig);

            // Tighter packing to fit in 760px width
            btnScan = new Button { Text = "Scan", Location = new Point(6, 10), Width = 80 };
            btnScan.Click += BtnScan_Click;
            tabSig.Controls.Add(btnScan);

            btnLoadSigs = new Button { Text = "Load...", Location = new Point(90, 10), Width = 80 };
            btnLoadSigs.Click += BtnLoadSigs_Click;
            tabSig.Controls.Add(btnLoadSigs);

            btnExtractAll = new Button { Text = "Extract All", Location = new Point(175, 10), Width = 90 };
            btnExtractAll.Click += BtnExtractAll_Click;
            tabSig.Controls.Add(btnExtractAll);

            chkRecursive = new CheckBox { Text = "Recursive (-M)", Location = new Point(270, 14), AutoSize = true };
            tabSig.Controls.Add(chkRecursive);
            
            chkOpcodes = new CheckBox { Text = "Opcodes (-A)", Location = new Point(375, 14), AutoSize = true };
            tabSig.Controls.Add(chkOpcodes);

            var lblDepth = new Label { Text = "Depth:", Location = new Point(475, 15), AutoSize = true };
            tabSig.Controls.Add(lblDepth);

            numDepth = new NumericUpDown { Location = new Point(520, 12), Width = 35, Minimum = 1, Maximum = 5, Value = 2 };
            tabSig.Controls.Add(numDepth);

            btnClear = new Button { Text = "Clear", Location = new Point(565, 10), Width = 60 };
            btnClear.Click += BtnClear_Click;
            tabSig.Controls.Add(btnClear);

            btnSaveLog = new Button { Text = "Save Log", Location = new Point(630, 10), Width = 80 };
            btnSaveLog.Click += BtnSaveLog_Click;
            tabSig.Controls.Add(btnSaveLog);

            lstResults = new ListView
            {
                Location = new Point(10, 45),
                Width = 730,
                Height = 400,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            lstResults.Columns.Add("Decimal", 100);
            lstResults.Columns.Add("Hexadecimal", 100);
            lstResults.Columns.Add("Description", 500);
            tabSig.Controls.Add(lstResults);

            contextMenu = new ContextMenuStrip();
            var extractItem = new ToolStripMenuItem("Extract from here...");
            extractItem.Click += ExtractItem_Click;
            contextMenu.Items.Add(extractItem);
            lstResults.ContextMenuStrip = contextMenu;

            // --- Tab 2: Entropy ---
            var tabEntropy = new TabPage("Entropy Analysis");
            tabControl.TabPages.Add(tabEntropy);

            btnEntropy = new Button { Text = "Run Entropy Analysis", Location = new Point(10, 10), Width = 150 };
            btnEntropy.Click += BtnEntropy_Click;
            tabEntropy.Controls.Add(btnEntropy);

            lblEntropyStatus = new Label { Text = "Not analyzed.", Location = new Point(170, 15), AutoSize = true };
            tabEntropy.Controls.Add(lblEntropyStatus);

            pnlEntropy = new Panel
            {
                Location = new Point(10, 45),
                Width = 730,
                Height = 400,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            pnlEntropy.Paint += PnlEntropy_Paint;
            pnlEntropy.Resize += (s, e) => pnlEntropy.Invalidate(); // Redraw on resize
            tabEntropy.Controls.Add(pnlEntropy);

            // --- Tab 3: Search (Raw) ---
            var tabSearch = new TabPage("Raw Search");
            tabControl.TabPages.Add(tabSearch);

            var lblSearch = new Label { Text = "Search Pattern:", Location = new Point(10, 15), AutoSize = true };
            tabSearch.Controls.Add(lblSearch);

            txtSearch = new TextBox { Location = new Point(100, 12), Width = 300 };
            tabSearch.Controls.Add(txtSearch);

            rdoText = new RadioButton { Text = "Text", Location = new Point(410, 11), Checked = true, AutoSize = true };
            tabSearch.Controls.Add(rdoText);

            rdoHex = new RadioButton { Text = "Hex (e.g. 1F 8B)", Location = new Point(470, 11), AutoSize = true };
            tabSearch.Controls.Add(rdoHex);

            btnSearch = new Button { Text = "Find", Location = new Point(580, 10), Width = 80 };
            btnSearch.Click += BtnSearch_Click;
            tabSearch.Controls.Add(btnSearch);

            lstSearch = new ListView
            {
                Location = new Point(10, 45),
                Width = 730,
                Height = 400,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            lstSearch.Columns.Add("Offset", 100);
            lstSearch.Columns.Add("Description", 500);
            lstSearch.Columns.Add("Description", 500);
            tabSearch.Controls.Add(lstSearch);

            // --- Tab 4: Strings ---
            var tabStrings = new TabPage("Strings");
            tabControl.TabPages.Add(tabStrings);

            var lblMinLen = new Label { Text = "Min Length:", Location = new Point(10, 15), AutoSize = true };
            tabStrings.Controls.Add(lblMinLen);

            numMinLen = new NumericUpDown { Location = new Point(85, 12), Width = 50, Minimum = 3, Maximum = 100, Value = 4 };
            tabStrings.Controls.Add(numMinLen);

            tabStrings.Controls.Add(numMinLen);

            btnStringsScan = new Button { Text = "Extract Strings", Location = new Point(150, 10), Width = 120 };
            btnStringsScan.Click += BtnStringsScan_Click;
            tabStrings.Controls.Add(btnStringsScan);

            var lblFilter = new Label { Text = "Filter:", Location = new Point(280, 15), AutoSize = true };
            tabStrings.Controls.Add(lblFilter);

            txtStringsFilter = new TextBox { Location = new Point(320, 12), Width = 150 };
            txtStringsFilter.TextChanged += TxtStringsFilter_TextChanged;
            tabStrings.Controls.Add(txtStringsFilter);

            btnSaveStrings = new Button { Text = "Save to File", Location = new Point(480, 10), Width = 100 };
            btnSaveStrings.Click += BtnSaveStrings_Click;
            tabStrings.Controls.Add(btnSaveStrings);

            lblStringsStatus = new Label { Text = "Ready", Location = new Point(590, 15), AutoSize = true };
            tabStrings.Controls.Add(lblStringsStatus);

            lstStrings = new ListView
            {
                Location = new Point(10, 45),
                Width = 730,
                Height = 400,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                VirtualMode = true, // Crucial for performance
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            lstStrings.Columns.Add("Offset", 100);
            lstStrings.Columns.Add("Hex", 100);
            lstStrings.Columns.Add("String", 500);
            lstStrings.RetrieveVirtualItem += LstStrings_RetrieveVirtualItem;
            tabStrings.Controls.Add(lstStrings);
            
            // Footer
            progressBar = new ProgressBar { Location = new Point(10, 540), Width = 560, Height = 15 };
            this.Controls.Add(progressBar);

            lblStatus = new Label { Text = "Ready", Location = new Point(580, 540), AutoSize = true };
            this.Controls.Add(lblStatus);
        }

        // --- Event Handlers ---

        // --- Event Handlers ---
        
        private void BtnBrowse_Click(object? sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtInputFile.Text = ofd.FileName;
                }
            }
        }

        private async void BtnScan_Click(object? sender, EventArgs e)
        {
            string file = txtInputFile.Text;
            if (!File.Exists(file)) return;

            btnScan.Enabled = false;
            lstResults.Items.Clear();
            lblStatus.Text = "Scanning signatures...";

            var progress = new Progress<float>(p => progressBar.Value = (int)(p * 100));

            try
            {
                // Filter signatures based on CheckBox
                var sigs = Scanner.DefaultSignatures.ToList();
                if (_customSignatures.Count > 0) sigs.AddRange(_customSignatures); // Add Custom

                if (!chkOpcodes.Checked)
                {
                    sigs.RemoveAll(s => s.Name.Contains("Loop/Branch"));
                }

                var results = await Scanner.ScanAsync(file, progress, sigs);

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
                lblStatus.Text = $"Found {results.Count} signatures.";
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

        private void BtnLoadSigs_Click(object? sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog { Filter = "Signature Files (*.magic;*.txt)|*.magic;*.txt|All Files|*.*" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    var loaded = Scanner.LoadSignatures(ofd.FileName);
                    if (loaded.Count > 0)
                    {
                        _customSignatures.AddRange(loaded);
                        MessageBox.Show($"Loaded {loaded.Count} signatures.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        lblStatus.Text = $"Custom signatures loaded: {_customSignatures.Count}";
                    }
                    else
                    {
                        MessageBox.Show("No valid signatures found in file.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private async void BtnEntropy_Click(object? sender, EventArgs e)
        {
            string file = txtInputFile.Text;
            if (!File.Exists(file)) return;

            btnEntropy.Enabled = false;
            lblStatus.Text = "Calculating entropy...";
            lblEntropyStatus.Text = "Analyzing...";

            var progress = new Progress<float>(p => progressBar.Value = (int)(p * 100));

            try
            {
                // Adjustable block size? 
                // Binwalk default is usually 1KB or depends on graph.
                // For a 1GB file, 1KB blocks = 1 million points.
                // We might want to scale block size based on file size for performance.
                long len = new FileInfo(file).Length;
                int blockSize = 1024;
                if (len > 10 * 1024 * 1024) blockSize = 4096;
                if (len > 100 * 1024 * 1024) blockSize = 16384;

                _entropyData = await Scanner.CalculateEntropyAsync(file, blockSize, progress);
                pnlEntropy.Invalidate(); // Trigger paint
                lblStatus.Text = "Entropy analysis complete.";
                lblEntropyStatus.Text = $"Done. Block Size: {blockSize} bytes.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
            finally
            {
                btnEntropy.Enabled = true;
                progressBar.Value = 0;
            }
        }

        private void PnlEntropy_Paint(object? sender, PaintEventArgs e)
        {
            if (_entropyData == null || _entropyData.Count < 2) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            float w = pnlEntropy.Width;
            float h = pnlEntropy.Height;
            float xStep = w / _entropyData.Count;

            // Y Axis: 0 to 8.0
            // Map 0 -> h, 8 -> 0

            using (Pen pen = new Pen(Color.Blue, 1)) // Thinner pen for dense data
            using (Pen gridPen = new Pen(Color.LightGray, 1))
            {
                // Draw Grid
                for (int i = 0; i <= 8; i++)
                {
                    float y = h - (i * (h / 8));
                    g.DrawLine(gridPen, 0, y, w, y);
                }

                // Draw Graph
                PointF[] points = new PointF[_entropyData.Count];
                for (int i = 0; i < _entropyData.Count; i++)
                {
                    float x = i * xStep;
                    float yVal = (float)_entropyData[i].Entropy;
                    float y = h - (yVal * (h / 8)); // Scaling
                    points[i] = new PointF(x, y);
                }

                // If points too many, DrawLines can crash or be slow.
                // But generally okay for ~100k points in GDI+.
                // If sparse, DrawLines is perfect.
                g.DrawLines(pen, points);
            }

            // Rising Entropy -> Encryption/Compression
            // High sustained (close to 8) = Encrypted/Compressed
            // Low/varying = Code/Text/Padding
        }

        private async void BtnSearch_Click(object? sender, EventArgs e)
        {
            string file = txtInputFile.Text;
            if (!File.Exists(file)) return;

            btnSearch.Enabled = false;
            lstSearch.Items.Clear();
            lblStatus.Text = "Searching...";

            var progress = new Progress<float>(p => progressBar.Value = (int)(p * 100));
            
            try 
            {
                byte[] pattern = null;
                string desc = "";

                if (rdoText.Checked)
                {
                    pattern = System.Text.Encoding.ASCII.GetBytes(txtSearch.Text);
                    desc = "Text Search";
                }
                else
                {
                    // Hex String to Byte[]
                    string hex = txtSearch.Text.Replace(" ", "").Replace("0x", "");
                    if (hex.Length % 2 != 0) throw new Exception("Invalid Hex String");
                    pattern = new byte[hex.Length / 2];
                    for (int i = 0; i < hex.Length; i += 2)
                        pattern[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
                    desc = "Hex Search";
                }

                var results = await Scanner.SearchCustomAsync(file, pattern, desc, progress);
                
                lstSearch.BeginUpdate();
                foreach (var res in results)
                {
                    var item = new ListViewItem(res.Offset.ToString());
                    item.SubItems.Add(res.Description);
                    lstSearch.Items.Add(item);
                }
                lstSearch.EndUpdate();
                lblStatus.Text = $"Found {results.Count} matches.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Search Error: " + ex.Message);
            }
            finally
            {
                btnSearch.Enabled = true;
                progressBar.Value = 0;
            }
        }

        private async void BtnExtractAll_Click(object? sender, EventArgs e)
        {
            if (lstResults.Items.Count == 0) return;

            using (var fbd = new FolderBrowserDialog { Description = "Select Output Directory" })
            {
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    btnExtractAll.Enabled = false;
                    lblStatus.Text = "Extracting...";
                    
                    try
                    {
                        var scanResults = lstResults.Items.Cast<ListViewItem>().Select(i => (ScanResult)i.Tag!).ToList();
                        int depth = chkRecursive.Checked ? (int)numDepth.Value : 0;
                        
                        await ExtractRecursiveAsync(txtInputFile.Text, fbd.SelectedPath, scanResults, depth);
                        
                        MessageBox.Show("Extraction complete.");
                        lblStatus.Text = "Extraction complete.";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error extracting: " + ex.Message);
                    }
                    finally
                    {
                        btnExtractAll.Enabled = true;
                    }
                }
            }
        }

        // Recursive Extraction Engine
        private async Task ExtractRecursiveAsync(string inputFile, string outputDir, List<ScanResult> results, int depth)
        {
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            using (var fs = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                for (int i = 0; i < results.Count; i++)
                {
                    var res = results[i];
                    long nextOffset = (i < results.Count - 1) ? results[i+1].Offset : fs.Length;
                    long size = nextOffset - res.Offset;
                    if (size <= 0) continue;

                    string cleanName = res.Description.Split(' ')[0];
                    foreach(char c in Path.GetInvalidFileNameChars()) cleanName = cleanName.Replace(c, '_');
                    
                    // Create organized folder for this extraction? Binwalk usually dumps to folder `_filename.extracted`
                    // Here we are inside a loop of signatures.
                    // Let's name the file: offset_name.bin
                    string outName = $"{res.Offset:X}_{cleanName}.bin";
                    string outPath = Path.Combine(outputDir, outName);

                    // 1. Carve
                    await CarveFileAsync(fs, res.Offset, size, outPath);

                    // 2. Recurse?
                    if (depth > 0)
                    {
                        // Try to decompress known types to allow deeper scanning
                        string decompressedPath = Path.Combine(outputDir, $"{outName}.extracted");
                        bool isCompressed = false;

                        try
                        {
                            if (cleanName.IndexOf("GZIP", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // Decompress GZIP
                                using (var originalFs = new FileStream(outPath, FileMode.Open, FileAccess.Read))
                                using (var gz = new System.IO.Compression.GZipStream(originalFs, System.IO.Compression.CompressionMode.Decompress))
                                using (var target = new FileStream(decompressedPath, FileMode.Create))
                                {
                                    await gz.CopyToAsync(target);
                                }
                                isCompressed = true;
                            }
                            else if (cleanName.IndexOf("Zip", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // Zip is complex as it is an archive, not a stream. 
                                // Binwalk simply extracts it. 
                                // For now, we rely on the carved file being the zip itself.
                                // We might need to unzip it to a folder?
                                // Let's skip complex Zip recursion for this iteration and focus on streams.
                            }
                        }
                        catch 
                        { 
                            // Decompression failed (corrupt or partial), ignore
                        }

                        // If we successfully decompressed, scan the decompressed file
                        if (isCompressed && File.Exists(decompressedPath))
                        {
                            var newResults = await Scanner.ScanAsync(decompressedPath);
                            if (newResults.Count > 0)
                            {
                                string subDir = Path.Combine(outputDir, $"_{outName}.extracted");
                                await ExtractRecursiveAsync(decompressedPath, subDir, newResults, depth - 1);
                            }
                        }
                        else
                        {
                            // Try scanning the carved file directly (in case it is a filesystem like SquashFS)
                            // Filesystems are not "compressed streams" in the same way, but they contain files.
                            // Binwalk usually mounts them or runs unsquashfs. We can't easily do that natively in C#.
                            // But we CAN scan them for signatures inside the FS image? 
                            // Yes, that's "Search inside".
                            
                            var newResults = await Scanner.ScanAsync(outPath);
                            if (newResults.Count > 0)
                            {
                                string subDir = Path.Combine(outputDir, $"_{outName}.rec");
                                await ExtractRecursiveAsync(outPath, subDir, newResults, depth - 1);
                            }
                        }
                    }
                }
            }
        }

        private async Task CarveFileAsync(FileStream src, long offset, long length, string dest)
        {
            byte[] buffer = new byte[8192];
            src.Seek(offset, SeekOrigin.Begin);
            long remaining = length;
            
            using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write))
            {
                while (remaining > 0)
                {
                    int toRead = (int)Math.Min(buffer.Length, remaining);
                    int read = await src.ReadAsync(buffer, 0, toRead);
                    if (read == 0) break;
                    await dst.WriteAsync(buffer, 0, read);
                    remaining -= read;
                }
            }
        }

        private void BtnClear_Click(object? sender, EventArgs e)
        {
            txtInputFile.Text = "";
            lstResults.Items.Clear();
            lstSearch.Items.Clear();
            if (_entropyData != null) _entropyData.Clear();
            pnlEntropy.Invalidate();
            
            lblStatus.Text = "Cleared.";
            progressBar.Value = 0;
            lblEntropyStatus.Text = "Not analyzed.";
        }

        private void BtnSaveLog_Click(object? sender, EventArgs e)
        {
            if (lstResults.Items.Count == 0)
            {
                MessageBox.Show("No results to save.");
                return;
            }

            using (var sfd = new SaveFileDialog { Filter = "Text Files|*.txt", FileName = "binwalk_log.txt" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        using (var sw = new StreamWriter(sfd.FileName))
                        {
                            sw.WriteLine($"BinWalk Scan Results for: {txtInputFile.Text}");
                            sw.WriteLine($"Date: {DateTime.Now}");
                            sw.WriteLine("--------------------------------------------------");
                            sw.WriteLine(String.Format("{0,-15} {1,-15} {2}", "DECIMAL", "HEX", "DESCRIPTION"));
                            sw.WriteLine("--------------------------------------------------");

                            foreach (ListViewItem item in lstResults.Items)
                            {
                                var res = (ScanResult)item.Tag!;
                                sw.WriteLine(String.Format("{0,-15} {1,-15} {2}", 
                                    res.Offset, 
                                    "0x" + res.Offset.ToString("X"), 
                                    res.Description));
                            }
                        }
                        MessageBox.Show("Log saved successfully.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error saving log: " + ex.Message);
                    }
                }
            }
        }

        private void ExtractItem_Click(object? sender, EventArgs e)
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
                            fs.CopyTo(outFs);
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

        private async void BtnStringsScan_Click(object? sender, EventArgs e)
        {
            string file = txtInputFile.Text;
            if (!File.Exists(file)) return;

            btnStringsScan.Enabled = false;
            lstStrings.VirtualListSize = 0;
            _allStringData.Clear();
            _filteredStringData.Clear();
            txtStringsFilter.Text = ""; // Reset filter
            
            lblStatus.Text = "Extracting strings...";
            lblStringsStatus.Text = "Scanning...";

            var progress = new Progress<float>(p => progressBar.Value = (int)(p * 100));

            try
            {
                int minLen = (int)numMinLen.Value;
                _allStringData = await Scanner.ExtractStringsAsync(file, minLen, progress);
                
                // Initial: No filter, so filtered = all
                _filteredStringData = new List<StringResult>(_allStringData);
                
                lstStrings.VirtualListSize = _filteredStringData.Count;
                lblStatus.Text = $"Found {_allStringData.Count} strings.";
                lblStringsStatus.Text = $"Found {_allStringData.Count} strings.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
            finally
            {
                btnStringsScan.Enabled = true;
                progressBar.Value = 0;
            }
        }

        private void TxtStringsFilter_TextChanged(object? sender, EventArgs e)
        {
            ApplyStringsFilter();
        }

        private void ApplyStringsFilter()
        {
            string filter = txtStringsFilter.Text.ToLower();
            if (string.IsNullOrWhiteSpace(filter))
            {
                _filteredStringData = new List<StringResult>(_allStringData);
            }
            else
            {
                _filteredStringData = _allStringData
                    .Where(s => s.Text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            lstStrings.VirtualListSize = _filteredStringData.Count;
            lstStrings.Invalidate(); // Force redraw
            lblStringsStatus.Text = $"Showing {_filteredStringData.Count} / {_allStringData.Count}";
        }

        private void LstStrings_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (_filteredStringData == null || e.ItemIndex >= _filteredStringData.Count) return;
            var item = _filteredStringData[e.ItemIndex];
            e.Item = new ListViewItem(item.Offset.ToString());
            e.Item.SubItems.Add("0x" + item.Offset.ToString("X"));
            e.Item.SubItems.Add(item.Text);
        }

        private void BtnSaveStrings_Click(object? sender, EventArgs e)
        {
            // Save FILTERED list, so user can export what they see
            if (_filteredStringData.Count == 0)
            {
                MessageBox.Show("No strings to save.");
                return;
            }

            using (var sfd = new SaveFileDialog { Filter = "Text Files|*.txt", FileName = "strings_dump.txt" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        using (var sw = new StreamWriter(sfd.FileName))
                        {
                            sw.WriteLine($"Strings Dump for: {txtInputFile.Text}");
                            sw.WriteLine($"Filter: {(string.IsNullOrWhiteSpace(txtStringsFilter.Text) ? "[None]" : txtStringsFilter.Text)}");
                            sw.WriteLine($"Count: {_filteredStringData.Count}");
                            sw.WriteLine("--------------------------------------------------");
                            
                            foreach (var s in _filteredStringData)
                            {
                                sw.WriteLine($"{s.Offset}\t0x{s.Offset:X}\t{s.Text}");
                            }
                        }
                        MessageBox.Show("Strings saved successfully.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error saving: " + ex.Message);
                    }
                }
            }
        }
    }
}
