using System.Globalization;
using Avalonia.Media;
using DVModManager.Converters;
using Xunit;

namespace DVModManager.Tests.Converters;

public class DependencyColorConverterTests
{
    private readonly DependencyColorConverter _converter = DependencyColorConverter.Instance;

    [Fact]
    public void Convert_WhenValueIsTrue_ReturnsMissingBrush()
    {
        var result = _converter.Convert(true, typeof(IBrush), null, CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.Parse("#fab387"), brush.Color);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    [InlineData("not a bool")]
    public void Convert_WhenValueIsNotTrue_ReturnsSatisfiedBrush(object? value)
    {
        var result = _converter.Convert(value, typeof(IBrush), null, CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.Parse("#a6adc8"), brush.Color);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack(null, typeof(bool), null, CultureInfo.InvariantCulture));
    }
}
