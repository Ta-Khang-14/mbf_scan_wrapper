using mbf_scan_service.Logging;
using Serilog;
using System.Drawing;
using System.Windows.Forms;
using System.IO;

namespace mbf_scan_service;

public partial class Form1 : Form
{
    private TextBox _logTextBox = null!;
    private NotifyIcon? _notifyIcon;
    private bool _isExiting = false;

    public Form1()
    {
        InitializeComponent();
        SetupLogDisplay();
        SetupSystemTray();
        Log.Information("MBF Scan Service started - API available at http://localhost:5000");
    }

    private void SetupLogDisplay()
    {
        Text = "MBF Scan Service";
        Size = new Size(800, 500);
        StartPosition = FormStartPosition.CenterScreen;

        var logLabel = new Label
        {
            Text = "Scan Service Log",
            Dock = DockStyle.Top,
            Height = 25,
            TextAlign = ContentAlignment.BottomLeft,
            Padding = new Padding(5, 5, 0, 0)
        };

        _logTextBox = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.LightGreen
        };

        var clearButton = new Button
        {
            Text = "Clear Log",
            Dock = DockStyle.Bottom,
            Height = 30
        };
        clearButton.Click += (_, _) =>
        {
            Invoke(() => _logTextBox.Clear());
        };

        Controls.Add(_logTextBox);
        Controls.Add(logLabel);
        Controls.Add(clearButton);

        var textBoxSink = new TextBoxSink(_logTextBox, this);

        var serilogConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Sink(textBoxSink)
            .WriteTo.File(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "mbf_scan_.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Logger = serilogConfig;
    }

    private void SetupSystemTray()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "MBF Scan Service",
            Visible = true
        };

        // Load generated icon
        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scanner.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                _notifyIcon.Icon = new Icon(iconPath);
            }
            catch
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }
        }
        else
        {
            _notifyIcon.Icon = SystemIcons.Application;
        }

        var contextMenu = new ContextMenuStrip();

        contextMenu.Items.Add("Show Window", null, (_, _) => ShowWindow());
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();

        MinimizeBox = true;
        FormClosing += Form1_FormClosing;
    }

    private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_isExiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _notifyIcon?.ShowBalloonTip(2000, "MBF Scan Service", "Service is still running. Double-click to show window.", ToolTipIcon.Info);
        }
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        Log.Information("Application shutting down...");

        _notifyIcon?.Dispose();
        LoggingSetup.Shutdown();

        Application.Exit();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!_isExiting && !IsHandleCreated)
        {
            CreateHandle();
        }
        base.SetVisibleCore(value);
    }

    private class TextBoxSink : Serilog.Core.ILogEventSink
    {
        private readonly TextBox _textBox;
        private readonly Form _form;
        private readonly Queue<string> _logQueue = new();
        private readonly System.Timers.Timer _flushTimer;
        private readonly object _lock = new();

        public TextBoxSink(TextBox textBox, Form form)
        {
            _textBox = textBox;
            _form = form;
            _flushTimer = new System.Timers.Timer(100);
            _flushTimer.Elapsed += (_, _) => Flush();
            _flushTimer.Start();
        }

        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            var message = $"{logEvent.Timestamp:HH:mm:ss} [{logEvent.Level,-11}] {logEvent.RenderMessage()}";
            if (logEvent.Exception != null)
            {
                message += $"\n  {logEvent.Exception}";
            }

            lock (_lock)
            {
                _logQueue.Enqueue(message);
                if (_logQueue.Count > 1000)
                {
                    for (int i = 0; i < 100; i++)
                        _logQueue.Dequeue();
                }
            }
        }

        private void Flush()
        {
            List<string> messages;
            lock (_lock)
            {
                if (_logQueue.Count == 0) return;
                messages = _logQueue.ToList();
                _logQueue.Clear();
            }

            try
            {
                if (_form.IsDisposed) return;
                _form.Invoke(() =>
                {
                    foreach (var msg in messages)
                    {
                        _textBox.AppendText(msg + Environment.NewLine);
                    }
                    _textBox.SelectionStart = _textBox.Text.Length;
                    _textBox.ScrollToCaret();
                });
            }
            catch { }
        }
    }
}
