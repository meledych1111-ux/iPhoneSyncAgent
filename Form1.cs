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
            {
                saveFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "iPhoneSync");
            }
            txtSavePath.Text = saveFolder;
            Directory.CreateDirectory(saveFolder);
            
            currentPort = Properties.Settings.Default.Port;
            if (currentPort == 0) currentPort = 15000;
            cmbPort.SelectedItem = currentPort.ToString();
        }

        private void InitializeTray()
        {
            contextMenuStrip = new ContextMenuStrip();
            contextMenuStrip.Items.Add("📂 Открыть", null, (s, e) => ShowWindow());
            contextMenuStrip.Items.Add("📁 Папка сохранения", null, (s, e) => OpenSaveFolder());
            contextMenuStrip.Items.Add("-");
            contextMenuStrip.Items.Add("▶ Запустить", null, async (s, e) => await StartServerAsync());
            contextMenuStrip.Items.Add("⏹ Остановить", null, (s, e) => StopServer());
            contextMenuStrip.Items.Add("-");
            contextMenuStrip.Items.Add("❌ Выход", null, (s, e) => CleanupAndExit());
            
            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = SystemIcons.Application;
            notifyIcon.Text = "iPhoneSync Agent";
            notifyIcon.ContextMenuStrip = contextMenuStrip;
            notifyIcon.DoubleClick += (s, e) => ShowWindow();
            notifyIcon.Visible = true;
        }

        private async Task StartServerAsync()
        {
            if (isRunning) return;
            try
            {
                tcpListener = new TcpListener(IPAddress.Any, currentPort);
                tcpListener.Start();
                isRunning = true;
                UpdateUI(true);
                Log($"✅ Сервер активен: порт {currentPort}");
                Log($"📁 Куда сохраняем: {saveFolder}");
                _ = Task.Run(() => HandleClientsAsync());
            }
            catch (Exception ex) { Log($"❌ Ошибка запуска: {ex.Message}"); }
        }

        private void StopServer()
        {
            if (!isRunning) return;
            tcpListener?.Stop();
            isRunning = false;
            UpdateUI(false);
            Log("⏹ Сервер остановлен");
        }

        private async Task HandleClientsAsync()
        {
            while (isRunning && tcpListener != null)
            {
                try
                {
                    var client = await tcpListener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClientAsync(client));
                }
                catch { }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    byte[] buffer = new byte[16384];
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) return;

                    string requestData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    string[] lines = requestData.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    if (lines.Length == 0) return;

                    string[] parts = lines[0].Split(' ');
                    if (parts.Length < 2) return;

                    string method = parts[0];
                    string path = parts[1];

                    // Поддержка CORS для iPhone
                    if (method == "OPTIONS")
                    {
                        await SendResponseAsync(stream, "204 No Content", "", true);
                        return;
                    }

                    // Проверка статуса (API)
                    if (path == "/status" || path == "/status/")
                    {
                        await SendResponseAsync(stream, "200 OK", "{\"status\":\"ok\"}", true, "application/json");
                        return;
                    }

                    // ПРИЕМ ЛЮБЫХ ФАЙЛОВ (Фото, Документы, Музыка)
                    if (path.StartsWith("/upload") && method == "POST")
                    {
                        await HandleFileUpload(stream, lines, buffer, bytesRead);
                        return;
                    }

                    // ОТДАЧА ИНТЕРФЕЙСА PWA (чтобы не было 404)
                    string requestedFile = path == "/" ? "index.html" : path.TrimStart('/');
                    string fullFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, requestedFile);
                    
                    // Если не нашли в bin, смотрим в корне (для отладки)
                    if (!File.Exists(fullFilePath))
                        fullFilePath = Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.FullName, requestedFile);

                    if (File.Exists(fullFilePath))
                    {
                        byte[] fileBytes = File.ReadAllBytes(fullFilePath);
                        await SendRawResponseAsync(stream, "200 OK", fileBytes, GetContentType(fullFilePath));
                    }
                    else
                    {
                        await SendResponseAsync(stream, "404 Not Found", "File Not Found", true);
                    }
                }
            }
            catch (Exception ex) { Log($"Ошибка: {ex.Message}"); }
        }

        private async Task HandleFileUpload(NetworkStream stream, string[] lines, byte[] initialBuffer, int initialBytesRead)
        {
            //StringComparer.OrdinalIgnoreCase делает поиск X-File-Name независимым от регистра
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string rawStr = Encoding.UTF8.GetString(initialBuffer, 0, initialBytesRead);
            int headerEndIndex = rawStr.IndexOf("\r\n\r\n") + 4;

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrEmpty(line)) break;
                int colon = line.IndexOf(':');
                if (colon > 0) headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
            }

            headers.TryGetValue("X-File-Name", out string? fileName);
            headers.TryGetValue("X-File-Size", out string? fileSizeStr);
            long.TryParse(fileSizeStr, out long fileSize);

            if (string.IsNullOrEmpty(fileName)) {
                Log("❌ Ошибка: iPhone не передал имя файла");
                await SendResponseAsync(stream, "400 Bad Request", "No Filename", true);
                return;
            }

            fileName = Uri.UnescapeDataString(fileName);
            DateTime now = DateTime.Now;
            
            // Создаем папку по дате
            string dateFolder = now.ToString("yyyy-MM-dd");
            string targetPath = Path.Combine(saveFolder, dateFolder);
            Directory.CreateDirectory(targetPath);

            // ИМЯ ФАЙЛА: Дата_Время_УникальныйID.расширение
            string uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
            string extension = Path.GetExtension(fileName);
            string newFileName = $"{now:yyyy-MM-dd_HH-mm-ss}_{uniqueId}{extension}";
            string filePath = Path.Combine(targetPath, newFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                int remaining = initialBytesRead - headerEndIndex;
                if (remaining > 0) await fileStream.WriteAsync(initialBuffer, headerEndIndex, remaining);

                long totalRead = remaining;
                byte[] buffer = new byte[65536];
                while (totalRead < fileSize)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (read == 0) break;
                    await fileStream.WriteAsync(buffer, 0, read);
                    totalRead += read;
                }
            }

            Log($"📥 Принят файл: {newFileName} ({FormatBytes(fileSize)})");
            await SendResponseAsync(stream, "200 OK", "OK", true);
        }

        private async Task SendResponseAsync(NetworkStream stream, string status, string content, bool addCors, string contentType = "text/plain")
        {
            byte[] body = Encoding.UTF8.GetBytes(content);
            await SendRawResponseAsync(stream, status, body, contentType, addCors);
        }

        private async Task SendRawResponseAsync(NetworkStream stream, string status, byte[] body, string contentType, bool addCors = true)
        {
            string head = $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n" +
                          (addCors ? "Access-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: *\r\nAccess-Control-Allow-Headers: *\r\n" : "") +
                          "Connection: close\r\n\r\n";
            byte[] hBytes = Encoding.UTF8.GetBytes(head);
            await stream.WriteAsync(hBytes, 0, hBytes.Length);
            await stream.WriteAsync(body, 0, body.Length);
        }

        private string GetContentType(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            return ext switch { ".html" => "text/html", ".json" => "application/json", ".js" => "application/javascript", ".css" => "text/css", ".png" => "image/png", ".jpg" => "image/jpeg", _ => "application/octet-stream" };
        }

        private void Log(string msg) { if (txtLog.InvokeRequired) { txtLog.Invoke(new Action(() => Log(msg))); return; } txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}"); txtLog.ScrollToCaret(); }
        private void UpdateUI(bool r) { if (InvokeRequired) { Invoke(new Action(() => UpdateUI(r))); return; } btnStart.Enabled = !r; btnStop.Enabled = r; lblStatus.Text = r ? "✅ Статус: Работает" : "⭕ Статус: Остановлен"; lblStatus.ForeColor = r ? Color.Green : Color.Red; cmbPort.Enabled = !r; }
        private void BtnBrowse_Click(object? s, EventArgs e) { using (var d = new FolderBrowserDialog()) { if (d.ShowDialog() == DialogResult.OK) { saveFolder = d.SelectedPath; txtSavePath.Text = saveFolder; } } }
        private void CmbPort_SelectedIndexChanged(object? s, EventArgs e) { if (int.TryParse(cmbPort.SelectedItem?.ToString(), out int p)) currentPort = p; }
        private void Form1_Resize(object? s, EventArgs e) { if (this.WindowState == FormWindowState.Minimized) this.Hide(); }
        private string FormatBytes(long b) { string[] s = { "Б", "КБ", "МБ", "ГБ" }; double l = b; int o = 0; while (l >= 1024 && o < s.Length - 1) { o++; l /= 1024; } return $"{l:0.##} {s[o]}"; }
        private void ShowWindow() { this.Show(); this.WindowState = FormWindowState.Normal; this.Activate(); }
        private void OpenSaveFolder() { System.Diagnostics.Process.Start("explorer.exe", saveFolder); }
        private void CleanupAndExit() { if (isRunning) StopServer(); Application.Exit(); Environment.Exit(0); }
    }
}
