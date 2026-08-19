using FluentAssertions;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Tests.TestHelpers;
using Xunit;

namespace VoidCapital.Api.Tests.Controllers;

/// <summary>
/// F10: settings money knobs must reject out-of-range values. The controllers
/// are [ApiController], so DataAnnotations on the request records are enforced
/// by automatic model validation (400 before persistence). Validation runs via
/// <see cref="RecordValidator"/>, which mirrors how MVC reads attributes from
/// the primary constructor parameters.
/// </summary>
public class SettingsValidationTests
{
    [Fact]
    public void UpdateSettingsRequest_ValidValues_Pass()
    {
        var request = new UpdateSettingsRequest(
            AutoExecute: true,
            MinConfidence: 0.5m,
            NegativeLimit: 100000m,
            InterestRate: 0.1825m,
            Watchlist: new[] { "RELIANCE" });

        RecordValidator.Validate(request).Should().BeEmpty();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void UpdateSettingsRequest_MinConfidenceOutsideZeroToOne_Fails(decimal minConfidence)
    {
        var request = new UpdateSettingsRequest(true, minConfidence, 0m, 0m, Array.Empty<string>());

        var errors = RecordValidator.Validate(request);
        errors.Should().Contain(e => e.MemberNames.Contains(nameof(UpdateSettingsRequest.MinConfidence)));
    }

    [Fact]
    public void UpdateSettingsRequest_NegativeLimitBelowZero_Fails()
    {
        var request = new UpdateSettingsRequest(true, 0.5m, -1m, 0m, Array.Empty<string>());

        var errors = RecordValidator.Validate(request);
        errors.Should().Contain(e => e.MemberNames.Contains(nameof(UpdateSettingsRequest.NegativeLimit)));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(0.51)]
    public void UpdateSettingsRequest_InterestRateOutsideZeroToHalf_Fails(decimal interestRate)
    {
        var request = new UpdateSettingsRequest(true, 0.5m, 0m, interestRate, Array.Empty<string>());

        var errors = RecordValidator.Validate(request);
        errors.Should().Contain(e => e.MemberNames.Contains(nameof(UpdateSettingsRequest.InterestRate)));
    }

    [Fact]
    public void UpdateSettingsRequest_AnnualInterestRateBoundary_AcceptsMax()
    {
        // 0.5 = 50% annual is the documented ceiling; 0.1825 = 18.25% annual
        // (0.05% daily) is the reckless-agent seed value.
        var request = new UpdateSettingsRequest(true, 0.5m, 0m, 0.5m, Array.Empty<string>());

        RecordValidator.Validate(request).Should().BeEmpty();
    }

    [Fact]
    public void GlobalSettingsRequest_ValidValues_Pass()
    {
        var request = new GlobalSettingsRequest(MinConfidence: 0.5m, NegativeLimit: 100000m, InterestRate: 0.1825m, Watchlist: new[] { "RELIANCE" });

        RecordValidator.Validate(request).Should().BeEmpty();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void GlobalSettingsRequest_MinConfidenceOutsideZeroToOne_Fails(decimal minConfidence)
    {
        var request = new GlobalSettingsRequest(minConfidence, 0m, 0m, Array.Empty<string>());

        var errors = RecordValidator.Validate(request);
        errors.Should().Contain(e => e.MemberNames.Contains(nameof(GlobalSettingsRequest.MinConfidence)));
    }
}