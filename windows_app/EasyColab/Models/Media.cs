using CommunityToolkit.Mvvm.ComponentModel;

namespace EasyColab.Models;

/// <summary>
/// Port of Swift <c>LocalMediaItem</c>. Swift <c>url: URL</c> (a local file URL) is stored as the absolute
/// local file path string in <see cref="Url"/>.
/// </summary>
public sealed record LocalMediaItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>Absolute local file path (Swift <c>URL.path</c>).</summary>
    public required string Url { get; init; }
    public required string Filename { get; init; }
    public required bool IsImage { get; init; }
    public required DateTime CreatedAt { get; init; }
    public double? ElapsedSeconds { get; init; }
}

/// <summary>
/// Port of Swift <c>ChatTurnItem</c>. Swift mutates <c>chatHistory[idx].content</c> / <c>.thinking</c> in place while
/// streaming, so this is an observable class (bindings update live) instead of a value record.
/// </summary>
public sealed partial class ChatTurnItem : ObservableObject, IEquatable<ChatTurnItem>
{
    public ChatTurnItem(string role, string content, string thinking = "", bool hasImage = false)
    {
        Role = role;
        Content = content;
        Thinking = thinking;
        HasImage = hasImage;
    }

    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>"user" or "assistant".</summary>
    [ObservableProperty] public partial string Role { get; set; }

    [ObservableProperty] public partial string Content { get; set; }

    [ObservableProperty] public partial string Thinking { get; set; }

    [ObservableProperty] public partial bool HasImage { get; set; }

    // Identity semantics (each turn has a unique Id, as with Swift's `let id = UUID()`).
    public bool Equals(ChatTurnItem? other) => other is not null && other.Id == Id;

    public override bool Equals(object? obj) => Equals(obj as ChatTurnItem);

    public override int GetHashCode() => Id.GetHashCode();
}
