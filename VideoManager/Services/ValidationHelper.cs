using System.ComponentModel.DataAnnotations;

namespace VideoManager.Services;

/// <summary>
/// Static utility class for validating entities using Data Annotations.
/// </summary>
public static class ValidationHelper
{
    /// <summary>
    /// Validates an entity's Data Annotations. Throws a <see cref="ValidationException"/>
    /// if validation fails, with all error messages joined by "; ".
    /// </summary>
    /// <param name="entity">The entity to validate.</param>
    /// <exception cref="ValidationException">Thrown when validation fails.</exception>
    public static void ValidateEntity(object entity)
    {
        var context = new ValidationContext(entity);
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(entity, context, results, validateAllProperties: true))
        {
            var errors = string.Join("; ", results.Select(r => r.ErrorMessage));
            throw new ValidationException($"Validation failed: {errors}");
        }
    }
}
