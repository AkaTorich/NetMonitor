using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Linq;
using System.IO;
using System.Text;

namespace RDPLoginMonitor
{
    public class NetworkMonitor
    {
        private readonly Dictionary<string, NetworkDevice> _knownDevices = new Dictionary<string, NetworkDevice>();
        private readonly object _lockObject = new object();
        private bool _isRunning = false;
        private readonly Dictionary<string, string> _vendorDatabase;
        private readonly string _macDatabasePath;

        // События для логирования
        public event Action<string, LogLevel> OnLogMessage;

        public event Action<NetworkDevice> OnNewDeviceDetected;
        public event Action<NetworkDevice> OnDeviceStatusChanged;

        public NetworkMonitor(string macDatabasePath = "MAC.db")
        {
            _macDatabasePath = macDatabasePath;
            _vendorDatabase = LoadVendorDatabase();
        }

        /// <summary>
        /// Логирование сообщений через событие
        /// </summary>
        private void WriteLog(string message, LogLevel level = LogLevel.Info)
        {
            OnLogMessage?.Invoke(message, level);
        }

        /// <summary>
        /// Загружает базу данных производителей из файла MAC.db
        /// </summary>
        private Dictionary<string, string> LoadVendorDatabase()
        {
            var database = new Dictionary<string, string>();

            try
            {
                // Поиск файла в разных местах
                string actualPath = _macDatabasePath;
                if (!File.Exists(actualPath))
                {
                    var possiblePaths = new[]
                    {
                        Path.Combine(Directory.GetCurrentDirectory(), "MAC.db"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MAC.db"),
                        Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "MAC.db"),
                        Path.Combine(Environment.CurrentDirectory, "MAC.db")
                    };

                    foreach (var path in possiblePaths)
                    {
                        if (File.Exists(path))
                        {
                            actualPath = path;
                            WriteLog($"Найден файл MAC.db: {path}", LogLevel.Success);
                            break;
                        }
                    }
                }

                if (!File.Exists(actualPath))
                {
                    WriteLog($"ОШИБКА: Файл базы данных MAC не найден!", LogLevel.Error);
                    WriteLog($"Искали в: {_macDatabasePath}", LogLevel.Error);
                    WriteLog($"Текущая директория: {Directory.GetCurrentDirectory()}", LogLevel.Debug);
                    return database;
                }

                WriteLog($"Загружаем базу данных MAC из файла: {actualPath}", LogLevel.Info);

                // Читаем файл с правильной кодировкой
                var lines = File.ReadAllLines(actualPath, Encoding.UTF8);
                int loadedCount = 0;
                int skippedCount = 0;

                foreach (var line in lines)
                {
                    // Пропускаем пустые строки
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        skippedCount++;
                        continue;
                    }

                    // Проверяем наличие табуляции
                    if (!line.Contains('\t'))
                    {
                        skippedCount++;
                        continue;
                    }

                    // Разделяем по ПЕРВОЙ табуляции
                    var tabIndex = line.IndexOf('\t');
                    if (tabIndex < 0 || tabIndex >= line.Length - 1)
                    {
                        skippedCount++;
                        continue;
                    }

                    var macPrefix = line.Substring(0, tabIndex).Trim().ToUpper();
                    var vendor = line.Substring(tabIndex + 1).Trim();

                    // Проверяем корректность MAC префикса
                    if (IsValidMacPrefix(macPrefix))
                    {
                        database[macPrefix] = vendor;
                        loadedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                }

                WriteLog($"=== ИТОГИ ЗАГРУЗКИ БАЗЫ MAC ===", LogLevel.Success);
                WriteLog($"Загружено записей: {loadedCount}", LogLevel.Success);
                WriteLog($"Пропущено строк: {skippedCount}", LogLevel.Info);
                WriteLog($"Размер базы в памяти: {database.Count}", LogLevel.Success);

                // Проверяем наличие известных префиксов
                var testPrefixes = new[] { "FC253F", "FC019E", "FC01CD" };
                WriteLog("Проверка загрузки известных префиксов:", LogLevel.Info);
                foreach (var prefix in testPrefixes)
                {
                    if (database.ContainsKey(prefix))
                    {
                        WriteLog($"  ✓ {prefix} -> {database[prefix]}", LogLevel.Success);
                    }
                    else
                    {
                        WriteLog($"  ✗ {prefix} -> НЕ НАЙДЕН!", LogLevel.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"КРИТИЧЕСКАЯ ОШИБКА загрузки базы данных MAC: {ex.Message}", LogLevel.Error);
            }

            return database;
        }

        /// <summary>
        /// Проверяет корректность MAC префикса
        /// </summary>
        private bool IsValidMacPrefix(string macPrefix)
        {
            if (string.IsNullOrEmpty(macPrefix)) return false;

            // Префикс должен быть 6 символов
            if (macPrefix.Length != 6) return false;

            // Проверяем что все символы - это hex
            foreach (char c in macPrefix)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Перезагружает базу данных MAC из файла
        /// </summary>
        public void ReloadMacDatabase()
        {
            WriteLog("Перезагрузка базы данных MAC...", LogLevel.Info);
            var newDatabase = LoadVendorDatabase();
            lock (_lockObject)
            {
                _vendorDatabase.Clear();
                foreach (var kvp in newDatabase)
                {
                    _vendorDatabase[kvp.Key] = kvp.Value;
                }
            }
            WriteLog($"База данных MAC перезагружена. Записей: {_vendorDatabase.Count}", LogLevel.Success);
        }

        public void StartMonitoring()
        {
            if (_isRunning) return;
            _isRunning = true;

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
                if (string.IsNullOrEmpty(localIP))
                {
                    WriteLog("Не удалось определить локальный IP", LogLevel.Error);
                    return;
                }

                WriteLog($"Начинаем сканирование с локального IP: {localIP}", LogLevel.Info);

                var networkPrefix = GetNetworkPrefix(localIP);
                WriteLog($"Сканируем сеть: {networkPrefix}.1-254", LogLevel.Info);

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

                WriteLog($"Сканирование завершено. Найдено устройств: {_knownDevices.Count}", LogLevel.Success);
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка сканирования сети: {ex.Message}", LogLevel.Error);
            }
        }

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

                WriteLog($"ARP таблица получена, размер: {output.Length} символов", LogLevel.Debug);

                // Парсим ARP таблицу
                var lines = output.Split('\n');
                int deviceCount = 0;

                foreach (var line in lines)
                {
                    // Ищем строки вида: IP-адрес MAC-адрес тип
                    var match = Regex.Match(line.Trim(), @"(\d+\.\d+\.\d+\.\d+)\s+([0-9a-fA-F-]{17})\s+(\w+)");
                    if (match.Success)
                    {
                        deviceCount++;
                        var ip = match.Groups[1].Value;
                        var mac = match.Groups[2].Value.ToUpper().Replace("-", ":");

                        WriteLog($"ARP запись #{deviceCount}: IP={ip}, MAC={mac}", LogLevel.Debug);

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

                WriteLog($"Обработано {deviceCount} записей из ARP таблицы", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка сканирования ARP: {ex.Message}", LogLevel.Error);
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
                        WriteLog($"Пинг успешен: {ipAddress} ({reply.RoundtripTime}ms)", LogLevel.Debug);

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

                        WriteLog($"Устройство {device.IPAddress}: MAC={device.MACAddress}, Vendor={device.Vendor}, Type={device.DeviceType}", LogLevel.Info);
                        ProcessDevice(device);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка сканирования {ipAddress}: {ex.Message}", LogLevel.Debug);
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
                                    WriteLog($"Найден локальный IP через NetworkInterface: {ip}", LogLevel.Debug);
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
                            WriteLog($"Найден локальный IP через DNS: {ipStr}", LogLevel.Debug);
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
                        WriteLog($"Найден локальный IP через socket: {endPoint.Address}", LogLevel.Debug);
                        return endPoint.Address.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка получения локального IP: {ex.Message}", LogLevel.Error);
            }

            WriteLog("Используем fallback IP: 192.168.1.100", LogLevel.Warning);
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
            {
                WriteLog($"GetVendorFromMAC: MAC адрес пустой или неизвестен", LogLevel.Debug);
                return "Неизвестно";
            }

            try
            {
                WriteLog($"=== ПОИСК ВЕНДОРА ДЛЯ MAC: {macAddress} ===", LogLevel.Debug);

                // Очищаем MAC от всех разделителей
                var cleanMac = macAddress.Replace(":", "").Replace("-", "").Replace(" ", "").ToUpper();
                WriteLog($"Очищенный MAC: {cleanMac}", LogLevel.Debug);

                // Проверяем длину
                if (cleanMac.Length < 6)
                {
                    WriteLog($"ОШИБКА: MAC слишком короткий после очистки: {cleanMac.Length} символов", LogLevel.Warning);
                    return "Неизвестно";
                }

                // Берем первые 6 символов
                var prefix = cleanMac.Substring(0, 6);
                WriteLog($"Префикс для поиска: {prefix}", LogLevel.Debug);

                // Специальные случаи для известных префиксов
                if (prefix.StartsWith("01005E"))
                {
                    return "Multicast адрес";
                }
                if (prefix == "FFFFFF")
                {
                    return "Broadcast адрес";
                }

                lock (_lockObject)
                {
                    WriteLog($"Размер базы данных: {_vendorDatabase.Count} записей", LogLevel.Debug);

                    if (_vendorDatabase.Count == 0)
                    {
                        WriteLog("ОШИБКА: База данных MAC пуста!", LogLevel.Error);
                        return "Неизвестно";
                    }

                    if (_vendorDatabase.ContainsKey(prefix))
                    {
                        var vendor = _vendorDatabase[prefix];
                        WriteLog($"✓ НАЙДЕН ВЕНДОР: {vendor}", LogLevel.Success);
                        return vendor;
                    }
                    else
                    {
                        WriteLog($"✗ Вендор НЕ НАЙДЕН для префикса: {prefix}", LogLevel.Debug);

                        // Пробуем альтернативные префиксы для некоторых производителей
                        var alternativePrefixes = GetAlternativePrefixes(prefix);
                        foreach (var altPrefix in alternativePrefixes)
                        {
                            if (_vendorDatabase.ContainsKey(altPrefix))
                            {
                                var vendor = _vendorDatabase[altPrefix];
                                WriteLog($"✓ НАЙДЕН через альтернативный префикс {altPrefix}: {vendor}", LogLevel.Success);
                                return vendor;
                            }
                        }

                        return DetermineVendorByPattern(prefix);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"КРИТИЧЕСКАЯ ОШИБКА в GetVendorFromMAC: {ex.Message}", LogLevel.Error);
                return "Неизвестно";
            }
        }

        /// <summary>
        /// Получает альтернативные префиксы для поиска
        /// </summary>
        private List<string> GetAlternativePrefixes(string prefix)
        {
            var alternatives = new List<string>();

            // Некоторые производители имеют несколько префиксов
            if (prefix.StartsWith("04D4C4"))
            {
                alternatives.Add("04D9F5"); // ASUSTek
                alternatives.Add("04D3CF"); // ASUSTek
            }
            else if (prefix.StartsWith("C4EB42"))
            {
                alternatives.Add("C4E984"); // TP-Link
                alternatives.Add("C46E1F"); // TP-Link
            }

            return alternatives;
        }

        /// <summary>
        /// Определяет производителя по паттерну MAC адреса
        /// </summary>
        private string DetermineVendorByPattern(string prefix)
        {
            // Известные паттерны для производителей, которых может не быть в базе
            if (prefix.StartsWith("04D4C4"))
            {
                return "ASUSTek Computer Inc. (вероятно)";
            }
            if (prefix.StartsWith("C4EB42"))
            {
                return "TP-Link Technologies (вероятно)";
            }
            if (prefix.StartsWith("04D9F5"))
            {
                return "ASUSTek Computer Inc.";
            }

            // Проверяем первые 2 символа для общих категорий
            var firstTwo = prefix.Substring(0, 2);
            switch (firstTwo)
            {
                case "00":
                    return "Старое сетевое оборудование";
                case "02":
                    return "Локально администрируемый адрес";
                case "AA":
                case "AB":
                case "AC":
                    return "Виртуальная машина (вероятно)";
                default:
                    return "Неизвестно";
            }
        }

        private string DetermineDeviceType(NetworkDevice device)
        {
            var hostname = device.Hostname?.ToLower() ?? "";
            var vendor = device.Vendor?.ToLower() ?? "";

            // Проверяем специальные типы адресов
            if (vendor == "multicast")
                return "📡 Multicast адрес";
            if (vendor == "broadcast")
                return "📢 Broadcast адрес";

            // Сначала пытаемся определить тип только по hostname (более точно)
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
            if (hostname.Contains("galaxy") && (hostname.Contains("tab") || hostname.Contains("note")))
                return "📱 Samsung планшет";
            if (hostname.Contains("galaxy") || hostname.Contains("sm-") || hostname.Contains("phone"))
                return "📱 Samsung телефон";
            if (hostname.Contains("switch"))
                return "🎮 Nintendo Switch";
            if (hostname.Contains("playstation") || hostname.Contains("ps"))
                return "🎮 PlayStation";
            if (hostname.Contains("xbox"))
                return "🎮 Xbox";
            if (hostname.Contains("surface"))
                return "💻 Surface планшет";
            if (hostname.Contains("router") || hostname.Contains("gateway") || hostname.Contains("openwrt"))
                return "🌐 Роутер";
            if (hostname.Contains("printer") || hostname.Contains("canon") || hostname.Contains("epson") || hostname.Contains("hp-"))
                return "🖨️ Принтер";
            if (hostname.Contains("camera") || hostname.Contains("cam") || hostname.Contains("nvr"))
                return "📹 IP камера";
            if (hostname.Contains("tv") || hostname.Contains("smart") || hostname.Contains("roku") || hostname.Contains("chromecast"))
                return "📺 Smart TV";
            if (hostname.Contains("android") || hostname.Contains("mobile"))
                return "📱 Android телефон";
            if (hostname.Contains("tablet") || hostname.Contains("tab-"))
                return "📱 Планшет";

            // Теперь проверяем по вендору из MAC базы данных
            if (!string.IsNullOrEmpty(vendor) && vendor != "неизвестно")
            {
                // Apple устройства
                if (vendor.Contains("apple"))
                    return "📱 Apple устройство";

                // Сетевое оборудование
                if (vendor.Contains("cisco") || vendor.Contains("tp-link") || vendor.Contains("d-link") ||
                    vendor.Contains("netgear") || vendor.Contains("asus") || vendor.Contains("router"))
                    return "🌐 Сетевое оборудование";

                // Принтеры
                if (vendor.Contains("hewlett") || vendor.Contains("canon") || vendor.Contains("epson") ||
                    vendor.Contains("brother") || vendor.Contains("xerox"))
                    return "🖨️ Принтер";

                // Мобильные устройства
                if (vendor.Contains("samsung") || vendor.Contains("xiaomi") || vendor.Contains("huawei") ||
                    vendor.Contains("oppo") || vendor.Contains("vivo") || vendor.Contains("realme"))
                    return "📱 Мобильное устройство";

                // Компьютеры
                if (vendor.Contains("dell") || vendor.Contains("lenovo") || vendor.Contains("acer") ||
                    vendor.Contains("intel") || vendor.Contains("microsoft"))
                    return "💻 Компьютер";

                // Smart TV
                if (vendor.Contains("lg electronics") || vendor.Contains("sony") || vendor.Contains("panasonic"))
                    return "📺 Smart устройство";

                // Игровые консоли
                if (vendor.Contains("nintendo") || vendor.Contains("playstation") || vendor.Contains("xbox"))
                    return "🎮 Игровая консоль";

                // Камеры
                if (vendor.Contains("hikvision") || vendor.Contains("dahua") || vendor.Contains("axis"))
                    return "📹 IP камера";

                // Если вендор известен, но тип не определен
                return $"🔧 {vendor}";
            }

            // Если ничего не найдено
            return "❓ Неизвестное устройство";
        }

        private string DetectOperatingSystem(NetworkDevice device)
        {
            var hostname = device.Hostname?.ToLower() ?? "";

            // Определяем ОС только по hostname (убираем захардкоженные правила для производителей)
            if (hostname.Contains("iphone")) return "iOS (iPhone)";
            if (hostname.Contains("ipad")) return "iPadOS";
            if (hostname.Contains("mac") || hostname.Contains("macbook") || hostname.Contains("imac")) return "macOS";
            if (hostname.Contains("apple-tv") || hostname.Contains("appletv")) return "tvOS";
            if (hostname.Contains("watch")) return "watchOS";
            if (hostname.Contains("android")) return "Android";
            if (hostname.Contains("linux") || hostname.Contains("ubuntu") || hostname.Contains("debian")) return "Linux";
            if (hostname.Contains("windows") || hostname.Contains("desktop") || hostname.Contains("pc")) return "Windows";

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

        /// <summary>
        /// Диагностика базы данных MAC адресов
        /// </summary>
        public void DiagnoseMacDatabase()
        {
            WriteLog("\n=== ДИАГНОСТИКА БАЗЫ ДАННЫХ MAC ===", LogLevel.Info);
            WriteLog($"Путь к файлу: {Path.GetFullPath(_macDatabasePath)}", LogLevel.Info);
            WriteLog($"Файл существует: {File.Exists(_macDatabasePath)}", LogLevel.Info);
            WriteLog($"Текущая директория: {Directory.GetCurrentDirectory()}", LogLevel.Info);

            // Ищем MAC.db в разных местах
            var possiblePaths = new[]
            {
                _macDatabasePath,
                Path.Combine(Directory.GetCurrentDirectory(), "MAC.db"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MAC.db"),
                Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "MAC.db"),
                Path.Combine(Environment.CurrentDirectory, "MAC.db")
            };

            WriteLog("\nПоиск файла MAC.db в возможных местах:", LogLevel.Info);
            string foundPath = null;
            foreach (var path in possiblePaths)
            {
                var exists = File.Exists(path);
                WriteLog($"  {path} -> {(exists ? "НАЙДЕН" : "не найден")}", exists ? LogLevel.Success : LogLevel.Debug);
                if (exists && foundPath == null)
                {
                    foundPath = path;
                    var info = new FileInfo(path);
                    WriteLog($"    Размер: {info.Length} байт, Дата: {info.LastWriteTime}", LogLevel.Info);
                }
            }

            if (foundPath != null && File.Exists(foundPath))
            {
                var lines = File.ReadAllLines(foundPath);
                WriteLog($"\nВсего строк в файле: {lines.Length}", LogLevel.Info);

                // Анализируем первые строки
                WriteLog("\nАнализ первых 10 строк:", LogLevel.Info);
                for (int i = 0; i < Math.Min(10, lines.Length); i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    WriteLog($"\nСтрока {i + 1}: '{line}'", LogLevel.Debug);
                    WriteLog($"  Длина: {line.Length} символов", LogLevel.Debug);

                    // Проверяем наличие табуляции
                    if (line.Contains('\t'))
                    {
                        var parts = line.Split('\t');
                        WriteLog($"  ✓ Найдена табуляция, частей: {parts.Length}", LogLevel.Success);
                        if (parts.Length >= 2)
                        {
                            WriteLog($"  MAC префикс: '{parts[0]}' (длина: {parts[0].Length})", LogLevel.Info);
                            WriteLog($"  Вендор: '{parts[1]}'", LogLevel.Info);
                        }
                    }
                    else
                    {
                        WriteLog($"  ✗ НЕТ ТАБУЛЯЦИИ!", LogLevel.Warning);
                    }
                }
            }

            lock (_lockObject)
            {
                WriteLog($"\n=== СТАТУС БАЗЫ ДАННЫХ В ПАМЯТИ ===", LogLevel.Info);
                WriteLog($"Записей загружено: {_vendorDatabase.Count}", LogLevel.Success);

                if (_vendorDatabase.Count > 0)
                {
                    WriteLog("\nПервые 20 записей из памяти:", LogLevel.Info);
                    foreach (var kvp in _vendorDatabase.Take(20))
                    {
                        WriteLog($"  {kvp.Key} -> {kvp.Value}", LogLevel.Debug);
                    }

                    // Проверяем конкретные префиксы из твоего файла
                    var testPrefixes = new[] { "FC253F", "FC019E", "FC01CD", "FC10BD", "FC1186" };
                    WriteLog("\nПроверка префиксов из примера:", LogLevel.Info);
                    foreach (var prefix in testPrefixes)
                    {
                        if (_vendorDatabase.ContainsKey(prefix))
                        {
                            WriteLog($"  ✓ {prefix} -> {_vendorDatabase[prefix]}", LogLevel.Success);
                        }
                        else
                        {
                            WriteLog($"  ✗ {prefix} -> НЕ НАЙДЕН", LogLevel.Warning);
                        }
                    }
                }
                else
                {
                    WriteLog("⚠️ БАЗА ДАННЫХ ПУСТА!", LogLevel.Error);
                }
            }

            // Тестируем MAC адреса
            var testMacs = new[]
            {
                "FC:25:3F:12:34:56", // Apple из твоего файла
                "FC:01:9E:12:34:56", // VIEVU из твоего файла  
                "FC:01:CD:12:34:56", // FUNDACION TEKNIKER из твоего файла
                "F4-BD-7C-12-34-56", // Тест с дефисами
                "fc253f123456"       // Тест без разделителей
            };

            WriteLog("\n=== ТЕСТИРОВАНИЕ ОПРЕДЕЛЕНИЯ ВЕНДОРОВ ===", LogLevel.Info);
            foreach (var mac in testMacs)
            {
                var vendor = GetVendorFromMAC(mac);
                WriteLog($"\nРезультат для {mac}: {vendor}", vendor != "Неизвестно" ? LogLevel.Success : LogLevel.Warning);
            }

            WriteLog("\n=== КОНЕЦ ДИАГНОСТИКИ ===\n", LogLevel.Info);
        }

        public bool IsRunning => _isRunning;
    }
}