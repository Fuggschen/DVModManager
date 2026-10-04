using System.Globalization;
using Avalonia.Media;
using DVModManager.Converters;
using DVModManager.Models;
using Xunit;

namespace DVModManager.Tests.Converters;

public class ModStateToBrushConverterTests
{
    private readonly ModStateToBrushConverter _converter = ModStateToBrushConverter.Instance;

    [Theory]
    [InlineData(ModState.Active, "#2ecc71")]
    [InlineData(ModState.UpdateAvailable, "#f39c12")]
    [InlineData(ModState.MissingDependency, "#e74c3c")]
    [InlineData(ModState.NoMetadata, "#e74c3c")]
    [InlineData(ModState.Inactive, "#7f8c8d")]
    [InlineData(ModState.Missing, "#7f8c8d")]
    public void Convert_WhenModState_ReturnsExpectedColor(ModState state, string hexColor)
    {
        var result = _converter.Convert(state, typeof(IBrush), null, CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.Parse(hexColor), brush.Color);
    }

    [Fact]
    public void Convert_WhenNotModState_ReturnsGrayBrush()
    {
        var result = _converter.Convert("invalid", typeof(IBrush), null, CultureInfo.InvariantCulture);
        Assert.Equal(Brushes.Gray, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack(null, typeof(ModState), null, CultureInfo.InvariantCulture));
    }
}
