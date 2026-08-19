using System.ComponentModel.DataAnnotations;

namespace VoidCapital.Api.Modules.Signals.DTOs;

/// <summary>
/// Body for the batch approve/reject endpoints. Ids must be non-empty and
/// capped (S5) so a garbage payload cannot trigger a huge execution batch.
/// </summary>
public record BatchSignalRequest(
    [Required, MinLength(1), MaxLength(100)] int[] Ids);