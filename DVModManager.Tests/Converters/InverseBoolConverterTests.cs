using System.Globalization;
using DVModManager.Converters;
using Xunit;

namespace DVModManager.Tests.Converters;

public class InverseBoolConverterTests
{
    private readonly InverseBoolConverter _converter = InverseBoolConverter.Instance;

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Convert_WhenValueIsBool_ReturnsInvertedValue(bool input, bool expected)
    {
        var result = _converter.Convert(input, typeof(bool), null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("text")]
    [InlineData(123)]
    [InlineData(null)]
    public void Convert_WhenValueIsNotBool_ReturnsOriginalValue(object? input)
    {
        var result = _converter.Convert(input, typeof(bool), null, CultureInfo.InvariantCulture);
        Assert.Equal(input, result);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ConvertBack_WhenValueIsBool_ReturnsInvertedValue(bool input, bool expected)
    {
        var result = _converter.ConvertBack(input, typeof(bool), null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ConvertBack_WhenValueIsNotBool_ReturnsOriginalValue()
    {
        var result = _converter.ConvertBack("test", typeof(bool), null, CultureInfo.InvariantCulture);
        Assert.Equal("test", result);
    }
}
