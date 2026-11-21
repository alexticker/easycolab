using System.Runtime.InteropServices;
using EasyColab.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace EasyColab;

/// <summary>
/// Native Windows 11 WinUI 3 main window with Mica Alt backdrop, centered smart sizing,
/// window bounds/state persistence, live theme switching, and language-rebuild support.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int DefaultLogicalWidth = 1380;
    private const int DefaultLogicalHeight = 860;
    private const int MinLogicalWidth = 1100;
    private const int MinLogicalHeight = 720;

    private const string KeyWinX = "winBoundsX";
    private const string KeyWinY = "winBoundsY";
    private const string KeyWinW = "winBoundsW";
    private const string KeyWinH = "winBoundsH";
    private const string KeyWinMax = "winMaximized";
    private const string KeyAppTheme = "winAppTheme";

    private MainStudioPage? _currentPage;
    private bool _didInitialAppear;
    private bool _isClosed;
    private RectInt32? _lastNormalBounds;

    /// <summary>Preserved across live language-change page rebuilds.</summary>
    public bool ShowRightLogPanelState { get; set; } = true;

    /// <summary>Preserved across live language-change page rebuilds.</summary>
    public bool ShowFileListState { get; set; } = false;

    /// <summary>Current user theme override: Default (follow Windows), Light, or Dark.</summary>
    public ElementTheme ThemePreference { get; private set; } = ElementTheme.Default;

    public event EventHandler? ThemePreferenceChanged;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        Title = "EasyColab";
        AppWindow.Title = "EasyColab";

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        // Ctrl+Shift+/ (VK_OEM_2 = 0xBF = 191) cannot be expressed by enum name in XAML
        var slashGuideAccel = new KeyboardAccelerator
        {
            Key = (VirtualKey)191,
            Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
        };
        slashGuideAccel.Invoked += OpenGuideAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(slashGuideAccel);

        LoadAndApplySavedTheme();
        ConfigureWindowBounds();
        ApplyTitleBarTheme(RootGrid.ActualTheme);

        AppWindow.Changed += OnAppWindowChanged;
        RootGrid.ActualThemeChanged += OnRootActualThemeChanged;
        RootGrid.Loaded += OnRootLoaded;
        Closed += OnWindowClosed;
        LanguageManager.Shared.LanguageChanged += OnLanguageChanged;

        MountStudioPage();
    }

    private void LoadAndApplySavedTheme()
    {
        var saved = SettingsStore.Shared.GetString(KeyAppTheme) ?? "system";
        ThemePreference = saved switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        RootGrid.RequestedTheme = ThemePreference;
    }

    /// <summary>Cycles System -> Light -> Dark and persists preference.</summary>
    public void CycleThemePreference()
    {
        ThemePreference = ThemePreference switch
        {
            ElementTheme.Default => ElementTheme.Light,
            ElementTheme.Light => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        string raw = ThemePreference switch
        {
            ElementTheme.Light => "light",
            ElementTheme.Dark => "dark",
            _ => "system",
        };
        SettingsStore.Shared.Set(KeyAppTheme, raw);
        RootGrid.RequestedTheme = ThemePreference;
        ApplyTitleBarTheme(RootGrid.ActualTheme);
        ThemePreferenceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Registers a non-interactive top header strip as the window's draggable titlebar.</summary>
    public void SetCustomTitleBar(UIElement dragArea)
    {
        try
        {
            SetTitleBar(dragArea);
            Title = "EasyColab";
            AppWindow.Title = "EasyColab";
        }
        catch
        {
            // Best-effort during teardown / early init.
        }
    }

    private void ConfigureWindowBounds()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        double scale = 1.0;
        try
        {
            uint dpi = GetDpiForWindow(hwnd);
            if (dpi >= 96) scale = dpi / 96.0;
        }
        catch
        {
            scale = 1.0;
        }

        int minW = (int)Math.Round(MinLogicalWidth * scale);
        int minH = (int)Math.Round(MinLogicalHeight * scale);

        var primaryDisplay = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var primaryWork = primaryDisplay?.WorkArea ?? new RectInt32(0, 0, (int)Math.Round(1600 * scale), (int)Math.Round(960 * scale));

        minW = Math.Min(minW, Math.Max(640, primaryWork.Width - 40));
        minH = Math.Min(minH, Math.Max(480, primaryWork.Height - 40));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = minW;
            presenter.PreferredMinimumHeight = minH;
        }

        var store = SettingsStore.Shared;
        bool hasSaved = store.Contains(KeyWinW) && store.Contains(KeyWinH) && store.Contains(KeyWinX) && store.Contains(KeyWinY);
        if (hasSaved)
        {
            int sx = store.GetInt(KeyWinX, 0);
            int sy = store.GetInt(KeyWinY, 0);
            int sw = Math.Max(minW, store.GetInt(KeyWinW, 0));
            int sh = Math.Max(minH, store.GetInt(KeyWinH, 0));

            if (IsRectVisibleOnAnyDisplay(sx, sy, sw, sh))
            {
                var rect = new RectInt32(sx, sy, sw, sh);
                _lastNormalBounds = rect;
                AppWindow.MoveAndResize(rect);

                if (store.GetBool(KeyWinMax, false) && AppWindow.Presenter is OverlappedPresenter maxPresenter)
                {
                    maxPresenter.Maximize();
                }
                return;
            }
        }

        // First launch or saved display disconnected: fit comfortably inside work area (<= 90%) and center cleanly.
        int maxInitW = (int)Math.Round(primaryWork.Width * 0.90);
        int maxInitH = (int)Math.Round(primaryWork.Height * 0.90);
        int desiredW = Math.Clamp((int)Math.Round(DefaultLogicalWidth * scale), minW, Math.Max(minW, maxInitW));
        int desiredH = Math.Clamp((int)Math.Round(DefaultLogicalHeight * scale), minH, Math.Max(minH, maxInitH));

        int centerX = primaryWork.X + Math.Max(0, (primaryWork.Width - desiredW) / 2);
        int centerY = primaryWork.Y + Math.Max(0, (primaryWork.Height - desiredH) / 2);
        var centered = new RectInt32(centerX, centerY, desiredW, desiredH);
        _lastNormalBounds = centered;
        AppWindow.MoveAndResize(centered);
    }

    private static bool IsRectVisibleOnAnyDisplay(int x, int y, int w, int h)
    {
        try
        {
            var displays = DisplayArea.FindAll();
            for (int i = 0; i < displays.Count; i++)
            {
                var work = displays[i].WorkArea;
                int interLeft = Math.Max(x, work.X);
                int interTop = Math.Max(y, work.Y);
                int interRight = Math.Min(x + w, work.X + work.Width);
                int interBottom = Math.Min(y + h, work.Y + work.Height);
                if (interRight - interLeft >= 240 && interBottom - interTop >= 180)
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_isClosed) return;
        if (!args.DidPositionChange && !args.DidSizeChange && !args.DidPresenterChange) return;

        if (sender.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Restored)
        {
            var pos = sender.Position;
            var sz = sender.Size;
            if (sz.Width > 300 && sz.Height > 200)
            {
                _lastNormalBounds = new RectInt32(pos.X, pos.Y, sz.Width, sz.Height);
            }
        }
    }

    private void SaveWindowBounds()
    {
        try
        {
            var store = SettingsStore.Shared;
            bool isMaximized = AppWindow.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Maximized;
            store.Set(KeyWinMax, isMaximized);

            RectInt32? bounds = _lastNormalBounds;
            if (!isMaximized)
            {
                var pos = AppWindow.Position;
                var sz = AppWindow.Size;
                if (sz.Width > 300 && sz.Height > 200)
                {
                    bounds = new RectInt32(pos.X, pos.Y, sz.Width, sz.Height);
                }
            }

            if (bounds is { } b)
            {
                store.Set(KeyWinX, b.X);
                store.Set(KeyWinY, b.Y);
                store.Set(KeyWinW, b.Width);
                store.Set(KeyWinH, b.Height);
            }
        }
        catch { }
    }

    private void ApplyTitleBarTheme(ElementTheme theme)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

        if (theme == ElementTheme.Dark)
        {
            tb.ButtonForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            tb.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
            tb.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF);
            tb.ButtonPressedForegroundColor = Microsoft.UI.Colors.White;
            tb.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF);
            tb.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF);
        }
        else
        {
            tb.ButtonForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0x19, 0x19, 0x19);
            tb.ButtonHoverForegroundColor = Microsoft.UI.Colors.Black;
            tb.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x0D, 0x00, 0x00, 0x00);
            tb.ButtonPressedForegroundColor = Microsoft.UI.Colors.Black;
            tb.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0x18, 0x00, 0x00, 0x00);
            tb.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(0x70, 0x00, 0x00, 0x00);
        }
    }

    private void MountStudioPage()
    {
        if (_isClosed) return;

        if (_currentPage is not null)
        {
            _currentPage.Detach();
        }

        RootGrid.Children.Clear();
        var next = new MainStudioPage();
        _currentPage = next;
        RootGrid.Children.Add(next);
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        ApplyTitleBarTheme(RootGrid.ActualTheme);
        _currentPage?.RegisterTitleBarDragArea();

        if (_didInitialAppear) return;
        _didInitialAppear = true;

        bool seenGuide = SettingsStore.Shared.GetBool("hasSeenOnboardingGuide_v1", false)
            || SettingsStore.Shared.GetBool("hasSeenEasyColabGuideV1", false);
        if (!seenGuide)
        {
            UiDispatcher.After(TimeSpan.FromMilliseconds(350), () =>
            {
                if (!_isClosed)
                {
                    StudioViewModel.Shared.OpenOnboardingGuide(0);
                }
            });
        }
    }

    private void OnRootActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyTitleBarTheme(sender.ActualTheme);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UiDispatcher.Run(() =>
        {
            if (!_isClosed)
            {
                MountStudioPage();
            }
        });
    }

    private void ToggleLanguageAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        StudioViewModel.Shared.ToggleLanguage();
        args.Handled = true;
    }

    private void OpenGuideAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        StudioViewModel.Shared.OpenOnboardingGuide(0);
        args.Handled = true;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_isClosed) return;
        _isClosed = true;

        SaveWindowBounds();

        AppWindow.Changed -= OnAppWindowChanged;
        LanguageManager.Shared.LanguageChanged -= OnLanguageChanged;
        RootGrid.ActualThemeChanged -= OnRootActualThemeChanged;
        RootGrid.Loaded -= OnRootLoaded;

        _currentPage?.Detach();
        _currentPage = null;

        StudioViewModel.Shared.Shutdown();
        SettingsStore.Shared.Flush();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
}
