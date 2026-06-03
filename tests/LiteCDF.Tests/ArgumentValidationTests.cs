namespace LiteCDF.Tests;

using Infrastructure;

using System.IO;

using Xunit;

public class ArgumentValidationTests
{
    [Fact]
    public void Open_null_byte_array_throws_empty_data_stream()
    {
        var ex = Assert.Throws<CdfException>(static () => Cdf.Open((byte[])null!));
        Assert.Equal(Errors.EmptyDataStream, ex.Message);
    }

    [Fact]
    public void Open_empty_byte_array_throws_empty_data_stream()
    {
        var ex = Assert.Throws<CdfException>(static () => Cdf.Open([]));
        Assert.Equal(Errors.EmptyDataStream, ex.Message);
    }

    [Fact]
    public void Open_null_stream_throws_empty_data_stream()
    {
        var ex = Assert.Throws<CdfException>(static () => Cdf.Open((Stream)null!));
        Assert.Equal(Errors.EmptyDataStream, ex.Message);
    }

    [Fact]
    public void Open_empty_stream_throws_empty_data_stream()
    {
        using var ms = new MemoryStream();
        var ex = Assert.Throws<CdfException>(() => Cdf.Open(ms));
        Assert.Equal(Errors.EmptyDataStream, ex.Message);
    }

    [Fact]
    public void OpenAndReadStream_null_predicate_throws()
    {
        var bytes = new CdfBuilder().AddStream("A", "a").Build();

        var ex = Assert.Throws<CdfException>(() => Cdf.OpenAndReadStream(bytes, null!));
        Assert.Equal(Errors.StreamNamePredicateNull, ex.Message);
    }

    [Fact]
    public void OpenAndReadStream_empty_byte_array_throws_empty_data_stream()
    {
        var ex = Assert.Throws<CdfException>(static () => Cdf.OpenAndReadStream([], static n => true));
        Assert.Equal(Errors.EmptyDataStream, ex.Message);
    }

    [Fact]
    public void OpenAndReadMultipleStreams_null_predicate_throws()
    {
        var bytes = new CdfBuilder().AddStream("A", "a").Build();

        var ex = Assert.Throws<CdfException>(() => Cdf.OpenAndReadMultipleStreams(bytes, null!));
        Assert.Equal(Errors.StreamNamePredicateNull, ex.Message);
    }

    [Fact]
    public void Open_nonexistent_file_throws_cdf_exception()
    {
        var path = Path.Combine(Path.GetTempPath(), $"litecdf-missing-{Path.GetRandomFileName()}.cfb");

        Assert.Throws<CdfException>(() => Cdf.Open(path));
    }

    [Fact]
    public void OpenAndReadStream_null_byte_array_throws_empty_data_stream()
    {
        var ex = Assert.Throws<CdfException>(static () => Cdf.OpenAndReadStream((byte[])null!, static n => true));
        Assert.Equal(Errors.EmptyDataStream, ex.Message);
    }

    [Fact]
    public void OpenAndReadStream_filepath_null_predicate_throws()
    {
        var ex = Assert.Throws<CdfException>(static () => Cdf.OpenAndReadStream("anything.cfb", null!));
        Assert.Equal(Errors.StreamNamePredicateNull, ex.Message);
    }

    [Fact]
    public void OpenAndReadStream_null_stream_throws_empty_data_stream()
    {
        var ex = Assert.Throws<CdfException>(static () => Cdf.OpenAndReadStream((Stream)null!, static n => true));
        Assert.Equal(Errors.EmptyDataStream, ex.Message);
    }

    [Fact]
    public void OpenAndReadStream_empty_stream_throws_empty_data_stream()
    {
        using var ms = new MemoryStream();
        var ex = Assert.Throws<CdfException>(() => Cdf.OpenAndReadStream(ms, n => true));
        Assert.Equal(Errors.EmptyDataStream, ex.Message);
    }

    [Fact]
    public void OpenAndReadStream_stream_null_predicate_throws()
    {
        var bytes = new CdfBuilder().AddStream("A", "a").Build();
        using var ms = new MemoryStream(bytes);

        var ex = Assert.Throws<CdfException>(() => Cdf.OpenAndReadStream(ms, null!));
        Assert.Equal(Errors.StreamNamePredicateNull, ex.Message);
    }

    [Fact]
    public void OpenAndReadStream_nonexistent_file_throws_cdf_exception()
    {
        var path = Path.Combine(Path.GetTempPath(), $"litecdf-missing-{Path.GetRandomFileName()}.cfb");

        Assert.Throws<CdfException>(() => Cdf.OpenAndReadStream(path, n => true));
    }

    [Fact]
    public void OpenAndReadMultipleStreams_null_byte_array_throws_empty_data_stream()
    {
        var ex = Assert.Throws<CdfException>(static () => Cdf.OpenAndReadMultipleStreams((byte[])null!, static n => true));
        Assert.Equal(Errors.EmptyDataStream, ex.Message);
    }

    [Fact]
    public void OpenAndReadMultipleStreams_filepath_null_predicate_throws()
    {
        var ex = Assert.Throws<CdfException>(static () => Cdf.OpenAndReadMultipleStreams("anything.cfb", null!));
        Assert.Equal(Errors.StreamNamePredicateNull, ex.Message);
    }

    [Fact]
    public void OpenAndReadMultipleStreams_null_stream_throws_empty_data_stream()
    {
        var ex = Assert.Throws<CdfException>(static () => Cdf.OpenAndReadMultipleStreams((Stream)null!, static n => true));
        Assert.Equal(Errors.EmptyDataStream, ex.Message);
    }

    [Fact]
    public void OpenAndReadMultipleStreams_empty_stream_throws_empty_data_stream()
    {
        using var ms = new MemoryStream();
        var ex = Assert.Throws<CdfException>(() => Cdf.OpenAndReadMultipleStreams(ms, n => true));
        Assert.Equal(Errors.EmptyDataStream, ex.Message);
    }

    [Fact]
    public void OpenAndReadMultipleStreams_stream_null_predicate_throws()
    {
        var bytes = new CdfBuilder().AddStream("A", "a").Build();
        using var ms = new MemoryStream(bytes);

        var ex = Assert.Throws<CdfException>(() => Cdf.OpenAndReadMultipleStreams(ms, null!));
        Assert.Equal(Errors.StreamNamePredicateNull, ex.Message);
    }

    [Fact]
    public void OpenAndReadMultipleStreams_nonexistent_file_throws_cdf_exception()
    {
        var path = Path.Combine(Path.GetTempPath(), $"litecdf-missing-{Path.GetRandomFileName()}.cfb");

        Assert.Throws<CdfException>(() => Cdf.OpenAndReadMultipleStreams(path, n => true));
    }
}
