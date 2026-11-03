namespace AvasRoutingTool.ViewModels
{
    public enum DangerAction
    {
        RebootChip,
        RebootMcu
    }

    /// <summary>
    /// A destructive action waiting for the operator's confirmation. The title names the device. When <see cref="TypedName"/>
    /// is set the operator must type exactly that text before the confirm button is enabled.
    /// </summary>
    public sealed record DangerRequest(DangerAction Action, string DeviceName, string Title, string Body, string ConfirmLabel, string? TypedName);
}
