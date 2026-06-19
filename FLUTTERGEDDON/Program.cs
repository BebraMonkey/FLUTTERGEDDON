using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FLUTTERGEDDON;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var resources = new ResourcePack();
        using var player = new LoopingMusic(resources.ExtractMusic());
        player.SetVolume(SettingsStore.Load().Volume * 10);
        player.Play();
        Application.Run(new MainForm(resources, player));
    }
}

internal static class AppInfo
{
    public const string UpdateOwner = "BebraMonkey";
    public const string UpdateRepo = "FLUTTERGEDDON";
    public const string UpdateAssetName = "FLUTTERGEDDON.exe";
    public const string Sha256AssetName = "FLUTTERGEDDON.exe.sha256";

    public static string VersionText =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public static bool UpdateConfigured =>
        !UpdateOwner.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(UpdateRepo);
}

internal sealed class ResourcePack : IDisposable
{
    private readonly Assembly _assembly = Assembly.GetExecutingAssembly();
    private readonly List<Image> _images = new();
    private string? _musicPath;

    public IReadOnlyList<Image> Images => _images;
    public Icon AppIcon { get; }
    public Image PanelBackground { get; }
    public Image PanelBackgroundBlink { get; }
    public Image PanelBackgroundProcessing { get; }
    public Image UpdatePony { get; }
    public string Changelog { get; }

    public ResourcePack()
    {
        using var iconStream = OpenResource("fluttergeddon.ico");
        AppIcon = new Icon(iconStream);
        for (var i = 2; i <= 6; i++)
        {
            using var stream = OpenResource($"flutter_{i}.png");
            _images.Add(Image.FromStream(stream));
        }
        using var panelStream = OpenResource("panel_background.png");
        PanelBackground = Image.FromStream(panelStream);
        using var blinkStream = OpenResource("panel_background_blink.png");
        PanelBackgroundBlink = Image.FromStream(blinkStream);
        using var processingStream = OpenResource("panel_background_processing.png");
        PanelBackgroundProcessing = Image.FromStream(processingStream);
        using var updatePonyStream = OpenResource("update_pony.png");
        UpdatePony = Image.FromStream(updatePonyStream);
        Changelog = ReadTextResource("CHANGELOG.txt");
    }

    public Stream OpenResource(string fileName)
    {
        var resourceName = _assembly.GetManifestResourceNames()
            .First(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        return _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Resource not found: {fileName}");
    }

    public string ExtractMusic()
    {
        if (_musicPath is not null)
            return _musicPath;

        _musicPath = Path.Combine(Path.GetTempPath(), "FLUTTERGEDDON_FLUTTERSHY3.mp3");
        using var input = OpenResource("FLUTTERSHY3.mp3");
        using var output = File.Create(_musicPath);
        input.CopyTo(output);
        return _musicPath;
    }

    private string ReadTextResource(string fileName)
    {
        using var input = OpenResource(fileName);
        using var reader = new StreamReader(input, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        foreach (var image in _images)
            image.Dispose();
        AppIcon.Dispose();
        PanelBackground.Dispose();
        PanelBackgroundBlink.Dispose();
        PanelBackgroundProcessing.Dispose();
        UpdatePony.Dispose();
    }
}

internal sealed class LoopingMusic : IDisposable
{
    private readonly string _alias;

    public LoopingMusic(string filePath)
    {
        _alias = "fluttergeddon_" + Process.GetCurrentProcess().Id;
        MciSendString($"open \"{filePath}\" type mpegvideo alias {_alias}", null, 0, IntPtr.Zero);
    }

    public void Play() => MciSendString($"play {_alias} repeat", null, 0, IntPtr.Zero);

    public void Stop() => MciSendString($"stop {_alias}", null, 0, IntPtr.Zero);

    public void SetVolume(int volume)
    {
        volume = Math.Clamp(volume, 0, 1000);
        MciSendString($"setaudio {_alias} volume to {volume}", null, 0, IntPtr.Zero);
    }

    public void Dispose()
    {
        Stop();
        MciSendString($"close {_alias}", null, 0, IntPtr.Zero);
    }

    [DllImport("winmm.dll", EntryPoint = "mciSendStringW", CharSet = CharSet.Unicode)]
    private static extern int MciSendString(string command, string? buffer, int bufferSize, IntPtr hwndCallback);
}

internal sealed class MainForm : Form
{
    private readonly ResourcePack _resources;
    private readonly LoopingMusic _music;
    private readonly OverlayController _overlay;
    private readonly List<TunnelRing> _rings = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly Random _random = new();
    private readonly TextBox _steamPathBox;
    private readonly Label _versionState;
    private readonly Label _winhttpState;
    private readonly TextBox _logBox;
    private readonly Button _closeButton;
    private readonly Button _enableButton;
    private readonly Button _disableButton;
    private readonly Button _chooseButton;
    private readonly Button _refreshButton;
    private readonly Button _changelogButton;
    private readonly Button _languageButton;
    private readonly Label _debugHotspot;
    private readonly TrackBar _volumeSlider;
    private readonly Label _volumeLabel;
    private readonly Label _pathLabel;
    private readonly Label _versionNameLabel;
    private readonly Label _winhttpNameLabel;
    private readonly Button _flutterModeButton;
    private AppSettings _settings;
    private bool _flutterMode;
    private bool _busyVisual;
    private bool _processingVisualPreview;
    private string _language;
    private double _time;
    private double _tunnelPhase;

    public MainForm(ResourcePack resources, LoopingMusic music)
    {
        _resources = resources;
        _music = music;
        _overlay = new OverlayController(resources.Images, resources.AppIcon);
        _settings = LoadSettings();
        _flutterMode = _settings.FlutterMode;
        _language = _settings.Language;

        Text = $"FLUTTERGEDDON v{AppInfo.VersionText}";
        Icon = resources.AppIcon;
        ClientSize = new Size(980, 660);
        MinimumSize = new Size(760, 520);
        DoubleBuffered = true;
        BackColor = Color.FromArgb(246, 220, 205);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f, FontStyle.Regular);

        _closeButton = new Button
        {
            Text = "",
            Width = 190,
            Height = 34,
            Left = 18,
            Top = 18,
            BackColor = Color.FromArgb(255, 230, 176),
            ForeColor = Color.FromArgb(96, 65, 55),
            FlatStyle = FlatStyle.Flat
        };
        _closeButton.FlatAppearance.BorderColor = Color.FromArgb(214, 132, 166);
        _closeButton.Click += (_, _) => Close();
        Controls.Add(_closeButton);

        _flutterModeButton = MakeButton("", 218, 18, 180, 34);
        _flutterModeButton.Click += (_, _) => SetFlutterMode(!_flutterMode, save: true);
        Controls.Add(_flutterModeButton);

        _changelogButton = MakeButton("", 408, 18, 140, 34);
        _changelogButton.Click += (_, _) => ShowChangelog();
        Controls.Add(_changelogButton);

        _languageButton = MakeButton("", 558, 18, 110, 34);
        _languageButton.Click += (_, _) => ToggleLanguage();
        Controls.Add(_languageButton);

        _debugHotspot = new Label
        {
            Text = "",
            Width = 34,
            Height = 34,
            Left = 0,
            Top = ClientSize.Height - 34,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            BackColor = Color.Transparent,
            TabStop = false,
            Cursor = Cursors.Default
        };
        _debugHotspot.Click += (_, _) => ShowDebugMenu();
        Controls.Add(_debugHotspot);

        _pathLabel = MakeLabel("", 18, 62, 150, 22, bold: true);
        Controls.Add(_pathLabel);

        _steamPathBox = new TextBox
        {
            Left = 18,
            Top = 86,
            Width = 520,
            Height = 28,
            Text = _settings.SteamPath,
            BackColor = Color.FromArgb(255, 246, 224),
            ForeColor = Color.FromArgb(96, 65, 55),
            BorderStyle = BorderStyle.FixedSingle
        };
        Controls.Add(_steamPathBox);

        _chooseButton = MakeButton("", 548, 84, 120, 32);
        _chooseButton.Click += (_, _) => ChooseSteamFolder();
        Controls.Add(_chooseButton);

        _refreshButton = MakeButton("", 676, 84, 110, 32);
        _refreshButton.Click += (_, _) => RefreshDllState();
        Controls.Add(_refreshButton);

        var stateBox = new Panel
        {
            Left = 18,
            Top = 124,
            Width = 360,
            Height = 70,
            BackColor = Color.FromArgb(235, 214, 190)
        };
        stateBox.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(205, 122, 160), 2);
            e.Graphics.DrawRectangle(pen, 0, 0, stateBox.Width - 1, stateBox.Height - 1);
        };
        Controls.Add(stateBox);

