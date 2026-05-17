using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace LengLeng.KeyboardLayoutIndicator;

internal sealed class TrayIconHost : IDisposable
{
    private readonly string _settingsPath;
    private readonly Func<bool> _beforePhysicalKeyDown;
    private readonly Action _afterPhysicalKeyUp;
    private readonly Action<uint> _beforePhysicalNonIndicatorKeyDown;
    private readonly Action _requestLayoutRefresh;
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private TrayIconContext? _context;
    private SynchronizationContext? _syncContext;

    public TrayIconHost(
        string settingsPath,
        Func<bool> beforePhysicalKeyDown,
        Action afterPhysicalKeyUp,
        Action<uint> beforePhysicalNonIndicatorKeyDown,
        Action requestLayoutRefresh)
    {
        _settingsPath = settingsPath;
        _beforePhysicalKeyDown = beforePhysicalKeyDown;
        _afterPhysicalKeyUp = afterPhysicalKeyUp;
        _beforePhysicalNonIndicatorKeyDown = beforePhysicalNonIndicatorKeyDown;
        _requestLayoutRefresh = requestLayoutRefresh;
    }

    public void Start()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "LengLeng tray icon"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public void SetWatchedVirtualKey(ushort virtualKey)
    {
        Post(context => context.SetWatchedVirtualKey(virtualKey));
    }

    public void UpdateStatus(
        LayoutSnapshot layout,
        bool isEnglish,
        string indicatorKey,
        bool userIndicatorState,
        bool actualIndicatorState,
        ForegroundWindowSnapshot foregroundWindow,
        bool indicatorOutputPaused,
        bool showLayoutOverlayForProtectedWindows,
        int layoutOverlayDurationMs)
    {
        Post(context => context.UpdateStatus(
            layout,
            isEnglish,
            indicatorKey,
            userIndicatorState,
            actualIndicatorState,
            foregroundWindow,
            indicatorOutputPaused,
            showLayoutOverlayForProtectedWindows,
            layoutOverlayDurationMs));
    }

    public void Dispose()
    {
        Post(context => context.ExitThread());
        if (_thread is not null && _thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _ready.Dispose();
    }

    private void Run()
    {
        try
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            }
            catch (InvalidOperationException)
            {
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            _context = new TrayIconContext(
                _settingsPath,
                _beforePhysicalKeyDown,
                _afterPhysicalKeyUp,
                _beforePhysicalNonIndicatorKeyDown,
                _requestLayoutRefresh);
            _ready.Set();
            Application.Run(_context);
        }
        catch (Exception ex)
        {
            FileLog.Write("agent", "Tray icon failed.", ex);
            _ready.Set();
        }
    }

    private void Post(Action<TrayIconContext> action)
    {
        if (!_ready.IsSet)
        {
            _ready.Wait(TimeSpan.FromSeconds(5));
        }

        var context = _context;
        var syncContext = _syncContext;
        if (context is null || syncContext is null)
        {
            return;
        }

        syncContext.Post(
            _ =>
            {
                try
                {
                    action(context);
                }
                catch (Exception ex)
                {
                    FileLog.Write("agent", "Tray action failed.", ex);
                }
            },
            null);
    }

    private sealed class TrayIconContext : ApplicationContext
    {
        private readonly string _settingsPath;
        private readonly NotifyIcon _notifyIcon;
        private readonly LockKeyUserInputHook _keyboardHook;
        private readonly ForegroundWindowWatcher _foregroundWindowWatcher;
        private readonly Action _requestLayoutRefresh;
        private readonly LayoutOverlayWindow _layoutOverlayWindow;
        private Icon? _currentIcon;
        private LayoutSnapshot _layout = LayoutSnapshot.Unknown;
        private bool _isEnglish = true;
        private string _indicatorKey = LockKeyCatalog.CapsLock;
        private bool _userIndicatorState;
        private bool _actualIndicatorState;
        private nint _lastOverlayWindow;
        private string _lastOverlayText = string.Empty;
        private DateTime _nextOverlayAllowedUtc = DateTime.MinValue;

        public TrayIconContext(
            string settingsPath,
            Func<bool> beforePhysicalKeyDown,
            Action afterPhysicalKeyUp,
            Action<uint> beforePhysicalNonIndicatorKeyDown,
            Action requestLayoutRefresh)
        {
            _settingsPath = settingsPath;
            _requestLayoutRefresh = requestLayoutRefresh;
            _notifyIcon = new NotifyIcon
            {
                Visible = true,
                Text = "LengLeng Keyboard Layout Indicator",
                ContextMenuStrip = BuildMenu()
            };

            _keyboardHook = new LockKeyUserInputHook
            {
                BeforePhysicalKeyDown = beforePhysicalKeyDown,
                AfterPhysicalKeyUp = afterPhysicalKeyUp,
                BeforePhysicalNonIndicatorKeyDown = beforePhysicalNonIndicatorKeyDown
            };
            _keyboardHook.Start();

            _foregroundWindowWatcher = new ForegroundWindowWatcher(requestLayoutRefresh);
            _foregroundWindowWatcher.Start();

            _layoutOverlayWindow = new LayoutOverlayWindow();

            RefreshIcon();
        }

