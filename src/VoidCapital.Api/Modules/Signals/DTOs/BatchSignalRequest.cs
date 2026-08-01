namespace VoidCapital.Api.Modules.Signals.DTOs;

/// <summary>Body for the batch approve/reject endpoints.</summary>
public record BatchSignalRequest(int[] Ids);