        _versionNameLabel = MakeLabel("version.dll", 12, 10, 110, 22, bold: true, parentBack: stateBox.BackColor);
        _winhttpNameLabel = MakeLabel("winhttp.dll", 12, 38, 110, 22, bold: true, parentBack: stateBox.BackColor);
        stateBox.Controls.Add(_versionNameLabel);
        stateBox.Controls.Add(_winhttpNameLabel);
        _versionState = MakeLabel("...", 132, 10, 200, 22, bold: true, parentBack: stateBox.BackColor);
        _winhttpState = MakeLabel("...", 132, 38, 200, 22, bold: true, parentBack: stateBox.BackColor);
        stateBox.Controls.Add(_versionState);
        stateBox.Controls.Add(_winhttpState);

        _disableButton = MakeButton("", 392, 124, 270, 34);
        _disableButton.Click += async (_, _) => await ToggleDllsAsync(enableCustom: false);
        Controls.Add(_disableButton);

        _enableButton = MakeButton("", 392, 162, 270, 34);
        _enableButton.Click += async (_, _) => await ToggleDllsAsync(enableCustom: true);
        Controls.Add(_enableButton);

        _logBox = new TextBox
        {
            Left = 676,
            Top = 124,
            Width = 280,
            Height = 72,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(69, 48, 58),
            ForeColor = Color.FromArgb(255, 243, 181),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8.5f)
        };
        Controls.Add(_logBox);

        _volumeLabel = MakeLabel("", 796, 84, 160, 22, bold: true);
        Controls.Add(_volumeLabel);

        _volumeSlider = new TrackBar
        {
            Left = 794,
            Top = 104,
            Width = 166,
            Height = 28,
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            Value = _settings.Volume,
            BackColor = Color.FromArgb(246, 220, 205)
        };
        _volumeSlider.ValueChanged += (_, _) =>
        {
            UpdateVolumeLabel();
            _music.SetVolume(_volumeSlider.Value * 10);
            _settings.Volume = _volumeSlider.Value;
            SaveSettings();
        };
        Controls.Add(_volumeSlider);
        _music.SetVolume(_volumeSlider.Value * 10);

        _timer.Interval = 90;
        _timer.Tick += (_, _) =>
        {
            _time += 0.09;
            if (_flutterMode)
                UpdateTunnelRings();
            Invalidate();
        };

        Shown += (_, _) =>
        {
            ApplyLanguage(refreshState: false);
            RefreshDllState();
            SetFlutterMode(_flutterMode, save: false);
            _timer.Start();
            _ = CheckForUpdatesAtStartupAsync();
        };
        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _overlay.Dispose();
            _music.Stop();
        };
    }

    private static Button MakeButton(string text, int left, int top, int width, int height)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = height,
            Left = left,
            Top = top,
            BackColor = Color.FromArgb(244, 191, 211),
            ForeColor = Color.FromArgb(96, 65, 55),
            FlatStyle = FlatStyle.Flat
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(214, 132, 166);
        return button;
    }

    private static Label MakeLabel(string text, int left, int top, int width, int height, bool bold = false, Color? parentBack = null)
    {
        return new Label
        {
            Text = text,
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            AutoEllipsis = true,
            BackColor = parentBack ?? Color.Transparent,
            ForeColor = Color.FromArgb(96, 65, 55),
            Font = new Font("Segoe UI", 9.5f, bold ? FontStyle.Bold : FontStyle.Regular)
        };
    }

    private static string ConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FLUTTERGEDDON", "steam_path.txt");

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FLUTTERGEDDON", "settings.json");

    private static AppSettings LoadSettings() => SettingsStore.Load();

    private void SaveSettings()
    {
        _settings.SteamPath = _steamPathBox.Text.Trim();
        _settings.FlutterMode = _flutterMode;
        _settings.Language = _language;
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    private string L(string ru, string en) => _language == "ru" ? ru : en;

    private void ApplyLanguage(bool refreshState)
    {
        _closeButton.Text = L("СТОП FLUTTERGEDDON", "STOP FLUTTERGEDDON");
        _pathLabel.Text = L("Папка Steam", "Steam folder");
        _chooseButton.Text = L("Выбрать...", "Browse...");
        _refreshButton.Text = L("Обновить", "Refresh");
        _disableButton.Text = L("Выключить DLL", "Disable DLL");
        _enableButton.Text = L("Включить DLL", "Enable DLL");
        _languageButton.Text = _language == "ru" ? "lang. RU" : "lang. EN";
        UpdateVolumeLabel();
        UpdateChangelogButton();
        SetFlutterMode(_flutterMode, save: false);
        if (refreshState)
            RefreshDllState();
    }

    private void UpdateVolumeLabel()
    {
        _volumeLabel.Text = L($"Громкость: {_volumeSlider.Value}%", $"Volume: {_volumeSlider.Value}%");
    }

    private void UpdateChangelogButton()
    {
        var changelogVersion = string.IsNullOrWhiteSpace(_settings.PendingChangelogVersion)
            ? AppInfo.VersionText
            : _settings.PendingChangelogVersion;
        var unread = !string.Equals(_settings.LastSeenChangelogVersion, changelogVersion, StringComparison.OrdinalIgnoreCase);
        _changelogButton.Text = unread ? "CHANGELOG !" : "CHANGELOG";
        _changelogButton.BackColor = unread ? Color.FromArgb(255, 230, 176) : Color.FromArgb(244, 191, 211);
    }

    private void ToggleLanguage()
    {
        _language = _language == "ru" ? "en" : "ru";
        ApplyLanguage(refreshState: true);
        SaveSettings();
    }

    private void ShowChangelog()
    {
        using var dialog = new Form
        {
            Text = "FLUTTERGEDDON CHANGELOG",
            Icon = _resources.AppIcon,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(560, 420),
            MinimumSize = new Size(420, 300),
            BackColor = Color.FromArgb(246, 220, 205),
            Font = new Font("Segoe UI", 10f)
        };
        var box = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(255, 246, 224),
            ForeColor = Color.FromArgb(96, 65, 55),
            BorderStyle = BorderStyle.FixedSingle,
            Text = GetChangelogText().Replace("\n", Environment.NewLine)
        };
        box.SelectionStart = 0;
        box.SelectionLength = 0;
        box.HideSelection = true;
        var close = MakeButton(L("Закрыть", "Close"), 0, 0, 110, 34);
        close.Dock = DockStyle.Bottom;
        close.Click += (_, _) => dialog.Close();
        dialog.Controls.Add(box);
        dialog.Controls.Add(close);
        dialog.Shown += (_, _) =>
        {
            box.SelectionStart = 0;
            box.SelectionLength = 0;
            close.Focus();
        };
        dialog.ShowDialog(this);

        _settings.LastSeenChangelogVersion = string.IsNullOrWhiteSpace(_settings.PendingChangelogVersion)
            ? AppInfo.VersionText
            : _settings.PendingChangelogVersion;
        SaveSettings();
        UpdateChangelogButton();
    }

    private void ShowDebugMenu()
    {
        using var dialog = new Form
        {
            Text = "FLUTTERGEDDON DEBUG",
            Icon = _resources.AppIcon,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(320, 206),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(246, 220, 205),
            Font = new Font("Segoe UI", 10f)
        };

        var testUpdateButton = MakeButton("Test update window", 18, 20, 280, 36);
        testUpdateButton.Click += (_, _) => PreviewUpdateWindow();
        dialog.Controls.Add(testUpdateButton);

        var processingVisualButton = MakeButton(GetProcessingVisualText(), 18, 76, 280, 36);
        processingVisualButton.Click += (_, _) =>
        {
            _settings.ProcessingVisualEnabled = !_settings.ProcessingVisualEnabled;
            _processingVisualPreview = _settings.ProcessingVisualEnabled;
            processingVisualButton.Text = GetProcessingVisualText();
            SaveSettings();
            Refresh();
        };
        dialog.Controls.Add(processingVisualButton);

        var closeButton = MakeButton(L("Закрыть", "Close"), 18, 134, 280, 36);
        closeButton.Click += (_, _) => dialog.Close();
        dialog.Controls.Add(closeButton);
        dialog.ShowDialog(this);
    }

    private string GetProcessingVisualText()
    {
        return _settings.ProcessingVisualEnabled
            ? "Processing visual: ON"
            : "Processing visual: OFF";
    }

    private bool ShouldDrawProcessingVisual => _busyVisual || _processingVisualPreview;

    private void PreviewUpdateWindow()
    {
        using var preview = new UpdateProgressForm(_resources.UpdatePony, AppInfo.VersionText + " preview")
        {
            Icon = _resources.AppIcon
        };
        using var timer = new System.Windows.Forms.Timer { Interval = 35 };
        var value = 0;
        timer.Tick += (_, _) =>
        {
            value += 2;
            if (value > 100)
            {
                timer.Stop();
                preview.Close();
                return;
            }
            preview.SetProgress(value);
        };
        preview.Shown += (_, _) => timer.Start();
        preview.ShowDialog(this);
    }

    private string GetChangelogText()
    {
        return string.IsNullOrWhiteSpace(_settings.PendingChangelogText)
            ? _resources.Changelog
            : _settings.PendingChangelogText;
    }

    private async Task CheckForUpdatesAtStartupAsync()
    {
        if (!AppInfo.UpdateConfigured)
        {
            Log(L("Автообновление не настроено: укажи GitHub owner/repo в Program.cs.",
                "Auto-update is not configured: set GitHub owner/repo in Program.cs."));
            return;
        }

        try
        {
            Log(L("Проверяю обновления...", "Checking for updates..."));
            ReleaseHistory? releaseHistory = null;
            try
            {
                releaseHistory = await GitHubUpdater.GetFullChangelogAsync();
                if (!string.IsNullOrWhiteSpace(releaseHistory.Text))
                {
                    _settings.PendingChangelogVersion = releaseHistory.LatestVersion;
                    _settings.PendingChangelogText = releaseHistory.Text;
                    SaveSettings();
                    UpdateChangelogButton();
                }
            }
            catch (Exception historyEx)
            {
                Log(L("Не удалось получить историю changelog: ", "Could not fetch changelog history: ") + FormatException(historyEx));
            }

            var update = await GitHubUpdater.CheckLatestAsync();
            if (update is null)
            {
                Log(L("Обновлений нет.", "No updates found."));
                return;
            }

            Log(L($"Найдена версия {update.Version}. Скачиваю...",
                $"Version {update.Version} found. Downloading..."));
            using var updateDialog = new UpdateProgressForm(_resources.UpdatePony, update.Version)
            {
                Icon = _resources.AppIcon
            };
            updateDialog.Show(this);
            var progress = new Progress<int>(value => updateDialog.SetProgress(value));
            var downloadedExe = await GitHubUpdater.DownloadAndVerifyAsync(update, progress);
            Log(L("Обновление скачано и проверено. Перезапускаю приложение...",
                "Update downloaded and verified. Restarting app..."));
            _settings.LastInstalledVersion = update.Version;
            _settings.PendingChangelogVersion = releaseHistory?.LatestVersion ?? update.Version;
            _settings.PendingChangelogText = !string.IsNullOrWhiteSpace(releaseHistory?.Text)
                ? releaseHistory.Text
                : string.IsNullOrWhiteSpace(update.Changelog)
                    ? _resources.Changelog
                    : update.Changelog;
            SaveSettings();
            GitHubUpdater.StartSelfReplace(downloadedExe);
            Application.Exit();
        }
        catch (Exception ex)
        {
            Log(L("Не удалось обновиться: ", "Update failed: ") + FormatException(ex));
        }
    }

    private static string FormatException(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
            messages.Add(current.Message);
        return string.Join(" | ", messages.Distinct());
    }

    private static int CompareVersionText(string left, string right)
    {
        return ParseVersion(left).CompareTo(ParseVersion(right));
    }

    private static Version ParseVersion(string value)
    {
        var clean = value.Trim().TrimStart('v', 'V');
        var dash = clean.IndexOfAny(new[] { '-', '+' });
        if (dash >= 0)
            clean = clean[..dash];
        return Version.TryParse(clean, out var version) ? version : new Version(0, 0, 0);
    }

    private void SetFlutterMode(bool enabled, bool save)
    {
        _flutterMode = enabled;
        _flutterModeButton.Text = _flutterMode ? "FLUTTERMODE ON" : "FLUTTERMODE OFF";
        _flutterModeButton.BackColor = _flutterMode ? Color.FromArgb(244, 191, 211) : Color.FromArgb(220, 204, 190);
        if (_flutterMode)
        {
            if (_rings.Count == 0)
                SeedTunnelPattern();
            _overlay.Show();
        }
        else
        {
            _rings.Clear();
            _overlay.Hide();
        }
        if (save)
            SaveSettings();
    }

    private void ChooseSteamFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = L("Выбери папку Steam", "Choose Steam folder"),
            SelectedPath = Directory.Exists(_steamPathBox.Text) ? _steamPathBox.Text : @"C:\Program Files (x86)\Steam",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _steamPathBox.Text = dialog.SelectedPath;
            RefreshDllState();
        }
    }

    private void RefreshDllState()
    {
        try
        {
            var steamPath = ValidateSteamPath();
            _settings.SteamPath = steamPath;
            SaveSettings();
            SetStateLabel(_versionState, GetDllState(Path.Combine(steamPath, "version.dll")));
            SetStateLabel(_winhttpState, GetDllState(Path.Combine(steamPath, "winhttp.dll")));
            Log(L("Состояние обновлено.", "State refreshed."));
        }
        catch (Exception ex)
        {
            _versionState.Text = L("недоступно", "unavailable");
            _winhttpState.Text = L("недоступно", "unavailable");
            _versionState.ForeColor = Color.FromArgb(180, 49, 87);
            _winhttpState.ForeColor = Color.FromArgb(180, 49, 87);
            Log(L("Ошибка: ", "Error: ") + ex.Message);
        }
    }

    private static string GetDllState(string activePath)
    {
        var active = File.Exists(activePath);
        var disabled = File.Exists(activePath + ".disabled");
        return (active, disabled) switch
        {
            (true, true) => "conflict",
            (true, false) => "enabled",
            (false, true) => "disabled",
            _ => "missing"
        };
    }

    private void SetStateLabel(Label label, string state)
    {
        label.Text = state switch
        {
            "enabled" => L("DLL включена", "DLL enabled"),
            "disabled" => L("DLL выключена", "DLL disabled"),
            "conflict" => L("конфликт", "conflict"),
            _ => L("файл не найден", "file not found")
        };
        label.ForeColor = state switch
        {
            "enabled" => Color.FromArgb(68, 132, 81),
            "disabled" => Color.FromArgb(190, 57, 91),
            "conflict" => Color.FromArgb(154, 111, 36),
            _ => Color.FromArgb(110, 92, 88)
        };
    }

    private string ValidateSteamPath()
    {
        var steamPath = _steamPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
            throw new DirectoryNotFoundException(L("Папка Steam не найдена.", "Steam folder was not found."));
        if (!File.Exists(Path.Combine(steamPath, "steam.exe")))
            throw new FileNotFoundException(L("В выбранной папке нет steam.exe.", "steam.exe was not found in the selected folder."));
        return steamPath;
    }

    private async Task ToggleDllsAsync(bool enableCustom)
    {
        var action = enableCustom
            ? L("восстановить DLL из .disabled", "restore DLLs from .disabled")
            : L("переименовать DLL в .disabled", "rename DLLs to .disabled");
        if (MessageBox.Show(this,
                L($"Steam и игры будут закрыты, затем FLUTTERGEDDON выполнит действие: {action}. Steam запустится снова. Продолжить?",
                    $"Steam and games will be closed, then FLUTTERGEDDON will run this action: {action}. Steam will reopen. Continue?"),
                "FLUTTERGEDDON",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        SetBusy(true);
        try
        {
            var steamPath = ValidateSteamPath();
            _settings.SteamPath = steamPath;
            SaveSettings();
            await Task.Run(() => ToggleWorker(steamPath, enableCustom));
            RefreshDllState();
        }
        catch (Exception ex)
        {
            Log(L("Ошибка: ", "Error: ") + ex.Message);
            MessageBox.Show(this, ex.Message, "FLUTTERGEDDON", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ToggleWorker(string steamPath, bool enableCustom)
    {
        Log(L("Начинаю переключение.", "Starting switch."));
        CloseSteamProcesses();
        WaitForSteamClose();
        SwitchDlls(steamPath, enableCustom);
        var steamExe = Path.Combine(steamPath, "steam.exe");
        Log(L("Запускаю Steam...", "Starting Steam..."));
        Process.Start(new ProcessStartInfo(steamExe) { WorkingDirectory = steamPath, UseShellExecute = true });
    }

    private void CloseSteamProcesses()
    {
        foreach (var name in new[] { "cs2", "steam", "steamwebhelper", "GameOverlayUI", "steamerrorreporter" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    Log(L($"Закрываю {process.ProcessName}.exe...", $"Closing {process.ProcessName}.exe..."));
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    Log(L($"Не смог закрыть {process.ProcessName}: {ex.Message}", $"Could not close {process.ProcessName}: {ex.Message}"));
                }
            }
        }
    }

    private void WaitForSteamClose()
    {
        var deadline = DateTime.UtcNow.AddSeconds(35);
        while (DateTime.UtcNow < deadline)
        {
            var alive = new[] { "cs2", "steam", "steamwebhelper", "GameOverlayUI", "steamerrorreporter" }
                .Where(name => Process.GetProcessesByName(name).Length > 0)
                .ToArray();
            if (alive.Length == 0)
            {
                Log(L("Steam/игры закрыты.", "Steam/games are closed."));
                return;
            }
            Log(L("Жду закрытия: ", "Waiting for close: ") + string.Join(", ", alive));
            Thread.Sleep(1000);
        }
        throw new TimeoutException(L("Не удалось дождаться закрытия Steam/игр.", "Timed out waiting for Steam/games to close."));
    }

    private void SwitchDlls(string steamPath, bool enableCustom)
    {
        foreach (var fileName in new[] { "version.dll", "winhttp.dll" })
        {
            var active = Path.Combine(steamPath, fileName);
            var disabled = active + ".disabled";
            if (enableCustom)
            {
                if (File.Exists(active))
                {
                    Log(L($"{fileName}: уже включен.", $"{fileName}: already enabled."));
                    continue;
                }
                if (!File.Exists(disabled))
                {
                    Log(L($"{fileName}: нет {Path.GetFileName(disabled)}, пропускаю.", $"{fileName}: {Path.GetFileName(disabled)} is missing, skipped."));
                    continue;
                }
                File.Move(disabled, active);
                Log($"{Path.GetFileName(disabled)} -> {fileName}");
            }
            else
            {
                if (!File.Exists(active))
                {
                    Log(L($"{fileName}: уже выключен.", $"{fileName}: already disabled."));
                    continue;
                }
                var target = disabled;
                if (File.Exists(target))
                    target = active + ".disabled." + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Move(active, target);
                Log($"{fileName} -> {Path.GetFileName(target)}");
            }
        }
    }

    private void SetBusy(bool busy)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetBusy(busy));
            return;
        }
        _busyVisual = busy && _settings.ProcessingVisualEnabled;
        _enableButton.Enabled = !busy;
        _disableButton.Enabled = !busy;
        Invalidate();
    }

    private void Log(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(text));
            return;
        }
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
    }

    private void UpdateTunnelRings()
    {
        if (_rings.Count == 0)
            SeedTunnelPattern();

        _tunnelPhase = (_tunnelPhase + 0.014) % 1.0;
        var count = _rings.Count;
        for (var i = 0; i < count; i++)
        {
            var ring = _rings[i];
            ring.Scale = (float)((i / (double)count + _tunnelPhase) % 1.0);
            ring.Angle += ring.AngularSpeed;
        }
    }

    private void SeedTunnelPattern()
    {
        _rings.Clear();
        _tunnelPhase = 0;
        const int ringCount = 6;
        for (var i = 0; i < ringCount; i++)
        {
            _rings.Add(new TunnelRing
            {
                Scale = i / (float)ringCount,
                Angle = i * 21f,
                AngularSpeed = 0.46f + i % 4 * 0.13f,
                Wobble = (float)Math.Sin(i * 1.37) * 10f,
                HueShift = i % 4
            });
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        DrawBackground(g);
        DrawPanel(g);
        DrawGlitches(g);
        DrawHeader(g);
    }

    private void DrawBackground(Graphics g)
    {
        if (ShouldDrawProcessingVisual)
        {
            DrawMatrixBackground(g, ClientRectangle);
            return;
        }

        using var brush = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(250, 229, 218),
            Color.FromArgb(238, 178, 205),
            LinearGradientMode.ForwardDiagonal);
        g.FillRectangle(brush, ClientRectangle);

        using var haze = new SolidBrush(Color.FromArgb(42, 255, 246, 191));
        for (var i = 0; i < 7; i++)
        {
            var x = (float)((i * 97 + _time * 30) % (ClientSize.Width + 180) - 90);
            var y = (float)(90 + Math.Sin(_time * 0.8 + i) * 42 + i * 28);
            g.FillEllipse(haze, x, y, 180, 42);
        }
    }

    private void DrawMatrixBackground(Graphics g)
    {
        DrawMatrixBackground(g, ClientRectangle);
    }

    private void DrawMatrixBackground(Graphics g, Rectangle bounds)
    {
        using var background = new SolidBrush(Color.FromArgb(8, 18, 12));
        g.FillRectangle(background, bounds);
        using var font = new Font("Consolas", 9f, FontStyle.Bold);
        using var dim = new SolidBrush(Color.FromArgb(72, 20, 120, 68));
        using var bright = new SolidBrush(Color.FromArgb(170, 80, 255, 146));
        const string chars = "01#$%*+VZLOMFLUTTERGEDDON";
        var stepX = 18;
        var stepY = 20;
        var phase = (int)(_time * 24);
        for (var x = bounds.Left - stepX; x < bounds.Right + stepX; x += stepX)
        {
            var columnOffset = Math.Abs((x / stepX * 37 + phase) % stepY);
            for (var y = bounds.Top - stepY; y < bounds.Bottom + stepY; y += stepY)
            {
                var index = Math.Abs((x * 3 + y * 5 + phase) % chars.Length);
                var brush = ((y + columnOffset) / stepY + x / stepX) % 7 == 0 ? bright : dim;
                g.DrawString(chars[index].ToString(), font, brush, x, y + columnOffset);
            }
        }
    }

    private void DrawPanel(Graphics g)
    {
        var rect = GetTunnelRect();
        using var panel = new SolidBrush(Color.FromArgb(232, 245, 214, 190));
        using var border = new Pen(Color.FromArgb(205, 122, 160), 3);
        if (ShouldDrawProcessingVisual)
            DrawMatrixBackground(g, rect);
        else
            g.FillRectangle(panel, rect);

        var cx = rect.Left + rect.Width / 2f;
        var cy = rect.Top + rect.Height / 2f;
        var max = Math.Min(rect.Width, rect.Height) * 0.88f;

        using (new GraphicsStateScope(g))
        {
            g.SetClip(rect);
            if (_flutterMode && !ShouldDrawProcessingVisual)
            {
                var oldSmoothing = g.SmoothingMode;
                var oldCompositing = g.CompositingQuality;
                g.SmoothingMode = SmoothingMode.HighSpeed;
                g.CompositingQuality = CompositingQuality.HighSpeed;

                foreach (var ring in _rings)
                {
                    if (ring.Scale < 0.035f)
                        continue;

                    var scale = ring.Scale * ring.Scale * 3.35f;
                    var w = max * scale * 1.45f;
                    var h = max * scale;
                    var wobble = (float)Math.Sin(_time * 2.1 + ring.Wobble) * 18f * scale;
                    var angle = ring.Angle;

                    BuildRotatedDiamond(ring.Points, cx + wobble, cy, w, h, angle);
                    var alpha = Math.Clamp((int)(24 + ring.Scale * 118), 24, 150);
                    var ringColor = ring.HueShift switch
                    {
                        0 => Color.FromArgb(alpha, 226, 114, 169),
                        1 => Color.FromArgb(alpha, 255, 229, 151),
                        2 => Color.FromArgb(alpha, 244, 154, 196),
                        _ => Color.FromArgb(alpha, 182, 230, 211)
                    };
                    using var ringPen = new Pen(ringColor, Math.Max(2, 5 * scale));
                    g.DrawPolygon(ringPen, ring.Points);
                }

                using var centerBrush = new SolidBrush(Color.FromArgb(210, 255, 246, 191));
                g.FillEllipse(centerBrush, cx - 14, cy - 14, 28, 28);
                g.SmoothingMode = oldSmoothing;
                g.CompositingQuality = oldCompositing;
            }
        }

        DrawPanelBackground(g, rect);
        g.DrawRectangle(border, rect);
    }

    private void DrawPanelBackground(Graphics g, Rectangle rect)
    {
        var blinkCycle = _time % 4.2;
        var image = ShouldDrawProcessingVisual
            ? _resources.PanelBackgroundProcessing
            : blinkCycle > 3.92 && blinkCycle < 4.1
                ? _resources.PanelBackgroundBlink
                : _resources.PanelBackground;
        var imageRatio = image.Width / (float)image.Height;
        var rectRatio = rect.Width / (float)rect.Height;
        RectangleF dest;
        if (imageRatio > rectRatio)
        {
            var width = rect.Height * imageRatio;
            dest = new RectangleF(rect.Left - (width - rect.Width) / 2f, rect.Top, width, rect.Height);
        }
        else
        {
            var height = rect.Width / imageRatio;
            dest = new RectangleF(rect.Left, rect.Top - (height - rect.Height) / 2f, rect.Width, height);
        }

        using var clipState = new GraphicsStateScope(g);
        g.SetClip(rect);
        if (ShouldDrawProcessingVisual)
        {
            using var attributes = new ImageAttributes();
            var matrix = new ColorMatrix { Matrix33 = 0.70f };
            attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            g.DrawImage(
                image,
                Rectangle.Round(dest),
                0,
                0,
                image.Width,
                image.Height,
                GraphicsUnit.Pixel,
                attributes);

            using var veil = new SolidBrush(Color.FromArgb(36, 80, 255, 146));
            g.FillRectangle(veil, rect);
            return;
        }

        g.DrawImage(image, dest);
        using var normalVeil = new SolidBrush(Color.FromArgb(54, 250, 229, 218));
        g.FillRectangle(normalVeil, rect);
    }

    private static void BuildRotatedDiamond(PointF[] target, float cx, float cy, float w, float h, float angleDegrees)
    {
        var a = angleDegrees * Math.PI / 180.0;
        var cos = (float)Math.Cos(a);
        var sin = (float)Math.Sin(a);
        SetRotatedPoint(target, 0, cx, cy, 0, -h / 2, cos, sin);
        SetRotatedPoint(target, 1, cx, cy, w / 2, 0, cos, sin);
        SetRotatedPoint(target, 2, cx, cy, 0, h / 2, cos, sin);
        SetRotatedPoint(target, 3, cx, cy, -w / 2, 0, cos, sin);
    }

    private static void SetRotatedPoint(PointF[] target, int index, float cx, float cy, float x, float y, float cos, float sin)
    {
        target[index] = new PointF(cx + x * cos - y * sin, cy + x * sin + y * cos);
    }

    private void DrawGlitches(Graphics g)
    {
        if (_random.NextDouble() > 0.88)
        {
            for (var i = 0; i < 4; i++)
            {
                var x = _random.Next(ClientSize.Width);
                var y = _random.Next(ClientSize.Height);
                var w = _random.Next(25, 190);
                using var brush = new SolidBrush(Color.FromArgb(_random.Next(35, 100), 97, 219, 210));
                g.FillRectangle(brush, x, y, w, _random.Next(2, 8));
            }
        }

        using var scan = new Pen(Color.FromArgb(35, 118, 84, 78));
        for (var y = 0; y < ClientSize.Height; y += 8)
            g.DrawLine(scan, 0, y, ClientSize.Width, y);
    }

    private void DrawHeader(Graphics g)
    {
        using var titleFont = new Font("Segoe UI Black", 42, FontStyle.Bold);
        using var shadow = new SolidBrush(Color.FromArgb(90, 140, 91, 103));
        using var pink = new SolidBrush(Color.FromArgb(197, 83, 132));
        using var yellow = new SolidBrush(Color.FromArgb(255, 246, 178));

        var title = "FLUTTERGEDDON";
        var rect = GetTunnelRect();
        g.DrawString(title, titleFont, shadow, rect.Left + 13, rect.Top + 13);
        g.DrawString(title, titleFont, yellow, rect.Left + 9, rect.Top + 9);
        g.DrawString(title, titleFont, pink, rect.Left + 7, rect.Top + 7);
    }

    private Rectangle GetTunnelRect()
    {
        var top = 214;
        return new Rectangle(34, top, ClientSize.Width - 68, Math.Max(220, ClientSize.Height - top - 34));
    }

    private sealed class TunnelRing
    {
        public float Scale;
        public float Angle;
        public float AngularSpeed;
        public float Wobble;
        public int HueShift;
        public readonly PointF[] Points = new PointF[4];
    }
}

