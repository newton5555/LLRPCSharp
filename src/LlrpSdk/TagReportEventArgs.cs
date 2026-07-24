namespace LlrpSdk;

/// <summary>
/// Provides one version-independent tag observation raised by <see cref="LlrpReader.TagsReported"/>.
/// </summary>
public sealed class TagReportEventArgs : EventArgs
{
    /// <summary>Initializes the event payload.</summary>
    /// <param name="report">The decoded tag observation.</param>
    public TagReportEventArgs(TagReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Report = report;
    }

    /// <summary>Gets the decoded tag observation.</summary>
    public TagReport Report { get; }
}
