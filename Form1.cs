using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace iPhoneSyncAgent
{
    public partial class Form1 : Form
    {
        private TcpListener? tcpListener;
        private string saveFolder = string.Empty;
        private bool isRunning = false;
        private int currentPort = 15000;

        public Form1()
        {
            InitializeComponent();
            LoadSettings();
            InitializeTray();
            this.Resize += Form1_Resize;
            
            btnStart.Click += async (s, e) => await StartServerAsync();
            btnStop.Click += (s, e) => StopServer();
            btnBrowse.Click += BtnBrowse_Click;
            cmbPort.SelectedIndexChanged += CmbPort_SelectedIndexChanged;
        }

        private void LoadSettings()
        {
            saveFolder = Properties.Settings.Default.SaveFolder;
            if (string.IsNullOrEmpty(saveFolder))
                saveFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "iPhoneSync");
            txtSavePath.Text = saveFolder;
            Directory.CreateDirectory(saveFolder);
            currentPort = Properties.Settings.Default.Port == 0 ? 15000 : Properties.Settings.Default.Port;
            cmbPort.SelectedItem = currentPort.ToString();
        }

        private void InitializeTray() {
            contextMenuStrip = new ContextMenuStrip();
            contextMenuStrip.Items.Add("📂 Открыть", null, (s, e) => ShowWindow());
            contextMenuStrip.Items.Add("❌ Выход", null, (s, e) => CleanupAndExit());
            notifyIcon = new NotifyIcon { Icon = SystemIcons.Application, Text = "iPhoneSync Agent", ContextMenuStrip = contextMenuStrip, Visible = true };
        }

        private async Task StartServerAsync()
        {
            if (isRunning) return;
            try {
                tcpListener = new TcpListener(IPAddress.Any, currentPort);
                tcpListener.Start();
                isRunning = true;
                UpdateUI(true);
                Log($"✅ Сервер запущен на порту {currentPort}");
                _ = Task.Run(HandleClientsAsync);
            } catch (Exception ex) { Log($"❌ Ошибка запуска: {ex.Message}"); }
        }

        private void StopServer() { isRunning = false; tcpListener?.Stop(); UpdateUI(false); Log("⏹ Сервер остановлен"); }

        private async Task HandleClientsAsync()
        {
            while (isRunning) {
                try {
                    var client = await tcpListener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClientAsync(client));
                } catch { }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            try {
                using (client)
                using (var stream = client.GetStream()) {
                    byte[] buffer = new byte[8192];
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) return;

                    string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    string[] lines = request.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    string[] parts = lines[0].Split(' ');
                    if (parts.Length < 2) return;

                    string method = parts[0], path = parts[1];
                    
                    // ЛОГИРУЕМ ЗАПРОС (чтобы понять, что хочет iPhone)
                    Log($"🌐 Запрос: {method} {path}");

                    if (method == "OPTIONS") {
                        await SendResponseAsync(stream, "204 No Content", "", true);
                        return;
                    }

                    // СТРОГОЕ РАЗДЕЛЕНИЕ
                    if (path == "/status") {
                        await SendResponseAsync(stream, "200 OK", "{\"status\":\"ok\"}", true, "application/json");
                    } else if (path.StartsWith("/upload") && method == "POST") {
                        await HandleFileUpload(stream, lines, buffer, bytesRead);
                    } else {
                        // Если зашли по корню (/) или просят файлы PWA
                        string fileName = (path == "/" || string.IsNullOrEmpty(path.Trim('/'))) ? "index.html" : path.TrimStart('/');
                        string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

                        if (File.Exists(fullPath)) {
                            byte[] fileBytes = File.ReadAllBytes(fullPath);
                            await SendRawResponseAsync(stream, "200 OK", fileBytes, GetContentType(fileName));
                        } else {
                            Log($"⚠ Файл не найден в папке программы: {fileName}");
                            await SendResponseAsync(stream, "404 Not Found", "File Not Found. Please put index.html near .exe", true);
                        }
                    }
                }
            } catch { }
        }

        private async Task HandleFileUpload(NetworkStream stream, string[] lines, byte[] initialBuffer, int initialBytesRead)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int headerEnd = Encoding.UTF8.GetString(initialBuffer, 0, initialBytesRead).IndexOf("\r\n\r\n") + 4;
            foreach (var line in lines.Skip(1)) {
                if (string.IsNullOrEmpty(line)) break;
                int colon = line.IndexOf(':');
                if (colon > 0) headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
            }

            headers.TryGetValue("X-File-Name", out string? fileName);
            headers.TryGetValue("X-File-Size", out string? fileSizeStr);
            long.TryParse(fileSizeStr, out long fileSize);

            if (string.IsNullOrEmpty(fileName)) return;
            fileName = Uri.UnescapeDataString(fileName);
            string dir = Path.Combine(saveFolder, DateTime.Now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(dir);
            string finalPath = Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid().ToString().Substring(0,8)}{Path.GetExtension(fileName)}");

            using (var fs = new FileStream(finalPath, FileMode.Create)) {
                int rem = initialBytesRead - headerEnd;
                if (rem > 0) await fs.WriteAsync(initialBuffer, headerEnd, rem);
                long total = rem;
                byte[] buf = new byte[65536];
                while (total < fileSize) {
                    int r = await stream.ReadAsync(buf, 0, buf.Length);
                    if (r == 0) break;
                    await fs.WriteAsync(buf, 0, r);
                    total += r;
                }
            }
            Log($"📥 Принят файл: {Path.GetFileName(finalPath)}");
            await SendResponseAsync(stream, "200 OK", "OK", true);
        }

        private async Task SendResponseAsync(NetworkStream stream, string status, string content, bool cors, string type = "text/plain") {
            byte[] body = Encoding.UTF8.GetBytes(content);
            await SendRawResponseAsync(stream, status, body, type, cors);
        }

        private async Task SendRawResponseAsync(NetworkStream stream, string status, byte[] body, string type, bool cors = true) {
            string h = $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\n" +
                       (cors ? "Access-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: *\r\nAccess-Control-Allow-Headers: *\r\n" : "") +
                       "Connection: close\r\n\r\n";
            byte[] hb = Encoding.UTF8.GetBytes(h);
            await stream.WriteAsync(hb, 0, hb.Length);
            await stream.WriteAsync(body, 0, body.Length);
            await stream.FlushAsync();
        }

        private string GetContentType(string p) { string e = Path.GetExtension(p).ToLower(); return e switch { ".html"=>"text/html", ".json"=>"application/json", ".js"=>"application/javascript", ".css"=>"text/css", _=>"application/octet-stream" }; }
        private void Log(string m) { if (txtLog.InvokeRequired) { txtLog.Invoke(new Action(()=>Log(m))); return; } txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {m}{Environment.NewLine}"); txtLog.ScrollToCaret(); }
        private void UpdateUI(bool r) { if (InvokeRequired) { Invoke(new Action(()=>UpdateUI(r))); return; } btnStart.Enabled = !r; btnStop.Enabled = r; lblStatus.Text = r ? "✅ Статус: Работает" : "⭕ Статус: Остановлен"; lblStatus.ForeColor = r ? Color.Green : Color.Red; }
        private void BtnBrowse_Click(object? s, EventArgs e) { using (var d = new FolderBrowserDialog()) if (d.ShowDialog() == DialogResult.OK) { saveFolder = d.SelectedPath; txtSavePath.Text = saveFolder; } }
        private void CmbPort_SelectedIndexChanged(object? s, EventArgs e) { if (int.TryParse(cmbPort.SelectedItem?.ToString(), out int p)) currentPort = p; }
        private void ShowWindow() { this.Show(); this.WindowState = FormWindowState.Normal; this.Activate(); }
        private void CleanupAndExit() { if (isRunning) StopServer(); Application.Exit(); Environment.Exit(0); }
        private void Form1_Resize(object? s, EventArgs e) { if (this.WindowState == FormWindowState.Minimized) this.Hide(); }
    }
}
