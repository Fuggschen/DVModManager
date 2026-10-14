using System.Globalization;
using DVModManager.Converters;
using Xunit;

namespace DVModManager.Tests.Converters;

public class NullToBoolConverterTests
{
    private readonly NullToBoolConverter _converter = NullToBoolConverter.Instance;

    [Fact]
    public void Convert_WhenNotNull_ReturnsTrue()
    {
        var result = _converter.Convert("hello", typeof(bool), null, CultureInfo.InvariantCulture);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Convert_WhenNull_ReturnsFalse()
    {
        var result = _converter.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture);
        Assert.Equal(false, result);
    }

    [Theory]
    [InlineData("invert")]
    [InlineData("INVERT")]
    public void Convert_WithInvertParam_WhenNotNull_ReturnsFalse(string param)
    {
        var result = _converter.Convert("hello", typeof(bool), param, CultureInfo.InvariantCulture);
        Assert.Equal(false, result);
    }

    [Theory]
    [InlineData("invert")]
    [InlineData("Invert")]
    public void Convert_WithInvertParam_WhenNull_ReturnsTrue(string param)
    {
        var result = _converter.Convert(null, typeof(bool), param, CultureInfo.InvariantCulture);
        Assert.Equal(true, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack(true, typeof(object), null, CultureInfo.InvariantCulture));
    }
}
