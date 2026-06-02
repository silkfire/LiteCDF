namespace LiteCDF.Tests.Infrastructure;

using System.Text;

using Xunit;

/// <summary>
/// Sanity checks that the synthesizer produces documents the reader accepts and round-trips correctly.
/// If these fail, the fault is in the test harness, not the library.
/// </summary>
public class CdfBuilderSelfTests
{
    [Fact]
    public void A_minimal_document_with_one_standard_stream_round_trips()
    {
        var payload = Encoding.UTF8.GetBytes(new string('A', 5000)); // >= 4096 -> standard stream

        var bytes = new CdfBuilder().AddStream("Workbook", payload).Build();

        var stream = Cdf.OpenAndReadStream(bytes, n => n == "Workbook");

        Assert.Equal(payload, stream);
    }

    [Fact]
    public void A_short_stream_round_trips()
    {
        var payload = Encoding.UTF8.GetBytes("small payload"); // < 4096 -> short stream

        var bytes = new CdfBuilder().AddStream("Tiny", payload).Build();

        var stream = Cdf.OpenAndReadStream(bytes, n => n == "Tiny");

        Assert.Equal(payload, stream);
    }
}
