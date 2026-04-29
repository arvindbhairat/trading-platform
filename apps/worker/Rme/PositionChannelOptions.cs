using System.ComponentModel.DataAnnotations;

namespace SignalStack.Worker.Rme;

public sealed class PositionChannelOptions
{
    public const string SectionName = "Worker:Rme:Channel";

    /// <summary>Bounded channel capacity per position. Default: 50 events (ADR-0003).</summary>
    [Range(1, 10_000)]
    public int ChannelCapacity { get; set; } = 50;

    /// <summary>
    /// Log a warning and surface the rme.channel.backlog_warn_depth gauge when
    /// any single position's backlog reaches this depth. Default: 40 (80 % of 50).
    /// REQ-RME-CONC-004.
    /// </summary>
    [Range(1, 10_000)]
    public int BacklogWarnDepth { get; set; } = 40;
}