internal sealed class AppSettings
{
    public string SteamPath { get; set; } = @"C:\Program Files (x86)\Steam";
    public int Volume { get; set; } = 50;
    public bool FlutterMode { get; set; } = true;
    public string Language { get; set; } = "";
    public string LastSeenChangelogVersion { get; set; } = "";
    public string LastInstalledVersion { get; set; } = "";
    public string PendingChangelogVersion { get; set; } = "";
    public string PendingChangelogText { get; set; } = "";
    public bool ProcessingVisualEnabled { get; set; }
}

internal sealed class ReleaseHistory
{
    public required string LatestVersion { get; init; }
    public required string Text { get; init; }
}

internal static class SettingsStore
{
    private static string ConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FLUTTERGEDDON", "steam_path.txt");

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FLUTTERGEDDON", "settings.json");

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (File.Exists(SettingsPath))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? settings;
            else if (File.Exists(ConfigPath))
                settings.SteamPath = File.ReadAllText(ConfigPath).Trim();
        }
        catch
        {
            // Ignore config read errors and fall back to defaults.
        }

        settings.Volume = Math.Clamp(settings.Volume, 0, 100);
        if (string.IsNullOrWhiteSpace(settings.SteamPath))
            settings.SteamPath = @"C:\Program Files (x86)\Steam";
        if (settings.Language is not ("ru" or "en"))
            settings.Language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase)
                ? "ru"
                : "en";
        return settings;
    }
}

