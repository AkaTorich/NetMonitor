using System;
using System.Collections.Generic;
using System.ComponentModel; // Для ListSortDirection
using System.Diagnostics; // Добавляем для EventLog
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net; // Для работы с IP адресами
using System.Text.RegularExpressions; // Добавляем для Regex
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RDPLoginMonitor
{
	public partial class MainForm : Form
	{
		private RDPMonitor _monitor;
		private NetworkMonitor _networkMonitor;
		private SortableBindingList<RDPFailedLogin> _loginAttempts;
		private SortableBindingList<NetworkDevice> _networkDevices;
		private System.Windows.Forms.Timer _statsTimer;         // Указываем точный тип
		private System.Windows.Forms.Timer _networkTimer;       // Указываем точный тип
		private System.Windows.Forms.Timer _autoScanTimer;      // Указываем точный тип
		// === Новое: буферизация логов и дебаунс обновлений устройств ===
		private readonly Queue<(string msg, LogLevel level)> _logQueue = new Queue<(string, LogLevel)>();
		private readonly object _logSync = new object();
		private System.Windows.Forms.Timer _logFlushTimer;
		private const int MaxLogLines = 8000;
		private bool _autoScrollLog = true;
		private readonly Queue<NetworkDevice> _pendingDeviceAdds = new Queue<NetworkDevice>();
		private readonly Queue<NetworkDevice> _pendingDeviceUpdates = new Queue<NetworkDevice>();
		private readonly object _deviceQueueSync = new object();
		private System.Windows.Forms.Timer _deviceFlushTimer;
		private System.Windows.Forms.Timer _resortTimer;
		private bool _needResortNetworkGrid = false;

		// НОВЫЕ ПОЛЯ ДЛЯ УСТРАНЕНИЯ ФЛУДА
		private HashSet<string> _processedEventIds = new HashSet<string>();
		private int _testMessageCount = 0;
		private const int MAX_TEST_MESSAGES = 50;
		private DateTime _lastEventLogRead = DateTime.MinValue;
		private bool _silentMode = false;
		private EventLogWatcher _eventWatcher;
		private bool _isRDPTestRunning = false;

		public MainForm()
		{
			InitializeComponent();
			InitializeMonitors();
			// Инициализация таймеров буферизации
			_logFlushTimer = new System.Windows.Forms.Timer { Interval = 150 };
			_logFlushTimer.Tick += (s, e) => { if (!IsDisposed && IsHandleCreated) FlushLogBuffer(); };
			_logFlushTimer.Start();
			_deviceFlushTimer = new System.Windows.Forms.Timer { Interval = 300 };
			_deviceFlushTimer.Tick += (s, e) => { if (!IsDisposed && IsHandleCreated) FlushDeviceUpdates(); };
			_deviceFlushTimer.Start();
			_resortTimer = new System.Windows.Forms.Timer { Interval = 800 };
			_resortTimer.Tick += (s, e) => { if (!IsDisposed && IsHandleCreated && _needResortNetworkGrid) { _needResortNetworkGrid = false; ResortGridsIfSorted(); } };
			_resortTimer.Start();
		}

		private void InitializeMonitors()
		{
			// ИЗМЕНЕНО: используем SortableBindingList для поддержки сортировки
			_loginAttempts = new SortableBindingList<RDPFailedLogin>();
			_networkDevices = new SortableBindingList<NetworkDevice>();
			// Отключаем мгновенную пересортировку при изменениях, будем сортировать дебаунсом
			_loginAttempts.AutoResortOnChange = false;
			_networkDevices.AutoResortOnChange = false;

			logGrid.DataSource = _loginAttempts;
			networkGrid.DataSource = _networkDevices;

			// Инициализация RDP монитора
			_monitor = new RDPMonitor();

			_monitor.OnFailedLogin += (login) =>
			{
				if (IsDisposed || !IsHandleCreated) return;
				if (InvokeRequired)
				{
					BeginInvoke(new Action(() => AddLoginAttempt(login)));
				}
				else
				{
					AddLoginAttempt(login);
				}
			};

			_monitor.OnSuspiciousActivity += (key, attempts) =>
			{
				if (IsDisposed || !IsHandleCreated) return;
				if (InvokeRequired)
				{
					BeginInvoke(new Action(() => ShowSuspiciousActivity(key, attempts)));
				}
				else
				{
					ShowSuspiciousActivity(key, attempts);
				}
			};

			_monitor.OnLogMessage += (message, level) =>
			{
				if (IsDisposed || !IsHandleCreated) return;
				if (InvokeRequired)
				{
					BeginInvoke(new Action(() => AddLogMessage(message, level)));
				}
				else
				{
					AddLogMessage(message, level);
				}
			};

			// Инициализация сетевого монитора
			_networkMonitor = new NetworkMonitor();

			// ВАЖНО: Сначала подключаем логирование, чтобы видеть загрузку базы MAC
			_networkMonitor.OnLogMessage += (message, level) =>
			{
				if (IsDisposed || !IsHandleCreated) return;
				if (InvokeRequired)
				{
					BeginInvoke(new Action(() => AddLogMessage(message, level)));
				}
				else
				{
					AddLogMessage(message, level);
				}
			};

			// Теперь подключаем остальные события
			_networkMonitor.OnNewDeviceDetected += (device) =>
			{
				if (IsDisposed || !IsHandleCreated) return;
				// Копим добавления и применяем пачкой
				lock (_deviceQueueSync)
				{
					_pendingDeviceAdds.Enqueue(device);
					_needResortNetworkGrid = true;
				}
				if (InvokeRequired)
				{
					BeginInvoke(new Action(() => { /* пусто, только чтобы дернуть очередь таймером */ }));
				}
			};

			_networkMonitor.OnDeviceStatusChanged += (device) =>
			{
				if (IsDisposed || !IsHandleCreated) return;
				// Копим апдейты и применяем пачкой
				lock (_deviceQueueSync)
				{
					_pendingDeviceUpdates.Enqueue(device);
				}
				if (InvokeRequired)
				{
					BeginInvoke(new Action(() => { /* пусто, только чтобы дернуть очередь таймером */ }));
				}
			};

			// Таймеры - указываем точный тип System.Windows.Forms.Timer
			_statsTimer = new System.Windows.Forms.Timer { Interval = 5000 };
			_statsTimer.Tick += StatsTimer_Tick;

			_networkTimer = new System.Windows.Forms.Timer { Interval = 10000 };
			_networkTimer.Tick += NetworkTimer_Tick;

			_autoScanTimer = new System.Windows.Forms.Timer { Interval = 300000 }; // 5 минут по умолчанию
			_autoScanTimer.Tick += AutoScanTimer_Tick;

			// Подключаем события после создания элементов
			this.Load += MainForm_Load;

			// Инициализация колонок для статистики
			InitializeStatisticsView();
		}

		// Пакетная дорисовка логов в RichTextBox
		private void FlushLogBuffer()
		{
			if (IsDisposed || logTextBox == null || logTextBox.IsDisposed) return;
			(List<(string msg, LogLevel level)> batch, bool autoScroll) = (null, false);
			lock (_logSync)
			{
				if (_logQueue.Count == 0) return;
				batch = _logQueue.ToList();
				_logQueue.Clear();
				autoScroll = _autoScrollLog;
			}
			if (batch == null || batch.Count == 0) return;
			// Минимизируем перерисовки
			logTextBox.SuspendLayout();
			int appended = 0;
			foreach (var (msg, level) in batch)
			{
				AppendColoredLine(msg, level);
				appended++;
			}
			TrimLogIfNeeded();
			if (autoScroll)
			{
				logTextBox.SelectionStart = logTextBox.TextLength;
				logTextBox.ScrollToCaret();
			}
			logTextBox.ResumeLayout();
		}

		private void AppendColoredLine(string message, LogLevel level)
		{
			var timestamp = DateTime.Now.ToString("HH:mm:ss");
			var formattedMessage = $"[{timestamp}] {message}";
			Color color = level == LogLevel.Error ? Color.Red :
				level == LogLevel.Warning ? Color.Orange :
				level == LogLevel.Success ? Color.Green :
				level == LogLevel.Network ? Color.Blue :
				level == LogLevel.Security ? Color.Purple :
				level == LogLevel.Debug ? Color.Gray : Color.Black;

			logTextBox.SelectionStart = logTextBox.TextLength;
			logTextBox.SelectionLength = 0;
			logTextBox.SelectionColor = color;
			logTextBox.AppendText(formattedMessage + Environment.NewLine);
			logTextBox.SelectionColor = logTextBox.ForeColor;
		}

		private void TrimLogIfNeeded()
		{
			// Ограничение количества строк для предотвращения лагов
			var lines = logTextBox.Lines;
			if (lines.Length > MaxLogLines)
			{
				var keep = lines.Skip(lines.Length - MaxLogLines).ToArray();
				logTextBox.Lines = keep;
			}
		}

		private void FlushDeviceUpdates()
		{
			if (IsDisposed || networkGrid == null || networkGrid.IsDisposed) return;
			List<NetworkDevice> adds = null;
			List<NetworkDevice> updates = null;
			lock (_deviceQueueSync)
			{
				if (_pendingDeviceAdds.Count == 0 && _pendingDeviceUpdates.Count == 0) return;
				adds = _pendingDeviceAdds.ToList();
				updates = _pendingDeviceUpdates.ToList();
				_pendingDeviceAdds.Clear();
				_pendingDeviceUpdates.Clear();
			}
			if ((adds == null || adds.Count == 0) && (updates == null || updates.Count == 0)) return;

			// Применяем изменения в UI-потоке
			foreach (var d in adds)
			{
				var existingAdd = _networkDevices.FirstOrDefault(x => x.IPAddress == d.IPAddress);
				if (existingAdd == null)
				{
					(_networkDevices as SortableBindingList<NetworkDevice>)?.Add(d);
				}
				else
				{
					existingAdd.MACAddress = d.MACAddress;
					existingAdd.Hostname = d.Hostname;
					existingAdd.Vendor = d.Vendor;
					existingAdd.DeviceType = d.DeviceType;
					existingAdd.Status = d.Status;
					existingAdd.LastSeen = d.LastSeen;
				}
			}
			foreach (var d in updates)
			{
				var existing = _networkDevices.FirstOrDefault(x => x.IPAddress == d.IPAddress);
				if (existing != null)
				{
					existing.Status = d.Status;
					existing.LastSeen = d.LastSeen;
				}
			}
			networkGrid.Refresh();
		}

		private void ResortGridsIfSorted()
		{
			try
			{
				(_networkDevices as SortableBindingList<NetworkDevice>)?.Resort();
				(_loginAttempts as SortableBindingList<RDPFailedLogin>)?.Resort();
			}
			catch { }
		}

		private void InitializeStatisticsView()
		{
			statisticsView.Columns.Clear();
			statisticsView.Columns.Add("Источник", 200);
			statisticsView.Columns.Add("Попыток", 80);
			statisticsView.Columns.Add("Последняя", 100);
			statisticsView.Columns.Add("Статус", 120);
		}

		// НОВЫЕ МЕТОДЫ ДЛЯ СОРТИРОВКИ

		private void ConfigureDataGridViewSorting()
		{
			// Настройка сортировки для logGrid (RDP события)
			ConfigureLogGridSorting();

			// Настройка сортировки для networkGrid (сетевые устройства)
			ConfigureNetworkGridSorting();
		}

		private void ConfigureLogGridSorting()
		{
			try
			{
				// Проверяем что колонки созданы
				if (logGrid.Columns.Count == 0)
				{
					AddLogMessage("⚠️ RDP логи: колонки еще не созданы, пропускаем настройку", LogLevel.Warning);
					return;
				}

				// Включаем сортировку для всех колонок
				foreach (DataGridViewColumn column in logGrid.Columns)
				{
					column.SortMode = DataGridViewColumnSortMode.Automatic;
				}

				// Обработчик событий сортировки для пользовательской логики
				logGrid.ColumnHeaderMouseClick += LogGrid_ColumnHeaderMouseClick;

				AddLogMessage("✅ Сортировка RDP логов настроена успешно", LogLevel.Success);
			}
			catch (Exception ex)
			{
				AddLogMessage($"❌ Ошибка настройки сортировки RDP логов: {ex.Message}", LogLevel.Error);
			}
		}

		private void SetupColumnHeaders()
		{
			try
			{
				// Лёгкая настройка заголовков без тяжёлой логики
				if (logGrid.Columns.Count > 0)
				{
					// Пример: если есть TimeStamp — показываем "Время"
					var timeCol = logGrid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Name.Contains("Time") || c.HeaderText.Contains("Время"));
					if (timeCol != null) timeCol.HeaderText = "🕐 Время";
				}

				if (networkGrid.Columns.Count > 0)
				{
					var ipCol = networkGrid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Name.Contains("IP") || c.HeaderText.Contains("IP"));
					if (ipCol != null) ipCol.HeaderText = "🌐 IP адрес";
				}

				AddLogMessage("✅ Заголовки колонок настроены", LogLevel.Debug);
			}
			catch (Exception ex)
			{
				AddLogMessage($"❌ Ошибка настройки заголовков: {ex.Message}", LogLevel.Error);
			}
		}

		private void AddSortingContextMenu()
		{
			try
			{
				// Контекстное меню для RDP логов
				var logContextMenu = new ContextMenuStrip();
				logContextMenu.Items.Add("📊 Сортировать по времени", null, (s, e) =>
				{
					var col = logGrid.Columns.Cast<DataGridViewColumn>()
						.FirstOrDefault(c => c.Name.Contains("Time") || c.HeaderText.Contains("Время"));
					if (col != null) logGrid.Sort(col, ListSortDirection.Descending);
				});
				logGrid.ContextMenuStrip = logContextMenu;

				// Контекстное меню для сетевых устройств
				var networkContextMenu = new ContextMenuStrip();
				networkContextMenu.Items.Add("🌐 Сортировать по IP", null, (s, e) =>
				{
					var col = networkGrid.Columns.Cast<DataGridViewColumn>()
						.FirstOrDefault(c => c.Name.Contains("IP") || c.HeaderText.Contains("IP"));
					if (col != null) networkGrid.Sort(col, ListSortDirection.Ascending);
				});
				networkGrid.ContextMenuStrip = networkContextMenu;

				AddLogMessage("✅ Контекстные меню сортировки добавлены", LogLevel.Debug);
			}
			catch (Exception ex)
			{
				AddLogMessage($"❌ Ошибка создания контекстных меню: {ex.Message}", LogLevel.Error);
			}
		}

		private void ConfigureNetworkGridSorting()
		{
			try
			{
				// Проверяем что колонки созданы
				if (networkGrid.Columns.Count == 0)
				{
					AddLogMessage("⚠️ Сетевые устройства: колонки еще не созданы, пропускаем настройку", LogLevel.Warning);
					return;
				}

				AddLogMessage($"🔧 Настраиваем сортировку для {networkGrid.Columns.Count} колонок сетевых устройств", LogLevel.Debug);

				// Включаем сортировку для всех колонок
				foreach (DataGridViewColumn column in networkGrid.Columns)
				{
					column.SortMode = DataGridViewColumnSortMode.Automatic;
					AddLogMessage($"   ✓ Колонка '{column.Name}' - сортировка включена", LogLevel.Debug);
				}

				// Обработчик событий сортировки для пользовательской логики
				networkGrid.ColumnHeaderMouseClick += NetworkGrid_ColumnHeaderMouseClick;

				AddLogMessage("✅ Сортировка сетевых устройств настроена успешно", LogLevel.Success);
			}
			catch (Exception ex)
			{
				AddLogMessage($"❌ Ошибка настройки сетевых устройств: {ex.Message}", LogLevel.Error);
			}
		}

		// 5. УПРОЩЕННЫЕ обработчики кликов (убираем специальную обработку IP)
		private void LogGrid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
		{
			try
			{
				var column = logGrid.Columns[e.ColumnIndex];
				var columnName = column.HeaderText;

				// Показываем информацию о сортировке (опционально)
				if (!_silentMode)
				{
					var direction = column.HeaderCell.SortGlyphDirection == SortOrder.Ascending ? "по возрастанию" : "по убыванию";
					AddLogMessage($"📊 Сортировка RDP логов по колонке '{columnName}' {direction}", LogLevel.Debug);
				}
				_needResortNetworkGrid = true;
			}
			catch (Exception ex)
			{
				AddLogMessage($"❌ Ошибка сортировки RDP логов: {ex.Message}", LogLevel.Error);
			}
		}

		// Обработчик клика по заголовку колонки в сетевых устройствах
		private void NetworkGrid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
		{
			try
			{
				var column = networkGrid.Columns[e.ColumnIndex];
				var columnName = column.HeaderText;

				// Показываем информацию о сортировке (опционально)
				if (!_silentMode)
				{
					var direction = column.HeaderCell.SortGlyphDirection == SortOrder.Ascending ? "по возрастанию" : "по убыванию";
					AddLogMessage($"🌐 Сортировка устройств по колонке '{columnName}' {direction}", LogLevel.Debug);
				}
				_needResortNetworkGrid = true;
			}
			catch (Exception ex)
			{
				AddLogMessage($"❌ Ошибка сортировки сетевых устройств: {ex.Message}", LogLevel.Error);
			}
		}

		private void MainForm_Load(object sender, EventArgs e)
		{
			// Подключаем события после полной загрузки формы
			if (autoScanIntervalNum != null)
				autoScanIntervalNum.ValueChanged += AutoScanIntervalNum_ValueChanged;

			if (autoScanCheckBox != null)
				autoScanCheckBox.CheckedChanged += AutoScanCheckBox_CheckedChanged;

			// Добавляем приветственное сообщение
			AddLogMessage("=== RDP & Network Security Monitor v2.1 ===", LogLevel.Info);
			AddLogMessage("Готов к работе. Для диагностики нажми 'Диагностика' или 'Диаг. сети'", LogLevel.Info);

			// УЛУЧШЕННАЯ проверка прав администратора при запуске
			CheckAdminRightsAndOfferRestart();

			// НОВОЕ: Настройка сортировки ПОСЛЕ полной загрузки формы
			// Используем Timer чтобы настроить сортировку после того как все колонки созданы
			var sortingSetupTimer = new System.Windows.Forms.Timer { Interval = 100 };
			sortingSetupTimer.Tick += (s, args) =>
			{
				sortingSetupTimer.Stop();
				sortingSetupTimer.Dispose();

				// Теперь настраиваем сортировку когда колонки точно созданы
				SetupDataGridSortingDelayed();
			};
			sortingSetupTimer.Start();
		}
		// НОВЫЙ МЕТОД: Отложенная настройка сортировки
		private void SetupDataGridSortingDelayed()
		{
			try
			{
				AddLogMessage("🔧 Настройка сортировки таблиц...", LogLevel.Debug);

				// Настройка сортировки для DataGridView
				ConfigureDataGridViewSorting();

				// Настройка заголовков колонок с иконками
				SetupColumnHeaders();

				// Добавление контекстных меню с опциями сортировки
				AddSortingContextMenu();

				AddLogMessage("✅ Сортировка настроена успешно", LogLevel.Success);
			}
			catch (Exception ex)
			{
				AddLogMessage($"❌ Ошибка настройки сортировки: {ex.Message}", LogLevel.Error);
			}
		}
		/// <summary>
		/// Проверяет права администратора и предлагает перезапуск если нужно
		/// </summary>
		private void CheckAdminRightsAndOfferRestart()
		{
			if (!_monitor.IsRunningAsAdministrator())
			{
				AddLogMessage("⚠️ Программа запущена БЕЗ прав администратора", LogLevel.Warning);
				AddLogMessage("📊 Доступные функции: диагностика сети, сканирование MAC базы", LogLevel.Info);
				AddLogMessage("🔒 Ограниченные функции: мониторинг RDP событий, полная диагностика", LogLevel.Warning);

				// Показываем диалог с предложением перезапуска
				var result = MessageBox.Show(
					"🔐 ПРАВА АДМИНИСТРАТОРА\n\n" +
					"Программа запущена без прав администратора.\n\n" +
					"✅ ДОСТУПНО БЕЗ АДМИНА:\n" +
					"• Сканирование сети\n" +
					"• Диагностика MAC базы данных\n" +
					"• Поиск устройств в локальной сети\n" +
					"• Анализ ARP таблицы\n\n" +
					"🔒 ТРЕБУЮТ ПРАВА АДМИНА:\n" +
					"• Мониторинг RDP событий (Security Log)\n" +
					"• Полная диагностика журнала событий\n" +
					"• Детектирование неудачных попыток входа\n\n" +
					"Хочешь перезапустить программу с правами администратора?",
					"Права доступа",
					MessageBoxButtons.YesNoCancel,
					MessageBoxIcon.Question);

				switch (result)
				{
					case DialogResult.Yes:
						RestartAsAdministrator();
						return;

					case DialogResult.No:
						AddLogMessage("✅ Продолжаем работу в ограниченном режиме", LogLevel.Info);
						AddLogMessage("💡 Совет: диагностика сети полностью доступна без прав админа", LogLevel.Info);

						// Отключаем кнопки, требующие права админа
						DisableAdminRequiredFeatures();
						break;

					case DialogResult.Cancel:
						AddLogMessage("❌ Выход из программы по запросу пользователя", LogLevel.Warning);
						this.Close();
						return;
				}
			}
			else
			{
				AddLogMessage("✅ Программа запущена с правами администратора - полный функционал доступен", LogLevel.Success);
			}
		}

		/// <summary>
		/// Перезапускает программу с правами администратора
		/// </summary>
		private void RestartAsAdministrator()
		{
			try
			{
				AddLogMessage("🔄 Перезапускаем программу с правами администратора...", LogLevel.Info);

				var startInfo = new System.Diagnostics.ProcessStartInfo
				{
					FileName = System.Reflection.Assembly.GetExecutingAssembly().Location,
					UseShellExecute = true,
					Verb = "runas" // Запрос прав администратора
				};

				System.Diagnostics.Process.Start(startInfo);
				this.Close();
			}
			catch (Exception ex)
			{
				AddLogMessage($"❌ Ошибка перезапуска: {ex.Message}", LogLevel.Error);
				MessageBox.Show(
					"Не удалось перезапустить программу с правами администратора.\n\n" +
					"Попробуй:\n" +
					"1. Закрыть программу\n" +
					"2. Нажать ПКМ на exe файле\n" +
					"3. Выбрать 'Запуск от имени администратора'\n\n" +
					"Или продолжи работу в текущем режиме.",
					"Ошибка перезапуска",
					MessageBoxButtons.OK,
					MessageBoxIcon.Warning);
			}
		}

		/// <summary>
		/// ПРОСТОЕ отключение кнопок, требующих права администратора
		/// </summary>
		private void DisableAdminRequiredFeatures()
		{
			// Просто отключаем кнопки и делаем их серыми
			startButton.Enabled = false;
			testRDPButton.Enabled = false;

			// Меняем цвет на серый
			startButton.BackColor = Color.LightGray;
			testRDPButton.BackColor = Color.LightGray;

			// Обновляем только подсказку внизу
			testInfoLabel.Text = "💡 Без прав админа доступны: сканирование сети, диагностика MAC базы. Для RDP мониторинга перезапусти как администратор. Кликай по заголовкам для сортировки!";
			testInfoLabel.ForeColor = Color.DarkBlue;
		}

		/// <summary>
		/// Обработчик кнопки перезапуска с правами администратора
		/// </summary>
		private void RestartAsAdminButton_Click(object sender, EventArgs e)
		{
			var result = MessageBox.Show(
				"Перезапустить программу с правами администратора?\n\n" +
				"После перезапуска будут доступны:\n" +
				"• Мониторинг RDP событий\n" +
				"• Анализ журнала Security\n" +
				"• RDP тестирование\n" +
				"• Полная диагностика\n\n" +
				"Текущие данные будут потеряны.",
				"Перезапуск с правами администратора",
				MessageBoxButtons.YesNo,
				MessageBoxIcon.Question);

			if (result == DialogResult.Yes)
			{
				RestartAsAdministrator();
			}
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
					// Добавляем сообщение о старте сетевого мониторинга
					AddLogMessage("Запускаем сетевой мониторинг...", LogLevel.Info);

					_networkMonitor.StartMonitoring();
					_networkTimer.Start();

					// ВАЖНО: Восстанавливаем список устройств после рестарта
					var knownDevices = _networkMonitor.GetAllKnownDevices();
					if (knownDevices.Count > 0)
					{
						AddLogMessage($"Восстанавливаем {knownDevices.Count} известных устройств...", LogLevel.Info);
						foreach (var device in knownDevices)
						{
							var existing = _networkDevices.FirstOrDefault(x => x.IPAddress == device.IPAddress);
							if (existing == null)
							{
								_networkDevices.Add(device);
							}
							else
							{
								existing.MACAddress = device.MACAddress;
								existing.Hostname = device.Hostname;
								existing.Vendor = device.Vendor;
								existing.DeviceType = device.DeviceType;
								existing.Status = device.Status;
								existing.LastSeen = device.LastSeen;
							}
						}
						networkGrid.Refresh();
					}

					// Запуск автосканирования если включено
					if (autoScanCheckBox?.Checked == true)
					{
						_autoScanTimer.Start();
						AddLogMessage($"Автосканирование запущено с интервалом {autoScanIntervalNum?.Value ?? 300} сек", LogLevel.Info);
					}

					// Начальное сканирование выполняется внутри StartMonitoring()
				}

				startButton.Enabled = false;
				stopButton.Enabled = true;
				statusLabel.Text = "Мониторинг активен";
				statusLabel.ForeColor = Color.Green;

				_statsTimer.Start();

				AddLogMessage("✅ Система мониторинга запущена успешно", LogLevel.Success);
				AddLogMessage("Мониторинг RDP событий: 4624, 4625, 4634, 4647, 4778, 4779", LogLevel.Info);
			}
			catch (UnauthorizedAccessException)
			{
				MessageBox.Show("Нет прав доступа к журналу событий.\nЗапусти программу от имени администратора.",
							   "Ошибка доступа", MessageBoxButtons.OK, MessageBoxIcon.Error);
				AddLogMessage("❌ Ошибка: Нет прав доступа к журналу событий", LogLevel.Error);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Ошибка запуска мониторинга: {ex.Message}",
							   "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
				AddLogMessage($"❌ Ошибка запуска: {ex.Message}", LogLevel.Error);
			}
		}
		private void StopButton_Click(object sender, EventArgs e)
		{
			_monitor.StopMonitoring();
			_networkMonitor.StopMonitoring();

			// Останавливаем RDP тест если он запущен
			_isRDPTestRunning = false;

			// Освобождаем EventLogWatcher
			if (_eventWatcher != null)
			{
				try
				{
					_eventWatcher.Dispose();
					_eventWatcher = null;
				}
				catch { }
			}

			startButton.Enabled = true;
			stopButton.Enabled = false;
			statusLabel.Text = "Мониторинг остановлен";
			statusLabel.ForeColor = Color.Red;

			_statsTimer.Stop();
			_networkTimer.Stop();
			_autoScanTimer.Stop();

			AddLogMessage("🛑 Система мониторинга остановлена", LogLevel.Warning);
		}

		private void ClearButton_Click(object sender, EventArgs e)
		{
			_loginAttempts.Clear();
			_networkDevices.Clear();
			logTextBox.Clear();
			statisticsView.Items.Clear();

			// Очищаем также наши новые коллекции
			_processedEventIds.Clear();
			_testMessageCount = 0;

			// ВАЖНО: Очищаем внутренний список устройств в NetworkMonitor
			_networkMonitor.ClearKnownDevices();

			AddLogMessage("🗑️ Данные очищены", LogLevel.Info);
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
						AddLogMessage($"💾 Данные экспортированы в {dialog.FileName}", LogLevel.Success);
					}
					catch (Exception ex)
					{
						MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
						AddLogMessage($"❌ Ошибка сохранения: {ex.Message}", LogLevel.Error);
					}
				}
			}
		}

		/// <summary>
		/// ИСПРАВЛЕННАЯ диагностика - безопасная многопоточность
		/// </summary>
		private void DiagnosticButton_Click(object sender, EventArgs e)
		{
			AddLogMessage("🔍 Запуск системной диагностики...", LogLevel.Info);

			// Переключаемся на текстовый лог для показа диагностики
			tabControl.SelectedIndex = 1;

			// Запускаем диагностику в отдельном потоке
			Task.Run(() =>
			{
				try
				{
					// ВСЕГДА доступная диагностика (не требует админа)
					PerformBasicDiagnosticSafe();

					// Диагностика, требующая прав админа
					if (_monitor.IsRunningAsAdministrator())
					{
						BeginInvoke(new Action(() => AddLogMessage("🔐 Запуск расширенной диагностики (с правами админа)...", LogLevel.Info)));

						// Вызываем диагностику RDP из главного потока
											BeginInvoke(new Action(() =>
					{
						try
						{
							_monitor.TestEventLogAccess();
						}
						catch (Exception ex)
						{
							AddLogMessage($"❌ Ошибка RDP диагностики: {ex.Message}", LogLevel.Error);
						}
					}));
					}
					else
					{
						BeginInvoke(new Action(() => AddLogMessage("⚠️ Расширенная диагностика пропущена (нет прав админа)", LogLevel.Warning)));
						BeginInvoke(new Action(() => AddLogMessage("💡 Для проверки RDP событий запусти программу от имени администратора", LogLevel.Info)));
					}

					BeginInvoke(new Action(() =>
					{
						AddLogMessage("✅ Диагностика завершена. Проверь результаты выше.", LogLevel.Success);
						ShowDiagnosticSummary();
					}));
				}
				catch (Exception ex)
				{
					BeginInvoke(new Action(() =>
					{
						AddLogMessage($"❌ Ошибка диагностики: {ex.Message}", LogLevel.Error);
					}));
				}
			});
		}

		/// <summary>
		/// БЕЗОПАСНАЯ базовая диагностика с правильным Invoke
		/// </summary>
		private void PerformBasicDiagnosticSafe()
		{
			BeginInvoke(new Action(() => AddLogMessage("=== БАЗОВАЯ ДИАГНОСТИКА СИСТЕМЫ ===", LogLevel.Info)));

			// 1. Проверка прав
			var hasAdmin = _monitor.IsRunningAsAdministrator();
						BeginInvoke(new Action(() => AddLogMessage($"🔐 Права администратора: {(hasAdmin ? "ДА" : "НЕТ")}",
					  hasAdmin ? LogLevel.Success : LogLevel.Warning)));

			// 2. Проверка сетевого подключения
			BeginInvoke(new Action(() => AddLogMessage("🌐 Проверка сетевого подключения...", LogLevel.Info)));
			try
			{
				var localIP = GetLocalIPForDiagnostic();
				if (!string.IsNullOrEmpty(localIP))
				{
					BeginInvoke(new Action(() => AddLogMessage($"✅ Локальный IP найден: {localIP}", LogLevel.Success)));

					var networkPrefix = GetNetworkPrefix(localIP);
					BeginInvoke(new Action(() => AddLogMessage($"📡 Сетевая подсеть: {networkPrefix}.0/24", LogLevel.Info)));
				}
				else
				{
					BeginInvoke(new Action(() => AddLogMessage("❌ Не удалось определить локальный IP", LogLevel.Error)));
				}
			}
			catch (Exception ex)
			{
				BeginInvoke(new Action(() => AddLogMessage($"❌ Ошибка проверки сети: {ex.Message}", LogLevel.Error)));
			}

			// 3. Проверка ARP таблицы (доступна без админа)
			BeginInvoke(new Action(() => AddLogMessage("🔍 Проверка ARP таблицы...", LogLevel.Info)));
			try
			{
				var arpDevices = GetARPDevicesCount();
				BeginInvoke(new Action(() => AddLogMessage($"📋 Устройств в ARP таблице: {arpDevices}", LogLevel.Success)));
			}
			catch (Exception ex)
			{
				BeginInvoke(new Action(() => AddLogMessage($"⚠️ Ошибка чтения ARP: {ex.Message}", LogLevel.Warning)));
			}

			// 4. Проверка MAC базы данных (не требует админа) - НЕ через Invoke!
			BeginInvoke(new Action(() => AddLogMessage("📚 Проверка базы данных MAC адресов...", LogLevel.Info)));

			// Диагностика MAC базы вызывается напрямую, так как у неё свой Debug вывод
			_networkMonitor.DiagnoseMacDatabase();

			BeginInvoke(new Action(() => AddLogMessage("=== БАЗОВАЯ ДИАГНОСТИКА ЗАВЕРШЕНА ===", LogLevel.Info)));
		}

		/// <summary>
		/// Показывает итоговую сводку диагностики
		/// </summary>
		private void ShowDiagnosticSummary()
		{
			var hasAdminRights = _monitor.IsRunningAsAdministrator();

			var summary = "🔍 ИТОГИ ДИАГНОСТИКИ\n\n";

			if (hasAdminRights)
			{
				summary += "✅ ПОЛНАЯ ДИАГНОСТИКА ВЫПОЛНЕНА\n\n" +
						  "Доступные функции:\n" +
						  "• Мониторинг RDP событий\n" +
						  "• Сканирование сети\n" +
						  "• Диагностика MAC базы\n" +
						  "• Анализ журнала Security\n\n" +
						  "Рекомендации:\n" +
						  "1. Используй кнопку 'RDP Тест' для проверки\n" +
						  "2. Проверь результаты в текстовом логе\n" +
						  "3. Настрой RDP если нужно";
			}
			else
			{
				summary += "⚠️ ОГРАНИЧЕННАЯ ДИАГНОСТИКА\n\n" +
						  "Доступные функции БЕЗ АДМИНА:\n" +
						  "• Сканирование сети ✅\n" +
						  "• Диагностика MAC базы ✅\n" +
						  "• Поиск устройств ✅\n\n" +
						  "Недоступные функции:\n" +
						  "• Мониторинг RDP событий ❌\n" +
						  "• Анализ журнала Security ❌\n\n" +
						  "Рекомендации:\n" +
						  "1. Для RDP мониторинга перезапусти как админ\n" +
						  "2. Сканирование сети работает полностью\n" +
						  "3. Используй 'Диаг. сети' для проверки MAC базы";
			}

			MessageBox.Show(summary, "Результаты диагностики",
						   MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		/// <summary>
		/// ИСПРАВЛЕННАЯ диагностика сети - полностью работает БЕЗ прав администратора
		/// </summary>
		private void DiagNetworkButton_Click(object sender, EventArgs e)
		{
			AddLogMessage("🌐 Запуск диагностики сетевого мониторинга...", LogLevel.Network);
			AddLogMessage("ℹ️ Эта функция НЕ требует права администратора", LogLevel.Info);

			tabControl.SelectedIndex = 1; // Переключаемся на текстовый лог

			Task.Run(() =>
			{
				try
				{
					// Диагностика MAC базы данных
					BeginInvoke(new Action(() => AddLogMessage("📚 Диагностика базы данных MAC адресов...", LogLevel.Info)));
					_networkMonitor.DiagnoseMacDatabase();

					// Проверка сетевых возможностей
					BeginInvoke(new Action(() => AddLogMessage("🔍 Проверка сетевых функций...", LogLevel.Info)));

					// Тест ARP
					var arpCount = GetARPDevicesCount();
					BeginInvoke(new Action(() => AddLogMessage($"📋 ARP таблица: найдено {arpCount} устройств", LogLevel.Success)));

					// Тест пинга
					BeginInvoke(new Action(() => AddLogMessage("🏓 Тест сетевого доступа...", LogLevel.Info)));
					TestNetworkConnectivitySafe();

					// Проверка возможности сканирования
					BeginInvoke(new Action(() => AddLogMessage("🔍 Тест сканирования сети...", LogLevel.Info)));
					TestNetworkScanningSafe();

									BeginInvoke(new Action(() =>
				{
					AddLogMessage("✅ Диагностика сети завершена", LogLevel.Success);
					
					var message = "🌐 ДИАГНОСТИКА СЕТИ ЗАВЕРШЕНА\n\n" +
								 "✅ ДОСТУПНЫЕ ФУНКЦИИ (БЕЗ АДМИНА):\n" +
								 "• Сканирование локальной сети\n" +
								 "• Определение устройств по MAC\n" +
									 "• Анализ ARP таблицы\n" +
									 "• Ping тестирование\n" +
									 "• Определение производителей устройств\n\n" +
									 $"📊 СТАТИСТИКА:\n" +
									 $"• MAC база: записей загружено\n" +
									 $"• ARP устройств: {arpCount}\n" +
									 $"• Сетевое подключение: проверено\n\n" +
									 "Проверить детали в логах?";

						var result = MessageBox.Show(message, "Диагностика сети",
												   MessageBoxButtons.YesNo, MessageBoxIcon.Information);

						if (result == DialogResult.Yes)
						{
							// Уже на вкладке текстового лога
							AddLogMessage("📝 Проверь результаты диагностики выше", LogLevel.Info);
						}
					}));
				}
				catch (Exception ex)
				{
					BeginInvoke(new Action(() =>
					{
						AddLogMessage($"❌ Ошибка диагностики сети: {ex.Message}", LogLevel.Error);
					}));
				}
			});
		}

		private void TestRDPButton_Click(object sender, EventArgs e)
		{
			if (_isRDPTestRunning)
			{
				// Останавливаем тест если он уже запущен
				_isRDPTestRunning = false;
				testRDPButton.Text = "🎯 RDP Тест";
				testRDPButton.BackColor = Color.LightGreen;
				AddLogMessage("⏹️ RDP тест остановлен пользователем", LogLevel.Warning);
				return;
			}

			AddLogMessage("🔍 Запуск RDP теста...", LogLevel.Info);

			// Переключаемся на текстовый лог для показа результатов
			tabControl.SelectedIndex = 1;

			// Спрашиваем про тихий режим
			var modeResult = MessageBox.Show(
				"Выберите режим мониторинга:\n\n" +
				"ДА = Тихий режим (только важные события)\n" +
				"НЕТ = Подробный режим (все события)\n" +
				"ОТМЕНА = Отменить тест",
				"Режим RDP теста",
				MessageBoxButtons.YesNoCancel,
				MessageBoxIcon.Question);

			if (modeResult == DialogResult.Cancel) return;

			_silentMode = (modeResult == DialogResult.Yes);

			Task.Run(() =>
			{
				try
				{
					// Показываем инструкции
										BeginInvoke(new Action(() =>
					{
						var instructions = "ТЕСТИРОВАНИЕ RDP - ИНСТРУКЦИИ:\n\n" +
								 "1. Убедись что RDP включен на этом компьютере\n" +
								 "2. Открой 'Подключение к удаленному рабочему столу' (mstsc)\n" +
								 "3. Подключись к: 127.0.0.1 или localhost\n" +
								 "4. Попробуй:\n" +
										 "   - Правильный пароль (должно создать LogonType 10)\n" +
										 "   - Неправильный пароль (должно создать событие 4625)\n" +
										 "5. Смотри результаты в текстовом логе\n\n" +
										 $"Режим: {(_silentMode ? "Тихий" : "Подробный")}\n\n" +
										 "Начать тест?";

						var result = MessageBox.Show(instructions, "RDP Тест",
												   MessageBoxButtons.YesNo, MessageBoxIcon.Question);

						if (result == DialogResult.Yes)
						{
							AddLogMessage("🎯 RDP тест запущен. Попробуй подключиться через RDP...", LogLevel.Success);
							AddLogMessage($"📋 Режим мониторинга: {(_silentMode ? "Тихий (только важные события)" : "Подробный (все события)")}", LogLevel.Info);

							// Включаем специальный режим мониторинга RDP
							StartRDPTestMonitoring();
						}
					}));
				}
				catch (Exception ex)
				{
					BeginInvoke(new Action(() =>
					{
						AddLogMessage($"❌ Ошибка RDP теста: {ex.Message}", LogLevel.Error);
					}));
				}
			});
		}

		private void StartRDPTestMonitoring()
		{
			_isRDPTestRunning = true;
			_processedEventIds.Clear();
			_testMessageCount = 0;

			// Обновляем UI кнопки
			BeginInvoke(new Action(() =>
			{
				testRDPButton.Text = "⏹️ Остановить";
				testRDPButton.BackColor = Color.LightCoral;
			}));

			// Специальный режим мониторинга для тестирования RDP
			Task.Run(() =>
			{
				var startTime = DateTime.Now;
				var foundRDP = false;
				var lastProcessedIndex = -1;

				AddLogMessageSafe("🔄 Начинаю мониторинг RDP событий в реальном времени...", LogLevel.Info);

				while (_isRDPTestRunning && (DateTime.Now - startTime).TotalMinutes < 10 && !foundRDP)
				{
					try
					{
						// Ограничиваем количество сообщений
						if (_testMessageCount >= MAX_TEST_MESSAGES && !_silentMode)
						{
							AddLogMessageSafe("⚠️ Достигнут лимит сообщений. Переключаюсь в тихий режим...", LogLevel.Warning);
							_silentMode = true;
						}

						using (var eventLog = new EventLog("Security"))
						{
							if (eventLog.Entries == null || eventLog.Entries.Count == 0)
							{
								if (!_silentMode)
									AddLogMessageSafe("⚠️ Журнал событий пуст или недоступен", LogLevel.Warning);
								continue;
							}

							// Проверяем только новые события после последнего обработанного
							var totalEntries = eventLog.Entries.Count;
							if (lastProcessedIndex >= totalEntries - 1)
							{
								System.Threading.Thread.Sleep(2000);
								continue;
							}

							// Обрабатываем только последние несколько событий
							var startIndex = Math.Max(lastProcessedIndex + 1, totalEntries - 5);
							var eventsProcessed = 0;

							for (int i = startIndex; i < totalEntries && _isRDPTestRunning; i++)
							{
								var entry = eventLog.Entries[i];
								lastProcessedIndex = i;

								// Только события после начала теста
								if (entry.TimeGenerated <= startTime) continue;

								// Создаем уникальный ID для события
								var eventId = $"{entry.TimeGenerated:yyyy-MM-dd HH:mm:ss.fff}_{entry.InstanceId}_{entry.Index}";

								// Пропускаем уже обработанные события
								if (_processedEventIds.Contains(eventId)) continue;
								_processedEventIds.Add(eventId);

								if (entry.InstanceId == 4624 || entry.InstanceId == 4625 || entry.InstanceId == 4634)
								{
									var login = ParseEventLogEntry(entry);
									if (login != null && IsRelevantTestEvent(login, entry.InstanceId))
									{
										eventsProcessed++;
										var message = FormatTestMessage(entry.InstanceId, login);

										if (login.LogonType == "10") // RDP найден!
										{
											foundRDP = true;
											message = $"🎉 УСПЕХ! Найдено RDP событие: {login.Username} с {login.SourceIP}";
											AddLogMessageSafe(message, LogLevel.Success);
											break;
										}
										else if (ShouldDisplayMessage(login))
										{
											AddLogMessageSafe(message, GetLogLevel(entry.InstanceId, login));
										}
									}
								}
							}

							// Показываем статистику периодически
							if (!_silentMode && eventsProcessed > 0)
							{
								var elapsed = DateTime.Now - startTime;
								AddLogMessageSafe($"📊 Обработано {eventsProcessed} новых событий за {elapsed:mm\\:ss}", LogLevel.Debug);
							}
						}
					}
					catch (Exception ex)
					{
						AddLogMessageSafe($"⚠️ Ошибка мониторинга: {ex.Message}", LogLevel.Error);

						// При ошибке увеличиваем задержку
						System.Threading.Thread.Sleep(5000);
						continue;
					}

					// Пауза между проверками
					System.Threading.Thread.Sleep(_silentMode ? 3000 : 2000);
				}

				// Завершение теста
				BeginInvoke(new Action(() =>
				{
					_isRDPTestRunning = false;
					testRDPButton.Text = "🎯 RDP Тест";
					testRDPButton.BackColor = Color.LightGreen;

					if (foundRDP)
					{
						AddLogMessage("✅ RDP ТЕСТ ПРОЙДЕН! Программа корректно обнаруживает RDP события.", LogLevel.Success);
					}
					else
					{
						var elapsed = DateTime.Now - startTime;
						AddLogMessage($"⚠️ RDP события не найдены за {elapsed:mm\\:ss}. Проверь настройки RDP и аудита.", LogLevel.Warning);
						AddLogMessage("💡 Рекомендации:", LogLevel.Info);
						AddLogMessage("   1. Панель управления → Система → Удаленный доступ → Включить", LogLevel.Info);
						AddLogMessage("   2. Проверь что аудит входов включен в локальной политике безопасности", LogLevel.Info);
						AddLogMessage("   3. Попробуй подключиться с другого устройства в сети", LogLevel.Info);
					}

					AddLogMessage($"📈 Статистика теста: обработано {_testMessageCount} сообщений, найдено {_processedEventIds.Count} уникальных событий", LogLevel.Info);
				}));
			});
		}

		// НОВЫЕ ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ДЛЯ УСТРАНЕНИЯ ФЛУДА

		private bool IsRelevantTestEvent(RDPFailedLogin login, long eventId)
		{
			// Всегда показываем события 4625 (неудачные попытки)
			if (eventId == 4625) return true;

			// Игнорируем системные события в тихом режиме
			if (_silentMode && login.LogonType == "5") return false;

			// Игнорируем компьютерные аккаунты (заканчиваются на $) в тихом режиме
			if (_silentMode && login.Username.EndsWith("$")) return false;

			// Показываем только важные типы входа
			var relevantLogonTypes = new[] { "2", "3", "7", "10", "11" };
			return relevantLogonTypes.Contains(login.LogonType);
		}

		private bool ShouldDisplayMessage(RDPFailedLogin login)
		{
			// В тихом режиме показываем меньше сообщений
			if (_silentMode)
			{
				// Показываем только неудачные попытки, RDP (10), разблокировку (7) и сетевые входы (3)
				var importantTypes = new[] { "3", "7", "10" };
				return importantTypes.Contains(login.LogonType) || login.Username != "СИСТЕМА";
			}

			return true;
		}

		private string FormatTestMessage(long eventId, RDPFailedLogin login)
		{
			var eventType = eventId == 4624 ? "Вход" : eventId == 4625 ? "НЕУДАЧА" : "Выход";
			var logonDesc = GetLogonTypeDescription(login.LogonType);

			return $"🔍 {eventType}: {login.Username} с {login.SourceIP} ({logonDesc})";
		}

		private string GetLogonTypeDescription(string logonType)
		{
			switch (logonType)
			{
				case "2": return "Интерактивный";
				case "3": return "Сетевой";
				case "4": return "Пакетный";
				case "5": return "Служба";
				case "7": return "Разблокировка";
				case "8": return "NetworkCleartext";
				case "9": return "NewCredentials";
				case "10": return "🎯 RDP";
				case "11": return "CachedInteractive";
				default: return $"Тип {logonType}";
			}
		}

		private LogLevel GetLogLevel(long eventId, RDPFailedLogin login)
		{
			if (eventId == 4625) return LogLevel.Security; // Неудачные попытки - красным
			if (login.LogonType == "10") return LogLevel.Success; // RDP - зеленым
			if (login.LogonType == "7") return LogLevel.Warning; // Разблокировка - оранжевым
			return LogLevel.Debug; // Остальное - серым
		}

		private void AddLogMessageSafe(string message, LogLevel level)
		{
			if (_testMessageCount < MAX_TEST_MESSAGES || level == LogLevel.Success || level == LogLevel.Error)
			{
				_testMessageCount++;
				if (InvokeRequired)
				{
					BeginInvoke(new Action(() => AddLogMessage(message, level)));
				}
				else
				{
					AddLogMessage(message, level);
				}
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
				AddLogMessage($"Ошибка парсинга EventLogEntry: {ex.Message}", LogLevel.Error);
				return null;
			}
		}

		/// <summary>
		/// Безопасная версия тестирования сетевого подключения (с правильным Invoke)
		/// </summary>
		private void TestNetworkConnectivitySafe()
		{
			try
			{
				// Тест пинга локального шлюза
				using (var ping = new System.Net.NetworkInformation.Ping())
				{
					var localIP = GetLocalIPForDiagnostic();
					if (!string.IsNullOrEmpty(localIP))
					{
						var gateway = GetNetworkPrefix(localIP) + ".1";
						var reply = ping.Send(gateway, 3000);

						if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
						{
							BeginInvoke(new Action(() => AddLogMessage($"✅ Ping шлюза {gateway}: успешно ({reply.RoundtripTime}ms)", LogLevel.Success)));
						}
						else
						{
							BeginInvoke(new Action(() => AddLogMessage($"⚠️ Ping шлюза {gateway}: {reply.Status}", LogLevel.Warning)));
						}
					}
				}

				// Тест DNS
				try
				{
					var hostEntry = System.Net.Dns.GetHostEntry("google.com");
					BeginInvoke(new Action(() => AddLogMessage("✅ DNS резолюция: работает", LogLevel.Success)));
				}
				catch
				{
					BeginInvoke(new Action(() => AddLogMessage("⚠️ DNS резолюция: проблемы", LogLevel.Warning)));
				}
			}
			catch (Exception ex)
			{
				BeginInvoke(new Action(() => AddLogMessage($"❌ Ошибка тестирования сети: {ex.Message}", LogLevel.Error)));
			}
		}

		/// <summary>
		/// Безопасная версия тестирования сканирования сети (с правильным Invoke)
		/// </summary>
		private void TestNetworkScanningSafe()
		{
			try
			{
				var localIP = GetLocalIPForDiagnostic();
				if (!string.IsNullOrEmpty(localIP))
				{
					BeginInvoke(new Action(() => AddLogMessage($"🔍 Тестовое сканирование с IP: {localIP}", LogLevel.Info)));

					// Быстрый тест сканирования 3 адресов
					var networkPrefix = GetNetworkPrefix(localIP);
					var testIPs = new[] { $"{networkPrefix}.1", $"{networkPrefix}.100", $"{networkPrefix}.254" };
					var foundDevices = 0;

					foreach (var testIP in testIPs)
					{
						try
						{
							using (var ping = new System.Net.NetworkInformation.Ping())
							{
								var reply = ping.Send(testIP, 1000);
								if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
								{
									foundDevices++;
									BeginInvoke(new Action(() => AddLogMessage($"📱 Найдено устройство: {testIP} ({reply.RoundtripTime}ms)", LogLevel.Success)));
								}
							}
						}
						catch { }
					}

					BeginInvoke(new Action(() => AddLogMessage($"📊 Результат теста: найдено {foundDevices} из {testIPs.Length} тестовых адресов", LogLevel.Info)));

					if (foundDevices > 0)
					{
						BeginInvoke(new Action(() => AddLogMessage("✅ Сканирование сети: работает корректно", LogLevel.Success)));
					}
					else
					{
						BeginInvoke(new Action(() => AddLogMessage("⚠️ Сканирование сети: возможны проблемы с файрволом", LogLevel.Warning)));
					}
				}
			}
			catch (Exception ex)
			{
				BeginInvoke(new Action(() => AddLogMessage($"❌ Ошибка тестирования сканирования: {ex.Message}", LogLevel.Error)));
			}
		}

		/// <summary>
		/// Получает локальный IP для диагностики
		/// </summary>
		private string GetLocalIPForDiagnostic()
		{
			try
			{
				foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
				{
					if (ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
						ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
					{
						foreach (var addr in ni.GetIPProperties().UnicastAddresses)
						{
							if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
							{
								var ip = addr.Address.ToString();
								if (ip.StartsWith("192.168.") || ip.StartsWith("10.") ||
									(ip.StartsWith("172.") && IsInRange172(ip)))
								{
									return ip;
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				// НЕ вызываем AddLogMessage отсюда, так как может быть вызвано из фонового потока
				System.Diagnostics.Debug.WriteLine($"Ошибка получения IP: {ex.Message}");
			}
			return null;
		}

		/// <summary>
		/// Проверяет диапазон 172.16-31.x.x
		/// </summary>
		private bool IsInRange172(string ip)
		{
			try
			{
				var parts = ip.Split('.');
				if (parts.Length >= 2)
				{
					var secondOctet = int.Parse(parts[1]);
					return secondOctet >= 16 && secondOctet <= 31;
				}
			}
			catch { }
			return false;
		}

		/// <summary>
		/// Получает префикс сети
		/// </summary>
		private string GetNetworkPrefix(string ipAddress)
		{
			var parts = ipAddress.Split('.');
			return $"{parts[0]}.{parts[1]}.{parts[2]}";
		}

		/// <summary>
		/// Подсчитывает устройства в ARP таблице
		/// </summary>
		private int GetARPDevicesCount()
		{
			try
			{
				var process = new System.Diagnostics.Process
				{
					StartInfo = new System.Diagnostics.ProcessStartInfo
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

				// Подсчитываем строки с MAC адресами
				var lines = output.Split('\n');
				var deviceCount = 0;

				foreach (var line in lines)
				{
					if (System.Text.RegularExpressions.Regex.IsMatch(line.Trim(),
						@"\d+\.\d+\.\d+\.\d+\s+[0-9a-fA-F-]{17}\s+\w+"))
					{
						deviceCount++;
					}
				}

				return deviceCount;
			}
			catch
			{
				return 0;
			}
		}

		private void AddLoginAttempt(RDPFailedLogin login)
		{
			// ИЗМЕНЕНО: используем метод Insert из SortableBindingList
			(_loginAttempts as SortableBindingList<RDPFailedLogin>)?.Insert(login);

			// Ограничиваем количество записей
			while (_loginAttempts.Count > 1000)
			{
				_loginAttempts.RemoveAt(_loginAttempts.Count - 1);
			}
			// Подсветка строк в зависимости от типа события
			if (logGrid.Rows.Count > 0)
			{
				var row = logGrid.Rows[0];

				if (login.EventType == "Неудачный вход")
				{
					row.DefaultCellStyle.BackColor = Color.LightPink;
					row.DefaultCellStyle.ForeColor = Color.DarkRed;
				}
				else if (login.EventType == "Успешный вход")
				{
					row.DefaultCellStyle.BackColor = Color.LightGreen;
					row.DefaultCellStyle.ForeColor = Color.DarkGreen;
				}
				else if (login.EventType == "Подозрительная активность")
				{
					row.DefaultCellStyle.BackColor = Color.Orange;
					row.DefaultCellStyle.ForeColor = Color.DarkOrange;
				}
				else if (login.EventType == "Блокировка аккаунта")
				{
					row.DefaultCellStyle.BackColor = Color.Red;
					row.DefaultCellStyle.ForeColor = Color.White;
				}
				else
				{
					row.DefaultCellStyle.BackColor = Color.White;
					row.DefaultCellStyle.ForeColor = Color.Black;
				}
			}

			// Обновляем статус в строке состояния с деталями
			string emoji;
			if (login.EventType == "Успешный вход")
				emoji = "✅";
			else if (login.EventType == "Неудачный вход")
				emoji = "❌";
			else if (login.EventType == "Выход пользователя")
				emoji = "👋";
			else if (login.EventType == "Завершение сеанса")
				emoji = "🔚";
			else
				emoji = "📝";

			statusLabel.Text = $"{emoji} Последнее RDP событие: {login.EventType} - {login.Username}@{login.SourceIP} в {login.TimeStamp:HH:mm:ss}";
		}

		private void AddLogMessage(string message, LogLevel level)
		{
			if (IsDisposed || logTextBox == null || logTextBox.IsDisposed) return;
			// Кладем лог в буфер, дорисуем по таймеру батчем
			lock (_logSync)
			{
				_logQueue.Enqueue((message, level));
				// Автоскролл только если каретка внизу
				_autoScrollLog = logTextBox.SelectionStart >= logTextBox.TextLength - 2;
			}
		}

		private void HandleNewDevice(NetworkDevice device)
		{
			// Добавление перенесено в батчевый апдейтер
			(_networkDevices as SortableBindingList<NetworkDevice>)?.Add(device);
			_needResortNetworkGrid = true;
			
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

			AddLogMessage($"🌐 Обнаружено новое устройство: {device.DeviceType} {device.IPAddress} ({device.Hostname}) - {riskLevel}", LogLevel.Network);

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
				$"🚨 ОБНАРУЖЕНА ПОДОЗРИТЕЛЬНАЯ RDP АКТИВНОСТЬ!\n\n" +
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

			AddLogMessage($"🚨 ТРЕВОГА! Подозрительная RDP активность от {key} ({attempts} попыток)", LogLevel.Security);

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

			AddLogMessage("🔍 Начинаем принудительное сканирование сети...", LogLevel.Info);

			Task.Run(() =>
			{
				_networkMonitor.PerformNetworkScan();

				BeginInvoke(new Action(() =>
				{
					scanNetworkButton.Enabled = true;
					scanNetworkButton.Text = "🔍 Сканировать сеть";
					AddLogMessage("✅ Сканирование сети завершено", LogLevel.Network);

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
			AddLogMessage("🔄 Запущено автоматическое сканирование сети...", LogLevel.Network);
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

					AddLogMessage($"⚙️ Интервал автосканирования изменен на {numUpDown.Value} секунд", LogLevel.Info);
				}
			}
			catch (Exception ex)
			{
				AddLogMessage($"❌ Ошибка изменения интервала: {ex.Message}", LogLevel.Error);
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
						AddLogMessage("🔄 Автоматическое сканирование включено", LogLevel.Info);
					}
					else
					{
						_autoScanTimer.Stop();
						AddLogMessage("⏸️ Автоматическое сканирование выключено", LogLevel.Warning);
					}
				}
			}
			catch (Exception ex)
			{
				AddLogMessage($"❌ Ошибка переключения автосканирования: {ex.Message}", LogLevel.Error);
			}
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			// Останавливаем тест если он запущен
			_isRDPTestRunning = false;

			if (_monitor?.IsRunning == true)
			{
				_monitor.StopMonitoring();
			}

			if (_networkMonitor?.IsRunning == true)
			{
				_networkMonitor.StopMonitoring();
			}

			// Освобождаем EventLogWatcher
			if (_eventWatcher != null)
			{
				try
				{
					_eventWatcher.Dispose();
					_eventWatcher = null;
				}
				catch { }
			}

			_statsTimer?.Stop();
			_networkTimer?.Stop();
			_autoScanTimer?.Stop();
			// Останавливаем и освобождаем наши фоновые таймеры
			try { _logFlushTimer?.Stop(); _logFlushTimer?.Dispose(); } catch { }
			try { _deviceFlushTimer?.Stop(); _deviceFlushTimer?.Dispose(); } catch { }
			try { _resortTimer?.Stop(); _resortTimer?.Dispose(); } catch { }

			base.OnFormClosing(e);
		}
	}
}