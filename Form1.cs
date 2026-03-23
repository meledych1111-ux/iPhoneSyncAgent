using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

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
                    // Читаем заголовки
                    string headers = "";
                    byte[] singleByte = new byte[1];
                    bool headersComplete = false;
                    
                    while (!headersComplete)
                    {
                        byte[] lineBuffer = new byte[4096];
                        int lineLength = 0;
                        int lastByte = 0;
                        
                        while (true)
                        {
                            int bytesRead = await stream.ReadAsync(singleByte, 0, 1);
                            if (bytesRead == 0) break;
                            
                            if (singleByte[0] == '\n')
                            {
                                if (lastByte == '\r')
                                {
                                    lineLength--;
                                    string line = Encoding.UTF8.GetString(lineBuffer, 0, lineLength);
                                    if (string.IsNullOrEmpty(line))
                                    {
                                        headersComplete = true;
                                        break;
                                    }
                                    headers += line + "\r\n";
                                    break;
                                }
                            }
                            else
                            {
                                lineBuffer[lineLength] = singleByte[0];
                                lineLength++;
                            }
                            lastByte = singleByte[0];
                        }
                        if (headersComplete) break;
                    }
                    
                    // Разбираем заголовки
                    string? fileName = null;
                    string? fileDate = null;
                    long fileSize = 0;
                    
                    var lines = headers.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("X-File-Name:"))
                            fileName = line.Substring(12).Trim();
                        else if (line.StartsWith("X-File-Date:"))
                            fileDate = line.Substring(12).Trim();
                        else if (line.StartsWith("X-File-Size:"))
                            long.TryParse(line.Substring(12).Trim(), out fileSize);
                    }
                    
                    if (string.IsNullOrEmpty(fileName))
                    {
                        Log("❌ Ошибка: не указано имя файла");
                        return;
                    }
                    
                    Log($"📁 Получен файл: {fileName}, ожидается: {FormatBytes(fileSize)}");
                    
                    // Создаём папку
                    DateTime photoDate = DateTime.TryParse(fileDate, out var date) ? date : DateTime.Now;
                    string dateFolder = photoDate.ToString("yyyy-MM-dd");
                    string fullPath = Path.Combine(saveFolder, dateFolder);
                    Directory.CreateDirectory(fullPath);
                    
                    // Имя файла с датой
                    string dateTimeStr = photoDate.ToString("yyyy-MM-dd_HH-mm-ss");
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);
                    string newFileName = $"{dateTimeStr}_{nameWithoutExt}{ext}";
                    string filePath = Path.Combine(fullPath, newFileName);
                    
                    // Уникальность имени
                    int counter = 1;
                    while (File.Exists(filePath))
                    {
                        newFileName = $"{dateTimeStr}_{nameWithoutExt}_{counter}{ext}";
                        filePath = Path.Combine(fullPath, newFileName);
                        counter++;
                    }
                    
                    // Сохраняем файл
                    using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                    {
                        long totalRead = 0;
                        byte[] buffer = new byte[8192];
                        int bytesRead;
                        
                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;
                        }
                        
                        Log($"📥 Сохранён: {newFileName} ({FormatBytes(totalRead)}) -> {fullPath}");
                    }
                    
                    // Отправляем ответ
                    string responseText = "OK";
                    byte[] response = Encoding.UTF8.GetBytes(responseText);
                    await stream.WriteAsync(response, 0, response.Length);
                    await stream.FlushAsync();
                    Log("✅ Ответ отправлен клиенту");
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка: {ex.Message}");
            }
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
            string[] sizes = { "Б", "КБ", "МБ", "ГБ" };
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
            System.Diagnostics.Process.Start("explorer.exe", saveFolder);
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