internal sealed class UpdatePackage
{
    public required string Version { get; init; }
    public required string ExeUrl { get; init; }
    public required string Sha256Url { get; init; }
    public required string Changelog { get; init; }
}

internal sealed class UpdateProgressForm : Form
{
    private readonly Label _title;
    private readonly Label _percent;
    private readonly UpdateProgressBar _bar;

    public UpdateProgressForm(Image pony, string version)
    {
        Text = "FLUTTERGEDDON UPDATE";
        ClientSize = new Size(520, 170);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(246, 220, 205);
        Font = new Font("Segoe UI", 10f, FontStyle.Bold);

        _title = new Label
        {
            Text = $"Updating to {version}",
            Left = 20,
            Top = 18,
            Width = 480,
            Height = 30,
            ForeColor = Color.FromArgb(96, 65, 55),
            BackColor = BackColor
        };
        Controls.Add(_title);

        _bar = new UpdateProgressBar(pony)
        {
            Left = 20,
            Top = 62,
            Width = 480,
            Height = 64
        };
        Controls.Add(_bar);

        _percent = new Label
        {
            Text = "0%",
            Left = 20,
            Top = 132,
            Width = 480,
            Height = 24,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(154, 83, 121),
            BackColor = BackColor
        };
        Controls.Add(_percent);
    }

