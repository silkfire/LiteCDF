# LiteCDF

![logo](https://raw.githubusercontent.com/silkfire/LiteCDF/master/img/logo.png)

[![NuGet](https://img.shields.io/nuget/v/LiteCDF.svg)](https://www.nuget.org/packages/LiteCDF)

A high performance reader of [compound document format](https://en.wikipedia.org/wiki/Compound_File_Binary_Format) (CDF) files.


## Usage

Opening a compound document:

```csharp
CompoundDocument document = Cdf.Open(@"C:\path\to\file.cf");
 ```

This will return a `CompoundDocument` object exposing all the directory entries contained in the file through its `DirectoryEntries` property. There are overloads available that can read from a `Stream` or a `byte[]` object for extra convenience.

Each entry is a `CompoundDocument.DirectoryEntry` exposing its `Id`, `Name`, `Type` (an `EntryType` of `Storage`, `Stream` or `RootStorage`) and `IsRootStorageDescendant`. If the entry is a stream, its data is lazily read on first access via the `Stream` property:

```csharp
foreach (var entry in document.DirectoryEntries)
{
    Console.WriteLine($"{entry.Name} ({entry.Type})");

    if (entry.Type == CompoundDocument.DirectoryEntry.EntryType.Stream)
    {
        byte[]? data = entry.Stream;
    }
}
 ```

If you just want to quickly extract a stream, you can use the `OpenAndReadStream` method instead. Its second parameter is a predicate that specifies how to match the name of the stream to extract:

```csharp
byte[]? stream = Cdf.OpenAndReadStream(@"C:\path\to\file.cf", n => n == "MyStream");
 ```

This method will return as soon as a matching stream has been found, or `null` if no stream matched.

To extract more than one stream that matches a specific pattern, you can use the `OpenAndReadMultipleStreams`:

```csharp
ReadOnlyDictionary<string, byte[]> streams = Cdf.OpenAndReadMultipleStreams(@"C:\path\to\file.cf", n => n.EndsWith("Stream"));
 ```

This will return any matching streams in the form of a read-only dictionary where each key-value pair represents the name and the associated byte array of the stream, respectively.

### Restricting to root storage descendants

Every method accepts an optional final `bool` parameter (`rootStorageDescendantsOnly`, or `rootStorageDescendantOnly` on `OpenAndReadStream`) that, when set to `true`, restricts the results to entries that are descendants of the root storage:

```csharp
CompoundDocument document = Cdf.Open(@"C:\path\to\file.cf", rootStorageDescendantsOnly: true);
 ```