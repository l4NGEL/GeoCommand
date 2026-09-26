using System.Net;
using System.Net.Http;
using System.Text;
using GeoCommand.Desktop.Services;
using GeoCommand.Desktop.ViewModels;

namespace GeoCommand.Desktop.Tests;

public class ClientHelpersTests
{
    [Fact]
    public async Task Problem_details_are_turned_into_single_readable_message()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                """{"title":"İşlem mevcut durumda yapılamıyor.","status":409,"detail":"ALFA-1 aracının zaten aktif bir görevi var."}""",
                Encoding.UTF8, "application/problem+json")
        };

        var message = await ApiClient.ReadProblemAsync(response, CancellationToken.None);

        Assert.Equal("İşlem mevcut durumda yapılamıyor. ALFA-1 aracının zaten aktif bir görevi var.", message);
    }

    [Fact]
    public async Task Non_json_error_falls_back_to_status_code()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>") };

        var message = await ApiClient.ReadProblemAsync(response, CancellationToken.None);

        Assert.Contains("502", message);
    }

    [Theory]
    [InlineData("26.09.2026 10:15", true)]
    [InlineData(" 01.01.2026 00:00 ", true)]
    [InlineData("2026-09-26 10:15", false)]
    [InlineData("26.09.2026", false)]
    [InlineData("", false)]
    public void History_time_input_uses_turkish_format(string text, bool valid)
    {
        Assert.Equal(valid, MainViewModel.TryParseLocal(text, out _));
    }

    [Fact]
    public void Parsed_history_time_is_local_time()
    {
        MainViewModel.TryParseLocal("26.09.2026 10:15", out var value);

        Assert.Equal(new DateTime(2026, 9, 26, 10, 15, 0), value.LocalDateTime);
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 26, 10, 15, 0)), value.Offset);
    }
}
