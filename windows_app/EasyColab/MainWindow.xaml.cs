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
/// Port of Swift <c>EasyColabApp</c> WindowGroup (1480×900 default, 1300×860 min,
/// custom titlebar, theme-aware caption buttons, global shortcuts, live language rebuild).
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int DefaultLogicalWidth = 1480;
    private const int DefaultLogicalHeight = 900;
    private const int MinLogicalWidth = 1300;
    private const int MinLogicalHeight = 860;

    private MainStudioPage? _currentPage;
    private bool _didInitialAppear;
    private bool _isClosed;

    /// <summary>Preserved across live language-change page rebuilds.</summary>
    public bool ShowRightLogPanelState { get; set; } = true;

    /// <summary>Preserved across live language-change page rebuilds.</summary>
    public bool ShowFileListState { get; set; } = false;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        Title = "EasyColab";
        AppWindow.Title = "EasyColab";

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

        ConfigureWindowSizing();
        ApplyTitleBarTheme(RootGrid.ActualTheme);

        RootGrid.ActualThemeChanged += OnRootActualThemeChanged;
        RootGrid.Loaded += OnRootLoaded;
        Closed += OnWindowClosed;
        LanguageManager.Shared.LanguageChanged += OnLanguageChanged;

        MountStudioPage();
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

    private void ConfigureWindowSizing()
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

        int desiredW = (int)Math.Round(DefaultLogicalWidth * scale);
        int desiredH = (int)Math.Round(DefaultLogicalHeight * scale);
        int minW = (int)Math.Round(MinLogicalWidth * scale);
        int minH = (int)Math.Round(MinLogicalHeight * scale);

        try
        {
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            if (display is not null)
            {
                var work = display.WorkArea;
                desiredW = Math.Min(desiredW, work.Width);
                desiredH = Math.Min(desiredH, work.Height);
                minW = Math.Min(minW, work.Width);
                minH = Math.Min(minH, work.Height);
            }
        }
        catch { }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = minW;
            presenter.PreferredMinimumHeight = minH;
        }

        AppWindow.Resize(new SizeInt32(desiredW, desiredH));
    }

    private void ApplyTitleBarTheme(ElementTheme theme)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

        if (theme == ElementTheme.Dark)
        {
            tb.ButtonForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0xF1, 0xF5, 0xF9);
            tb.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
            tb.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF);
            tb.ButtonPressedForegroundColor = Microsoft.UI.Colors.White;
            tb.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF);
            tb.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0x94, 0xA3, 0xB8);
        }
        else
        {
            tb.ButtonForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0x0F, 0x17, 0x2A);
            tb.ButtonHoverForegroundColor = Microsoft.UI.Colors.Black;
            tb.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x12, 0x00, 0x00, 0x00);
            tb.ButtonPressedForegroundColor = Microsoft.UI.Colors.Black;
            tb.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0x20, 0x00, 0x00, 0x00);
            tb.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0x64, 0x74, 0x8B);
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
