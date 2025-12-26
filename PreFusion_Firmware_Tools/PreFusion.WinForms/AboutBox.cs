using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace Ext2Read.WinForms
{
    public class AboutBox : Form
    {
        public AboutBox()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "About PreFusion Firmware Tools";
            this.Size = new System.Drawing.Size(450, 350);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;

            int y = 20;

            // Title
            var lblTitle = new Label
            {
                Text = "PreFusion Firmware Tools",
                Font = new System.Drawing.Font("Segoe UI", 16F, System.Drawing.FontStyle.Bold),
                AutoSize = true,
                Location = new System.Drawing.Point(20, y)
            };
            this.Controls.Add(lblTitle);
            y += 40;

            // Version
            var lblVersion = new Label
            {
                Text = "Version 1.0.0",
                Font = new System.Drawing.Font("Segoe UI", 10F),
                AutoSize = true,
                Location = new System.Drawing.Point(22, y)
            };
            this.Controls.Add(lblVersion);
            y += 30;

            // Author
            var lblAuthor = new Label
            {
                Text = "Ported & Developed by: Eliminater74",
                Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold),
                AutoSize = true,
                Location = new System.Drawing.Point(22, y)
            };
            this.Controls.Add(lblAuthor);
            y += 25;

            // GitHub Link
            var linkGithub = new LinkLabel
            {
                Text = "https://github.com/Eliminater74/ext2read",
                AutoSize = true,
                Location = new System.Drawing.Point(22, y)
            };
            linkGithub.LinkClicked += (s, e) => OpenUrl("https://github.com/Eliminater74/ext2read");
            this.Controls.Add(linkGithub);
            y += 40;

            // Divider
            var lblDivider = new Label
            {
                Text = "__________________________________________________",
                AutoSize = true,
                Location = new System.Drawing.Point(20, y - 15),
                ForeColor = System.Drawing.Color.Gray
            };
            this.Controls.Add(lblDivider);
            y += 20;

            // Original Credits
            var lblOriginal = new Label
            {
                Text = "Based on original work by:",
                Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular),
                AutoSize = true,
                Location = new System.Drawing.Point(22, y)
            };
            this.Controls.Add(lblOriginal);
            y += 20;

            var lblOrigAuthor = new Label
            {
                Text = "Manish Regmi (mregmi)",
                Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold),
                AutoSize = true,
                Location = new System.Drawing.Point(30, y)
            };
            this.Controls.Add(lblOrigAuthor);
            y += 20;

            // Original Link
            var linkOrigGithub = new LinkLabel
            {
                Text = "https://github.com/mregmi/ext2read",
                AutoSize = true,
                Location = new System.Drawing.Point(30, y)
            };
            linkOrigGithub.LinkClicked += (s, e) => OpenUrl("https://github.com/mregmi/ext2read");
            this.Controls.Add(linkOrigGithub);
            y += 30;

            // Contributors
            var lblContributors = new Label
            {
                Text = "Original Contributors:",
                Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Underline),
                AutoSize = true,
                Location = new System.Drawing.Point(22, y)
            };
            this.Controls.Add(lblContributors);
            y += 20;

            var lblContribNames = new Label
            {
                Text = "- darkdragon-001\n- hackedd (Paul)\n- xunzhaoderen (sen chai)",
                AutoSize = true,
                Location = new System.Drawing.Point(30, y)
            };
            this.Controls.Add(lblContribNames);
            y += 60;

            // Close Button
            var btnClose = new Button
            {
                Text = "OK",
                Location = new System.Drawing.Point(340, 270),
                Width = 80
            };
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);
            this.AcceptButton = btnClose;
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