        public void SetWatchedVirtualKey(ushort virtualKey)
        {
            _keyboardHook.SetWatchedVirtualKey(virtualKey);
        }

        public void UpdateStatus(
            LayoutSnapshot layout,
            bool isEnglish,
            string indicatorKey,
            bool userIndicatorState,
            bool actualIndicatorState,
            ForegroundWindowSnapshot foregroundWindow,
            bool indicatorOutputPaused,
            bool showLayoutOverlayForProtectedWindows,
            int layoutOverlayDurationMs)
        {
            var keyChanged = !string.Equals(_indicatorKey, indicatorKey, StringComparison.OrdinalIgnoreCase);
            var languageChanged = _isEnglish != isEnglish;
            _layout = layout;
            _isEnglish = isEnglish;
            _indicatorKey = LockKeyCatalog.Normalize(indicatorKey);
            _userIndicatorState = userIndicatorState;
            _actualIndicatorState = actualIndicatorState;

            if (keyChanged || languageChanged)
            {
                RefreshIcon();
                _notifyIcon.ContextMenuStrip = BuildMenu();
            }

            var tooltip =
                $"{(_isEnglish ? "ENG" : "OTHER")} | {LockKeyCatalog.GetDisplayName(_indicatorKey)} {(_actualIndicatorState ? "On" : "Off")}";
            _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;

            MaybeShowProtectedWindowOverlay(
                layout,
                isEnglish,
                foregroundWindow,
                indicatorOutputPaused,
                showLayoutOverlayForProtectedWindows,
                layoutOverlayDurationMs,
                languageChanged);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _foregroundWindowWatcher.Dispose();
                _keyboardHook.Dispose();
                _layoutOverlayWindow.Dispose();
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _currentIcon?.Dispose();
            }

            base.Dispose(disposing);
        }

        private ContextMenuStrip BuildMenu()
        {
            var settings = SettingsStore.LoadOrCreate(_settingsPath);
            var menu = new ContextMenuStrip();

            menu.Items.Add(new ToolStripMenuItem(
                $"{(_isEnglish ? "ENG" : "не ENG")} | {_layout.DisplayName} | {settings.IndicatorDisplayName}")
            {
                Enabled = false
            });

            menu.Items.Add(new ToolStripSeparator());

            var keyMenu = new ToolStripMenuItem("Чем мигать");
            foreach (var keyName in LockKeyCatalog.Names)
            {
                var item = new ToolStripMenuItem(LockKeyCatalog.GetDisplayName(keyName))
                {
                    Checked = string.Equals(settings.IndicatorKey, keyName, StringComparison.OrdinalIgnoreCase)
                };
                item.Click += (_, _) => UpdateSettings(
                    settings =>
                    {
                        settings.IndicatorKey = keyName;
                        settings.EnglishIndicatorState = "Preserve";
                    });
                keyMenu.DropDownItems.Add(item);
            }

            menu.Items.Add(keyMenu);

            var englishMenu = new ToolStripMenuItem("На английской раскладке");
            AddEnglishStateItem(englishMenu, settings, "Оставлять как есть", "Preserve");
            AddEnglishStateItem(englishMenu, settings, "Держать включенным", "On");
            AddEnglishStateItem(englishMenu, settings, "Держать выключенным", "Off");
            menu.Items.Add(englishMenu);

            var layoutMenu = new ToolStripMenuItem("Определение раскладки");
            AddLayoutStrategyItem(layoutMenu, settings, "Трей для всех окон", "TrayIndicatorFirst");
            AddLayoutStrategyItem(layoutMenu, settings, "Активное окно", "ForegroundWindow");
            AddLayoutStrategyItem(layoutMenu, settings, "Трей только для выбранных консолей", "TrayIndicatorForConsole");
            menu.Items.Add(layoutMenu);

            menu.Items.Add(new ToolStripSeparator());

            var calibrateItem = new ToolStripMenuItem("Указать область ENG мышью...");
            calibrateItem.Click += (_, _) => CalibrateEnglishIndicator();
            menu.Items.Add(calibrateItem);

            var resetCalibrationItem = new ToolStripMenuItem("Сбросить область ENG")
            {
                Enabled = settings.ManualEnglishIndicatorRect is not null
                    && settings.ManualEnglishIndicatorTemplate is not null
            };
            resetCalibrationItem.Click += (_, _) => UpdateSettings(
                settings =>
                {
                    settings.ManualEnglishIndicatorRect = null;
                    settings.ManualEnglishIndicatorTemplate = null;
                });
            menu.Items.Add(resetCalibrationItem);

            menu.Items.Add(new ToolStripSeparator());

            var openSettingsItem = new ToolStripMenuItem("Открыть настройки");
            openSettingsItem.Click += (_, _) => OpenSettings();
            menu.Items.Add(openSettingsItem);

            menu.Items.Add(new ToolStripSeparator());

            var stopServiceItem = new ToolStripMenuItem("Остановить службу и выйти");
            stopServiceItem.Click += (_, _) => StopServiceAndExit();
            menu.Items.Add(stopServiceItem);

            return menu;
        }

