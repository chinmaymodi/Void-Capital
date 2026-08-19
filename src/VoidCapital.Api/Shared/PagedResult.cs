namespace VoidCapital.Api.Shared;

/// <summary>
/// Paged list contract shared by the trade log and resolved-signals endpoints
/// (items + total + page + pageSize). Replaces anonymous objects so the
/// response is typed end to end (P2).
/// </summary>
public record PagedResult<T>(IEnumerable<T> Items, int Total, int Page, int PageSize);