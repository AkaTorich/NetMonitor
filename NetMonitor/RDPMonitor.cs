using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Linq;
using System.Security.Principal;

namespace RDPLoginMonitor
{
    /// <summary>
    /// Монитор RDP событий - ОПТИМИЗИРОВАННАЯ ВЕРСИЯ (устраняет зависание)
    /// </summary>
    public class RDPMonitor
    {
        private readonly Dictionary<string, int> _failedAttempts = new Dictionary<string, int>();
        private readonly Dictionary<string, DateTime> _lastAttempt = new Dictionary<string, DateTime>();
        private readonly object _lockObject = new object();
        private bool _isRunning = false;
        private EventLogWatcher _watcher;
        private EventLog _eventLog;

        // НОВЫЕ ПОЛЯ ДЛЯ ОПТИМИЗАЦИИ
        private CancellationTokenSource _cancellationTokenSource;
        private Task _monitoringTask;
        private readonly int MAX_EVENTS_TO_PROCESS = 100; // Ограничиваем количество событий
        private readonly TimeSpan STARTUP_SCAN_WINDOW = TimeSpan.FromMinutes(30); // Только последние 30 минут при запуске

        public int MaxFailedAttempts { get; set; } = 5;
        public TimeSpan TimeWindow { get; set; } = TimeSpan.FromMinutes(15);
        public string LogFilePath { get; set; } = "rdp_monitor.log";

        public event Action<RDPFailedLogin> OnFailedLogin;
        public event Action<string, int> OnSuspiciousActivity;
        public event Action<string, LogLevel> OnLogMessage;

