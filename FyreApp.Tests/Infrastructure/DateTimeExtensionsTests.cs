using System.Globalization;
using FyreApp.Infrastructure;
using Xunit;

namespace FyreApp.Tests.Infrastructure;

public class DateTimeExtensionsTests
{
    [Fact]
    public void ToDisplayDate_IsDayMonthNameYear_RegardlessOfCulture()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-US"); // would otherwise give 10/1/2026
        try
        {
            Assert.Equal("01 Oct 2026", new DateTime(2026, 10, 1).ToDisplayDate());
            Assert.Equal("01 Oct 2026, 2:05 PM", new DateTime(2026, 10, 1, 14, 5, 0).ToDisplayDateTime());
            Assert.Null(((DateTime?)null).ToDisplayDate());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
