using System;
using System.Drawing;
using System.Windows.Forms;

namespace RDPLoginMonitor
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        // Элементы управления
        private Panel controlPanel;
        private Button startButton;
        private Button stopButton;
        private Button clearButton;
        private Button saveButton;
        private Button scanNetworkButton;
        private Button diagnosticButton; // Кнопка диагностики
        private Button testRDPButton; // НОВАЯ КНОПКА RDP тест
        private Button diagNetworkButton; // НОВАЯ КНОПКА MAC тест
        private NumericUpDown maxAttemptsNum;
        private NumericUpDown timeWindowNum;
        private Label statusLabel;
        private Label maxAttemptsLabel;
        private Label timeWindowLabel;
        private CheckBox networkMonitorCheckBox;
        private CheckBox soundNotificationCheckBox;
        private NumericUpDown autoScanIntervalNum;
        private CheckBox autoScanCheckBox;

        // Вкладки
        private TabControl tabControl;
        private TabPage logTab;
        private TabPage textLogTab;
        private TabPage statsTab;
        private TabPage networkTab;

        // Элементы отображения данных
        private DataGridView logGrid;
        private DataGridView networkGrid;
        private RichTextBox logTextBox;
        private ListView statisticsView;

        // Статистика
        private Panel statsPanel;
        private Label totalAttemptsLabel;
        private Label failedAttemptsLabel;
        private Label networkDevicesLabel;
        private Label activeThreatsLabel;

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
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle4 = new System.Windows.Forms.DataGridViewCellStyle();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.controlPanel = new System.Windows.Forms.Panel();
            this.startButton = new System.Windows.Forms.Button();
            this.stopButton = new System.Windows.Forms.Button();
            this.clearButton = new System.Windows.Forms.Button();
            this.saveButton = new System.Windows.Forms.Button();
            this.scanNetworkButton = new System.Windows.Forms.Button();
            this.diagnosticButton = new System.Windows.Forms.Button();
            this.testRDPButton = new System.Windows.Forms.Button();
            this.maxAttemptsLabel = new System.Windows.Forms.Label();
            this.maxAttemptsNum = new System.Windows.Forms.NumericUpDown();
            this.timeWindowLabel = new System.Windows.Forms.Label();
            this.timeWindowNum = new System.Windows.Forms.NumericUpDown();
            this.networkMonitorCheckBox = new System.Windows.Forms.CheckBox();
            this.soundNotificationCheckBox = new System.Windows.Forms.CheckBox();
            this.autoScanLabel = new System.Windows.Forms.Label();
            this.autoScanIntervalNum = new System.Windows.Forms.NumericUpDown();
            this.autoScanCheckBox = new System.Windows.Forms.CheckBox();
            this.statusLabel = new System.Windows.Forms.Label();
            this.diagNetworkButton = new System.Windows.Forms.Button();
            this.testInfoLabel = new System.Windows.Forms.Label();
            this.statsPanel = new System.Windows.Forms.Panel();
            this.totalAttemptsLabel = new System.Windows.Forms.Label();
            this.failedAttemptsLabel = new System.Windows.Forms.Label();
            this.networkDevicesLabel = new System.Windows.Forms.Label();
            this.activeThreatsLabel = new System.Windows.Forms.Label();
            this.tabControl = new System.Windows.Forms.TabControl();
            this.logTab = new System.Windows.Forms.TabPage();
            this.logGrid = new System.Windows.Forms.DataGridView();
            this.textLogTab = new System.Windows.Forms.TabPage();
            this.logTextBox = new System.Windows.Forms.RichTextBox();
            this.statsTab = new System.Windows.Forms.TabPage();
            this.statisticsView = new System.Windows.Forms.ListView();
            this.networkTab = new System.Windows.Forms.TabPage();
            this.networkGrid = new System.Windows.Forms.DataGridView();
            this.controlPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.maxAttemptsNum)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.timeWindowNum)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.autoScanIntervalNum)).BeginInit();
            this.statsPanel.SuspendLayout();
            this.tabControl.SuspendLayout();
            this.logTab.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.logGrid)).BeginInit();
            this.textLogTab.SuspendLayout();
            this.statsTab.SuspendLayout();
            this.networkTab.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.networkGrid)).BeginInit();
            this.SuspendLayout();
            // 
            // controlPanel
            // 
            this.controlPanel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            this.controlPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.controlPanel.Controls.Add(this.startButton);
            this.controlPanel.Controls.Add(this.stopButton);
            this.controlPanel.Controls.Add(this.clearButton);
            this.controlPanel.Controls.Add(this.saveButton);
            this.controlPanel.Controls.Add(this.scanNetworkButton);
            this.controlPanel.Controls.Add(this.diagnosticButton);
            this.controlPanel.Controls.Add(this.testRDPButton);
            this.controlPanel.Controls.Add(this.maxAttemptsLabel);
            this.controlPanel.Controls.Add(this.maxAttemptsNum);
            this.controlPanel.Controls.Add(this.timeWindowLabel);
            this.controlPanel.Controls.Add(this.timeWindowNum);
            this.controlPanel.Controls.Add(this.networkMonitorCheckBox);
            this.controlPanel.Controls.Add(this.soundNotificationCheckBox);
            this.controlPanel.Controls.Add(this.autoScanLabel);
            this.controlPanel.Controls.Add(this.autoScanIntervalNum);
            this.controlPanel.Controls.Add(this.autoScanCheckBox);
            this.controlPanel.Controls.Add(this.statusLabel);
            this.controlPanel.Controls.Add(this.diagNetworkButton);
            this.controlPanel.Controls.Add(this.testInfoLabel);
            this.controlPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.controlPanel.Location = new System.Drawing.Point(0, 0);
            this.controlPanel.Name = "controlPanel";
            this.controlPanel.Size = new System.Drawing.Size(1315, 120);
            this.controlPanel.TabIndex = 0;
            // 
            // startButton
            // 
            this.startButton.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(120)))), ((int)(((byte)(0)))));
            this.startButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(150)))), ((int)(((byte)(0)))));
            this.startButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.startButton.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.startButton.ForeColor = System.Drawing.Color.White;
            this.startButton.Location = new System.Drawing.Point(10, 10);
            this.startButton.Name = "startButton";
            this.startButton.Size = new System.Drawing.Size(100, 35);
            this.startButton.TabIndex = 0;
            this.startButton.Text = "▶ Запустить";
            this.startButton.UseVisualStyleBackColor = false;
            this.startButton.Click += new System.EventHandler(this.StartButton_Click);
            // 
            // stopButton
            // 
            this.stopButton.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
            this.stopButton.Enabled = false;
            this.stopButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
            this.stopButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.stopButton.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.stopButton.ForeColor = System.Drawing.Color.White;
            this.stopButton.Location = new System.Drawing.Point(120, 10);
            this.stopButton.Name = "stopButton";
            this.stopButton.Size = new System.Drawing.Size(100, 35);
            this.stopButton.TabIndex = 1;
            this.stopButton.Text = "⏹ Остановить";
            this.stopButton.UseVisualStyleBackColor = false;
            this.stopButton.Click += new System.EventHandler(this.StopButton_Click);
            // 
            // clearButton
            // 
            this.clearButton.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(80)))), ((int)(((byte)(120)))));
            this.clearButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(100)))), ((int)(((byte)(150)))));
            this.clearButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.clearButton.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.clearButton.ForeColor = System.Drawing.Color.White;
            this.clearButton.Location = new System.Drawing.Point(230, 10);
            this.clearButton.Name = "clearButton";
            this.clearButton.Size = new System.Drawing.Size(100, 35);
            this.clearButton.TabIndex = 2;
            this.clearButton.Text = "🗑 Очистить";
            this.clearButton.UseVisualStyleBackColor = false;
            this.clearButton.Click += new System.EventHandler(this.ClearButton_Click);
            // 
            // saveButton
            // 
            this.saveButton.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(100)))), ((int)(((byte)(0)))));
            this.saveButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(130)))), ((int)(((byte)(0)))));
            this.saveButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.saveButton.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.saveButton.ForeColor = System.Drawing.Color.White;
            this.saveButton.Location = new System.Drawing.Point(340, 10);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new System.Drawing.Size(100, 35);
            this.saveButton.TabIndex = 3;
            this.saveButton.Text = "💾 Сохранить";
            this.saveButton.UseVisualStyleBackColor = false;
            this.saveButton.Click += new System.EventHandler(this.SaveButton_Click);
            // 
            // scanNetworkButton
            // 
            this.scanNetworkButton.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(60)))), ((int)(((byte)(60)))), ((int)(((byte)(90)))));
            this.scanNetworkButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(80)))), ((int)(((byte)(80)))), ((int)(((byte)(120)))));
            this.scanNetworkButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.scanNetworkButton.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.scanNetworkButton.ForeColor = System.Drawing.Color.White;
            this.scanNetworkButton.Location = new System.Drawing.Point(450, 10);
            this.scanNetworkButton.Name = "scanNetworkButton";
            this.scanNetworkButton.Size = new System.Drawing.Size(130, 35);
            this.scanNetworkButton.TabIndex = 4;
            this.scanNetworkButton.Text = "🔍 Сканировать сеть";
            this.scanNetworkButton.UseVisualStyleBackColor = false;
            this.scanNetworkButton.Click += new System.EventHandler(this.ScanNetworkButton_Click);
            // 
            // diagnosticButton
            // 
            this.diagnosticButton.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(90)))), ((int)(((byte)(60)))), ((int)(((byte)(90)))));
            this.diagnosticButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(80)))), ((int)(((byte)(120)))));
            this.diagnosticButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.diagnosticButton.Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Bold);
            this.diagnosticButton.ForeColor = System.Drawing.Color.White;
            this.diagnosticButton.Location = new System.Drawing.Point(590, 10);
            this.diagnosticButton.Name = "diagnosticButton";
            this.diagnosticButton.Size = new System.Drawing.Size(120, 35);
            this.diagnosticButton.TabIndex = 15;
            this.diagnosticButton.Text = "🔍 Диагностика";
            this.diagnosticButton.UseVisualStyleBackColor = false;
            this.diagnosticButton.Click += new System.EventHandler(this.DiagnosticButton_Click);
            // 
            // testRDPButton
            // 
            this.testRDPButton.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(90)))), ((int)(((byte)(60)))));
            this.testRDPButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(120)))), ((int)(((byte)(80)))));
            this.testRDPButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.testRDPButton.Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Bold);
            this.testRDPButton.ForeColor = System.Drawing.Color.White;
            this.testRDPButton.Location = new System.Drawing.Point(720, 10);
            this.testRDPButton.Name = "testRDPButton";
            this.testRDPButton.Size = new System.Drawing.Size(90, 35);
            this.testRDPButton.TabIndex = 16;
            this.testRDPButton.Text = "🎯 RDP Тест";
            this.testRDPButton.UseVisualStyleBackColor = false;
            this.testRDPButton.Click += new System.EventHandler(this.TestRDPButton_Click);
            // 
            // maxAttemptsLabel
            // 
            this.maxAttemptsLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.maxAttemptsLabel.ForeColor = System.Drawing.Color.White;
            this.maxAttemptsLabel.Location = new System.Drawing.Point(10, 55);
            this.maxAttemptsLabel.Name = "maxAttemptsLabel";
            this.maxAttemptsLabel.Size = new System.Drawing.Size(90, 20);
            this.maxAttemptsLabel.TabIndex = 5;
            this.maxAttemptsLabel.Text = "Макс. попыток:";
            // 
            // maxAttemptsNum
            // 
            this.maxAttemptsNum.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(50)))), ((int)(((byte)(50)))));
            this.maxAttemptsNum.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.maxAttemptsNum.ForeColor = System.Drawing.Color.White;
            this.maxAttemptsNum.Location = new System.Drawing.Point(105, 53);
            this.maxAttemptsNum.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.maxAttemptsNum.Name = "maxAttemptsNum";
            this.maxAttemptsNum.Size = new System.Drawing.Size(60, 20);
            this.maxAttemptsNum.TabIndex = 6;
            this.maxAttemptsNum.Value = new decimal(new int[] {
            5,
            0,
            0,
            0});
            // 
            // timeWindowLabel
            // 
            this.timeWindowLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.timeWindowLabel.ForeColor = System.Drawing.Color.White;
            this.timeWindowLabel.Location = new System.Drawing.Point(175, 55);
            this.timeWindowLabel.Name = "timeWindowLabel";
            this.timeWindowLabel.Size = new System.Drawing.Size(70, 20);
            this.timeWindowLabel.TabIndex = 7;
            this.timeWindowLabel.Text = "Окно (мин):";
            // 
            // timeWindowNum
            // 
            this.timeWindowNum.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(50)))), ((int)(((byte)(50)))));
            this.timeWindowNum.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.timeWindowNum.ForeColor = System.Drawing.Color.White;
            this.timeWindowNum.Location = new System.Drawing.Point(250, 53);
            this.timeWindowNum.Maximum = new decimal(new int[] {
            1440,
            0,
            0,
            0});
            this.timeWindowNum.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.timeWindowNum.Name = "timeWindowNum";
            this.timeWindowNum.Size = new System.Drawing.Size(60, 20);
            this.timeWindowNum.TabIndex = 8;
            this.timeWindowNum.Value = new decimal(new int[] {
            15,
            0,
            0,
            0});
            // 
            // networkMonitorCheckBox
            // 
            this.networkMonitorCheckBox.Checked = true;
            this.networkMonitorCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.networkMonitorCheckBox.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.networkMonitorCheckBox.ForeColor = System.Drawing.Color.White;
            this.networkMonitorCheckBox.Location = new System.Drawing.Point(320, 55);
            this.networkMonitorCheckBox.Name = "networkMonitorCheckBox";
            this.networkMonitorCheckBox.Size = new System.Drawing.Size(130, 20);
            this.networkMonitorCheckBox.TabIndex = 9;
            this.networkMonitorCheckBox.Text = "Мониторинг сети";
            // 
            // soundNotificationCheckBox
            // 
            this.soundNotificationCheckBox.Checked = true;
            this.soundNotificationCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.soundNotificationCheckBox.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.soundNotificationCheckBox.ForeColor = System.Drawing.Color.White;
            this.soundNotificationCheckBox.Location = new System.Drawing.Point(460, 55);
            this.soundNotificationCheckBox.Name = "soundNotificationCheckBox";
            this.soundNotificationCheckBox.Size = new System.Drawing.Size(150, 20);
            this.soundNotificationCheckBox.TabIndex = 10;
            this.soundNotificationCheckBox.Text = "Звуковые уведомления";
            // 
            // autoScanLabel
            // 
            this.autoScanLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.autoScanLabel.ForeColor = System.Drawing.Color.White;
            this.autoScanLabel.Location = new System.Drawing.Point(620, 54);
            this.autoScanLabel.Name = "autoScanLabel";
            this.autoScanLabel.Size = new System.Drawing.Size(140, 20);
            this.autoScanLabel.TabIndex = 11;
            this.autoScanLabel.Text = "Автосканирование (сек):";
            // 
            // autoScanIntervalNum
            // 
            this.autoScanIntervalNum.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(50)))), ((int)(((byte)(50)))));
            this.autoScanIntervalNum.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.autoScanIntervalNum.ForeColor = System.Drawing.Color.White;
            this.autoScanIntervalNum.Location = new System.Drawing.Point(765, 53);
            this.autoScanIntervalNum.Maximum = new decimal(new int[] {
            3600,
            0,
            0,
            0});
            this.autoScanIntervalNum.Minimum = new decimal(new int[] {
            30,
            0,
            0,
            0});
            this.autoScanIntervalNum.Name = "autoScanIntervalNum";
            this.autoScanIntervalNum.Size = new System.Drawing.Size(60, 20);
            this.autoScanIntervalNum.TabIndex = 12;
            this.autoScanIntervalNum.Value = new decimal(new int[] {
            300,
            0,
            0,
            0});
            // 
            // autoScanCheckBox
            // 
            this.autoScanCheckBox.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.autoScanCheckBox.ForeColor = System.Drawing.Color.White;
            this.autoScanCheckBox.Location = new System.Drawing.Point(835, 55);
            this.autoScanCheckBox.Name = "autoScanCheckBox";
            this.autoScanCheckBox.Size = new System.Drawing.Size(195, 20);
            this.autoScanCheckBox.TabIndex = 13;
            this.autoScanCheckBox.Text = "Авто-поиск новых устройств";
            // 
            // statusLabel
            // 
            this.statusLabel.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.statusLabel.ForeColor = System.Drawing.Color.LightBlue;
            this.statusLabel.Location = new System.Drawing.Point(946, 15);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(350, 30);
            this.statusLabel.TabIndex = 14;
            this.statusLabel.Text = "Готов к работе";
            // 
            // diagNetworkButton
            // 
            this.diagNetworkButton.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(90)))), ((int)(((byte)(90)))));
            this.diagNetworkButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(120)))), ((int)(((byte)(120)))));
            this.diagNetworkButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.diagNetworkButton.Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Bold);
            this.diagNetworkButton.ForeColor = System.Drawing.Color.White;
            this.diagNetworkButton.Location = new System.Drawing.Point(820, 10);
            this.diagNetworkButton.Name = "diagNetworkButton";
            this.diagNetworkButton.Size = new System.Drawing.Size(120, 35);
            this.diagNetworkButton.TabIndex = 19;
            this.diagNetworkButton.Text = "🌐 Диаг. сети";
            this.diagNetworkButton.UseVisualStyleBackColor = false;
            this.diagNetworkButton.Click += new System.EventHandler(this.DiagNetworkButton_Click);
            // 
            // testInfoLabel
            // 
            this.testInfoLabel.Font = new System.Drawing.Font("Segoe UI", 8F);
            this.testInfoLabel.ForeColor = System.Drawing.Color.LightBlue;
            this.testInfoLabel.Location = new System.Drawing.Point(10, 85);
            this.testInfoLabel.Name = "testInfoLabel";
            this.testInfoLabel.Size = new System.Drawing.Size(1000, 25);
            this.testInfoLabel.TabIndex = 17;
            this.testInfoLabel.Text = "💡 Совет: Для проверки RDP используй кнопку 'RDP Тест'. В тихом режиме меньше сообщений, в подробном - все события. Кликай по заголовкам колонок для сортировки!";
            // 
            // statsPanel
            // 
            this.statsPanel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(25)))), ((int)(((byte)(25)))), ((int)(((byte)(25)))));
            this.statsPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.statsPanel.Controls.Add(this.totalAttemptsLabel);
            this.statsPanel.Controls.Add(this.failedAttemptsLabel);
            this.statsPanel.Controls.Add(this.networkDevicesLabel);
            this.statsPanel.Controls.Add(this.activeThreatsLabel);
            this.statsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.statsPanel.Location = new System.Drawing.Point(0, 706);
            this.statsPanel.Name = "statsPanel";
            this.statsPanel.Size = new System.Drawing.Size(1315, 50);
            this.statsPanel.TabIndex = 1;
            // 
            // totalAttemptsLabel
            // 
            this.totalAttemptsLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.totalAttemptsLabel.ForeColor = System.Drawing.Color.White;
            this.totalAttemptsLabel.Location = new System.Drawing.Point(10, 15);
            this.totalAttemptsLabel.Name = "totalAttemptsLabel";
            this.totalAttemptsLabel.Size = new System.Drawing.Size(150, 20);
            this.totalAttemptsLabel.TabIndex = 0;
            this.totalAttemptsLabel.Text = "Всего попыток: 0";
            // 
            // failedAttemptsLabel
            // 
            this.failedAttemptsLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.failedAttemptsLabel.ForeColor = System.Drawing.Color.LightCoral;
            this.failedAttemptsLabel.Location = new System.Drawing.Point(170, 15);
            this.failedAttemptsLabel.Name = "failedAttemptsLabel";
            this.failedAttemptsLabel.Size = new System.Drawing.Size(120, 20);
            this.failedAttemptsLabel.TabIndex = 1;
            this.failedAttemptsLabel.Text = "Неудачных: 0";
            // 
            // networkDevicesLabel
            // 
            this.networkDevicesLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.networkDevicesLabel.ForeColor = System.Drawing.Color.LightBlue;
            this.networkDevicesLabel.Location = new System.Drawing.Point(300, 15);
            this.networkDevicesLabel.Name = "networkDevicesLabel";
            this.networkDevicesLabel.Size = new System.Drawing.Size(150, 20);
            this.networkDevicesLabel.TabIndex = 2;
            this.networkDevicesLabel.Text = "Устройств в сети: 0";
            // 
            // activeThreatsLabel
            // 
            this.activeThreatsLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.activeThreatsLabel.ForeColor = System.Drawing.Color.Orange;
            this.activeThreatsLabel.Location = new System.Drawing.Point(460, 15);
            this.activeThreatsLabel.Name = "activeThreatsLabel";
            this.activeThreatsLabel.Size = new System.Drawing.Size(150, 20);
            this.activeThreatsLabel.TabIndex = 3;
            this.activeThreatsLabel.Text = "Активных угроз: 0";
            // 
            // tabControl
            // 
            this.tabControl.Controls.Add(this.logTab);
            this.tabControl.Controls.Add(this.textLogTab);
            this.tabControl.Controls.Add(this.statsTab);
            this.tabControl.Controls.Add(this.networkTab);
            this.tabControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.tabControl.Location = new System.Drawing.Point(0, 120);
            this.tabControl.Name = "tabControl";
            this.tabControl.Padding = new System.Drawing.Point(3, 3);
            this.tabControl.SelectedIndex = 0;
            this.tabControl.Size = new System.Drawing.Size(1315, 586);
            this.tabControl.TabIndex = 2;
            // 
            // logTab
            // 
            this.logTab.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.logTab.Controls.Add(this.logGrid);
            this.logTab.Location = new System.Drawing.Point(4, 26);
            this.logTab.Name = "logTab";
            this.logTab.Size = new System.Drawing.Size(1307, 556);
            this.logTab.TabIndex = 0;
            this.logTab.Text = "🔐 Журнал RDP";
            // 
            // logGrid
            // 
            this.logGrid.AllowUserToAddRows = false;
            this.logGrid.BackgroundColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.logGrid.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.logGrid.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            dataGridViewCellStyle1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(50)))), ((int)(((byte)(50)))));
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Segoe UI", 9F);
            dataGridViewCellStyle1.ForeColor = System.Drawing.Color.White;
            dataGridViewCellStyle1.SelectionBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(70)))), ((int)(((byte)(70)))), ((int)(((byte)(70)))));
            dataGridViewCellStyle1.SelectionForeColor = System.Drawing.Color.White;
            this.logGrid.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            this.logGrid.DefaultCellStyle = dataGridViewCellStyle2;
            dataGridViewCellStyle2.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            dataGridViewCellStyle2.Font = new System.Drawing.Font("Segoe UI", 9F);
            dataGridViewCellStyle2.ForeColor = System.Drawing.Color.White;
            dataGridViewCellStyle2.SelectionBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(60)))), ((int)(((byte)(60)))), ((int)(((byte)(60)))));
            dataGridViewCellStyle2.SelectionForeColor = System.Drawing.Color.White;
            this.logGrid.DefaultCellStyle = dataGridViewCellStyle2;
            this.logGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.logGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            this.logGrid.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.logGrid.GridColor = System.Drawing.Color.FromArgb(((int)(((byte)(60)))), ((int)(((byte)(60)))), ((int)(((byte)(60)))));
            this.logGrid.Location = new System.Drawing.Point(0, 0);
            this.logGrid.Name = "logGrid";
            this.logGrid.ReadOnly = true;
            this.logGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.logGrid.Size = new System.Drawing.Size(1307, 556);
            this.logGrid.TabIndex = 0;
            // Включаем возможность сортировки
            this.logGrid.AllowUserToOrderColumns = true;
            this.logGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
            this.logGrid.EnableHeadersVisualStyles = false;
            // 
            // textLogTab
            // 
            this.textLogTab.BackColor = System.Drawing.Color.Black;
            this.textLogTab.Controls.Add(this.logTextBox);
            this.textLogTab.Location = new System.Drawing.Point(4, 26);
            this.textLogTab.Name = "textLogTab";
            this.textLogTab.Size = new System.Drawing.Size(1276, 556);
            this.textLogTab.TabIndex = 1;
            this.textLogTab.Text = "📝 Текстовый лог";
            // 
            // logTextBox
            // 
            this.logTextBox.BackColor = System.Drawing.Color.Black;
            this.logTextBox.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.logTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.logTextBox.Font = new System.Drawing.Font("Consolas", 9F);
            this.logTextBox.ForeColor = System.Drawing.Color.LightGreen;
            this.logTextBox.Location = new System.Drawing.Point(0, 0);
            this.logTextBox.Name = "logTextBox";
            this.logTextBox.ReadOnly = true;
            this.logTextBox.Size = new System.Drawing.Size(1276, 556);
            this.logTextBox.TabIndex = 0;
            this.logTextBox.Text = "";
            // 
            // statsTab
            // 
            this.statsTab.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.statsTab.Controls.Add(this.statisticsView);
            this.statsTab.Location = new System.Drawing.Point(4, 26);
            this.statsTab.Name = "statsTab";
            this.statsTab.Size = new System.Drawing.Size(1276, 556);
            this.statsTab.TabIndex = 2;
            this.statsTab.Text = "📊 Статистика угроз";
            // 
            // statisticsView
            // 
            this.statisticsView.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.statisticsView.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.statisticsView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.statisticsView.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.statisticsView.ForeColor = System.Drawing.Color.White;
            this.statisticsView.FullRowSelect = true;
            this.statisticsView.GridLines = true;
            this.statisticsView.HideSelection = false;
            this.statisticsView.Location = new System.Drawing.Point(0, 0);
            this.statisticsView.Name = "statisticsView";
            this.statisticsView.Size = new System.Drawing.Size(1276, 556);
            this.statisticsView.TabIndex = 0;
            this.statisticsView.UseCompatibleStateImageBehavior = false;
            this.statisticsView.View = System.Windows.Forms.View.Details;
            // Включаем сортировку для ListView
            this.statisticsView.Sorting = System.Windows.Forms.SortOrder.None;
            this.statisticsView.ListViewItemSorter = null;
            // 
            // networkTab
            // 
            this.networkTab.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.networkTab.Controls.Add(this.networkGrid);
            this.networkTab.Location = new System.Drawing.Point(4, 26);
            this.networkTab.Name = "networkTab";
            this.networkTab.Size = new System.Drawing.Size(1276, 556);
            this.networkTab.TabIndex = 3;
            this.networkTab.Text = "🌐 Сетевые устройства";
            // 
            // networkGrid
            // 
            this.networkGrid.AllowUserToAddRows = false;
            this.networkGrid.AllowUserToResizeRows = false;
            this.networkGrid.BackgroundColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.networkGrid.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.networkGrid.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle3;
            dataGridViewCellStyle3.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(50)))), ((int)(((byte)(50)))));
            dataGridViewCellStyle3.Font = new System.Drawing.Font("Segoe UI", 9F);
            dataGridViewCellStyle3.ForeColor = System.Drawing.Color.White;
            dataGridViewCellStyle3.SelectionBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(70)))), ((int)(((byte)(70)))), ((int)(((byte)(70)))));
            dataGridViewCellStyle3.SelectionForeColor = System.Drawing.Color.White;
            this.networkGrid.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle3;
            this.networkGrid.DefaultCellStyle = dataGridViewCellStyle4;
            dataGridViewCellStyle4.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            dataGridViewCellStyle4.Font = new System.Drawing.Font("Segoe UI", 9F);
            dataGridViewCellStyle4.ForeColor = System.Drawing.Color.White;
            dataGridViewCellStyle4.SelectionBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(60)))), ((int)(((byte)(60)))), ((int)(((byte)(60)))));
            dataGridViewCellStyle4.SelectionForeColor = System.Drawing.Color.White;
            this.networkGrid.DefaultCellStyle = dataGridViewCellStyle4;
            this.networkGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.DisplayedCells;
            this.networkGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            this.networkGrid.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.networkGrid.GridColor = System.Drawing.Color.FromArgb(((int)(((byte)(60)))), ((int)(((byte)(60)))), ((int)(((byte)(60)))));
            this.networkGrid.Location = new System.Drawing.Point(0, 0);
            this.networkGrid.Name = "networkGrid";
            this.networkGrid.ReadOnly = true;
            this.networkGrid.RowHeadersVisible = false;
            this.networkGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.networkGrid.Size = new System.Drawing.Size(1276, 556);
            this.networkGrid.TabIndex = 0;
            // Включаем возможность сортировки
            this.networkGrid.AllowUserToOrderColumns = true;
            this.networkGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
            this.networkGrid.EnableHeadersVisualStyles = false;
            // 
            // MainForm
            // 
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(20)))), ((int)(((byte)(20)))), ((int)(((byte)(20)))));
            this.ClientSize = new System.Drawing.Size(1315, 756);
            this.Controls.Add(this.tabControl);
            this.Controls.Add(this.controlPanel);
            this.Controls.Add(this.statsPanel);
            this.ForeColor = System.Drawing.Color.White;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MinimumSize = new System.Drawing.Size(1300, 650);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "RDP & Network Security Monitor v2.1 - Dark Theme with Sorting";
            this.controlPanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.maxAttemptsNum)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.timeWindowNum)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.autoScanIntervalNum)).EndInit();
            this.statsPanel.ResumeLayout(false);
            this.tabControl.ResumeLayout(false);
            this.logTab.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.logGrid)).EndInit();
            this.textLogTab.ResumeLayout(false);
            this.statsTab.ResumeLayout(false);
            this.networkTab.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.networkGrid)).EndInit();
            this.ResumeLayout(false);

        }

        private Label autoScanLabel;
        private Label testInfoLabel; // Новый элемент для подсказок
    }
}