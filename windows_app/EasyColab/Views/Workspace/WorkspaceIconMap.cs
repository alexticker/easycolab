namespace EasyColab.Views.Workspace;

/// <summary>
/// Maps SF Symbol names used in <see cref="OpenModelSpec.Icon"/>, <see cref="TaskModeItem.Icon"/>,
/// and Swift workspace views to Segoe Fluent Icons glyphs.
/// </summary>
internal static class WorkspaceIconMap
{
    public static string GlyphFor(string? sfSymbol) => sfSymbol switch
    {
        "film" or "film.stack" => "\uE8B2",
        "photo" or "photo.artframe" or "photo.badge.arrow.down" => "\uE91B",
        "photo.on.rectangle.angled" => "\uEB9F",
        "camera.aperture" or "camera.metering.matrix" => "\uE722",
        "wand.and.stars" or "wand.and.rays" => "\uE70F",
        "point.3.connected.trianglepath.dotted" => "\uE9D9",
        "macwindow.badge.plus" => "\uE8A7",
        "externaldrive.badge.icloud" => "\uE753",
        "brain" => "\uE99A",
        "cpu" => "\uE950",
        "bubble.left.and.bubble.right" or "bubble.left.and.text.bubble.right" => "\uE8BD",
        "eye" => "\uE7B3",
        "network" => "\uE774",
        "paintbrush.pointed" => "\uE790",
        "bolt" or "bolt.square" => "\uE945",
        "square.on.square" => "\uE8B9",
        "square.stack.3d.up.fill" => "\uE81E",
        "sparkles" or "sparkles.rectangle.stack" => "\uE734",
        "shippingbox.fill" => "\uE7B8",
        "text.alignleft" => "\uE8E4",
        "play.fill" or "play.rectangle" => "\uE768",
        "paperplane.fill" => "\uE724",
        "stop.fill" => "\uE71A",
        "info.circle" => "\uE946",
        "person.fill" => "\uE77B",
        "stopwatch.fill" => "\uE916",
        "clock.arrow.circlepath" => "\uE81C",
        "arrow.clockwise" => "\uE72C",
        "folder" => "\uE8B7",
        _ => "\uE734",
    };
}