    public void SetProgress(int value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetProgress(value));
            return;
        }

        value = Math.Clamp(value, 0, 100);
        _bar.Value = value;
        _percent.Text = $"{value}%";
    }
}

internal sealed class UpdateProgressBar : Control
{
    private readonly Image _pony;
    private int _value;

    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 100);
            Invalidate();
        }
    }

    public UpdateProgressBar(Image pony)
    {
        _pony = pony;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(246, 220, 205);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var barRect = new Rectangle(8, Height / 2 - 9, Width - 16, 18);
        using var back = new SolidBrush(Color.FromArgb(255, 246, 224));
        using var border = new Pen(Color.FromArgb(205, 122, 160), 2);
        g.FillRectangle(back, barRect);
        g.DrawRectangle(border, barRect);

        var fillWidth = Math.Max(4, (int)((barRect.Width - 4) * (_value / 100f)));
        using var fill = new LinearGradientBrush(
            new Rectangle(barRect.Left + 2, barRect.Top + 2, fillWidth, barRect.Height - 4),
            Color.FromArgb(255, 230, 176),
            Color.FromArgb(244, 154, 196),
            LinearGradientMode.Horizontal);
        g.FillRectangle(fill, barRect.Left + 2, barRect.Top + 2, fillWidth, barRect.Height - 4);

        var ponyW = 72;
        var ponyH = 52;
        var ponyX = Math.Clamp(barRect.Left + fillWidth - ponyW / 2, 0, Width - ponyW);
        var ponyY = Math.Max(0, barRect.Top - ponyH / 2 + 8);
        g.DrawImage(_pony, ponyX, ponyY, ponyW, ponyH);
    }
}

