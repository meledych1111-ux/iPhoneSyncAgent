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
                saveFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "iPhoneSync"
                );
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
                Log($"✅ Сервер запущен на порту {currentPort}");
                Log($"📁 Папка сохранения: {saveFolder}");
                
                _ = Task.Run(() => HandleClientsAsync());
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка запуска: {ex.Message}");
            }
        }

        private void StopServer()
        {
            if (!isRunning) return;
            
            tcpListener?.Stop();
            isRunning = false;
            UpdateUI(false);
            Log("⏹ Сервер остановлен");
        }

        private void UpdateUI(bool running)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateUI(running)));
                return;
            }
            
            btnStart.Enabled = !running;
            btnStop.Enabled = running;
            lblStatus.Text = running ? "✅ Статус: Работает" : "⭕ Статус: Остановлен";
            lblStatus.ForeColor = running ? Color.Green : Color.Red;
            cmbPort.Enabled = !running;
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
                catch (Exception ex)
                {
                    if (isRunning)
                        Log($"Ошибка приема клиента: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    // 1. Читаем запрос
                    byte[] buffer = new byte[8192];
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) return;

                    string requestData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    string[] lines = requestData.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    if (lines.Length == 0) return;

                    string firstLine = lines[0]; // Например: "POST /upload HTTP/1.1"
                    string[] parts = firstLine.Split(' ');
                    if (parts.Length < 2) return;

                    string method = parts[0];
                    string path = parts[1];

                    // 2. Обработка CORS (важно для iPhone)
                    if (method == "OPTIONS")
                    {
                        await SendResponseAsync(stream, "204 No Content", "", true);
                        return;
                    }

                    // 3. Маршрутизация
                    if (path == "/status" && method == "GET")
                    {
                        string json = $"{{\"status\":\"ok\", \"port\":{currentPort}}}";
                        await SendResponseAsync(stream, "200 OK", json, true, "application/json");
                    }
                    else if (path == "/upload" && method == "POST")
                    {
                        await HandleFileUpload(stream, lines, buffer, bytesRead);
                    }
                    else
                    {
                        // Пытаемся отдать статические файлы (index.html, manifest.json и т.д.)
                        string fileName = path == "/" ? "index.html" : path.TrimStart('/');
                        if (File.Exists(fileName))
                        {
                            byte[] fileBytes = File.ReadAllBytes(fileName);
                            string contentType = GetContentType(fileName);
                            await SendRawResponseAsync(stream, "200 OK", fileBytes, contentType);
                        }
                        else
                        {
                            await SendResponseAsync(stream, "404 Not Found", "Not Found", true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка обработки запроса: {ex.Message}");
            }
        }

        private async Task HandleFileUpload(NetworkStream stream, string[] lines, byte[] initialBuffer, int initialBytesRead)
        {
            try
            {
                Dictionary<string, string> headers = new Dictionary<string, string>();
                int headerEndIndex = 0;
                
                // Ищем конец заголовков в буфере
                string rawContent = Encoding.UTF8.GetString(initialBuffer, 0, initialBytesRead);
                headerEndIndex = rawContent.IndexOf("\r\n\r\n");
                if (headerEndIndex == -1) return;
                headerEndIndex += 4;

                foreach (var line in lines.Skip(1))
                {
                    if (string.IsNullOrEmpty(line)) break;
                    int colon = line.IndexOf(':');
                    if (colon > 0)
                        headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
                }

                headers.TryGetValue("X-File-Name", out string? fileName);
                headers.TryGetValue("X-File-Date", out string? fileDate);
                headers.TryGetValue("X-File-Size", out string? fileSizeStr);
                long.TryParse(fileSizeStr, out long fileSize);

                if (string.IsNullOrEmpty(fileName)) fileName = "upload_" + Guid.NewGuid().ToString().Substring(0, 8);
                fileName = Uri.UnescapeDataString(fileName);

                Log($"📥 Начало загрузки: {fileName} ({FormatBytes(fileSize)})");

                // Формируем имя файла по вашему запросу: Дата_Время_УникальныйID
                DateTime now = DateTime.Now;
                string dateFolder = now.ToString("yyyy-MM-dd");
                string fullPath = Path.Combine(saveFolder, dateFolder);
                Directory.CreateDirectory(fullPath);

                string uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
                string ext = Path.GetExtension(fileName);
                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string newFileName = $"{now:yyyy-MM-dd_HH-mm-ss}_{uniqueId}{ext}";
                string filePath = Path.Combine(fullPath, newFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    // Дописываем остаток из первого буфера
                    int remainingInInitial = initialBytesRead - headerEndIndex;
                    if (remainingInInitial > 0)
                    {
                        await fileStream.WriteAsync(initialBuffer, headerEndIndex, remainingInInitial);
                    }

                    long totalRead = remainingInInitial;
                    byte[] buffer = new byte[65536];
                    int bytesRead;

                    while (totalRead < fileSize && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;
                    }
                }

                Log($"✅ Файл сохранен: {newFileName}");
                await SendResponseAsync(stream, "200 OK", "OK", true);
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка при сохранении: {ex.Message}");
                await SendResponseAsync(stream, "500 Internal Server Error", ex.Message, true);
            }
        }

        private async Task SendResponseAsync(NetworkStream stream, string status, string content, bool addCors, string contentType = "text/plain")
        {
            byte[] body = Encoding.UTF8.GetBytes(content);
            await SendRawResponseAsync(stream, status, body, contentType, addCors);
        }

        private async Task SendRawResponseAsync(NetworkStream stream, string status, byte[] body, string contentType, bool addCors = true)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"HTTP/1.1 {status}\r\n");
            sb.Append($"Content-Type: {contentType}\r\n");
            sb.Append($"Content-Length: {body.Length}\r\n");
            if (addCors)
            {
                sb.Append("Access-Control-Allow-Origin: *\r\n");
                sb.Append("Access-Control-Allow-Methods: POST, GET, OPTIONS\r\n");
                sb.Append("Access-Control-Allow-Headers: X-File-Name, X-File-Date, X-File-Size, Content-Type\r\n");
            }
            sb.Append("Connection: close\r\n");
            sb.Append("\r\n");

            byte[] header = Encoding.UTF8.GetBytes(sb.ToString());
            await stream.WriteAsync(header, 0, header.Length);
            await stream.WriteAsync(body, 0, body.Length);
            await stream.FlushAsync();
        }

        private string GetContentType(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLower();
            return ext switch
            {
                ".html" => "text/html",
                ".json" => "application/json",
                ".js" => "application/javascript",
                ".css" => "text/css",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                _ => "application/octet-stream"
            };
        }

        private void Log(string message)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action(() => Log(message)));
                return;
            }
            
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            txtLog.AppendText($"[{timestamp}] {message}{Environment.NewLine}");
            txtLog.SelectionStart = txtLog.Text.Length;
            txtLog.ScrollToCaret();
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { \"Б\", \"КБ\", \"МБ\", \"ГБ\" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void BtnBrowse_Click(object? sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = saveFolder;
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    saveFolder = dialog.SelectedPath;
                    txtSavePath.Text = saveFolder;
                    Properties.Settings.Default.SaveFolder = saveFolder;
                    Properties.Settings.Default.Save();
                    Log($"Папка сохранения изменена: {saveFolder}");
                }
            }
        }

        private void CmbPort_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (int.TryParse(cmbPort.SelectedItem?.ToString(), out int port))
            {
                currentPort = port;
                Properties.Settings.Default.Port = port;
                Properties.Settings.Default.Save();
                
                if (isRunning)
                {
                    StopServer();
                    _ = StartServerAsync();
                }
            }
        }

        private void OpenSaveFolder()
        {
            System.Diagnostics.Process.Start(\"explorer.exe\", saveFolder);
        }

        private void ShowWindow()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(ShowWindow));
                return;
            }
            
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.Activate();
        }

        private void CleanupAndExit()
        {
            if (isRunning)
            {
                StopServer();
                System.Threading.Thread.Sleep(500);
            }
            notifyIcon.Visible = false;
            Application.Exit();
            Environment.Exit(0);
        }

        private void Form1_Resize(object? sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            base.OnFormClosing(e);
        }
    }
}
