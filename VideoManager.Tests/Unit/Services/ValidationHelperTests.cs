using System.ComponentModel.DataAnnotations;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="ValidationHelper.ValidateEntity"/> verifying
/// valid and invalid inputs for VideoEntry and Tag models.
/// </summary>
public class ValidationHelperTests
{
    #region Helpers

    private static VideoEntry CreateValidVideoEntry() => new()
    {
        Title = "Test Video",
        FileName = "test.mp4",
        FilePath = "/videos/test.mp4",
        FileSize = 1024,
        Width = 1920,
        Height = 1080,
        ImportedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };

    private static Tag CreateValidTag() => new()
    {
        Name = "Action"
    };

    #endregion

    #region VideoEntry — Valid

    [Fact]
    public void ValidateEntity_ValidVideoEntry_DoesNotThrow()
    {
        var entry = CreateValidVideoEntry();

        var exception = Record.Exception(() => ValidationHelper.ValidateEntity(entry));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateEntity_VideoEntryWithMaxLengthTitle_DoesNotThrow()
    {
        var entry = CreateValidVideoEntry();
        entry.Title = new string('A', 500);

        var exception = Record.Exception(() => ValidationHelper.ValidateEntity(entry));

        Assert.Null(exception);
    }

    #endregion

    #region VideoEntry — Invalid Title

    [Fact]
    public void ValidateEntity_VideoEntryWithEmptyTitle_ThrowsValidationException()
    {
        var entry = CreateValidVideoEntry();
        entry.Title = string.Empty;

        var ex = Assert.Throws<ValidationException>(() => ValidationHelper.ValidateEntity(entry));
        Assert.Contains("Validation failed", ex.Message);
    }

    [Fact]
    public void ValidateEntity_VideoEntryWithTitleExceeding500Chars_ThrowsValidationException()
    {
        var entry = CreateValidVideoEntry();
        entry.Title = new string('A', 501);

        var ex = Assert.Throws<ValidationException>(() => ValidationHelper.ValidateEntity(entry));
        Assert.Contains("Validation failed", ex.Message);
    }

    #endregion

    #region VideoEntry — Invalid FileName

    [Fact]
    public void ValidateEntity_VideoEntryWithEmptyFileName_ThrowsValidationException()
    {
        var entry = CreateValidVideoEntry();
        entry.FileName = string.Empty;

        var ex = Assert.Throws<ValidationException>(() => ValidationHelper.ValidateEntity(entry));
        Assert.Contains("Validation failed", ex.Message);
    }

    #endregion

    #region Tag — Valid

    [Fact]
    public void ValidateEntity_ValidTag_DoesNotThrow()
    {
        var tag = CreateValidTag();

        var exception = Record.Exception(() => ValidationHelper.ValidateEntity(tag));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateEntity_TagWithMaxLengthName_DoesNotThrow()
    {
        var tag = CreateValidTag();
        tag.Name = new string('T', 100);

        var exception = Record.Exception(() => ValidationHelper.ValidateEntity(tag));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateEntity_TagWithValidColor_DoesNotThrow()
    {
        var tag = CreateValidTag();
        tag.Color = "#FF5722FF";

        var exception = Record.Exception(() => ValidationHelper.ValidateEntity(tag));

        Assert.Null(exception);
    }

    #endregion

    #region Tag — Invalid Name

    [Fact]
    public void ValidateEntity_TagWithEmptyName_ThrowsValidationException()
    {
        var tag = CreateValidTag();
        tag.Name = string.Empty;

        var ex = Assert.Throws<ValidationException>(() => ValidationHelper.ValidateEntity(tag));
        Assert.Contains("Validation failed", ex.Message);
    }

    [Fact]
    public void ValidateEntity_TagWithNameExceeding100Chars_ThrowsValidationException()
    {
        var tag = CreateValidTag();
        tag.Name = new string('T', 101);

        var ex = Assert.Throws<ValidationException>(() => ValidationHelper.ValidateEntity(tag));
        Assert.Contains("Validation failed", ex.Message);
    }

    #endregion

    #region Tag — Invalid Color

    [Fact]
    public void ValidateEntity_TagWithColorExceeding9Chars_ThrowsValidationException()
    {
        var tag = CreateValidTag();
        tag.Color = "#FF5722FFA"; // 10 chars, exceeds [StringLength(9)]

        var ex = Assert.Throws<ValidationException>(() => ValidationHelper.ValidateEntity(tag));
        Assert.Contains("Validation failed", ex.Message);
    }

    #endregion
}
