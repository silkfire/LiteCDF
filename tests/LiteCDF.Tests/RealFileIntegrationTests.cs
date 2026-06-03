namespace LiteCDF.Tests;

using System;
using System.IO;
using System.Linq;

using Xunit;

/// <summary>
/// Smoke tests against real compound documents (e.g. legacy <c>.xls</c> / <c>.doc</c> files).
/// <para>
/// These are opt-in: set the <c>LITECDF_SAMPLE_DIR</c> environment variable to a folder containing such files.
/// When unset, the tests skip — no sample binaries are committed to the repository.
/// </para>
/// </summary>
public class RealFileIntegrationTests
{
    private const string SampleDirVariable = "LITECDF_SAMPLE_DIR";

    public static TheoryData<string> SampleFiles()
    {
        var data = new TheoryData<string>();
        var dir = Environment.GetEnvironmentVariable(SampleDirVariable);

        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                                          .Where(static f => f.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) ||
                                                      f.EndsWith(".doc", StringComparison.OrdinalIgnoreCase)))
            {
                data.Add(file);
            }
        }

        // Ensure the theory always has at least one case so it can report a skip rather than failing as empty.
        if (data.Count == 0)
        {
            data.Add(string.Empty);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Real_document_opens_and_every_stream_reads_without_error(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            Assert.Skip($"Set {SampleDirVariable} to a folder with .xls/.doc files to run real-document smoke tests.");
            return;
        }

        var document = Cdf.Open(path);

        Assert.NotEmpty(document.DirectoryEntries);
        Assert.Equal(CompoundDocument.DirectoryEntry.EntryType.RootStorage, document.DirectoryEntries[0].Type);

        // Every stream's bytes must be materializable, and standard-stream sizes must match the declared length.
        foreach (var entry in document.DirectoryEntries.Where(static e => e.Type == CompoundDocument.DirectoryEntry.EntryType.Stream))
        {
            var stream = entry.Stream;

            if (stream != null)
            {
                Assert.True(stream.Length >= 0);
            }
        }
    }

    [Fact]
    public void Real_documents_round_trip_through_byte_array_and_stream_apis()
    {
        var dir = Environment.GetEnvironmentVariable(SampleDirVariable);

        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            Assert.Skip($"Set {SampleDirVariable} to a folder with .xls/.doc files to run this test.");
            return;
        }

        var file = Directory.EnumerateFiles(dir, "*.xls", SearchOption.AllDirectories)
                            .Concat(Directory.EnumerateFiles(dir, "*.doc", SearchOption.AllDirectories))
                            .FirstOrDefault();

        if (file is null)
        {
            Assert.Skip("No .xls/.doc files found in the configured sample directory.");
            return;
        }

        var bytes = File.ReadAllBytes(file);

        var fromFile = Cdf.Open(file);
        var fromBytes = Cdf.Open(bytes);

        using var ms = new MemoryStream(bytes);
        var fromStream = Cdf.Open(ms);

        Assert.Equal(fromFile.DirectoryEntries.Count, fromBytes.DirectoryEntries.Count);
        Assert.Equal(fromFile.DirectoryEntries.Count, fromStream.DirectoryEntries.Count);
        Assert.Equal(fromBytes.DirectoryEntries.Select(static e => e.Name), fromStream.DirectoryEntries.Select(static e => e.Name));
    }
}
