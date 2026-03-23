namespace iPhoneSyncAgent
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.TextBox txtSavePath;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Button btnBrowse;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.ComboBox cmbPort;
        private System.Windows.Forms.Label lblPortLabel;
        private System.Windows.Forms.Label lblPathLabel;
        private System.Windows.Forms.Label lblLogLabel;
        private System.Windows.Forms.NotifyIcon notifyIcon;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.txtLog = new System.Windows.Forms.TextBox();
            this.txtSavePath = new System.Windows.Forms.TextBox();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnBrowse = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.cmbPort = new System.Windows.Forms.ComboBox();
            this.lblPortLabel = new System.Windows.Forms.Label();
            this.lblPathLabel = new System.Windows.Forms.Label();
            this.lblLogLabel = new System.Windows.Forms.Label();
            this.notifyIcon = new System.Windows.Forms.NotifyIcon(this.components);
            this.contextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.SuspendLayout();
            
            this.txtLog.BackColor = System.Drawing.Color.Black;
            this.txtLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtLog.ForeColor = System.Drawing.Color.LightGreen;
            this.txtLog.Location = new System.Drawing.Point(12, 220);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.Size = new System.Drawing.Size(610, 230);
            
            this.txtSavePath.BackColor = System.Drawing.Color.White;
            this.txtSavePath.Location = new System.Drawing.Point(12, 35);
            this.txtSavePath.Name = "txtSavePath";
            this.txtSavePath.ReadOnly = true;
            this.txtSavePath.Size = new System.Drawing.Size(450, 23);
            
            this.btnStart.BackColor = System.Drawing.Color.LightGreen;
            this.btnStart.Location = new System.Drawing.Point(12, 130);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(100, 30);
            this.btnStart.Text = "▶ Запустить";
            
            this.btnStop.BackColor = System.Drawing.Color.LightCoral;
            this.btnStop.Enabled = false;
            this.btnStop.Location = new System.Drawing.Point(118, 130);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(100, 30);
            this.btnStop.Text = "⏹ Остановить";
            
            this.btnBrowse.Location = new System.Drawing.Point(468, 33);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.Size = new System.Drawing.Size(75, 25);
            this.btnBrowse.Text = "Обзор...";
            
            this.lblStatus.AutoSize = true;
            this.lblStatus.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblStatus.Location = new System.Drawing.Point(12, 165);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(115, 15);
            this.lblStatus.Text = "Статус: Остановлен";
            
            this.cmbPort.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbPort.Items.AddRange(new object[] { "15000", "8080", "8081", "9000", "8888" });
            this.cmbPort.Location = new System.Drawing.Point(12, 92);
            this.cmbPort.Name = "cmbPort";
            this.cmbPort.Size = new System.Drawing.Size(100, 23);
            
            this.lblPortLabel.AutoSize = true;
            this.lblPortLabel.Location = new System.Drawing.Point(12, 74);
            this.lblPortLabel.Text = "Порт:";
            
            this.lblPathLabel.AutoSize = true;
            this.lblPathLabel.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblPathLabel.Location = new System.Drawing.Point(12, 17);
            this.lblPathLabel.Text = "Папка сохранения:";
            
            this.lblLogLabel.AutoSize = true;
            this.lblLogLabel.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblLogLabel.Location = new System.Drawing.Point(12, 202);
            this.lblLogLabel.Text = "Журнал событий:";
            
            this.ClientSize = new System.Drawing.Size(634, 461);
            this.Controls.Add(this.lblLogLabel);
            this.Controls.Add(this.lblPathLabel);
            this.Controls.Add(this.lblPortLabel);
            this.Controls.Add(this.cmbPort);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.btnBrowse);
            this.Controls.Add(this.btnStop);
            this.Controls.Add(this.btnStart);
            this.Controls.Add(this.txtSavePath);
            this.Controls.Add(this.txtLog);
            this.Name = "Form1";
            this.Text = "iPhoneSync Agent";
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