internal static class GitHubUpdater
{
    private static readonly HttpClient Http = CreateHttpClient();

    public static async Task<ReleaseHistory> GetFullChangelogAsync()
    {
        var releasesUrl = $"https://api.github.com/repos/{AppInfo.UpdateOwner}/{AppInfo.UpdateRepo}/releases?per_page=30";
        using var response = await Http.GetAsync(releasesUrl);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        var releases = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(stream)
            ?? new List<GitHubRelease>();
        releases = releases
            .Where(release => !string.IsNullOrWhiteSpace(release.TagName))
            .ToList();

        if (releases.Count == 0)
            return new ReleaseHistory { LatestVersion = AppInfo.VersionText, Text = "" };

        var builder = new StringBuilder();
        foreach (var release in releases)
        {
            var title = string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name;
            builder.AppendLine(title.Trim());
            builder.AppendLine(new string('-', Math.Min(48, Math.Max(8, title.Trim().Length))));
            builder.AppendLine(string.IsNullOrWhiteSpace(release.Body)
                ? "(no changelog text)"
                : release.Body.Trim());
            builder.AppendLine();
        }

        return new ReleaseHistory
        {
            LatestVersion = releases[0].TagName.Trim(),
            Text = builder.ToString().Trim()
        };
    }

    public static async Task<UpdatePackage?> CheckLatestAsync()
    {
        var releaseUrl = $"https://api.github.com/repos/{AppInfo.UpdateOwner}/{AppInfo.UpdateRepo}/releases/latest";
        using var response = await Http.GetAsync(releaseUrl);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream)
            ?? throw new InvalidOperationException("GitHub release response is empty.");

