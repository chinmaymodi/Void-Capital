using FluentAssertions;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Signals.DTOs;
using VoidCapital.Api.Tests.TestHelpers;
using Xunit;

namespace VoidCapital.Api.Tests.Controllers;

/// <summary>
/// A7 + S5: TradeRequest and BatchSignalRequest must reject garbage payloads
/// before they reach the ledger or the execution pipeline. Controllers are
/// [ApiController], so DataAnnotations on the primary constructor parameters
/// are enforced by automatic model validation (400 before persistence).
/// </summary>
public class RequestValidationTests
{
    // ---- A7: TradeRequest ----

    [Fact]
    public void TradeRequest_ValidValues_Pass()
    {
        var request = new TradeRequest("RELIANCE", 10);

        RecordValidator.Validate(request).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TradeRequest_EmptyOrNullSymbol_Fails(string? symbol)
    {
        var request = new TradeRequest(symbol!, 10);

        var errors = RecordValidator.Validate(request);
        errors.Should().Contain(e => e.MemberNames.Contains(nameof(TradeRequest.Symbol)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void TradeRequest_NonPositiveShares_Fails(int shares)
    {
        var request = new TradeRequest("RELIANCE", shares);

        var errors = RecordValidator.Validate(request);
        errors.Should().Contain(e => e.MemberNames.Contains(nameof(TradeRequest.Shares)));
    }

    // ---- S5: BatchSignalRequest ----

    [Fact]
    public void BatchSignalRequest_ValidIds_Pass()
    {
        var request = new BatchSignalRequest(new[] { 1, 2, 3 });

        RecordValidator.Validate(request).Should().BeEmpty();
    }

    [Fact]
    public void BatchSignalRequest_EmptyIds_Fails()
    {
        var request = new BatchSignalRequest(Array.Empty<int>());

        var errors = RecordValidator.Validate(request);
        errors.Should().Contain(e => e.MemberNames.Contains(nameof(BatchSignalRequest.Ids)));
    }

    [Fact]
    public void BatchSignalRequest_OverCapIds_Fails()
    {
        var request = new BatchSignalRequest(Enumerable.Range(1, 101).ToArray());

        var errors = RecordValidator.Validate(request);
        errors.Should().Contain(e => e.MemberNames.Contains(nameof(BatchSignalRequest.Ids)));
    }

    [Fact]
    public void BatchSignalRequest_ExactlyCapIds_Passes()
    {
        var request = new BatchSignalRequest(Enumerable.Range(1, 100).ToArray());

        RecordValidator.Validate(request).Should().BeEmpty();
    }
}