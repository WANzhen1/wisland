namespace WislandSharp.UI;

public sealed class IslandActions
{
    public Action<bool>? SetPassthrough { get; init; }
    public Action<bool>? SetPinned { get; init; }
    public Action? ShowMenu { get; init; }
    public Action? EditConfig { get; init; }
    public Action? ReloadConfig { get; init; }
    public Action? Exit { get; init; }
    public Action<int>? SetTheme { get; init; }
    public Action? RequestRender { get; init; }
    public Action? OpenTranslation { get; init; }
}
