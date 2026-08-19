using System.ComponentModel.DataAnnotations;

namespace VoidCapital.Api.Tests.TestHelpers;

/// <summary>
/// Validates a record exactly as the MVC model binder does: for each
/// primary-constructor parameter, run its ValidationAttributes against the
/// value of the matching property. ASP.NET Core reads validation metadata from
/// the record's PRIMARY CONSTRUCTOR PARAMETERS (it throws if metadata is
/// placed on the generated property instead), so tests must validate the same
/// way MVC does.
/// </summary>
internal static class RecordValidator
{
    public static IList<ValidationResult> Validate(object dto)
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
                if (result is not null && result != ValidationResult.Success)
                    results.Add(result);
            }
        }

        return results;
    }
}