        private void AddEnglishStateItem(
            ToolStripMenuItem parent,
            IndicatorSettings settings,
            string label,
            string value)
        {
            var item = new ToolStripMenuItem(label)
            {
                Checked = string.Equals(settings.EnglishIndicatorState, value, StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) => UpdateSettings(settings => settings.EnglishIndicatorState = value);
            parent.DropDownItems.Add(item);
        }

        private void AddLayoutStrategyItem(
            ToolStripMenuItem parent,
            IndicatorSettings settings,
            string label,
            string value)
        {
            var item = new ToolStripMenuItem(label)
            {
                Checked = string.Equals(settings.LayoutDetectionStrategy, value, StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) => UpdateSettings(settings => settings.LayoutDetectionStrategy = value);
            parent.DropDownItems.Add(item);
        }

        private void UpdateSettings(Action<IndicatorSettings> update)
        {
            var settings = SettingsStore.LoadOrCreate(_settingsPath);
            update(settings);
            SettingsStore.Save(_settingsPath, settings);
            _requestLayoutRefresh();
            _notifyIcon.ContextMenuStrip = BuildMenu();
        }

        private void CalibrateEnglishIndicator()
        {
            using var form = new TrayCalibrationForm();
            if (form.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            Thread.Sleep(180);
            if (!TrayInputIndicatorReader.TryCaptureEnglishTemplate(
                    form.SelectedRectangle,
                    out var template,
                    out var error))
            {
                _notifyIcon.ShowBalloonTip(
                    4000,
                    "LengLeng",
                    error ?? "Не удалось распознать выбранную область.",
                    ToolTipIcon.Warning);
                return;
            }

            UpdateSettings(
                settings =>
                {
                    settings.ManualEnglishIndicatorRect =
                        ScreenRectangle.FromRectangle(form.SelectedRectangle);
                    settings.ManualEnglishIndicatorTemplate = template;
                    settings.LayoutDetectionStrategy = "TrayIndicatorFirst";
                });

            _notifyIcon.ShowBalloonTip(
                3000,
                "LengLeng",
                "Область ENG сохранена.",
                ToolTipIcon.Info);
        }

        private void OpenSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo("notepad.exe", _settingsPath)
                {
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                FileLog.Write("agent", "Cannot open settings.", ex);
            }
        }

        private void StopServiceAndExit()
        {
            try
            {
                Process.Start(new ProcessStartInfo(
                    AppPaths.ExecutablePath,
                    $"--stop-service --config {CommandLine.Quote(_settingsPath)}")
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(AppPaths.ExecutablePath)
                        ?? Environment.CurrentDirectory
                });

                _notifyIcon.ShowBalloonTip(
                    2000,
                    "LengLeng",
                    "Запрошена остановка службы.",
                    ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                FileLog.Write("agent", "Cannot request service stop.", ex);
                _notifyIcon.ShowBalloonTip(
                    4000,
                    "LengLeng",
                    "Не удалось запросить остановку службы.",
                    ToolTipIcon.Error);
            }
        }

        private void RefreshIcon()
        {
            var previous = _currentIcon;
            _currentIcon = TrayIconRenderer.CreateIcon(_isEnglish, _indicatorKey);
            _notifyIcon.Icon = _currentIcon;
            previous?.Dispose();
        }

        private void MaybeShowProtectedWindowOverlay(
            LayoutSnapshot layout,
            bool isEnglish,
            ForegroundWindowSnapshot foregroundWindow,
            bool indicatorOutputPaused,
            bool showLayoutOverlayForProtectedWindows,
            int layoutOverlayDurationMs,
            bool languageChanged)
        {
            if (!showLayoutOverlayForProtectedWindows
                || !indicatorOutputPaused
                || foregroundWindow.Handle == 0
                || foregroundWindow.Bounds.IsEmpty)
            {
                return;
            }

            var text = FormatOverlayText(layout, isEnglish);
            var now = DateTime.UtcNow;
            var activatedProtectedWindow = foregroundWindow.Handle != _lastOverlayWindow;
            var textChanged = !string.Equals(_lastOverlayText, text, StringComparison.OrdinalIgnoreCase);
            if (!activatedProtectedWindow && !languageChanged && !textChanged && now < _nextOverlayAllowedUtc)
            {
                return;
            }

            _lastOverlayWindow = foregroundWindow.Handle;
            _lastOverlayText = text;
            _nextOverlayAllowedUtc = now.AddMilliseconds(layoutOverlayDurationMs + 600);
            _layoutOverlayWindow.ShowLayout(text, foregroundWindow.Bounds, layoutOverlayDurationMs);
        }

        private static string FormatOverlayText(LayoutSnapshot layout, bool isEnglish)
        {
            if (isEnglish)
            {
                return "ENG";
            }

            if (layout.IsKnown
                && !string.IsNullOrWhiteSpace(layout.TwoLetterLanguageName)
                && !string.Equals(layout.TwoLetterLanguageName, "other", StringComparison.OrdinalIgnoreCase))
            {
                return layout.TwoLetterLanguageName.ToUpperInvariant();
            }

            return "OTHER";
        }
    }
}
