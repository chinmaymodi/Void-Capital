using System.ComponentModel.DataAnnotations;
using System.Reflection;
using FluentAssertions;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using Xunit;

namespace VoidCapital.Api.Tests.Controllers;

/// <summary>
/// F10: settings money knobs must reject out-of-range values. The controllers
/// are [ApiController], so DataAnnotations on the request records are enforced
/// by automatic model validation (400 before persistence). ASP.NET Core reads
/// validation metadata from the record's PRIMARY CONSTRUCTOR PARAMETERS (it
/// throws if metadata is placed on the generated property instead), so these
/// tests validate the same way MVC does: attributes on the constructor
/// parameters, applied to the matching property values.
/// </summary>
public class SettingsValidationTests
{
    /// <summary>
    /// Validates a record exactly as the MVC model binder does: for each
    /// primary-constructor parameter, run its ValidationAttributes against
    /// the value of the matching property.
    /// </summary>
    private static IList<ValidationResult> ValidateRecord(object dto)
    {
        var results = new List<ValidationResult>();
        var type = dto.GetType();
        var ctor = type.GetConstructors().Single();
        var properties = type.GetProperties();

        foreach (var parameter in ctor.GetParameters())
        {
            var attributes = parameter
                .GetCustomAttributes(typeof(ValidationAttribute), inherit: true)
                .Cast<ValidationAttribute>();
            var property = properties.Single(p => p.Name == parameter.Name);
            var value = property.GetValue(dto);

            foreach (var attribute in attributes)
            {
                var result = attribute.GetValidationResult(
                    value, new ValidationContext(dto) { MemberName = property.Name });
                if (result != ValidationResult.Success)
                    results.Add(result);
            }
        }

        return results;
    }

    [Fact]
    public void UpdateSettingsRequest_ValidValues_Pass()
    {
        var request = new UpdateSettingsRequest(
            AutoExecute: true,
            MinConfidence: 0.5m,
            NegativeLimit: 100000m,
            InterestRate: 0.1825m,
            Watchlist: new[] { "RELIANCE" });

        ValidateRecord(request).Should().BeEmpty();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void UpdateSettingsRequest_MinConfidenceOutsideZeroToOne_Fails(decimal minConfidence)
    {
        var request = new UpdateSettingsRequest(true, minConfidence, 0m, 0m, Array.Empty<string>());

        var errors = ValidateRecord(request);
        errors.Should().Contain(e => e.MemberNames.Contains(nameof(UpdateSettingsRequest.MinConfidence)));
    }

    [Fact]
    public void UpdateSettingsRequest_NegativeLimitBelowZero_Fails()
    {
        var request = new UpdateSettingsRequest(true, 0.5m, -1m, 0m, Array.Empty<string>());

        var errors = ValidateRecord(request);
        errors.Should().Contain(e => e.MemberNames.Contains(nameof(UpdateSettingsRequest.NegativeLimit)));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(0.51)]
    public void UpdateSettingsRequest_InterestRateOutsideZeroToHalf_Fails(decimal interestRate)
    {
        var request = new UpdateSettingsRequest(true, 0.5m, 0m, interestRate, Array.Empty<string>());

        var errors = ValidateRecord(request);
        errors.Should().Contain(e => e.MemberNames.Contains(nameof(UpdateSettingsRequest.InterestRate)));
    }

    [Fact]
    public void UpdateSettingsRequest_AnnualInterestRateBoundary_AcceptsMax()
    {
        // 0.5 = 50% annual is the documented ceiling; 0.1825 = 18.25% annual
        // (0.05% daily) is the reckless-agent seed value.
        var request = new UpdateSettingsRequest(true, 0.5m, 0m, 0.5m, Array.Empty<string>());

        ValidateRecord(request).Should().BeEmpty();
    }

    [Fact]
    public void GlobalSettingsRequest_ValidValues_Pass()
    {
        var request = new GlobalSettingsRequest(MinConfidence: 0.5m, Watchlist: new[] { "RELIANCE" });

        ValidateRecord(request).Should().BeEmpty();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void GlobalSettingsRequest_MinConfidenceOutsideZeroToOne_Fails(decimal minConfidence)
    {
        var request = new GlobalSettingsRequest(minConfidence, Array.Empty<string>());

        var errors = ValidateRecord(request);
        errors.Should().Contain(e => e.MemberNames.Contains(nameof(GlobalSettingsRequest.MinConfidence)));
    }
}