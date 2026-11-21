using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace EasyColab.Controls;

/// <summary>
/// Port of Swift <c>FrameDropWell(title:subtitle:image:)</c>: 155 pt image drop target (dashed 10 pt rounded well,
/// accent 2 pt dash while a drag hovers), "选择本地图片 / Choose Image" picker button, aspect-fit thumbnail with an
/// ✕ clear button. The Swift <c>@Binding var image: NSImage?</c> becomes the TwoWay-bindable <see cref="ImagePath"/>
/// (absolute file path or null); use <see cref="ImageFileHelpers.EncodeImageFileToJpegBase64Async"/> for upload.
/// </summary>
public sealed partial class FrameDropWell : UserControl
{
    private int _loadVersion;

    public FrameDropWell()
    {
        InitializeComponent();
        PickButton.Content = L("选择本地图片", "Choose Image");
        SetTargeted(false);
        UpdateTexts();
        _ = RefreshPreviewAsync();
    }

    /// <summary>Raised whenever <see cref="ImagePath"/> changes (drop, pick, clear or external set). Arg = new path.</summary>
    public event TypedEventHandler<FrameDropWell, string?>? ImagePathChanged;

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(FrameDropWell),
        new PropertyMetadata(string.Empty, (d, _) => ((FrameDropWell)d).UpdateTexts()));

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(FrameDropWell),
        new PropertyMetadata(string.Empty, (d, _) => ((FrameDropWell)d).UpdateTexts()));

    /// <summary>Selected image file path (Swift <c>image</c> binding). Bind with <c>Mode=TwoWay</c>.</summary>
    public string? ImagePath
    {
        get => (string?)GetValue(ImagePathProperty);
        set => SetValue(ImagePathProperty, value);
    }

    public static readonly DependencyProperty ImagePathProperty = DependencyProperty.Register(
        nameof(ImagePath), typeof(string), typeof(FrameDropWell),
        new PropertyMetadata(null, (d, e) =>
        {
            var well = (FrameDropWell)d;
            _ = well.RefreshPreviewAsync();
            well.ImagePathChanged?.Invoke(well, e.NewValue as string);
        }));

    /// <summary>True when a decodable image is currently shown.</summary>
    public bool HasImage { get; private set; }

    private void UpdateTexts()
    {
        TitleText.Text = Title ?? string.Empty;
        SubtitleText.Text = Subtitle ?? string.Empty;
    }

    private void SetTargeted(bool targeted)
    {
        TargetedRect.Visibility = targeted ? Visibility.Visible : Visibility.Collapsed;
        WellRect.StrokeThickness = targeted ? 0 : 1.2;
    }

    private async Task RefreshPreviewAsync()
    {
        var version = ++_loadVersion;
        var path = ImagePath;
        BitmapImage? bmp = null;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(path);
                if (version != _loadVersion) return;
                using var ms = new MemoryStream(bytes);
                using var ras = ms.AsRandomAccessStream();
                var candidate = new BitmapImage();
                await candidate.SetSourceAsync(ras);
                bmp = candidate;
            }
            catch
            {
                bmp = null;
            }
        }
        if (version != _loadVersion) return;

        HasImage = bmp is not null;
        PreviewImage.Source = bmp;
        ImageHost.Visibility = HasImage ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = HasImage ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        ImagePath = null;
    }

    private async void PickButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            };
            foreach (var ext in ImageFileHelpers.PickerExtensions) picker.FileTypeFilter.Add(ext);
            if (App.MainAppWindow is { } window)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            if (await ImageFileHelpers.IsLoadableImageAsync(file.Path))
            {
                ImagePath = file.Path;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FrameDropWell picker failed: {ex}");
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            SetTargeted(true);
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e) => SetTargeted(false);

    private async void OnDrop(object sender, DragEventArgs e)
    {
        SetTargeted(false);
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            // Swift handles only the first provider.
            if (items.Count > 0 && items[0] is StorageFile file && !string.IsNullOrEmpty(file.Path))
            {
                if (await ImageFileHelpers.IsLoadableImageAsync(file.Path))
                {
                    ImagePath = file.Path;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FrameDropWell drop failed: {ex}");
        }
        finally
        {
            deferral.Complete();
        }
    }
}