        var latestVersion = release.TagName.Trim();
        if (CompareVersions(latestVersion, AppInfo.VersionText) <= 0)
            return null;

        var exeAsset = release.Assets.FirstOrDefault(a => a.Name.Equals(AppInfo.UpdateAssetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"{AppInfo.UpdateAssetName} was not found in latest GitHub release.");
        var shaAsset = release.Assets.FirstOrDefault(a => a.Name.Equals(AppInfo.Sha256AssetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"{AppInfo.Sha256AssetName} was not found in latest GitHub release.");

        return new UpdatePackage
        {
            Version = latestVersion,
            ExeUrl = exeAsset.BrowserDownloadUrl,
            Sha256Url = shaAsset.BrowserDownloadUrl,
            Changelog = release.Body
        };
    }

    public static async Task<string> DownloadAndVerifyAsync(UpdatePackage update, IProgress<int>? progress = null)
    {
        progress?.Report(0);
        var expectedSha = ExtractSha256(await Http.GetStringAsync(update.Sha256Url));
        progress?.Report(5);
        var tempDir = Path.Combine(Path.GetTempPath(), "FLUTTERGEDDON_update");
        Directory.CreateDirectory(tempDir);
        var tempExe = Path.Combine(tempDir, $"FLUTTERGEDDON_{update.Version.TrimStart('v', 'V')}.exe");

        using (var response = await Http.GetAsync(update.ExeUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = File.Create(tempExe);
            var buffer = new byte[1024 * 96];
            long readTotal = 0;
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read));
                readTotal += read;
                if (totalBytes is > 0)
                    progress?.Report(Math.Clamp(5 + (int)(readTotal * 88 / totalBytes.Value), 5, 93));
            }
        }

        var actualSha = await ComputeSha256Async(tempExe);
        if (!actualSha.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(tempExe);
            throw new InvalidOperationException("Downloaded update SHA256 does not match.");
        }

        progress?.Report(100);
        return tempExe;
    }

    public static void StartSelfReplace(string downloadedExe)
    {
        var currentExe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot resolve current executable path.");
        var script = Path.Combine(Path.GetTempPath(), "FLUTTERGEDDON_update", "replace_fluttergeddon.cmd");
        var pid = Environment.ProcessId;
        var lines = new[]
        {
            "@echo off",
            "setlocal",
            $"set \"PID={pid}\"",
            $"set \"NEW_EXE={downloadedExe}\"",
            $"set \"TARGET_EXE={currentExe}\"",
            ":wait_app",
            "tasklist /FI \"PID eq %PID%\" | find \"%PID%\" >nul",
            "if not errorlevel 1 (",
            "  timeout /t 1 /nobreak >nul",
            "  goto wait_app",
            ")",
            "move /Y \"%NEW_EXE%\" \"%TARGET_EXE%\" >nul",
            "start \"\" \"%TARGET_EXE%\"",
            "del \"%~f0\""
        };
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllLines(script, lines, Encoding.Default);
        Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };
        var client = new HttpClient(handler, disposeHandler: true);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FLUTTERGEDDON", AppInfo.VersionText));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        client.Timeout = TimeSpan.FromSeconds(45);
        return client;
    }

    private static string ExtractSha256(string text)
    {
        var token = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(part => part.Length == 64 && part.All(Uri.IsHexDigit));
        return token ?? throw new InvalidOperationException("SHA256 file does not contain a valid 64-character hash.");
    }

    private static async Task<string> ComputeSha256Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static int CompareVersions(string left, string right)
    {
        return ParseVersion(left).CompareTo(ParseVersion(right));
    }

    private static Version ParseVersion(string value)
    {
        var clean = value.Trim().TrimStart('v', 'V');
        var dash = clean.IndexOfAny(new[] { '-', '+' });
        if (dash >= 0)
            clean = clean[..dash];
        return Version.TryParse(clean, out var version) ? version : new Version(0, 0, 0);
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}

internal sealed class OverlayController : IDisposable
{
    private readonly IReadOnlyList<Image> _images;
    private readonly Icon _appIcon;
    private readonly List<OverlayWindow> _windows;
    private readonly List<Sprite> _sprites = new();
    private readonly List<Rectangle> _dirtyRects = new(16);
    private readonly Random _random = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly Rectangle _worldBounds;
    private double _time;

    public OverlayController(IReadOnlyList<Image> images, Icon appIcon)
    {
        _images = images;
        _appIcon = appIcon;
        _worldBounds = SystemInformation.VirtualScreen;
        _windows = Screen.AllScreens.Select(screen => new OverlayWindow(this, screen.Bounds, _appIcon)).ToList();

        for (var i = 0; i < 5; i++)
            _sprites.Add(CreateSprite(i));

        _timer.Interval = 100;
        _timer.Tick += (_, _) =>
        {
            _time += 0.1;
            MoveSprites();
            foreach (var dirtyRect in _dirtyRects)
                InvalidateDirtyRect(dirtyRect);
        };
    }

    public void Show()
    {
        foreach (var window in _windows)
        {
            window.Show();
            window.Invalidate();
        }
        _timer.Start();
    }

    public void Hide()
    {
        _timer.Stop();
        foreach (var window in _windows)
            window.Hide();
    }

    public void Dispose()
    {
        _timer.Stop();
        foreach (var window in _windows.ToArray())
            window.Close();
        foreach (var sprite in _sprites)
            sprite.Dispose();
        _timer.Dispose();
    }

    public void Draw(Graphics g, Rectangle screenBounds)
    {
        g.SmoothingMode = SmoothingMode.HighSpeed;
        g.InterpolationMode = InterpolationMode.Low;
        g.CompositingQuality = CompositingQuality.HighSpeed;

        foreach (var sprite in _sprites)
        {
            if (sprite.Bounds(80).IntersectsWith(screenBounds))
                DrawSprite(g, sprite, screenBounds);
        }
    }

    private Sprite CreateSprite(int index)
    {
        var angle = _random.NextDouble() * Math.PI * 2;
        var speed = _random.NextDouble() * 12.0 + 13.0;
        var original = _images[index % _images.Count];
        var scale = (float)(_random.NextDouble() * 0.18 + 0.52);
        return new Sprite
        {
            Bitmap = CreateScaledBitmap(original, scale),
            X = _random.Next(_worldBounds.Left, Math.Max(_worldBounds.Left + 1, _worldBounds.Right)),
            Y = _random.Next(_worldBounds.Top, Math.Max(_worldBounds.Top + 1, _worldBounds.Bottom)),
            Vx = (float)(Math.Cos(angle) * speed),
            Vy = (float)(Math.Sin(angle) * speed),
            Phase = _random.NextDouble() * Math.PI * 2
        };
    }

