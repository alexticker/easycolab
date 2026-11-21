using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;

namespace EasyColab.Views.Workspace;

/// <summary>
/// Port of Swift <c>mediaPreviewCard</c> in MainStudioView+Workspace.swift.
/// Displays either an HD image (loaded via memory stream so local files are never locked) or a video via
/// WinUI <see cref="MediaPlayerElement"/>, cleanly pausing and releasing the <see cref="MediaSource"/>
/// when switching to an image or unloading.
/// </summary>
public sealed partial class MediaPreviewCard : UserControl
{
    public StudioViewModel ViewModel => StudioViewModel.Shared;

    private bool _subscribed;
    private MediaSource? _activeMediaSource;
    private string? _loadedVideoPath;
    private int _imageLoadVersion;

    public MediaPreviewCard()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _subscribed = true;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ViewModel.MediaSelected += OnMediaSelected;
        }
        SyncMediaPreview(forceReloadVideo: false);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            _subscribed = false;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.MediaSelected -= OnMediaSelected;
        }
        // Release video player handle so local media files are never locked while hidden/unloaded.
        ReleaseVideoPlayer();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(StudioViewModel.CurrentMediaURL):
            case nameof(StudioViewModel.CurrentPreviewImage):
            case nameof(StudioViewModel.IsCurrentMediaImage):
            case nameof(StudioViewModel.IsCurrentMediaVideo):
                SyncMediaPreview(forceReloadVideo: false);
                break;

            case nameof(StudioViewModel.MediaElapsedMap):
            case nameof(StudioViewModel.SelectedModelId):
            case nameof(StudioViewModel.ActiveModelSpec):
                SyncMetadataAndPlaceholder();
                break;
        }
    }

    private void OnMediaSelected(object? sender, string? path)
    {
        SyncMediaPreview(forceReloadVideo: true);
    }

    private void SyncMetadataAndPlaceholder()
    {
        EmptyStageIcon.Glyph = ViewModel.ActiveModelSpec.Modality == ModelModality.Image ? "\uE91B" : "\uE8B2";

        var url = ViewModel.CurrentMediaURL;
        var elapsed = ViewModel.ElapsedForMedia(url);
        if (elapsed is { } el && el > 0)
        {
            HeaderElapsedText.Text = L($"生成耗时 {FormatDuration(el)}", $"Elapsed {FormatDuration(el)}");
            HeaderElapsedPill.Visibility = Visibility.Visible;
            FooterElapsedText.Text = FormatDuration(el, compact: true);
            FooterElapsedPill.Visibility = Visibility.Visible;
        }
        else
        {
            HeaderElapsedPill.Visibility = Visibility.Collapsed;
            FooterElapsedPill.Visibility = Visibility.Collapsed;
        }

        if (!string.IsNullOrEmpty(url))
        {
            MediaFooterBar.Visibility = Visibility.Visible;
            FooterFilenameText.Text = Path.GetFileName(url);
            ToolTipService.SetToolTip(FooterFilenameText, url);
            FooterMediaIcon.Glyph = ViewModel.IsCurrentMediaImage ? "\uE91B" : "\uE8B2";
            ReplayButton.Visibility = ViewModel.IsCurrentMediaVideo ? Visibility.Visible : Visibility.Collapsed;
            OpenFileButton.Visibility = ViewModel.IsCurrentMediaImage ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            MediaFooterBar.Visibility = Visibility.Collapsed;
        }
    }

    private void SyncMediaPreview(bool forceReloadVideo)
    {
        SyncMetadataAndPlaceholder();

        if (ViewModel.IsCurrentMediaImage && !string.IsNullOrEmpty(ViewModel.CurrentPreviewImage))
        {
            // Cleanly pause & release any video source before displaying an image.
            ReleaseVideoPlayer();
            VideoPlayer.Visibility = Visibility.Collapsed;
            EmptyStage.Visibility = Visibility.Collapsed;
            ImageStage.Visibility = Visibility.Visible;
            _ = LoadImagePreviewAsync(ViewModel.CurrentPreviewImage);
        }
        else if (ViewModel.IsCurrentMediaVideo && !string.IsNullOrEmpty(ViewModel.CurrentMediaURL))
        {
            var path = ViewModel.CurrentMediaURL;
            ++_imageLoadVersion;
            PreviewImage.Source = null;
            ImageStage.Visibility = Visibility.Collapsed;

            if (!File.Exists(path))
            {
                ReleaseVideoPlayer();
                VideoPlayer.Visibility = Visibility.Collapsed;
                EmptyStage.Visibility = Visibility.Visible;
                return;
            }

            EmptyStage.Visibility = Visibility.Collapsed;
            VideoPlayer.Visibility = Visibility.Visible;

            if (!forceReloadVideo && string.Equals(_loadedVideoPath, path, StringComparison.OrdinalIgnoreCase) && _activeMediaSource is not null)
            {
                return;
            }

            LoadVideo(path);
        }
        else
        {
            ++_imageLoadVersion;
            PreviewImage.Source = null;
            ReleaseVideoPlayer();
            ImageStage.Visibility = Visibility.Collapsed;
            VideoPlayer.Visibility = Visibility.Collapsed;
            EmptyStage.Visibility = Visibility.Visible;
        }
    }

    private async Task LoadImagePreviewAsync(string path)
    {
        int version = ++_imageLoadVersion;
        BitmapImage? bmp = null;

        if (File.Exists(path))
        {
            try
            {
                byte[] bytes = await File.ReadAllBytesAsync(path);
                if (version != _imageLoadVersion) return;
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

        if (version != _imageLoadVersion) return;
        PreviewImage.Source = bmp;
        if (bmp is null)
        {
            ImageStage.Visibility = Visibility.Collapsed;
            EmptyStage.Visibility = Visibility.Visible;
        }
    }

    private void LoadVideo(string path)
    {
        ReleaseVideoPlayer();
        try
        {
            var source = MediaSource.CreateFromUri(new Uri(path));
            _activeMediaSource = source;
            _loadedVideoPath = path;
            VideoPlayer.Source = source;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MediaPreviewCard video load failed: {ex}");
            ReleaseVideoPlayer();
            VideoPlayer.Visibility = Visibility.Collapsed;
            EmptyStage.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Pauses active video playback without clearing the selected media state (e.g. when switching workspace mode).</summary>
    public void PausePlayback()
    {
        try
        {
            VideoPlayer.MediaPlayer?.Pause();
        }
        catch { }
    }

    private void ReleaseVideoPlayer()
    {
        try
        {
            VideoPlayer.MediaPlayer?.Pause();
        }
        catch { }

        try
        {
            VideoPlayer.Source = null;
        }
        catch { }

        try
        {
            _activeMediaSource?.Dispose();
        }
        catch { }

        _activeMediaSource = null;
        _loadedVideoPath = null;
    }

    private void ReplayButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RecordLocalInteraction();
        try
        {
            if (VideoPlayer.MediaPlayer is { } player)
            {
                player.PlaybackSession.Position = TimeSpan.Zero;
                player.Play();
            }
            else if (!string.IsNullOrEmpty(ViewModel.CurrentMediaURL) && File.Exists(ViewModel.CurrentMediaURL))
            {
                LoadVideo(ViewModel.CurrentMediaURL);
            }
        }
        catch { }
    }

    private void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RecordLocalInteraction();
        if (!string.IsNullOrEmpty(ViewModel.CurrentMediaURL))
        {
            LocalRuntimePaths.OpenInExplorer(ViewModel.CurrentMediaURL, reveal: false);
        }
    }

    private void RevealInExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RecordLocalInteraction();
        if (!string.IsNullOrEmpty(ViewModel.CurrentMediaURL))
        {
            LocalRuntimePaths.OpenInExplorer(ViewModel.CurrentMediaURL, reveal: true);
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RecordLocalInteraction();
        LocalRuntimePaths.OpenInExplorer(ViewModel.LocalSaveDir, reveal: false);
    }

    private void RefreshMediaButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RecordLocalInteraction();
        ViewModel.RefreshLocalMedia();
    }
}