        public bool IsRunningAsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public void StartMonitoring()
        {
            if (_isRunning) return;

            // Проверяем права администратора
            if (!IsRunningAsAdministrator())
            {
                WriteLog("ВНИМАНИЕ: Программа запущена не от имени администратора. Доступ к Security логам может быть ограничен.", LogLevel.Warning);
            }

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            WriteLog("Запускаем RDP Monitor...", LogLevel.Info);

            // ИСПРАВЛЕНИЕ: Быстрая проверка доступности без зависания
            Task.Run(() =>
            {
                if (!QuickCheckEventLogAccess())
                {
                    WriteLog("Не удается получить доступ к журналу Security. Пытаемся использовать альтернативные методы.", LogLevel.Warning);
                }

                // Запускаем мониторинг в отдельной задаче
                _monitoringTask = Task.Run(() => MonitorEventLogOptimized(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
            });

            WriteLog("RDP Monitor запущен", LogLevel.Info);
        }

        // НОВЫЙ МЕТОД: Быстрая проверка доступа без чтения всех событий
        private bool QuickCheckEventLogAccess()
        {
            try
            {
                using (var eventLog = new EventLog("Security"))
                {
                    var count = eventLog.Entries.Count;
                    WriteLog($"Доступ к журналу Security получен. Найдено {count} записей.", LogLevel.Success);

                    // ВАЖНО: Не читаем все события сразу!
                    if (count > 10000)
                    {
                        WriteLog($"Большой журнал ({count} записей). Будем читать только последние события.", LogLevel.Info);
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка доступа к журналу Security: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        public void StopMonitoring()
        {
            WriteLog("Останавливаем RDP Monitor...", LogLevel.Info);

            _isRunning = false;

            // ИСПРАВЛЕНИЕ: Корректная остановка с CancellationToken
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
            }

            // Не блокируем UI-поток: отправляем отмену и продолжаем, задача завершится в фоне
            // Если нужно дождаться, делайте это асинхронно вне UI.

            // Безопасное закрытие EventLogWatcher
            if (_watcher != null)
            {
                try
                {
                    _watcher.Enabled = false;
                    _watcher.EventRecordWritten -= OnEventRecordWritten;
                    _watcher.Dispose();
                    _watcher = null;
                    WriteLog("EventLogWatcher остановлен и освобожден", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    WriteLog($"Ошибка остановки EventLogWatcher: {ex.Message}", LogLevel.Warning);
                }
            }

            // Безопасное закрытие EventLog
            if (_eventLog != null)
            {
                try
                {
                    _eventLog.EnableRaisingEvents = false;
                    _eventLog.EntryWritten -= EventLog_EntryWritten;
                    _eventLog.Dispose();
                    _eventLog = null;
                    WriteLog("EventLog остановлен и освобожден", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    WriteLog($"Ошибка остановки EventLog: {ex.Message}", LogLevel.Warning);
                }
            }

            // Освобождаем ресурсы
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }

            WriteLog("RDP Monitor остановлен", LogLevel.Warning);
        }

        // НОВЫЙ ОПТИМИЗИРОВАННЫЙ МЕТОД МОНИТОРИНГА
        private void MonitorEventLogOptimized(CancellationToken cancellationToken)
        {
            try
            {
                // Метод 1: Пытаемся использовать EventLogWatcher
                WriteLog("Пытаемся использовать EventLogWatcher...", LogLevel.Debug);

                var query = new EventLogQuery("Security", PathType.LogName,
                    "*[System[(EventID=4624 or EventID=4625 or EventID=4634 or EventID=4647 or EventID=4778 or EventID=4779)]]");

                _watcher = new EventLogWatcher(query);
                _watcher.EventRecordWritten += OnEventRecordWritten;
                _watcher.Enabled = true;

                WriteLog("EventLogWatcher успешно запущен. Мониторинг событий: 4624, 4625, 4634, 4647, 4778, 4779", LogLevel.Success);

                // ИСПРАВЛЕНИЕ: Читаем недавние события без зависания
                ReadRecentEventsOptimized(cancellationToken);

                // Основной цикл мониторинга
                while (_isRunning && !cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Периодически очищаем старые записи
                    CleanupOldEntries();

                    // Проверяем состояние каждые 2 секунды
                    Thread.Sleep(2000);
                }

                if (_watcher != null && _watcher.Enabled)
                {
                    _watcher.Enabled = false;
                }
            }
            catch (OperationCanceledException)
            {
                WriteLog("Мониторинг остановлен по запросу пользователя", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка EventLogWatcher: {ex.Message}", LogLevel.Error);
                WriteLog("Переключаемся на EventLog fallback...", LogLevel.Info);

                try
                {
                    // Fallback к обычному EventLog
                    MonitorEventLogFallbackOptimized(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    WriteLog("Fallback мониторинг остановлен по запросу пользователя", LogLevel.Info);
                }
                catch (Exception fallbackEx)
                {
                    WriteLog($"Ошибка в fallback режиме: {fallbackEx.Message}", LogLevel.Error);
                }
            }
        }

        // ОПТИМИЗИРОВАННОЕ ЧТЕНИЕ НЕДАВНИХ СОБЫТИЙ
        private void ReadRecentEventsOptimized(CancellationToken cancellationToken)
        {
            try
            {
                WriteLog("Читаем последние RDP события...", LogLevel.Debug);

                // ИСПРАВЛЕНИЕ: Ограничиваем временное окно для избежания зависания
                var timeFilter = DateTime.Now.Subtract(STARTUP_SCAN_WINDOW);
                var timeFilterString = timeFilter.ToString("yyyy-MM-ddTHH:mm:ss.000Z");

                var query = new EventLogQuery("Security", PathType.LogName,
                    $"*[System[(EventID=4624 or EventID=4625 or EventID=4634 or EventID=4647 or EventID=4778 or EventID=4779) and TimeCreated[@SystemTime >= '{timeFilterString}']]]");

                using (var reader = new EventLogReader(query))
                {
                    EventRecord eventRecord;
                    int count = 0;
                    var processedEvents = 0;

                    while ((eventRecord = reader.ReadEvent()) != null &&
                           count < MAX_EVENTS_TO_PROCESS &&
                           !cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            var login = ParseEventRecord(eventRecord);
                            if (login != null && ShouldProcessEvent(login))
                            {
                                ProcessEventRecord(login, (int)eventRecord.Id);
                                processedEvents++;
                            }
                            count++;
                        }
                        catch (Exception ex)
                        {
                            WriteLog($"Ошибка обработки события: {ex.Message}", LogLevel.Warning);
                        }
                        finally
                        {
                            eventRecord.Dispose();
                        }

                        // Проверяем отмену каждые 10 событий
                        if (count % 10 == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }

                    WriteLog($"Обработано {processedEvents} недавних RDP событий из {count} просмотренных", LogLevel.Info);
                }
            }
            catch (OperationCanceledException)
            {
                throw; // Передаем отмену дальше
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка чтения последних событий: {ex.Message}", LogLevel.Error);
            }
        }

        // ОПТИМИЗИРОВАННЫЙ FALLBACK РЕЖИМ
        private void MonitorEventLogFallbackOptimized(CancellationToken cancellationToken)
        {
            try
            {
                WriteLog("Используем EventLog fallback метод", LogLevel.Info);

                _eventLog = new EventLog("Security");
                _eventLog.EntryWritten += EventLog_EntryWritten;
                _eventLog.EnableRaisingEvents = true;

                WriteLog("EventLog fallback активен", LogLevel.Success);

                // ИСПРАВЛЕНИЕ: Быстрое чтение недавних записей без зависания
                ReadRecentEventLogEntriesOptimized(cancellationToken);

                while (_isRunning && !cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Thread.Sleep(1000);
                    CleanupOldEntries();
                }

                if (_eventLog != null)
                {
                    _eventLog.EnableRaisingEvents = false;
                }
            }
            catch (OperationCanceledException)
            {
                throw; // Передаем отмену дальше
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка EventLog fallback: {ex.Message}", LogLevel.Error);
                WriteLog("Переключаемся на polling режим...", LogLevel.Info);
                MonitorByPollingOptimized(cancellationToken);
            }
        }

        // ОПТИМИЗИРОВАННОЕ ЧТЕНИЕ EVENTLOG ЗАПИСЕЙ
        private void ReadRecentEventLogEntriesOptimized(CancellationToken cancellationToken)
        {
            try
            {
                WriteLog("Читаем последние записи из EventLog...", LogLevel.Debug);

                var totalEntries = _eventLog.Entries.Count;
                var cutoffTime = DateTime.Now.Subtract(STARTUP_SCAN_WINDOW);

                // ИСПРАВЛЕНИЕ: Читаем только последние записи, не все подряд
                var startIndex = Math.Max(0, totalEntries - MAX_EVENTS_TO_PROCESS);
                var processedCount = 0;

                for (int i = totalEntries - 1; i >= startIndex && !cancellationToken.IsCancellationRequested; i--)
                {
                    try
                    {
                        var entry = _eventLog.Entries[i];

                        // Прерываем если событие слишком старое
                        if (entry.TimeGenerated < cutoffTime)
                            break;

                        if (IsInterestingEventId((int)entry.InstanceId))
                        {
                            var login = ParseEventLogEntry(entry);
                            if (login != null && ShouldProcessEvent(login))
                            {
                                ProcessEventLogEntry(login, (int)entry.InstanceId);
                                processedCount++;
                            }
                        }

                        // Проверяем отмену каждые 10 записей
                        if ((totalEntries - i) % 10 == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Ошибка обработки записи EventLog: {ex.Message}", LogLevel.Warning);
                    }
                }

                WriteLog($"Обработано {processedCount} недавних EventLog записей", LogLevel.Info);
            }
            catch (OperationCanceledException)
            {
                throw; // Передаем отмену дальше
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка чтения EventLog записей: {ex.Message}", LogLevel.Error);
            }
        }

        // ОПТИМИЗИРОВАННЫЙ POLLING РЕЖИМ
        private void MonitorByPollingOptimized(CancellationToken cancellationToken)
        {
            try
            {
                WriteLog("Используем polling режим (проверка каждые 10 секунд)", LogLevel.Info);

                var lastCheck = DateTime.Now.AddMinutes(-5);

                while (_isRunning && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var timeFilterString = lastCheck.ToString("yyyy-MM-ddTHH:mm:ss.000Z");
                        var query = new EventLogQuery("Security", PathType.LogName,
                            $"*[System[(EventID=4624 or EventID=4625 or EventID=4634 or EventID=4647 or EventID=4778 or EventID=4779) and TimeCreated[@SystemTime >= '{timeFilterString}']]]");

                        using (var reader = new EventLogReader(query))
                        {
                            EventRecord eventRecord;
                            int count = 0;

                            while ((eventRecord = reader.ReadEvent()) != null &&
                                   count < 50 && // Ограничиваем количество в polling режиме
                                   !cancellationToken.IsCancellationRequested)
                            {
                                try
                                {
                                    var login = ParseEventRecord(eventRecord);
                                    if (login != null && ShouldProcessEvent(login))
                                    {
                                        ProcessEventRecord(login, (int)eventRecord.Id);
                                        count++;
                                    }
                                }
                                finally
                                {
                                    eventRecord.Dispose();
                                }
                            }

                            if (count > 0)
                            {
                                WriteLog($"Polling: обнаружено {count} новых событий", LogLevel.Info);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Ошибка в polling: {ex.Message}", LogLevel.Error);
                    }

                    lastCheck = DateTime.Now;

                    // Ждем 10 секунд с проверкой отмены
                    for (int i = 0; i < 100 && _isRunning && !cancellationToken.IsCancellationRequested; i++)
                    {
                        Thread.Sleep(100);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                WriteLog("Polling режим остановлен по запросу пользователя", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WriteLog($"Критическая ошибка polling: {ex.Message}", LogLevel.Error);
            }
        }

        private bool IsInterestingEventId(int eventId)
        {
            return eventId == 4624 || eventId == 4625 || eventId == 4634 ||
                   eventId == 4647 || eventId == 4778 || eventId == 4779;
        }

        // УЛУЧШЕННАЯ ФИЛЬТРАЦИЯ СОБЫТИЙ
        private bool ShouldProcessEvent(RDPFailedLogin login)
        {
            // Исключаем только очевидно нерелевантные события

            // Исключаем пустые или системные аккаунты только для LogonType 5
            if (login.LogonType == "5" &&
                (string.IsNullOrEmpty(login.Username) ||
                 login.Username == "СИСТЕМА" ||
                 login.Username == "SYSTEM"))
            {
                return false;
            }

            // Разрешаем все остальные события для более полного мониторинга
            return true;
        }

        private void EventLog_EntryWritten(object sender, EntryWrittenEventArgs e)
        {
            try
            {
                var entry = e.Entry;

                if (IsInterestingEventId((int)entry.InstanceId))
                {
                    var login = ParseEventLogEntry(entry);
                    if (login != null && ShouldProcessEvent(login))
                    {
                        // Логируем только важные события (не LogonType 5)
                        if (login.LogonType != "5")
                        {
                            WriteLog($"EventRecord {entry.InstanceId}: {login.Username} с {login.SourceIP} (LogonType: {login.LogonType})", LogLevel.Debug);
                        }

                        ProcessEventLogEntry(login, (int)entry.InstanceId);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка обработки EventLog события: {ex.Message}", LogLevel.Error);
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

                var message = entry.Message ?? "";

                // Извлекаем имя пользователя
                var userMatch = Regex.Match(message, @"Account Name:\s*([^\r\n\t]+)");
                if (!userMatch.Success)
                {
                    userMatch = Regex.Match(message, @"Имя учетной записи:\s*([^\r\n\t]+)");
                }
                login.Username = userMatch.Success ? userMatch.Groups[1].Value.Trim() : "Unknown";

                // Извлекаем IP адрес
                var ipMatch = Regex.Match(message, @"Source Network Address:\s*([^\r\n\t]+)");
                if (!ipMatch.Success)
                {
                    ipMatch = Regex.Match(message, @"Адрес источника в сети:\s*([^\r\n\t]+)");
                }
                login.SourceIP = ipMatch.Success ? ipMatch.Groups[1].Value.Trim() : "Unknown";

                // Извлекаем тип входа
                var logonTypeMatch = Regex.Match(message, @"Logon Type:\s*([^\r\n\t]+)");
                if (!logonTypeMatch.Success)
                {
                    logonTypeMatch = Regex.Match(message, @"Тип входа:\s*([^\r\n\t]+)");
                }
                login.LogonType = logonTypeMatch.Success ? logonTypeMatch.Groups[1].Value.Trim() : "Unknown";

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
            string status, eventType;

            switch (eventId)
            {
                case 4625:
                    status = "Неудачный";
                    eventType = "Неудачный вход";
                    login.Status = status;
                    login.EventType = eventType;
                    ProcessFailedLogin(login);
                    break;

                case 4624:
                    status = "Успешный";
                    eventType = "Успешный вход";
                    login.Status = status;
                    login.EventType = eventType;
                    ProcessSuccessfulLogin(login);
                    break;

                case 4647:
                    status = "Выход";
                    eventType = "Выход пользователя";
                    login.Status = status;
                    login.EventType = eventType;
                    OnFailedLogin?.Invoke(login);
                    break;

                case 4634:
                    status = "Завершение сеанса";
                    eventType = "Завершение сеанса";
                    login.Status = status;
                    login.EventType = eventType;
                    OnFailedLogin?.Invoke(login);
                    break;

                case 4778:
                    status = "Подключение восстановлено";
                    eventType = "RDP переподключение";
                    login.Status = status;
                    login.EventType = eventType;
                    OnFailedLogin?.Invoke(login);
                    break;

                case 4779:
                    status = "Подключение разорвано";
                    eventType = "RDP отключение";
                    login.Status = status;
                    login.EventType = eventType;
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
                var eventId = (int)eventRecord.Id;

                var failedLogin = ParseEventRecord(eventRecord);
                if (failedLogin == null) return;

                // Логируем только важные события (не LogonType 5)
                if (failedLogin.LogonType != "5")
                {
                    WriteLog($"EventRecord {eventId}: {failedLogin.Username} с {failedLogin.SourceIP} (LogonType: {failedLogin.LogonType})", LogLevel.Debug);
                }

                if (ShouldProcessEvent(failedLogin))
                {
                    ProcessEventRecord(failedLogin, eventId);
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка обработки EventRecord: {ex.Message}", LogLevel.Error);
            }
        }

        private void ProcessEventRecord(RDPFailedLogin login, int eventId)
        {
            string status, eventType;

            switch (eventId)
            {
                case 4625:
                    status = "Неудачный";
                    eventType = "Неудачный вход";
                    login.Status = status;
                    login.EventType = eventType;
                    ProcessFailedLogin(login);
                    break;

                case 4624:
                    status = "Успешный";
                    eventType = "Успешный вход";
                    login.Status = status;
                    login.EventType = eventType;
                    ProcessSuccessfulLogin(login);
                    break;

                case 4647:
                    status = "Выход";
                    eventType = "Выход пользователя";
                    login.Status = status;
                    login.EventType = eventType;
                    OnFailedLogin?.Invoke(login);
                    break;

                case 4634:
                    status = "Завершение сеанса";
                    eventType = "Завершение сеанса";
                    login.Status = status;
                    login.EventType = eventType;
                    OnFailedLogin?.Invoke(login);
                    break;

                case 4778:
                    status = "Подключение восстановлено";
                    eventType = "RDP переподключение";
                    login.Status = status;
                    login.EventType = eventType;
                    OnFailedLogin?.Invoke(login);
                    break;

                case 4779:
                    status = "Подключение разорвано";
                    eventType = "RDP отключение";
                    login.Status = status;
                    login.EventType = eventType;
                    OnFailedLogin?.Invoke(login);
                    break;
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
                    EventId = (int)eventRecord.Id,
                    Computer = eventRecord.MachineName
                };

                // Для разных типов событий структура может отличаться
                if (properties.Count > 5)
                {
                    login.Username = properties[5].Value?.ToString() ?? "Unknown";
                }

                if (properties.Count > 18)
                {
                    login.SourceIP = properties[18].Value?.ToString() ?? "Unknown";
                }

                // Для некоторых событий IP в другом месте
                if (login.SourceIP == "Unknown" && properties.Count > 19)
                {
                    login.SourceIP = properties[19].Value?.ToString() ?? "Unknown";
                }

                if (properties.Count > 8)
                {
                    login.LogonType = properties[8].Value?.ToString() ?? "Unknown";
                }

                login.Description = eventRecord.FormatDescription() ?? "Нет описания";

                return login;
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка парсинга EventRecord: {ex.Message}", LogLevel.Error);
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

                // Логируем только важные неудачные попытки
                if (failedLogin.LogonType != "5" && failedLogin.Username != "СИСТЕМА")
                {
                    WriteLog($"Неудачный вход: {failedLogin.Username} с {failedLogin.SourceIP} (попытка #{_failedAttempts[key]}, тип: {failedLogin.LogonType})", LogLevel.Warning);
                }

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
            // Логируем только важные успешные входы
            if (login.LogonType != "5" && login.Username != "СИСТЕМА")
            {
                WriteLog($"Успешный вход: {login.Username} с {login.SourceIP} (тип: {login.LogonType})", LogLevel.Success);
            }

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

        // Метод для тестирования
        public void TestEventLogAccess()
        {
            WriteLog("=== ДИАГНОСТИКА ДОСТУПА К ЖУРНАЛУ СОБЫТИЙ ===", LogLevel.Info);

            try
            {
                // Проверяем права администратора
                WriteLog($"Права администратора: {(IsRunningAsAdministrator() ? "ДА" : "НЕТ")}",
                    IsRunningAsAdministrator() ? LogLevel.Success : LogLevel.Warning);

                // ИСПРАВЛЕНИЕ: Быстрая проверка без чтения всех событий
                using (var eventLog = new EventLog("Security"))
                {
                    var count = eventLog.Entries.Count;
                    WriteLog($"Доступ к Security журналу: ДА, записей: {count}", LogLevel.Success);

                    // Читаем только последние 20 записей для анализа
                    WriteLog("Анализ последних 20 записей Security журнала:", LogLevel.Info);

                    var loginTypeCounts = new Dictionary<string, int>();
                    var rdpCount = 0;
                    var entriesAnalyzed = 0;

                    // ИСПРАВЛЕНИЕ: Читаем с конца, ограничиваем количество
                    for (int i = Math.Max(0, eventLog.Entries.Count - 20); i < eventLog.Entries.Count && entriesAnalyzed < 20; i++)
                    {
                        try
                        {
                            var entry = eventLog.Entries[i];
                            entriesAnalyzed++;

                            WriteLog($"  EventID: {entry.InstanceId}, Time: {entry.TimeGenerated:HH:mm:ss}, Source: {entry.Source}", LogLevel.Debug);

                            // Анализируем логон тайпы
                            if (entry.InstanceId == 4624 || entry.InstanceId == 4625)
                            {
                                var message = entry.Message ?? "";
                                var logonTypeMatch = Regex.Match(message, @"Logon Type:\s*([^\r\n\t]+)");
                                if (logonTypeMatch.Success)
                                {
                                    var logonType = logonTypeMatch.Groups[1].Value.Trim();
                                    if (!loginTypeCounts.ContainsKey(logonType))
                                        loginTypeCounts[logonType] = 0;
                                    loginTypeCounts[logonType]++;

                                    if (logonType == "10")
                                    {
                                        rdpCount++;
                                        WriteLog($"    *** НАЙДЕН RDP ВХОД! LogonType: {logonType} ***", LogLevel.Success);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            WriteLog($"Ошибка анализа записи: {ex.Message}", LogLevel.Warning);
                        }
                    }

                    WriteLog($"--- СТАТИСТИКА ЛОГОН ТАЙПОВ ---", LogLevel.Info);
                    foreach (var kvp in loginTypeCounts.OrderBy(x => x.Key))
                    {
                        var explanation = GetLogonTypeExplanation(kvp.Key);
                        WriteLog($"  LogonType {kvp.Key}: {kvp.Value} раз - {explanation}", LogLevel.Info);
                    }

                    WriteLog($"--- ИТОГИ ---", LogLevel.Info);
                    WriteLog($"Найдено RDP событий (LogonType 10): {rdpCount}",
                        rdpCount > 0 ? LogLevel.Success : LogLevel.Warning);

                    if (rdpCount == 0)
                    {
                        WriteLog("РЕКОМЕНДАЦИИ ДЛЯ RDP ТЕСТИРОВАНИЯ:", LogLevel.Warning);
                        WriteLog("1. Убедись что RDP включен: Панель управления -> Система -> Удаленный доступ", LogLevel.Info);
                        WriteLog("2. Попробуй подключиться через RDP клиент (mstsc) к localhost или IP этого компьютера", LogLevel.Info);
                        WriteLog("3. Включи аудит входов: gpedit.msc -> Audit Policy -> Audit logon events", LogLevel.Info);
                        WriteLog("4. Проверь что у пользователя есть права на RDP подключение", LogLevel.Info);
                    }
                }

                // Быстрая проверка EventLogReader
                WriteLog("Тестируем EventLogReader для RDP событий...", LogLevel.Info);
                try
                {
                    var query = new EventLogQuery("Security", PathType.LogName,
                        "*[System[EventID=4624 or EventID=4625] and EventData[Data[@Name='LogonType']='10']]");

                    using (var reader = new EventLogReader(query))
                    {
                        var rdpEvent = reader.ReadEvent();
                        if (rdpEvent != null)
                        {
                            WriteLog("EventLogReader нашел RDP события!", LogLevel.Success);
                            rdpEvent.Dispose();
                        }
                        else
                        {
                            WriteLog("EventLogReader не нашел RDP событий (LogonType 10)", LogLevel.Warning);
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"Ошибка тестирования EventLogReader: {ex.Message}", LogLevel.Warning);
                }

            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка диагностики: {ex.Message}", LogLevel.Error);
            }

            WriteLog("=== КОНЕЦ ДИАГНОСТИКИ ===", LogLevel.Info);
        }

        private string GetLogonTypeExplanation(string logonType)
        {
            var explanations = new Dictionary<string, string>
            {
                {"0", "Системная загрузка"},
                {"2", "Интерактивный (консоль)"},
                {"3", "Сетевой (SMB, HTTP)"},
                {"4", "Пакетная обработка"},
                {"5", "Служба"},
                {"7", "Разблокировка"},
                {"8", "NetworkCleartext"},
                {"9", "NewCredentials"},
                {"10", "🎯 RDP/Terminal Services"},
                {"11", "CachedInteractive"},
                {"%%2313", "Неизвестный тип"}
            };

            return explanations.ContainsKey(logonType) ? explanations[logonType] : "Неизвестный";
        }
    }
}