    private static Bitmap CreateScaledBitmap(Image image, float scale)
    {
        var width = Math.Max(1, (int)(image.Width * scale));
        var height = Math.Max(1, (int)(image.Height * scale));
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.CompositingQuality = CompositingQuality.HighSpeed;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(image, 0, 0, width, height);
        return bitmap;
    }

    private void MoveSprites()
    {
        _dirtyRects.Clear();
        foreach (var sprite in _sprites)
        {
            var oldBounds = sprite.Bounds(96);
            sprite.Vx += (float)Math.Sin(_time * 3.4 + sprite.Phase) * 0.22f;
            sprite.Vy += (float)Math.Cos(_time * 3.8 + sprite.Phase) * 0.22f;
            var speed = MathF.Sqrt(sprite.Vx * sprite.Vx + sprite.Vy * sprite.Vy);
            if (speed > 30f)
            {
                sprite.Vx *= 30f / speed;
                sprite.Vy *= 30f / speed;
            }
            if (speed < 12f)
            {
                sprite.Vx *= 1.12f;
                sprite.Vy *= 1.12f;
            }

            sprite.X += sprite.Vx + (float)Math.Sin(_time * 18 + sprite.Phase) * 2.6f;
            sprite.Y += sprite.Vy + (float)Math.Cos(_time * 16 + sprite.Phase) * 2.6f;

            var margin = 380;
            if (sprite.X > _worldBounds.Right + margin || sprite.X < _worldBounds.Left - margin ||
                sprite.Y > _worldBounds.Bottom + margin || sprite.Y < _worldBounds.Top - margin)
            {
                RespawnFromEdge(sprite, _worldBounds);
            }

            var newBounds = sprite.Bounds(96);
            _dirtyRects.Add(Rectangle.Union(oldBounds, newBounds));
        }
    }

    private void InvalidateDirtyRect(Rectangle worldRect)
    {
        foreach (var window in _windows)
        {
            var intersection = Rectangle.Intersect(worldRect, window.ScreenBounds);
            if (intersection.IsEmpty)
                continue;

            intersection.Offset(-window.ScreenBounds.Left, -window.ScreenBounds.Top);
            window.Invalidate(intersection, false);
        }
    }

    private void RespawnFromEdge(Sprite sprite, Rectangle bounds)
    {
        var side = _random.Next(4);
        var targetX = _random.Next(bounds.Left + 80, bounds.Right - 80);
        var targetY = _random.Next(bounds.Top + 80, bounds.Bottom - 80);
        switch (side)
        {
            case 0:
                sprite.X = bounds.Left - 280;
                sprite.Y = _random.Next(bounds.Top, bounds.Bottom);
                break;
            case 1:
                sprite.X = bounds.Right + 280;
                sprite.Y = _random.Next(bounds.Top, bounds.Bottom);
                break;
            case 2:
                sprite.X = _random.Next(bounds.Left, bounds.Right);
                sprite.Y = bounds.Top - 220;
                break;
            default:
                sprite.X = _random.Next(bounds.Left, bounds.Right);
                sprite.Y = bounds.Bottom + 220;
                break;
        }

        var dx = targetX - sprite.X;
        var dy = targetY - sprite.Y;
        var length = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        var speed = _random.NextDouble() * 12.0 + 14.0;
        sprite.Vx = (float)(dx / length * speed);
        sprite.Vy = (float)(dy / length * speed);
    }

    private void DrawSprite(Graphics g, Sprite sprite, Rectangle screenBounds)
    {
        var w = sprite.Bitmap.Width;
        var h = sprite.Bitmap.Height;
        var jitterX = (float)Math.Sin(_time * 20 + sprite.Phase) * 3f;
        var jitterY = (float)Math.Cos(_time * 17 + sprite.Phase) * 2f;
        var x = sprite.X - screenBounds.Left + jitterX;
        var y = sprite.Y - screenBounds.Top + jitterY;

        using var state = new GraphicsStateScope(g);
        g.TranslateTransform(x + w / 2, y + h / 2);
        var tilt = (float)(Math.Atan2(sprite.Vy, Math.Max(1f, Math.Abs(sprite.Vx))) * 180.0 / Math.PI);
        tilt = Math.Clamp(tilt, -42f, 42f);
        g.RotateTransform(tilt + (float)Math.Sin(_time * 5 + sprite.Phase) * 6f);
        g.ScaleTransform(sprite.Vx < 0 ? -1 : 1, 1);
        g.TranslateTransform(-w / 2, -h / 2);

        g.DrawImageUnscaled(sprite.Bitmap, 0, 0);

        if (((int)(_time * 8 + sprite.Phase) % 9) == 0)
        {
            var glitchX = Math.Abs((int)(Math.Sin(_time + sprite.Phase) * w)) % Math.Max(1, w);
            var glitchY = Math.Abs((int)(Math.Cos(_time + sprite.Phase) * h)) % Math.Max(1, h);
            using var cyan = new SolidBrush(Color.FromArgb(130, 85, 255, 235));
            using var pink = new SolidBrush(Color.FromArgb(95, 255, 90, 164));
            g.FillRectangle(cyan, glitchX, glitchY, Math.Min(80, Math.Max(24, w / 4)), 4);
            g.FillRectangle(pink, Math.Max(0, glitchX - 10), Math.Min(Math.Max(0, h - 4), glitchY + 7), Math.Min(60, Math.Max(18, w / 5)), 3);
        }
    }

    private sealed class Sprite : IDisposable
    {
        public required Bitmap Bitmap;
        public float X;
        public float Y;
        public float Vx;
        public float Vy;
        public double Phase;

        public Rectangle Bounds(int padding)
        {
            return new Rectangle(
                (int)X - padding,
                (int)Y - padding,
                Bitmap.Width + padding * 2,
                Bitmap.Height + padding * 2);
        }

        public void Dispose() => Bitmap.Dispose();
    }
}

internal sealed class OverlayWindow : Form
{
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;

    private readonly OverlayController _controller;
    private readonly Rectangle _screenBounds;

    public Rectangle ScreenBounds => _screenBounds;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
            return cp;
        }
    }

    public OverlayWindow(OverlayController controller, Rectangle screenBounds, Icon appIcon)
    {
        _controller = controller;
        _screenBounds = screenBounds;
        Icon = appIcon;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = _screenBounds;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.Magenta);
        _controller.Draw(e.Graphics, _screenBounds);
    }
}

internal sealed class GraphicsStateScope : IDisposable
{
    private readonly Graphics _graphics;
    private readonly GraphicsState _state;

    public GraphicsStateScope(Graphics graphics)
    {
        _graphics = graphics;
        _state = graphics.Save();
    }

    public void Dispose() => _graphics.Restore(_state);
}

internal sealed class ImageAttributesScope : IDisposable
{
    public System.Drawing.Imaging.ImageAttributes Attributes { get; } = new();

    public ImageAttributesScope(float opacity, Color tint)
    {
        var matrix = new System.Drawing.Imaging.ColorMatrix
        {
            Matrix00 = tint.R / 255f,
            Matrix11 = tint.G / 255f,
            Matrix22 = tint.B / 255f,
            Matrix33 = opacity,
            Matrix44 = 1f
        };
        Attributes.SetColorMatrix(matrix);
    }

    public void Dispose() => Attributes.Dispose();
}
