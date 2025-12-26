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

        // Tab 1: Signatures
        private ListView lstResults;
        private Button btnScan;
        private Button btnExtractAll;
        private ContextMenuStrip contextMenu;

        // Tab 2: Entropy
        private Button btnEntropy;
        private Panel pnlEntropy;
        private Label lblEntropyStatus;
        private List<EntropyResult> _entropyData;

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

            btnScan = new Button { Text = "Scan Signatures", Location = new Point(10, 10), Width = 120 };
            btnScan.Click += BtnScan_Click;
            tabSig.Controls.Add(btnScan);

            btnExtractAll = new Button { Text = "Extract All Found", Location = new Point(140, 10), Width = 120 };
            btnExtractAll.Click += BtnExtractAll_Click;
            tabSig.Controls.Add(btnExtractAll);

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
                BackColor = Color.White
            };
            pnlEntropy.Paint += PnlEntropy_Paint;
            pnlEntropy.Resize += (s, e) => pnlEntropy.Invalidate(); // Redraw on resize
            tabEntropy.Controls.Add(pnlEntropy);

            // Footer
            progressBar = new ProgressBar { Location = new Point(10, 540), Width = 560, Height = 15 };
            this.Controls.Add(progressBar);

            lblStatus = new Label { Text = "Ready", Location = new Point(580, 540), AutoSize = true };
            this.Controls.Add(lblStatus);
        }

        // --- Event Handlers ---

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
            lblStatus.Text = "Scanning signatures...";

            var progress = new Progress<float>(p => progressBar.Value = (int)(p * 100));

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

        private async void BtnEntropy_Click(object sender, EventArgs e)
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

        private void PnlEntropy_Paint(object sender, PaintEventArgs e)
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

        private async void BtnExtractAll_Click(object sender, EventArgs e)
        {
            if (lstResults.Items.Count == 0) return;

            using (var fbd = new FolderBrowserDialog { Description = "Select Output Directory" })
            {
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    btnExtractAll.Enabled = false;
                    lblStatus.Text = "Extracting all...";
                    int count = 0;

                    try
                    {
                        using (var fs = new FileStream(txtInputFile.Text, FileMode.Open, FileAccess.Read))
                        {
                            foreach (ListViewItem item in lstResults.Items)
                            {
                                var res = (ScanResult)item.Tag;
                                string filename = $"0x{res.Offset:X}_{res.Description.Split(' ')[0]}.bin";
                                // Clean filename
                                foreach (char c in Path.GetInvalidFileNameChars()) filename = filename.Replace(c, '_');

                                string outPath = Path.Combine(fbd.SelectedPath, filename);

                                // Extract Logic: Dump from Offset to WHERE?
                                // BinWalk extracts known types by parsing header (e.g. ZIP size).
                                // We don't have parsers for size yet.
                                // Default behavior: Dump until EOF (or reasonable limit?).
                                // Users often want "carving".
                                // For now, let's dump until EOF, but maybe in a loop this is huge duplicative data (Matryoshka).
                                // This "Extract All" is dangerous if we dump 1GB 50 times.
                                // Let's dump a FIXED amount (e.g. preview) or ask user?
                                // Official BinWalk extracts properly. 
                                // Since we lack parsers, let's warn the user or just implement single extraction correctly first.
                                // But I can't leave "Extract All" broken.
                                // I will revert to "Extract Item" logic in loop: Dump until EOF (warning: disk space!).
                                // Wait, simple workaround: Dump until NEXT signature offset?
                                // That's a common heuristic.

                                long nextOffset = fs.Length;
                                if (item.Index < lstResults.Items.Count - 1)
                                {
                                    var nextRes = (ScanResult)lstResults.Items[item.Index + 1].Tag;
                                    nextOffset = nextRes.Offset;
                                }

                                long size = nextOffset - res.Offset;
                                if (size <= 0) continue; // Should not happen 

                                byte[] buffer = new byte[8192];
                                long remaining = size;
                                fs.Seek(res.Offset, SeekOrigin.Begin);

                                using (var outFs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
                                {
                                    while (remaining > 0)
                                    {
                                        int toRead = (int)Math.Min(buffer.Length, remaining);
                                        int read = await fs.ReadAsync(buffer, 0, toRead);
                                        if (read == 0) break;
                                        await outFs.WriteAsync(buffer, 0, read);
                                        remaining -= read;
                                    }
                                }
                                count++;
                                lblStatus.Text = $"Extracted {count}/{lstResults.Items.Count}";
                            }
                        }
                        MessageBox.Show($"Extracted {count} items.");
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
    }
}
