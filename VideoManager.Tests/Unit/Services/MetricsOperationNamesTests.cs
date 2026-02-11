using System.Reflection;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

public class MetricsOperationNamesTests
{
    /// <summary>
    /// All required constant names as specified in the design document.
    /// </summary>
    private static readonly string[] RequiredConstants =
    [
        nameof(MetricsOperationNames.ThumbnailGeneration),
        nameof(MetricsOperationNames.Import),
        nameof(MetricsOperationNames.ImportFile),
        nameof(MetricsOperationNames.Search),
        nameof(MetricsOperationNames.DatabaseQuery),
        nameof(MetricsOperationNames.BatchDelete),
        nameof(MetricsOperationNames.BatchTag),
        nameof(MetricsOperationNames.BatchCategory),
        nameof(MetricsOperationNames.CompensationScan),
    ];

    [Fact]
    public void MetricsOperationNames_ContainsAllRequiredConstants()
    {
        var fields = typeof(MetricsOperationNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => f.Name)
            .ToHashSet();

        foreach (var required in RequiredConstants)
        {
            Assert.Contains(required, fields);
        }
    }

    [Fact]
    public void MetricsOperationNames_AllConstantsHaveNonEmptyValues()
    {
        var fields = typeof(MetricsOperationNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string));

        foreach (var field in fields)
        {
            var value = (string?)field.GetRawConstantValue();
            Assert.False(string.IsNullOrWhiteSpace(value), $"Constant '{field.Name}' must have a non-empty value.");
        }
    }

    [Fact]
    public void MetricsOperationNames_AllConstantValuesAreUnique()
    {
        var fields = typeof(MetricsOperationNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string));

        var values = new HashSet<string>();
        foreach (var field in fields)
        {
            var value = (string)field.GetRawConstantValue()!;
            Assert.True(values.Add(value), $"Duplicate constant value '{value}' found in field '{field.Name}'.");
        }
    }

    [Theory]
    [InlineData(nameof(MetricsOperationNames.ThumbnailGeneration), "thumbnail_generation")]
    [InlineData(nameof(MetricsOperationNames.Import), "import")]
    [InlineData(nameof(MetricsOperationNames.ImportFile), "import_file")]
    [InlineData(nameof(MetricsOperationNames.Search), "search")]
    [InlineData(nameof(MetricsOperationNames.DatabaseQuery), "database_query")]
    [InlineData(nameof(MetricsOperationNames.BatchDelete), "batch_delete")]
    [InlineData(nameof(MetricsOperationNames.BatchTag), "batch_tag")]
    [InlineData(nameof(MetricsOperationNames.BatchCategory), "batch_category")]
    [InlineData(nameof(MetricsOperationNames.CompensationScan), "compensation_scan")]
    public void MetricsOperationNames_ConstantHasExpectedValue(string fieldName, string expectedValue)
    {
        var field = typeof(MetricsOperationNames).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(expectedValue, (string?)field.GetRawConstantValue());
    }

    [Fact]
    public void MetricsOperationNames_IsStaticClass()
    {
        var type = typeof(MetricsOperationNames);
        Assert.True(type.IsAbstract && type.IsSealed, "MetricsOperationNames should be a static class.");
    }
}
