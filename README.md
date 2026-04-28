# OpenMcdf

![GitHub Actions](https://github.com/ironfede/openmcdf/actions/workflows/dotnet-desktop.yml/badge.svg)
[![CodeQL](https://github.com/openmcdf/openmcdf/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/openmcdf/openmcdf/actions/workflows/github-code-scanning/codeql)
[![OpenMcdf NuGet](https://img.shields.io/nuget/v/OpenMcdf?label=OpenMcdf%20NuGet)](https://www.nuget.org/packages/OpenMcdf)
[![OpenMcdf.Ole NuGet](https://img.shields.io/nuget/vpre/OpenMcdf.Ole?label=OpenMcdf.Ole%20NuGet)](https://www.nuget.org/packages/OpenMcdf.Ole)
[![NuGet Downloads](https://img.shields.io/nuget/dt/OpenMcdf)](https://www.nuget.org/packages/OpenMcdf)

OpenMcdf is a fully .NET / C# library to manipulate [Compound File Binary File Format](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-cfb/53989ce4-7b05-4f8d-829b-d08d6148375b)
files, also known as [Structured Storage](https://learn.microsoft.com/en-us/windows/win32/stg/structured-storage-start-page).

Compound files include multiple streams of information (document summary, user data) in a single container, and is used
as the bases for many different file formats:

- Advanced Authoring Format (.aaf)
- Microsoft Office (.doc, .xls, .ppt)
- Outlook messages (.msg)
- Visual Studio Solution Options (.suo)
- Windows thumbnails cache files (Thumbs.db)

OpenMcdf v3 has a rewritten API and supports:

- An idiomatic dotnet API and exception hierarchy
- Fast and efficient enumeration and manipulation of storages and streams
- File sizes up to 16 TB (using major format version 4 with 4096 byte sectors)
- Transactions (i.e. commit and/or revert)
- Consolidation (i.e. reclamation of space by removing free sectors)
- Nullable attributes

## Limitations

- Limited error tolerance/recovery
- No support for single writer, multiple readers
- No support for red-black tree balancing

Directory entries are stored in a binary search tree where the entries are sorted but the tree is not balanced. i.e.
the tree is "all-black", which is a valid red-black tree but has suboptimal performance for traversing large trees
(though still considerably faster than some other clients).

Clients such as LibreOffice create trees with red-violations, which OpenMcdf is tolerant to reading and writing.
Files with balanced red-black trees such as those created by Microsoft implementations will currently become unbalanced
upon adding or removing directory entries. Fortunately, since other clients are also tolerant of trees that are either
unbalanced or have red-violations, this should not be a major issue. The Wine implementation also has the same
limitation.

## Getting started

To create a new compound file:

```C#
byte[] b = new byte[10000];

using var root = RootStorage.Create("test.cfb");
using CfbStream stream = root.CreateStream("MyStream");
stream.Write(b, 0, b.Length);
```

To open an Excel workbook (.xls) and access its main data stream:

```C#
using var root = RootStorage.OpenRead("report.xls");
using CfbStream workbookStream = root.OpenStream("Workbook");
```

To create or delete storages and streams:

```C#
using var root = RootStorage.Create("test.cfb");
root.CreateStorage("MyStorage");
root.CreateStream("MyStream");
root.Delete("MyStream");
```

For transacted storages, changes can either be committed or reverted:

```C#
using var root = RootStorage.Create("test.cfb", StorageModeFlags.Transacted);
root.Commit();
//
root.Revert();
```

A root storage can be consolidated to reduce its on-disk size:

```C#
root.Flush(consolidate: true);
```

## Object Linking and Embedding (OLE) Property Set Data Structures

Support for reading and writing [OLE Properties](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-oleps/bf7aeae8-c47a-4939-9f45-700158dac3bc) is available via the [OpenMcdf.Ole](https://www.nuget.org/packages/OpenMcdf.Ole) package. However, ***the API is experimental and subject to change***.

```C#
OlePropertiesContainer co = new(stream);
foreach (OleProperty prop in co.Properties)
{
  ...
}
```

OpenMcdf runs happily on the [Mono](http://www.mono-project.com/) platform and multi-targets
[**netstandard2.0**](https://learn.microsoft.com/en-us/dotnet/standard/net-standard?tabs=net-standard-2-0),
**net8.0**, and **net10.0** to maximize client compatibility and support modern dotnet features.
