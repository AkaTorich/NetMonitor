using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Linq;

namespace RDPLoginMonitor
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Success,
        Network,
        Security
    }

    // Модель попытки входа RDP
    public class RDPFailedLogin
    {
        public DateTime TimeStamp { get; set; }
        public string Username { get; set; }
        public string SourceIP { get; set; }
        public string Computer { get; set; }
        public int EventId { get; set; }
        public string Description { get; set; }
        public string Status { get; set; }
        public string EventType { get; set; }
    }

    // Модель сетевого устройства
    public class NetworkDevice
    {
        public string IPAddress { get; set; }
        public string MACAddress { get; set; }
        public string Hostname { get; set; }
        public string Vendor { get; set; }
        public string DeviceType { get; set; }
        public string OperatingSystem { get; set; }
        public string Status { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public bool IsNew { get; set; }
        public List<int> OpenPorts { get; set; } = new List<int>();
        public string Description { get; set; }
    }

    // Монитор RDP событий
    public class RDPMonitor
    {
        private readonly Dictionary<string, int> _failedAttempts = new Dictionary<string, int>();
        private readonly Dictionary<string, DateTime> _lastAttempt = new Dictionary<string, DateTime>();
        private readonly object _lockObject = new object();
        private bool _isRunning = false;

        public int MaxFailedAttempts { get; set; } = 5;
        public TimeSpan TimeWindow { get; set; } = TimeSpan.FromMinutes(15);
        public string LogFilePath { get; set; } = "rdp_monitor.log";

        public event Action<RDPFailedLogin> OnFailedLogin;
        public event Action<string, int> OnSuspiciousActivity;
        public event Action<string, LogLevel> OnLogMessage;

        public void StartMonitoring()
        {
            if (_isRunning) return;
            _isRunning = true;
            Task.Run(() => MonitorEventLog());
            WriteLog("RDP Monitor запущен", LogLevel.Info);
        }

        public void StopMonitoring()
        {
            _isRunning = false;
            WriteLog("RDP Monitor остановлен", LogLevel.Warning);
        }

        private void MonitorEventLog()
        {
            try
            {
                // Используем EventLogQuery с правильным конструктором
                var query = new EventLogQuery("Security", PathType.LogName,
                    "*[System[EventID=4625 or EventID=4624 or EventID=4647 or EventID=4634]]");

                using (var watcher = new EventLogWatcher(query))
                {
                    watcher.EventRecordWritten += OnEventRecordWritten;
                    watcher.Enabled = true;

                    WriteLog("Мониторинг событий входа в систему активен", LogLevel.Success);

                    while (_isRunning)
                    {
                        Thread.Sleep(1000);
                        CleanupOldEntries();
                    }

                    watcher.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка мониторинга: {ex.Message}", LogLevel.Error);

                // Fallback к простому мониторингу через EventLog
                MonitorEventLogFallback();
            }
        }

        private void MonitorEventLogFallback()
        {
            try
            {
                WriteLog("Переключение на альтернативный метод мониторинга", LogLevel.Info);

                var eventLog = new EventLog("Security");
                eventLog.EntryWritten += EventLog_EntryWritten;
                eventLog.EnableRaisingEvents = true;

                while (_isRunning)
                {
                    Thread.Sleep(1000);
                    CleanupOldEntries();
                }

                eventLog.EnableRaisingEvents = false;
                eventLog.Dispose();
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка альтернативного мониторинга: {ex.Message}", LogLevel.Error);
            }
        }

        private void EventLog_EntryWritten(object sender, EntryWrittenEventArgs e)
        {
            try
            {
                var entry = e.Entry;

                // Проверяем интересующие нас события
                if (entry.InstanceId == 4625 || entry.InstanceId == 4624 ||
                    entry.InstanceId == 4647 || entry.InstanceId == 4634)
                {
                    // Проверяем, что это RDP подключение (LogonType 10)
                    if (entry.Message != null && entry.Message.Contains("Logon Type:\t\t10"))
                    {
                        var login = ParseEventLogEntry(entry);
                        if (login != null)
                        {
                            ProcessEventLogEntry(login, (int)entry.InstanceId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка обработки события EventLog: {ex.Message}", LogLevel.Error);
            }
        }

        private RDPFailedLogin ParseEventLogEntry(EventLogEntry entry)
        {
            try
            {
                var login = new RDPFailedLogin
                {
                    TimeStamp = entry.TimeGenerated,
                    EventId = (int)entry.InstanceId,
                    Computer = entry.MachineName,
                    Description = entry.Message ?? "Нет описания"
                };

                // Парсим сообщение для извлечения данных
                var message = entry.Message ?? "";

                // Извлекаем имя пользователя
                var userMatch = Regex.Match(message, @"Account Name:\s*([^\r\n\t]+)");
                login.Username = userMatch.Success ? userMatch.Groups[1].Value.Trim() : "Unknown";

                // Извлекаем IP адрес
                var ipMatch = Regex.Match(message, @"Source Network Address:\s*([^\r\n\t]+)");
                login.SourceIP = ipMatch.Success ? ipMatch.Groups[1].Value.Trim() : "Unknown";

                return login;
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка парсинга EventLogEntry: {ex.Message}", LogLevel.Error);
                return null;
            }
        }

        private void ProcessEventLogEntry(RDPFailedLogin login, int eventId)
        {
            switch (eventId)
            {
                case 4625: // Неудачный вход
                    login.Status = "Неудачный";
                    login.EventType = "Неудачный вход";
                    ProcessFailedLogin(login);
                    break;
                case 4624: // Успешный вход
                    login.Status = "Успешный";
                    login.EventType = "Успешный вход";
                    ProcessSuccessfulLogin(login);
                    break;
                case 4647: // Инициирован выход пользователем
                    login.Status = "Выход";
                    login.EventType = "Выход пользователя";
                    OnFailedLogin?.Invoke(login);
                    break;
                case 4634: // Сеанс завершен
                    login.Status = "Завершение сеанса";
                    login.EventType = "Завершение сеанса";
                    OnFailedLogin?.Invoke(login);
                    break;
            }
        }

        private void OnEventRecordWritten(object sender, EventRecordWrittenEventArgs e)
        {
            if (e.EventRecord == null) return;

            try
            {
                var eventRecord = e.EventRecord;
                var eventId = eventRecord.Id;

                var failedLogin = ParseEventRecord(eventRecord);
                if (failedLogin == null) return;

                switch (eventId)
                {
                    case 4625: // Неудачный вход
                        failedLogin.Status = "Неудачный";
                        failedLogin.EventType = "Неудачный вход";
                        ProcessFailedLogin(failedLogin);
                        break;
                    case 4624: // Успешный вход
                        failedLogin.Status = "Успешный";
                        failedLogin.EventType = "Успешный вход";
                        ProcessSuccessfulLogin(failedLogin);
                        break;
                    case 4647: // Инициирован выход пользователем
                        failedLogin.Status = "Выход";
                        failedLogin.EventType = "Выход пользователя";
                        OnFailedLogin?.Invoke(failedLogin);
                        break;
                    case 4634: // Сеанс завершен
                        failedLogin.Status = "Завершение сеанса";
                        failedLogin.EventType = "Завершение сеанса";
                        OnFailedLogin?.Invoke(failedLogin);
                        break;
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка обработки события: {ex.Message}", LogLevel.Error);
            }
        }

        private RDPFailedLogin ParseEventRecord(EventRecord eventRecord)
        {
            try
            {
                var properties = eventRecord.Properties;

                var login = new RDPFailedLogin
                {
                    TimeStamp = eventRecord.TimeCreated ?? DateTime.Now,
                    EventId = eventRecord.Id,
                    Computer = eventRecord.MachineName
                };

                if (properties.Count > 5)
                {
                    login.Username = properties[5].Value?.ToString() ?? "Unknown";
                }

                if (properties.Count > 19)
                {
                    login.SourceIP = properties[19].Value?.ToString() ?? "Unknown";
                }

                if (properties.Count > 8)
                {
                    var logonType = properties[8].Value?.ToString();
                    if (logonType != "10") // Только RDP подключения
                        return null;
                }

                login.Description = eventRecord.FormatDescription() ?? "Нет описания";

                return login;
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка парсинга события: {ex.Message}", LogLevel.Error);
                return null;
            }
        }

        private void ProcessFailedLogin(RDPFailedLogin failedLogin)
        {
            lock (_lockObject)
            {
                var key = $"{failedLogin.SourceIP}_{failedLogin.Username}";

                if (!_failedAttempts.ContainsKey(key))
                {
                    _failedAttempts[key] = 0;
                }

                _failedAttempts[key]++;
                _lastAttempt[key] = failedLogin.TimeStamp;

                WriteLog($"Неудачный вход: {failedLogin.Username} с {failedLogin.SourceIP} (попытка #{_failedAttempts[key]})", LogLevel.Warning);

                OnFailedLogin?.Invoke(failedLogin);

                if (_failedAttempts[key] >= MaxFailedAttempts)
                {
                    failedLogin.EventType = "Подозрительная активность";
                    WriteLog($"ПОДОЗРИТЕЛЬНАЯ АКТИВНОСТЬ: {_failedAttempts[key]} неудачных попыток для {failedLogin.Username} с {failedLogin.SourceIP}", LogLevel.Security);
                    OnSuspiciousActivity?.Invoke(key, _failedAttempts[key]);
                }
            }
        }

        private void ProcessSuccessfulLogin(RDPFailedLogin login)
        {
            WriteLog($"Успешный вход: {login.Username} с {login.SourceIP}", LogLevel.Success);
            OnFailedLogin?.Invoke(login);

            lock (_lockObject)
            {
                var key = $"{login.SourceIP}_{login.Username}";
                if (_failedAttempts.ContainsKey(key))
                {
                    _failedAttempts.Remove(key);
                    _lastAttempt.Remove(key);
                }
            }
        }

        private void CleanupOldEntries()
        {
            lock (_lockObject)
            {
                var cutoffTime = DateTime.Now - TimeWindow;
                var keysToRemove = new List<string>();

                foreach (var kvp in _lastAttempt)
                {
                    if (kvp.Value < cutoffTime)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _failedAttempts.Remove(key);
                    _lastAttempt.Remove(key);
                }
            }
        }

        public void WriteLog(string message, LogLevel level)
        {
            try
            {
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
                File.AppendAllText(LogFilePath, logEntry + Environment.NewLine);
                OnLogMessage?.Invoke(message, level);
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"Ошибка записи в лог: {ex.Message}", LogLevel.Error);
            }
        }

        public Dictionary<string, int> GetCurrentFailedAttempts()
        {
            lock (_lockObject)
            {
                return new Dictionary<string, int>(_failedAttempts);
            }
        }

        public bool IsRunning => _isRunning;
    }

    // Монитор сетевых устройств
    public class NetworkMonitor
    {
        private readonly Dictionary<string, NetworkDevice> _knownDevices = new Dictionary<string, NetworkDevice>();
        private readonly object _lockObject = new object();
        private bool _isRunning = false;
        private readonly Dictionary<string, string> _vendorDatabase;

        public event Action<NetworkDevice> OnNewDeviceDetected;
        public event Action<NetworkDevice> OnDeviceStatusChanged;

        public NetworkMonitor()
        {
            _vendorDatabase = InitializeVendorDatabase();
        }

        public void StartMonitoring()
        {
            if (_isRunning) return;
            _isRunning = true;

            // Выполняем первоначальное сканирование
            Task.Run(() => PerformNetworkScan());
        }

        public void StopMonitoring()
        {
            _isRunning = false;
        }

        public void PerformNetworkScan()
        {
            if (!_isRunning) return;

            try
            {
                var localIP = GetLocalIPAddress();
                if (string.IsNullOrEmpty(localIP)) return;

                System.Diagnostics.Debug.WriteLine($"Начинаем сканирование с локального IP: {localIP}");

                var networkPrefix = GetNetworkPrefix(localIP);
                System.Diagnostics.Debug.WriteLine($"Сканируем сеть: {networkPrefix}.1-254");

                // Сначала сканируем ARP таблицу для быстрого обнаружения
                ScanARPTable();

                // Затем пингуем весь диапазон
                var tasks = new List<Task>();
                for (int i = 1; i <= 254; i++)
                {
                    var ip = $"{networkPrefix}.{i}";
                    tasks.Add(Task.Run(() => ScanDevice(ip)));
                }

                Task.WaitAll(tasks.ToArray(), TimeSpan.FromMinutes(3));

                System.Diagnostics.Debug.WriteLine($"Сканирование завершено. Найдено устройств: {_knownDevices.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка сканирования сети: {ex.Message}");
            }
        }

        // Новый метод для сканирования ARP таблицы
        private void ScanARPTable()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "arp",
                        Arguments = "-a",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                System.Diagnostics.Debug.WriteLine($"ARP таблица:\n{output}");

                // Парсим ARP таблицу
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    // Ищем строки вида: IP-адрес MAC-адрес тип
                    var match = Regex.Match(line.Trim(), @"(\d+\.\d+\.\d+\.\d+)\s+([0-9a-fA-F-]{17})\s+(\w+)");
                    if (match.Success)
                    {
                        var ip = match.Groups[1].Value;
                        var mac = match.Groups[2].Value.ToUpper().Replace("-", ":");

                        System.Diagnostics.Debug.WriteLine($"Найдено в ARP: {ip} -> {mac}");

                        // Создаем устройство из ARP записи
                        var device = new NetworkDevice
                        {
                            IPAddress = ip,
                            MACAddress = mac,
                            Hostname = GetHostname(ip),
                            Status = "Активен",
                            LastSeen = DateTime.Now
                        };

                        device.Vendor = GetVendorFromMAC(device.MACAddress);
                        device.DeviceType = DetermineDeviceType(device);
                        device.OperatingSystem = DetectOperatingSystem(device);
                        device.OpenPorts = ScanCommonPorts(ip);
                        device.Description = GenerateDeviceDescription(device);

                        ProcessDevice(device);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка сканирования ARP: {ex.Message}");
            }
        }

        private void ScanDevice(string ipAddress)
        {
            try
            {
                using (var ping = new Ping())
                {
                    // Увеличиваем timeout для мобильных устройств
                    var reply = ping.Send(ipAddress, 2000);
                    if (reply.Status == IPStatus.Success)
                    {
                        System.Diagnostics.Debug.WriteLine($"Пинг успешен: {ipAddress} ({reply.RoundtripTime}ms)");

                        var device = new NetworkDevice
                        {
                            IPAddress = ipAddress,
                            MACAddress = GetMACAddress(ipAddress),
                            Hostname = GetHostname(ipAddress),
                            Status = "Активен",
                            LastSeen = DateTime.Now
                        };

                        device.Vendor = GetVendorFromMAC(device.MACAddress);
                        device.DeviceType = DetermineDeviceType(device);
                        device.OperatingSystem = DetectOperatingSystem(device);
                        device.OpenPorts = ScanCommonPorts(ipAddress);
                        device.Description = GenerateDeviceDescription(device);

                        System.Diagnostics.Debug.WriteLine($"Обработано устройство: {device.IPAddress} -> {device.DeviceType}");
                        ProcessDevice(device);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Пинг неудачен: {ipAddress} - {reply.Status}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка сканирования {ipAddress}: {ex.Message}");
            }
        }

        private void ProcessDevice(NetworkDevice device)
        {
            lock (_lockObject)
            {
                var key = device.IPAddress;

                if (_knownDevices.ContainsKey(key))
                {
                    // Обновляем существующее устройство
                    var existingDevice = _knownDevices[key];
                    existingDevice.Status = device.Status;
                    existingDevice.LastSeen = device.LastSeen;

                    OnDeviceStatusChanged?.Invoke(existingDevice);
                }
                else
                {
                    // Новое устройство
                    device.FirstSeen = DateTime.Now;
                    device.IsNew = true;
                    _knownDevices[key] = device;

                    OnNewDeviceDetected?.Invoke(device);
                }
            }
        }

        public void UpdateDeviceStatuses()
        {
            if (!_isRunning) return;

            var devicesToCheck = new List<NetworkDevice>();
            lock (_lockObject)
            {
                devicesToCheck.AddRange(_knownDevices.Values);
            }

            foreach (var device in devicesToCheck)
            {
                Task.Run(() =>
                {
                    try
                    {
                        using (var ping = new Ping())
                        {
                            var reply = ping.Send(device.IPAddress, 1000);
                            var newStatus = reply.Status == IPStatus.Success ? "Активен" : "Недоступен";

                            if (device.Status != newStatus)
                            {
                                device.Status = newStatus;
                                device.LastSeen = DateTime.Now;
                                OnDeviceStatusChanged?.Invoke(device);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        device.Status = "Ошибка";
                        OnDeviceStatusChanged?.Invoke(device);
                    }
                });
            }
        }

        private string GetLocalIPAddress()
        {
            try
            {
                // Метод 1: Через NetworkInterface (более надежный)
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                var ip = addr.Address.ToString();
                                // Проверяем что это локальная сеть
                                if (ip.StartsWith("192.168.") || ip.StartsWith("10.") ||
                                    (ip.StartsWith("172.") && IsInRange172(ip)))
                                {
                                    System.Diagnostics.Debug.WriteLine($"Найден локальный IP через NetworkInterface: {ip}");
                                    return ip;
                                }
                            }
                        }
                    }
                }

                // Метод 2: Через DNS (fallback)
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        var ipStr = ip.ToString();
                        if (ipStr.StartsWith("192.168.") || ipStr.StartsWith("10.") ||
                            (ipStr.StartsWith("172.") && IsInRange172(ipStr)))
                        {
                            System.Diagnostics.Debug.WriteLine($"Найден локальный IP через DNS: {ipStr}");
                            return ipStr;
                        }
                    }
                }

                // Метод 3: Подключение к внешнему адресу для определения локального IP
                using (var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork,
                                                                 System.Net.Sockets.SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    var endPoint = socket.LocalEndPoint as System.Net.IPEndPoint;
                    if (endPoint != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Найден локальный IP через socket: {endPoint.Address}");
                        return endPoint.Address.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка получения локального IP: {ex.Message}");
            }

            System.Diagnostics.Debug.WriteLine("Используем fallback IP: 192.168.1.100");
            return "192.168.1.100"; // Fallback
        }

        private bool IsInRange172(string ip)
        {
            try
            {
                var parts = ip.Split('.');
                if (parts.Length >= 2)
                {
                    var secondOctet = int.Parse(parts[1]);
                    return secondOctet >= 16 && secondOctet <= 31; // 172.16.0.0 - 172.31.255.255
                }
            }
            catch { }
            return false;
        }

        private string GetNetworkPrefix(string ipAddress)
        {
            var parts = ipAddress.Split('.');
            return $"{parts[0]}.{parts[1]}.{parts[2]}";
        }

        private string GetMACAddress(string ipAddress)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "arp",
                        Arguments = $"-a {ipAddress}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var match = Regex.Match(output, @"([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})");
                return match.Success ? match.Value.ToUpper() : "Неизвестно";
            }
            catch (Exception)
            {
                return "Неизвестно";
            }
        }

        private string GetHostname(string ipAddress)
        {
            try
            {
                var hostEntry = System.Net.Dns.GetHostEntry(ipAddress);
                return hostEntry.HostName;
            }
            catch (Exception)
            {
                return "Неизвестно";
            }
        }

        private string GetVendorFromMAC(string macAddress)
        {
            if (string.IsNullOrEmpty(macAddress) || macAddress == "Неизвестно")
                return "Неизвестно";

            try
            {
                var prefix = macAddress.Replace(":", "").Replace("-", "").Substring(0, 6).ToUpper();
                return _vendorDatabase.ContainsKey(prefix) ? _vendorDatabase[prefix] : "Неизвестно";
            }
            catch (Exception)
            {
                return "Неизвестно";
            }
        }

        private Dictionary<string, string> InitializeVendorDatabase()
        {
            // Расширенная база данных производителей с типами устройств
            return new Dictionary<string, string>
            {
            // Apple устройства (расширенная база)
            {"001B63", "Apple"}, // iPhone/iPad
            {"00237D", "Apple"}, // iPhone/iPad
            {"0026BB", "Apple"}, // iPhone/iPad/iPod
            {"00A040", "Apple"}, // Mac/AirPort
            {"C82A14", "Apple"}, // iPhone/iPad
            {"E0ACCB", "Apple"}, // Mac/AirPort
            {"3C0754", "Apple"}, // iPhone/iPad
            {"A4C361", "Apple"}, // iPhone/iPad
            {"F0DBE2", "Apple"}, // iPhone/iPad
            {"84F3EB", "Apple"}, // iPhone/iPad
            {"6C72E7", "Apple"}, // iPhone/iPad
            {"AC3613", "Apple"}, // iPhone/iPad
            {"001EC2", "Apple"}, // MacBook/iMac
            {"70CD60", "Apple"}, // MacBook/iMac
            {"78CA39", "Apple"}, // MacBook/iMac
            {"B8F6B1", "Apple"}, // Apple TV/AirPort
            {"D89695", "Apple"}, // Apple TV
            {"28E02C", "Apple"}, // Apple TV
            {"7CF31B", "Apple"}, // Apple TV
            {"F81EDF", "Apple"}, // Apple TV
                
                // Samsung устройства
                {"001632", "Samsung"},
                {"002454", "Samsung"},
                {"5C577E", "Samsung"},
                {"E84E84", "Samsung"},
                {"30D6C9", "Samsung"},
                {"C85195", "Samsung"},
                {"DC71E8", "Samsung"},
                
                // Роутеры и сетевое оборудование
                {"001485", "Netgear"},
                {"002722", "Netgear"},
                {"A42BB0", "Netgear"},
                {"001CF0", "D-Link"},
                {"0026F2", "D-Link"},
                {"002191", "Tp-Link"},
                {"C46E1F", "Tp-Link"},
                {"F8A2D6", "Tp-Link"},
                {"001999", "Belkin"},
                {"944452", "Belkin"},
                {"EC1A59", "Belkin"},
                {"00146C", "Linksys"},
                {"68F728", "Linksys"},
                
                // Компьютеры и ноутбуки
                {"00904D", "Dell"},
                {"001E8C", "Dell"},
                {"002219", "Dell"},
                {"B8CA3A", "Dell"},
                {"001A4B", "HP"},
                {"70106F", "HP"},
                {"009027", "HP"},
                {"00E020", "Intel"},
                {"001B21", "Intel"},
                {"E4B97A", "Intel"},
                {"001E4F", "Lenovo"},
                {"005CF6", "Lenovo"},
                {"689423", "Lenovo"},
                {"00E04D", "Broadcom"},
                {"001560", "ASUS"},
                {"2C56DC", "ASUS"},
                {"F46D04", "ASUS"},
                
                // IoT и умные устройства
                {"ECF4BB", "Amazon"},
                {"F0272D", "Amazon"},
                {"18B430", "Nest"},
                {"9CFEFB", "Sonos"},
                {"000E58", "Sonos"},
                {"54FA3E", "Ring"},
                {"002618", "Philips"},
                {"001788", "Philips"},
                
                // Игровые консоли
                {"002248", "Nintendo"},
                {"009BF3", "Nintendo"},
                {"0403D6", "Nintendo"},
                {"001E3D", "Microsoft"},
                {"001DD8", "Microsoft"},
                {"08002E", "Microsoft"},
                {"002090", "Sony"},
                {"001C9E", "Sony"},
                {"001E56", "Sony"},
                
                // Принтеры
                {"001E0B", "Canon"},
                {"002507", "Canon"},
                {"003018", "Epson"},
                {"001279", "Brother"},
                
                // Камеры и видеонаблюдение
                {"0007AB", "Axis"},
                {"001B2F", "Hikvision"},
                {"4C9EFF", "Ubiquiti"},
                
                // Виртуализация
                {"005056", "VMware"},
                {"000C29", "VMware"},
                {"001C14", "VMware"},
                {"525400", "QEMU"},
                {"080027", "VirtualBox"},
                
                // Мобильные операторы и модемы
                {"001F5B", "Huawei"},
                {"002E1E", "ZTE"},
                {"000474", "Qualcomm"},
                
                // Generic
                {"000000", "Generic"},
                {"001122", "Unknown Device"}
            };
        }

        private string DetermineDeviceType(NetworkDevice device)
        {
            var hostname = device.Hostname?.ToLower() ?? "";
            var vendor = device.Vendor?.ToLower() ?? "";
            var mac = device.MACAddress?.Replace(":", "").Replace("-", "").ToUpper() ?? "";

            // Определяем по производителю Apple с улучшенной логикой
            if (vendor.Contains("apple"))
            {
                // Проверяем по hostname
                if (hostname.Contains("iphone") || hostname.Contains("phone"))
                    return "📱 iPhone";
                if (hostname.Contains("ipad") || hostname.Contains("pad"))
                    return "📱 iPad";
                if (hostname.Contains("macbook") || hostname.Contains("imac") || hostname.Contains("mac"))
                    return "💻 Mac компьютер";
                if (hostname.Contains("appletv") || hostname.Contains("apple-tv"))
                    return "📺 Apple TV";
                if (hostname.Contains("watch"))
                    return "⌚ Apple Watch";
                if (hostname.Contains("airpods") || hostname.Contains("beats"))
                    return "🎧 Apple аудио";

                // Дополнительная проверка по MAC адресу для определения типа Apple устройства
                if (IsAppleMobileDevice(mac))
                {
                    // Если не смогли определить точно - пробуем по другим признакам
                    var ports = device.OpenPorts ?? new List<int>();

                    // iPad обычно имеет больше портов чем iPhone
                    if (ports.Count >= 3 || ports.Contains(5353)) // Bonjour обычно есть на iPad
                        return "📱 iPad";
                    else if (ports.Count <= 2)
                        return "📱 iPhone";
                }

                // Если это стационарное Apple устройство
                if (IsAppleDesktopMAC(mac))
                    return "💻 Mac компьютер";

                return "🍎 Apple устройство";
            }

            // Samsung с улучшенной логикой
            if (vendor.Contains("samsung"))
            {
                if (hostname.Contains("galaxy") && (hostname.Contains("tab") || hostname.Contains("note")))
                    return "📱 Samsung планшет";
                if (hostname.Contains("galaxy") || hostname.Contains("sm-") || hostname.Contains("phone"))
                    return "📱 Samsung телефон";
                if (hostname.Contains("tv") || hostname.Contains("smart"))
                    return "📺 Samsung Smart TV";
                return "📱 Samsung устройство";
            }

            // Игровые консоли
            if (vendor.Contains("nintendo"))
            {
                if (hostname.Contains("switch")) return "🎮 Nintendo Switch";
                return "🎮 Nintendo консоль";
            }

            if (vendor.Contains("sony"))
            {
                if (hostname.Contains("playstation") || hostname.Contains("ps")) return "🎮 PlayStation";
                if (hostname.Contains("tv")) return "📺 Sony TV";
                return "📺 Sony устройство";
            }

            if (vendor.Contains("microsoft"))
            {
                if (hostname.Contains("xbox")) return "🎮 Xbox";
                if (hostname.Contains("surface")) return "💻 Surface планшет";
                return "💻 Microsoft устройство";
            }

            // Определяем по имени хоста (улучшенная логика)
            if (hostname.Contains("router") || hostname.Contains("gateway") || hostname.Contains("openwrt"))
                return "🌐 Роутер";

            if (hostname.Contains("printer") || hostname.Contains("canon") ||
                hostname.Contains("epson") || hostname.Contains("hp-") || vendor.Contains("canon"))
                return "🖨️ Принтер";

            if (hostname.Contains("camera") || hostname.Contains("cam") || hostname.Contains("nvr") || vendor.Contains("axis"))
                return "📹 IP камера";

            if (hostname.Contains("tv") || hostname.Contains("smart") || hostname.Contains("roku") || hostname.Contains("chromecast"))
                return "📺 Smart TV";

            if (hostname.Contains("android") || hostname.Contains("phone") || hostname.Contains("mobile"))
                return "📱 Android телефон";

            if (hostname.Contains("tablet") || hostname.Contains("tab-"))
                return "📱 Планшет";

            // Определяем по производителю сетевого оборудования
            if (vendor.Contains("netgear") || vendor.Contains("d-link") ||
                vendor.Contains("tp-link") || vendor.Contains("linksys") || vendor.Contains("belkin"))
                return "🌐 Сетевое оборудование";

            if (vendor.Contains("sonos"))
                return "🔊 Sonos колонка";

            if (vendor.Contains("nest") || vendor.Contains("google"))
                return "🏠 Google/Nest устройство";

            if (vendor.Contains("ring"))
                return "🔔 Ring устройство";

            if (vendor.Contains("philips"))
                return "💡 Philips умные устройства";

            if (vendor.Contains("amazon"))
                return "🗣️ Amazon Echo/Alexa";

            if (vendor.Contains("vmware") || vendor.Contains("qemu") || vendor.Contains("virtualbox"))
                return "🖥️ Виртуальная машина";

            if (vendor.Contains("dell") || vendor.Contains("hp") || vendor.Contains("lenovo") ||
                vendor.Contains("asus") || vendor.Contains("intel"))
                return "💻 Компьютер";

            if (vendor.Contains("huawei") || vendor.Contains("zte") || vendor.Contains("qualcomm"))
                return "📡 Модем/Роутер";

            // Расширенная проверка по открытым портам
            var openPorts = device.OpenPorts ?? new List<int>();
            if (openPorts.Contains(80) || openPorts.Contains(443))
            {
                if (openPorts.Contains(22) || openPorts.Contains(23))
                    return "🌐 Сетевое устройство";
                return "🌐 Web-сервер";
            }

            if (openPorts.Contains(3389))
                return "💻 Windows компьютер";

            if (openPorts.Contains(22))
                return "🐧 Linux/Unix сервер";

            if (openPorts.Contains(5353)) // Bonjour/mDNS
                return "📱 Мобильное устройство";

            // По умолчанию
            return "❓ Неизвестное устройство";
        }

        // Проверяем является ли MAC адрес мобильным Apple устройством
        private bool IsAppleMobileDevice(string mac)
        {
            if (string.IsNullOrEmpty(mac) || mac.Length < 6) return false;

            var prefix = mac.Substring(0, 6);
            // MAC префиксы для мобильных Apple устройств
            var mobileApplePrefixes = new[]
            {
                "001B63", "00237D", "0026BB", "A4C361", "F0DBE2",
                "84F3EB", "6C72E7", "AC3613", "3C0754", "C82A14"
            };

            return mobileApplePrefixes.Contains(prefix);
        }

        // Проверяем является ли MAC адрес настольным Apple устройством
        private bool IsAppleDesktopMAC(string mac)
        {
            if (string.IsNullOrEmpty(mac) || mac.Length < 6) return false;

            var prefix = mac.Substring(0, 6);
            // MAC префиксы для настольных Apple устройств
            var desktopApplePrefixes = new[]
            {
                "00A040", "E0ACCB", "001EC2", "70CD60", "78CA39"
            };

            return desktopApplePrefixes.Contains(prefix);
        }

        private string DetectOperatingSystem(NetworkDevice device)
        {
            var hostname = device.Hostname?.ToLower() ?? "";
            var vendor = device.Vendor?.ToLower() ?? "";
            var deviceType = device.DeviceType?.ToLower() ?? "";

            if (vendor.Contains("apple"))
            {
                if (deviceType.Contains("iphone")) return "iOS (iPhone)";
                if (deviceType.Contains("ipad")) return "iPadOS";
                if (deviceType.Contains("mac") || hostname.Contains("mac")) return "macOS";
                if (deviceType.Contains("apple tv")) return "tvOS";
                if (deviceType.Contains("watch")) return "watchOS";
                return "Apple OS";
            }

            if (vendor.Contains("microsoft") || hostname.Contains("desktop") || hostname.Contains("pc"))
                return "Windows";

            if (vendor.Contains("samsung") || hostname.Contains("android") || deviceType.Contains("android"))
                return "Android";

            if (hostname.Contains("linux") || hostname.Contains("ubuntu") || hostname.Contains("debian"))
                return "Linux";

            if (vendor.Contains("vmware"))
                return "ESXi/VM";

            // Определяем по портам
            var ports = device.OpenPorts ?? new List<int>();
            if (ports.Contains(3389)) // RDP
                return "Windows";
            if (ports.Contains(22) && !ports.Contains(80)) // SSH без web
                return "Linux/Unix";
            if (ports.Contains(5353)) // Bonjour
                return "macOS/iOS";

            return "Неизвестно";
        }

        private List<int> ScanCommonPorts(string ipAddress)
        {
            var openPorts = new List<int>();
            var commonPorts = new[] { 21, 22, 23, 25, 53, 80, 110, 143, 443, 993, 995, 3389, 5900, 8080 };

            foreach (var port in commonPorts)
            {
                try
                {
                    using (var tcpClient = new System.Net.Sockets.TcpClient())
                    {
                        var result = tcpClient.BeginConnect(ipAddress, port, null, null);
                        var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(100));

                        if (success && tcpClient.Connected)
                        {
                            openPorts.Add(port);
                        }

                        tcpClient.Close();
                    }
                }
                catch
                {
                    // Игнорируем ошибки подключения
                }
            }

            return openPorts;
        }

        private string GenerateDeviceDescription(NetworkDevice device)
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(device.DeviceType))
                parts.Add(device.DeviceType);

            if (!string.IsNullOrEmpty(device.OperatingSystem) && device.OperatingSystem != "Неизвестно")
                parts.Add($"ОС: {device.OperatingSystem}");

            if (device.OpenPorts.Any())
                parts.Add($"Порты: {string.Join(", ", device.OpenPorts)}");

            return string.Join(" | ", parts);
        }

        public bool IsRunning => _isRunning;
    }
}