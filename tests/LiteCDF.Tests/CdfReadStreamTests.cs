namespace LiteCDF.Tests;

using Infrastructure;

using System.IO;
using System.Linq;
using System.Text;

using Xunit;

public class CdfReadStreamTests
{
    [Fact]
    public void OpenAndReadStream_returns_the_matching_stream()
    {
        var payload = Encoding.UTF8.GetBytes(new string('m', 5_000));
        var bytes = new CdfBuilder().AddStream("Other", "x").AddStream("Target", payload).Build();

        var result = Cdf.OpenAndReadStream(bytes, static n => n == "Target");

        Assert.Equal(payload, result);
    }

    [Fact]
    public void OpenAndReadStream_returns_null_when_nothing_matches()
    {
        var bytes = new CdfBuilder().AddStream("A", "a").AddStream("B", "b").Build();

        var result = Cdf.OpenAndReadStream(bytes, static n => n == "DoesNotExist");

        Assert.Null(result);
    }

    [Fact]
    public void OpenAndReadStream_returns_the_first_match_when_predicate_is_broad()
    {
        var bytes = new CdfBuilder()
                    .AddStream("Stream1", "first")
                    .AddStream("Stream2", "second")
                    .Build();

        var result = Cdf.OpenAndReadStream(bytes, static n => n != null && n.StartsWith("Stream"));

        Assert.Equal("first", Encoding.UTF8.GetString(result!));
    }

    [Fact]
    public void OpenAndReadStream_predicate_receives_null_for_empty_padding_slots()
    {
        var bytes = new CdfBuilder().AddStream("Only", "v").Build(); // creates empty padding entries

        // A predicate that never matches forces a full scan, which reaches the trailing empty slots (null names).
        var sawNull = false;
        var result = Cdf.OpenAndReadStream(bytes, n =>
        {
            sawNull |= n is null;
            return false;
        });

        Assert.Null(result);
        Assert.True(sawNull);
    }

    [Fact]
    public void OpenAndReadStream_from_stream_and_file_agree_with_byte_array()
    {
        var payload = Encoding.UTF8.GetBytes(new string('p', 5_000));
        var bytes = new CdfBuilder().AddStream("P", payload).Build();

        using var ms = new MemoryStream(bytes);

        var path = Path.Combine(Path.GetTempPath(), $"litecdf-{Path.GetRandomFileName()}.cf");
        File.WriteAllBytes(path, bytes);

        try
        {
            Assert.Equal(payload, Cdf.OpenAndReadStream(bytes, static n => n == "P"));
            Assert.Equal(payload, Cdf.OpenAndReadStream(ms, static n => n == "P"));
            Assert.Equal(payload, Cdf.OpenAndReadStream(path, static n => n == "P"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OpenAndReadMultipleStreams_returns_all_matches_keyed_by_name()
    {
        var bytes = new CdfBuilder()
                    .AddStream("Doc_1", "one")
                    .AddStream("Skip", "no")
                    .AddStream("Doc_2", "two")
                    .Build();

        var result = Cdf.OpenAndReadMultipleStreams(bytes, static n => n != null && n.StartsWith("Doc_"));

        Assert.Equal(2, result.Count);
        Assert.Equal("one", Encoding.UTF8.GetString(result["Doc_1"]));
        Assert.Equal("two", Encoding.UTF8.GetString(result["Doc_2"]));
        Assert.False(result.ContainsKey("Skip"));
    }

    [Fact]
    public void OpenAndReadMultipleStreams_returns_an_empty_dictionary_when_nothing_matches()
    {
        var bytes = new CdfBuilder().AddStream("A", "a").Build();

        var result = Cdf.OpenAndReadMultipleStreams(bytes, static n => n == "Missing");

        Assert.Empty(result);
    }

    [Fact]
    public void OpenAndReadMultipleStreams_from_stream_matches_byte_array()
    {
        var bytes = new CdfBuilder().AddStream("M1", "1").AddStream("M2", "2").Build();

        using var ms = new MemoryStream(bytes);

        var fromArray = Cdf.OpenAndReadMultipleStreams(bytes, static n => n != null && n.StartsWith('M'));
        var fromStream = Cdf.OpenAndReadMultipleStreams(ms, static n => n != null && n.StartsWith('M'));

        Assert.Equal(fromArray.Keys.OrderBy(static k => k), fromStream.Keys.OrderBy(static k => k));
    }

    [Fact]
    public void OpenAndReadMultipleStreams_from_file_matches_byte_array()
    {
        var bytes = new CdfBuilder().AddStream("F1", "1").AddStream("F2", "2").Build();

        var path = Path.Combine(Path.GetTempPath(), $"litecdf-{Path.GetRandomFileName()}.cf");
        File.WriteAllBytes(path, bytes);

        try
        {
            var fromArray = Cdf.OpenAndReadMultipleStreams(bytes, static n => n != null && n.StartsWith('F'));
            var fromFile = Cdf.OpenAndReadMultipleStreams(path, static n => n != null && n.StartsWith('F'));

            Assert.Equal(fromArray.Keys.OrderBy(static k => k), fromFile.Keys.OrderBy(static k => k));
            Assert.Equal("1", Encoding.UTF8.GetString(fromFile["F1"]));
            Assert.Equal("2", Encoding.UTF8.GetString(fromFile["F2"]));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
