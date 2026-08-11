using VoidCapital.Api.Services;

namespace VoidCapital.Api.Modules.Signals.DTOs;

/// <summary>Status of an async signal-generation job (see ISignalJobService).</summary>
public record SignalJobDto(
    int JobId,
    string Status,
    DateTime StartedAt,
    DateTime? FinishedAt,
    string? Message)
{
    public static SignalJobDto From(SignalJob job) => new(
        job.JobId,
        job.Status.ToString(),
        job.StartedAt,
        job.FinishedAt,
        job.Message);
}