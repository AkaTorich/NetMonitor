using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RDPLoginMonitor
{
    public partial class MainForm : Form
    {
        private RDPMonitor _monitor;
        private NetworkMonitor _networkMonitor;
        private BindingList<RDPFailedLogin> _loginAttempts;
        private BindingList<NetworkDevice> _networkDevices;
        private Timer _statsTimer;
        private Timer _networkTimer;
        private Timer _autoScanTimer;

        public MainForm()
        {
            InitializeComponent();
            InitializeMonitors();
        }

        private void InitializeMonitors()
        {
            _loginAttempts = new BindingList<RDPFailedLogin>();
            _networkDevices = new BindingList<NetworkDevice>();

            logGrid.DataSource = _loginAttempts;
            networkGrid.DataSource = _networkDevices;

            // Инициализация RDP монитора
            _monitor = new RDPMonitor();

            _monitor.OnFailedLogin += (login) =>
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(() => AddLoginAttempt(login)));
                }
                else
                {
                    AddLoginAttempt(login);
                }
            };

            _monitor.OnSuspiciousActivity += (key, attempts) =>
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(() => ShowSuspiciousActivity(key, attempts)));
                }
                else
                {
                    ShowSuspiciousActivity(key, attempts);
                }
            };

            _monitor.OnLogMessage += (message, level) =>
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(() => AddLogMessage(message, level)));
                }
                else
                {
                    AddLogMessage(message, level);
                }
            };

            // Инициализация сетевого монитора
            _networkMonitor = new NetworkMonitor();

            _networkMonitor.OnNewDeviceDetected += (device) =>
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(() => HandleNewDevice(device)));
                }
                else
                {
                    HandleNewDevice(device);
                }
            };

            _networkMonitor.OnDeviceStatusChanged += (device) =>
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(() => UpdateDeviceStatus(device)));
                }
                else
                {
                    UpdateDeviceStatus(device);
                }
            };

            // Таймеры
            _statsTimer = new Timer { Interval = 5000 };
            _statsTimer.Tick += StatsTimer_Tick;

            _networkTimer = new Timer { Interval = 10000 };
            _networkTimer.Tick += NetworkTimer_Tick;

            _autoScanTimer = new Timer { Interval = 300000 }; // 5 минут по умолчанию
            _autoScanTimer.Tick += AutoScanTimer_Tick;

            // Подключаем события после создания элементов
            this.Load += MainForm_Load;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // Подключаем события после полной загрузки формы
            if (autoScanIntervalNum != null)
                autoScanIntervalNum.ValueChanged += AutoScanIntervalNum_ValueChanged;

            if (autoScanCheckBox != null)
                autoScanCheckBox.CheckedChanged += AutoScanCheckBox_CheckedChanged;
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            try
            {
                _monitor.MaxFailedAttempts = (int)maxAttemptsNum.Value;
                _monitor.TimeWindow = TimeSpan.FromMinutes((double)timeWindowNum.Value);

                _monitor.StartMonitoring();

                if (networkMonitorCheckBox.Checked)
                {
                    _networkMonitor.StartMonitoring();
                    _networkTimer.Start();

                    // Запуск автосканирования если включено
                    if (autoScanCheckBox?.Checked == true)
                    {
                        _autoScanTimer.Start();
                        AddLogMessage($"Автосканирование запущено с интервалом {autoScanIntervalNum?.Value ?? 300} сек", LogLevel.Info);
                    }
                }

                startButton.Enabled = false;
                stopButton.Enabled = true;
                statusLabel.Text = "Мониторинг активен";
                statusLabel.ForeColor = Color.Green;

                _statsTimer.Start();

                AddLogMessage("Система мониторинга запущена", LogLevel.Info);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Нет прав доступа к журналу событий.\nЗапусти программу от имени администратора.",
                               "Ошибка доступа", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска мониторинга: {ex.Message}",
                               "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            _monitor.StopMonitoring();
            _networkMonitor.StopMonitoring();

            startButton.Enabled = true;
            stopButton.Enabled = false;
            statusLabel.Text = "Мониторинг остановлен";
            statusLabel.ForeColor = Color.Red;

            _statsTimer.Stop();
            _networkTimer.Stop();

            AddLogMessage("Система мониторинга остановлена", LogLevel.Warning);
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            _loginAttempts.Clear();
            _networkDevices.Clear();
            logTextBox.Clear();
            statisticsView.Items.Clear();
            AddLogMessage("Данные очищены", LogLevel.Info);
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt";
                dialog.FileName = $"security_log_{DateTime.Now:yyyyMMdd_HHmmss}";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var lines = new List<string>
                        {
                            "Время,Пользователь,IP адрес,Компьютер,Статус,Тип события,Описание"
                        };

                        foreach (var item in _loginAttempts)
                        {
                            lines.Add($"{item.TimeStamp:yyyy-MM-dd HH:mm:ss},{item.Username},{item.SourceIP},{item.Computer},{item.Status},{item.EventType},\"{item.Description}\"");
                        }

                        lines.Add("");
                        lines.Add("=== СЕТЕВЫЕ УСТРОЙСТВА ===");
                        lines.Add("IP адрес,MAC адрес,Имя хоста,Производитель,Статус,Первое обнаружение,Последняя активность");

                        foreach (var device in _networkDevices)
                        {
                            lines.Add($"{device.IPAddress},{device.MACAddress},{device.Hostname},{device.Vendor},{device.Status},{device.FirstSeen:yyyy-MM-dd HH:mm:ss},{device.LastSeen:yyyy-MM-dd HH:mm:ss}");
                        }

                        File.WriteAllLines(dialog.FileName, lines);
                        MessageBox.Show("Лог сохранен успешно!", "Сохранение", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        AddLogMessage($"Данные экспортированы в {dialog.FileName}", LogLevel.Info);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void AddLoginAttempt(RDPFailedLogin login)
        {
            _loginAttempts.Insert(0, login);

            // Ограничиваем количество записей
            while (_loginAttempts.Count > 1000)
            {
                _loginAttempts.RemoveAt(_loginAttempts.Count - 1);
            }

            // Подсветка строк в зависимости от типа события
            if (logGrid.Rows.Count > 0)
            {
                var row = logGrid.Rows[0];
                switch (login.EventType)
                {
                    case "Неудачный вход":
                        row.DefaultCellStyle.BackColor = Color.LightPink;
                        row.DefaultCellStyle.ForeColor = Color.DarkRed;
                        break;
                    case "Успешный вход":
                        row.DefaultCellStyle.BackColor = Color.LightGreen;
                        row.DefaultCellStyle.ForeColor = Color.DarkGreen;
                        break;
                    case "Подозрительная активность":
                        row.DefaultCellStyle.BackColor = Color.Orange;
                        row.DefaultCellStyle.ForeColor = Color.DarkOrange;
                        break;
                    case "Блокировка аккаунта":
                        row.DefaultCellStyle.BackColor = Color.Red;
                        row.DefaultCellStyle.ForeColor = Color.White;
                        break;
                    default:
                        row.DefaultCellStyle.BackColor = Color.White;
                        row.DefaultCellStyle.ForeColor = Color.Black;
                        break;
                }
            }
        }

        private void AddLogMessage(string message, LogLevel level)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var formattedMessage = $"[{timestamp}] {message}";

            // Устанавливаем цвет в зависимости от уровня
            Color color;
            switch (level)
            {
                case LogLevel.Error:
                    color = Color.Red;
                    break;
                case LogLevel.Warning:
                    color = Color.Orange;
                    break;
                case LogLevel.Success:
                    color = Color.Green;
                    break;
                case LogLevel.Network:
                    color = Color.Blue;
                    break;
                case LogLevel.Security:
                    color = Color.Purple;
                    break;
                default:
                    color = Color.Black;
                    break;
            }

            logTextBox.SelectionStart = logTextBox.TextLength;
            logTextBox.SelectionLength = 0;
            logTextBox.SelectionColor = color;
            logTextBox.AppendText(formattedMessage + Environment.NewLine);
            logTextBox.SelectionColor = logTextBox.ForeColor;
            logTextBox.ScrollToCaret();
        }

        private void HandleNewDevice(NetworkDevice device)
        {
            _networkDevices.Insert(0, device);

            // Определяем уровень важности устройства
            var deviceIcon = GetDeviceIcon(device.DeviceType);
            var riskLevel = AssessDeviceRisk(device);

            // Уведомление о новом устройстве с деталями
            var message = $"{deviceIcon} НОВОЕ УСТРОЙСТВО В СЕТИ!\n\n" +
                         $"🔗 IP: {device.IPAddress}\n" +
                         $"🏷️ MAC: {device.MACAddress}\n" +
                         $"💻 Хост: {device.Hostname}\n" +
                         $"🏭 Производитель: {device.Vendor}\n" +
                         $"📱 Тип: {device.DeviceType}\n" +
                         $"🖥️ ОС: {device.OperatingSystem}\n" +
                         $"🔌 Открытые порты: {(device.OpenPorts.Any() ? string.Join(", ", device.OpenPorts) : "Нет")}\n\n" +
                         $"⚠️ Уровень риска: {riskLevel}\n\n" +
                         $"Требует внимания!";

            var result = MessageBox.Show(message, "СЕТЕВОЕ ОПОВЕЩЕНИЕ",
                                       MessageBoxButtons.YesNo,
                                       GetAlertIcon(riskLevel));

            if (result == DialogResult.Yes)
            {
                tabControl.SelectedIndex = 3; // Переключаемся на вкладку сети
            }

            AddLogMessage($"Обнаружено новое устройство: {device.DeviceType} {device.IPAddress} ({device.Hostname}) - {riskLevel}", LogLevel.Network);

            // Звуковое уведомление в зависимости от риска
            if (soundNotificationCheckBox.Checked)
            {
                if (riskLevel.Contains("ВЫСОКИЙ"))
                    System.Media.SystemSounds.Hand.Play();
                else if (riskLevel.Contains("СРЕДНИЙ"))
                    System.Media.SystemSounds.Exclamation.Play();
                else
                    System.Media.SystemSounds.Asterisk.Play();
            }
        }

        private string GetDeviceIcon(string deviceType)
        {
            if (deviceType.Contains("📱")) return "📱";
            if (deviceType.Contains("💻")) return "💻";
            if (deviceType.Contains("🌐")) return "🌐";
            if (deviceType.Contains("🖨️")) return "🖨️";
            if (deviceType.Contains("📹")) return "📹";
            if (deviceType.Contains("📺")) return "📺";
            if (deviceType.Contains("🎮")) return "🎮";
            if (deviceType.Contains("🔊")) return "🔊";
            if (deviceType.Contains("🏠")) return "🏠";
            return "❓";
        }

        private string AssessDeviceRisk(NetworkDevice device)
        {
            var riskScore = 0;

            // Открытые порты увеличивают риск
            riskScore += device.OpenPorts.Count * 2;

            // Сервисные порты (SSH, RDP, Telnet)
            if (device.OpenPorts.Contains(22) || device.OpenPorts.Contains(3389) || device.OpenPorts.Contains(23))
                riskScore += 10;

            // Web серверы
            if (device.OpenPorts.Contains(80) || device.OpenPorts.Contains(443) || device.OpenPorts.Contains(8080))
                riskScore += 3;

            // Неизвестные устройства более подозрительны
            if (device.DeviceType.Contains("Неизвестное") || device.Vendor.Contains("Unknown"))
                riskScore += 5;

            // Камеры и IoT устройства
            if (device.DeviceType.Contains("📹") || device.DeviceType.Contains("🏠") || device.DeviceType.Contains("💡"))
                riskScore += 3;

            // Определяем уровень риска
            if (riskScore >= 15)
                return "🔴 ВЫСОКИЙ РИСК";
            else if (riskScore >= 8)
                return "🟡 СРЕДНИЙ РИСК";
            else if (riskScore >= 3)
                return "🟢 НИЗКИЙ РИСК";
            else
                return "✅ БЕЗОПАСНО";
        }

        private MessageBoxIcon GetAlertIcon(string riskLevel)
        {
            if (riskLevel.Contains("ВЫСОКИЙ"))
                return MessageBoxIcon.Warning;
            else if (riskLevel.Contains("СРЕДНИЙ"))
                return MessageBoxIcon.Information;
            else
                return MessageBoxIcon.Information;
        }

        private void UpdateDeviceStatus(NetworkDevice device)
        {
            var existingDevice = _networkDevices.FirstOrDefault(d => d.IPAddress == device.IPAddress);
            if (existingDevice != null)
            {
                existingDevice.Status = device.Status;
                existingDevice.LastSeen = device.LastSeen;

                // Обновляем отображение
                networkGrid.Refresh();
            }
        }

        private void ShowSuspiciousActivity(string key, int attempts)
        {
            var result = MessageBox.Show(
                $"🚨 ОБНАРУЖЕНА ПОДОЗРИТЕЛЬНАЯ АКТИВНОСТЬ!\n\n" +
                $"Источник: {key}\n" +
                $"Количество попыток: {attempts}\n" +
                $"Время: {DateTime.Now:HH:mm:ss}\n\n" +
                $"Рекомендуется немедленная проверка!\n\n" +
                $"Показать детали?",
                "КРИТИЧЕСКОЕ ПРЕДУПРЕЖДЕНИЕ БЕЗОПАСНОСТИ",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                tabControl.SelectedIndex = 2; // Переключаемся на вкладку статистики
            }

            AddLogMessage($"ТРЕВОГА! Подозрительная активность от {key} ({attempts} попыток)", LogLevel.Security);

            if (soundNotificationCheckBox.Checked)
            {
                System.Media.SystemSounds.Hand.Play();
            }
        }

        private void StatsTimer_Tick(object sender, EventArgs e)
        {
            UpdateStatistics();
        }

        private void NetworkTimer_Tick(object sender, EventArgs e)
        {
            // Обновление статуса сетевых устройств
            Task.Run(() => _networkMonitor.UpdateDeviceStatuses());
        }

        private void UpdateStatistics()
        {
            statisticsView.Items.Clear();
            var attempts = _monitor.GetCurrentFailedAttempts();

            foreach (var kvp in attempts.OrderByDescending(x => x.Value))
            {
                var item = new ListViewItem(kvp.Key);
                item.SubItems.Add(kvp.Value.ToString());
                item.SubItems.Add(DateTime.Now.ToString("HH:mm:ss"));

                if (kvp.Value >= _monitor.MaxFailedAttempts)
                {
                    item.SubItems.Add("🚨 КРИТИЧНО");
                    item.BackColor = Color.Red;
                    item.ForeColor = Color.White;
                }
                else if (kvp.Value >= _monitor.MaxFailedAttempts / 2)
                {
                    item.SubItems.Add("⚠️ Подозрительно");
                    item.BackColor = Color.Orange;
                    item.ForeColor = Color.Black;
                }
                else
                {
                    item.SubItems.Add("✅ Норма");
                    item.BackColor = Color.LightGreen;
                    item.ForeColor = Color.Black;
                }

                statisticsView.Items.Add(item);
            }

            // Обновляем счетчики на форме
            totalAttemptsLabel.Text = $"Всего попыток: {_loginAttempts.Count}";
            failedAttemptsLabel.Text = $"Неудачных: {_loginAttempts.Count(x => x.EventType == "Неудачный вход")}";
            networkDevicesLabel.Text = $"Устройств в сети: {_networkDevices.Count}";
            activeThreatsLabel.Text = $"Активных угроз: {attempts.Count(x => x.Value >= _monitor.MaxFailedAttempts)}";
        }

        private void ScanNetworkButton_Click(object sender, EventArgs e)
        {
            scanNetworkButton.Enabled = false;
            scanNetworkButton.Text = "Сканирование...";

            AddLogMessage("Начинаем принудительное сканирование сети...", LogLevel.Info);

            Task.Run(() =>
            {
                _networkMonitor.PerformNetworkScan();

                Invoke(new Action(() =>
                {
                    scanNetworkButton.Enabled = true;
                    scanNetworkButton.Text = "🔍 Сканировать сеть";
                    AddLogMessage("Сканирование сети завершено", LogLevel.Network);

                    // Показываем диагностическую информацию
                    var deviceCount = _networkDevices.Count;
                    var message = $"Сканирование завершено!\n\n" +
                                 $"Найдено устройств: {deviceCount}\n\n" +
                                 $"Если твой iPad не найден, попробуй:\n" +
                                 $"1. Убедись что iPad подключен к той же Wi-Fi сети\n" +
                                 $"2. Открой любое приложение с интернетом на iPad\n" +
                                 $"3. Проверь что iPad не в режиме энергосбережения\n" +
                                 $"4. Попробуй пропинговать iPad с компьютера\n\n" +
                                 $"Проверить детали в логах?";

                    var result = MessageBox.Show(message, "Результат сканирования",
                                               MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                    if (result == DialogResult.Yes)
                    {
                        tabControl.SelectedIndex = 1; // Переключаемся на текстовый лог
                    }
                }));
            });
        }

        private void AutoScanTimer_Tick(object sender, EventArgs e)
        {
            // Автоматическое полное сканирование сети
            AddLogMessage("Запущено автоматическое сканирование сети...", LogLevel.Network);
            Task.Run(() => _networkMonitor.PerformNetworkScan());
        }

        private void AutoScanIntervalNum_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                // Изменение интервала автосканирования во время работы
                var numUpDown = sender as NumericUpDown;
                if (numUpDown != null && _autoScanTimer != null)
                {
                    var newInterval = (int)numUpDown.Value * 1000; // секунды в миллисекунды
                    _autoScanTimer.Interval = newInterval;

                    AddLogMessage($"Интервал автосканирования изменен на {numUpDown.Value} секунд", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                AddLogMessage($"Ошибка изменения интервала: {ex.Message}", LogLevel.Error);
            }
        }

        private void AutoScanCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                var checkBox = sender as CheckBox;
                if (checkBox != null && _autoScanTimer != null)
                {
                    if (checkBox.Checked && _monitor?.IsRunning == true)
                    {
                        _autoScanTimer.Start();
                        AddLogMessage("Автоматическое сканирование включено", LogLevel.Info);
                    }
                    else
                    {
                        _autoScanTimer.Stop();
                        AddLogMessage("Автоматическое сканирование выключено", LogLevel.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                AddLogMessage($"Ошибка переключения автосканирования: {ex.Message}", LogLevel.Error);
            }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_monitor?.IsRunning == true)
            {
                _monitor.StopMonitoring();
            }

            if (_networkMonitor?.IsRunning == true)
            {
                _networkMonitor.StopMonitoring();
            }

            _statsTimer?.Stop();
            _networkTimer?.Stop();
            _autoScanTimer?.Stop();

            base.OnFormClosing(e);
        }
    }
}