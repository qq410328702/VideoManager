using System.ComponentModel.DataAnnotations;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for Data Annotations validation correctness.
/// **Feature: video-manager-optimization-v2, Property 2: Data Annotations 验证正确性**
/// **Validates: Requirements 3.1, 3.2**
///
/// For any string s, when s exceeds the StringLength limit of a field, validation should fail;
/// when s is non-empty and within the limit, validation should pass.
/// Covers VideoEntry.Title (500), Tag.Name (100), and Tag.Color (9).
/// </summary>
public class ValidationPropertyTests
{
    #region Generators

    /// <summary>
    /// Generates non-empty strings with length in [1, maxLength].
    /// Ensures at least one non-whitespace character so [Required] validation passes.
    /// </summary>
    private static FsCheck.Arbitrary<string> ValidStringArb(int maxLength)
    {
        // Use only non-whitespace characters to guarantee [Required] passes
        var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789#_!@".ToCharArray();
        var charGen = FsCheck.Fluent.Gen.Elements(chars);
        var charArrayGen = FsCheck.Fluent.Gen.ArrayOf(charGen);
        var constrainedGen = FsCheck.Fluent.Gen.Where(charArrayGen,
            arr => arr.Length >= 1 && arr.Length <= maxLength);
        var stringGen = FsCheck.Fluent.Gen.Select(constrainedGen,
            arr => new string(arr));
        return FsCheck.Fluent.Arb.From(stringGen);
    }

    /// <summary>
    /// Generates strings with length in [minLength, maxLength] that exceed a given limit.
    /// </summary>
    private static FsCheck.Arbitrary<string> OverLengthStringArb(int minLength, int maxLength)
    {
        var chars = "XYZxyz0123456789#!@_".ToCharArray();
        var charGen = FsCheck.Fluent.Gen.Elements(chars);
        var charArrayGen = FsCheck.Fluent.Gen.ArrayOf(charGen);
        var constrainedGen = FsCheck.Fluent.Gen.Where(charArrayGen,
            arr => arr.Length >= minLength && arr.Length <= maxLength);
        var stringGen = FsCheck.Fluent.Gen.Select(constrainedGen,
            arr => new string(arr));
        return FsCheck.Fluent.Arb.From(stringGen);
    }

    #endregion

    #region VideoEntry.Title

    /// <summary>
    /// Property: For any non-empty string s with length &lt;= 500,
    /// a VideoEntry with that Title and a valid FileName should pass validation.
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property VideoEntry_ValidTitle_PassesValidation()
    {
        return FsCheck.Fluent.Prop.ForAll(ValidStringArb(500), title =>
        {
            var entry = new VideoEntry
            {
                Title = title,
                FileName = "test.mp4",
                FilePath = "/videos/test.mp4",
                ImportedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            var exception = Record.Exception(() => ValidationHelper.ValidateEntity(entry));
            return exception == null;
        });
    }

    /// <summary>
    /// Property: For any string s with length &gt; 500,
    /// a VideoEntry with that Title should fail validation (throw ValidationException).
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property VideoEntry_OverLengthTitle_FailsValidation()
    {
        return FsCheck.Fluent.Prop.ForAll(OverLengthStringArb(501, 1000), title =>
        {
            var entry = new VideoEntry
            {
                Title = title,
                FileName = "test.mp4",
                FilePath = "/videos/test.mp4",
                ImportedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                ValidationHelper.ValidateEntity(entry);
                return false; // Should have thrown
            }
            catch (ValidationException)
            {
                return true;
            }
        });
    }

    #endregion

    #region Tag.Name

    /// <summary>
    /// Property: For any non-empty string s with length &lt;= 100,
    /// a Tag with that Name should pass validation.
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property Tag_ValidName_PassesValidation()
    {
        return FsCheck.Fluent.Prop.ForAll(ValidStringArb(100), name =>
        {
            var tag = new Tag { Name = name };

            var exception = Record.Exception(() => ValidationHelper.ValidateEntity(tag));
            return exception == null;
        });
    }

    /// <summary>
    /// Property: For any string s with length &gt; 100,
    /// a Tag with that Name should fail validation.
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property Tag_OverLengthName_FailsValidation()
    {
        return FsCheck.Fluent.Prop.ForAll(OverLengthStringArb(101, 500), name =>
        {
            var tag = new Tag { Name = name };

            try
            {
                ValidationHelper.ValidateEntity(tag);
                return false; // Should have thrown
            }
            catch (ValidationException)
            {
                return true;
            }
        });
    }

    #endregion

    #region Tag.Color

    /// <summary>
    /// Property: For any string s with length &gt; 9,
    /// a Tag with that Color should fail validation.
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property Tag_OverLengthColor_FailsValidation()
    {
        return FsCheck.Fluent.Prop.ForAll(OverLengthStringArb(10, 100), color =>
        {
            var tag = new Tag
            {
                Name = "ValidTag",
                Color = color
            };

            try
            {
                ValidationHelper.ValidateEntity(tag);
                return false; // Should have thrown
            }
            catch (ValidationException)
            {
                return true;
            }
        });
    }

    /// <summary>
    /// Property: For any non-empty string s with length &lt;= 9,
    /// a Tag with a valid Name and that Color should pass validation.
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property Tag_ValidColor_PassesValidation()
    {
        return FsCheck.Fluent.Prop.ForAll(ValidStringArb(9), color =>
        {
            var tag = new Tag
            {
                Name = "ValidTag",
                Color = color
            };

            var exception = Record.Exception(() => ValidationHelper.ValidateEntity(tag));
            return exception == null;
        });
    }

    #endregion
}
