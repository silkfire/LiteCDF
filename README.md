# LiteCDF

![logo](https://github.com/silkfire/LiteCDF/raw/master/img/logo.png)

[![NuGet](https://img.shields.io/nuget/v/LiteCDF.svg)](https://www.nuget.org/packages/LiteCDF)

A high performance reader of [compound document format](https://en.wikipedia.org/wiki/Compound_File_Binary_Format) (CDF) files.


## Usage

Opening a compound file:

```csharp
CompoundFile cf = Cdf.Open(@"C:\path\to\file.cf");
 ```

This will return an object with all the directory entries contained in the file. There are overloads available that can read from a `Stream` or a `byte[]` object for extra convenience.

If you just want to quickly extract a stream, you can use the `OpenAndReadStream` method instead. Its second parameter is a predicate that specifies how to match the name of the stream to extract:

```csharp
byte[] stream = Cdf.OpenAndReadStream(@"C:\path\to\file.cf", n => n == "MyStream");
 ```

This method will return as soon as a matching stream has been found.

To extract more than one stream that matches a specific pattern, you can use the `OpenAndReadMultipleStreams`:

```csharp
Dictionary<string, byte[]> streams = Cdf.OpenAndReadMultipleStreams(@"C:\path\to\file.cf", n => n.EndsWith("Stream"));
 ```

This will return any matching streams in the form of a dictionary where each key-value pair represents the name and the associated byte array of the stream, respectively.