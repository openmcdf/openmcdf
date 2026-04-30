namespace OpenMcdf.Tests;

enum WriteMode
{
    SingleByte,
    Array,
    Span,
}

[TestClass]
public sealed class StreamTests
{
#if WINDOWS && false
    [TestMethod]
    [DynamicData(nameof(TestData.ShortVersionsAndSizes), typeof(TestData))]
    public void CreateTestStreamFiles(Version version, int length)
    {
        string directory = "Reference";
        Directory.CreateDirectory(directory);

        string fileName = Path.Combine(directory, $"TestStream_v{(int)version}_{length}.cfs");
        using var rootStorage = StructuredStorage.Storage.Create(fileName, StructuredStorage.StorageModes.AccessReadWrite | StructuredStorage.StorageModes.ShareExclusive, version == Version.V4);
        using StructuredStorage.Stream stream = rootStorage.CreateStream("TestStream");

        // Test files are filled with bytes equal to their position modulo 256
        for (int i = 0; i < length; i++)
            stream.WriteByte((byte)i);
    }
#endif

    [TestMethod]
    [DataRow("TestStream_v3_0.cfs")]
    public void OpenStream(string fileName)
    {
        using var rootStorage = RootStorage.OpenRead(fileName);
        Assert.IsTrue(rootStorage.TryOpenStream("TestStream", out CfbStream? _));
        Assert.IsFalse(rootStorage.TryOpenStream(string.Empty, out CfbStream? _));

        Assert.ThrowsExactly<FileNotFoundException>(() => rootStorage.OpenStream(string.Empty));

        using CfbStream stream = rootStorage.OpenStream("TestStream");
        Assert.AreEqual("TestStream", stream.EntryInfo.Name);
    }

    [TestMethod]
    [DynamicData(nameof(TestData.ShortVersionsAndSizes), typeof(TestData))]
    public void ReadViaCopyTo(Version version, int length)
    {
        // Test files are filled with bytes equal to their position modulo 256
        using MemoryStream expectedStream = new(length);
        for (int i = 0; i < length; i++)
            expectedStream.WriteByte((byte)i);

        string fileName = $"TestStream_v{(int)version}_{length}.cfs";
        using var rootStorage = RootStorage.OpenRead(fileName);

        using Stream stream = rootStorage.OpenStream("TestStream");
        Assert.AreEqual(length, stream.Length);

        using MemoryStream actualStream = new();
        stream.CopyTo(actualStream);

        StreamAssert.AreEqual(expectedStream, actualStream);
    }

#if !NETSTANDARD2_0 && !NETFRAMEWORK
    [TestMethod]
    [DynamicData(nameof(TestData.ShortVersionsAndSizes), typeof(TestData))]
    public void ReadSpan(Version version, int length)
    {
        // Test files are filled with bytes equal to their position modulo 256
        byte[] expectedBuffer = TestData.CreateByteArray(length);

        string fileName = $"TestStream_v{(int)version}_{length}.cfs";
        using var rootStorage = RootStorage.OpenRead(fileName);

        using Stream stream = rootStorage.OpenStream("TestStream");
        Assert.AreEqual(length, stream.Length);

        byte[] actualBuffer = new byte[length];
        stream.ReadExactly(actualBuffer);

        CollectionAssert.AreEqual(expectedBuffer, actualBuffer);
    }
#endif

    [TestMethod]
    [DynamicData(nameof(TestData.ShortVersionsAndSizes), typeof(TestData))]
    public void ReadSingleByte(Version version, int length)
    {
        string fileName = $"TestStream_v{(int)version}_{length}.cfs";
        using var rootStorage = RootStorage.OpenRead(fileName);
        using Stream stream = rootStorage.OpenStream("TestStream");
        Assert.AreEqual(length, stream.Length);

        // Test files are filled with bytes equal to their position modulo 256
        using MemoryStream expectedStream = new(length);
        for (int i = 0; i < length; i++)
            expectedStream.WriteByte((byte)i);

        using MemoryStream actualStream = new();
        for (int i = 0; i < length; i++)
        {
            int value = stream.ReadByte();
            Assert.AreNotEqual(-1, value, "End of stream");
            actualStream.WriteByte((byte)value);
        }

        StreamAssert.AreEqual(expectedStream, actualStream);
    }

    [TestMethod]
    [DataRow(Version.V3, 64)] // Mini-stream
    [DataRow(Version.V4, 4096)] // Regular stream
    public void Seek(Version version, int length)
    {
        string fileName = $"TestStream_v{(int)version}_{length}.cfs";
        using var rootStorage = RootStorage.OpenRead(fileName);
        using Stream stream = rootStorage.OpenStream("TestStream");

        Assert.AreEqual(0, stream.Seek(0, SeekOrigin.Begin));
        Assert.AreEqual(0, stream.Seek(0, SeekOrigin.Current));
        Assert.AreEqual(length, stream.Seek(0, SeekOrigin.End));

        stream.Position = 0;

        Assert.ThrowsExactly<IOException>(() => stream.Seek(-1, SeekOrigin.Begin));
        Assert.ThrowsExactly<IOException>(() => stream.Seek(-1, SeekOrigin.Current));
        Assert.ThrowsExactly<IOException>(() => stream.Seek(-1 - length, SeekOrigin.End));

        Assert.ThrowsExactly<ArgumentException>(() => stream.Seek(length, (SeekOrigin)3));
    }

    [TestMethod]
    [DynamicData(nameof(TestData.VersionsAndSizes), typeof(TestData))]
    public void WriteArray(Version version, int length) => WriteCore(version, length, WriteMode.Array);

#if !NETSTANDARD2_0 && !NETFRAMEWORK
    [TestMethod]
    [DynamicData(nameof(TestData.VersionsAndSizes), typeof(TestData))]
    public void WriteSpan(Version version, int length) => WriteCore(version, length, WriteMode.Span);

    [TestMethod]
    [DynamicData(nameof(TestData.ShortVersionsAndSizes), typeof(TestData))]
    public void WriteSingleByte(Version version, int length) => WriteCore(version, length, WriteMode.SingleByte);
#endif

    static void WriteCore(Version version, int length, WriteMode mode)
    {
        using MemoryStream memoryStream = new();
        using var rootStorage = RootStorage.Create(memoryStream, version);
        using CfbStream stream = rootStorage.CreateStream("TestStream");
        Assert.AreEqual(0, stream.Length);

        byte[] expectedBuffer = TestData.CreateByteArray(length);
        stream.Write(expectedBuffer, mode);
        Assert.AreEqual(length, stream.Length);
        Assert.AreEqual(length, stream.Position);

        rootStorage.Validate();

        byte[] actualBuffer = new byte[length];
        stream.Position = 0;
        stream.ReadExactly(actualBuffer);

        CollectionAssert.AreEqual(expectedBuffer, actualBuffer);
    }

    [TestMethod]
    [DynamicData(nameof(TestData.VersionsAndSizes), typeof(TestData))]
    public void WriteThenRead(Version version, int length)
    {
        byte[] expectedBuffer = TestData.CreateByteArray(length);

        using MemoryStream memoryStream = new();
        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            using CfbStream stream = rootStorage.CreateStream("TestStream");
            Assert.AreEqual(0, stream.Length);

            stream.Write(expectedBuffer, 0, expectedBuffer.Length);
        }

        byte[] actualBuffer = new byte[length];
        using (var rootStorage = RootStorage.Open(memoryStream, StorageModeFlags.LeaveOpen))
        {
            using CfbStream stream = rootStorage.OpenStream("TestStream");
            rootStorage.Validate();
            Assert.AreEqual(length, stream.Length);

            stream.ReadExactly(actualBuffer);
            CollectionAssert.AreEqual(expectedBuffer, actualBuffer);
        }

#if WINDOWS
        using (var rootStorage = StructuredStorage.Storage.Open(memoryStream))
        {
            IEnumerable<string> entries = rootStorage.EnumerateEntries();
            using StructuredStorage.Stream stream = rootStorage.OpenStream("TestStream");
            Assert.AreEqual(length, stream.Length);

            stream.ReadExactly(actualBuffer);
            CollectionAssert.AreEqual(expectedBuffer, actualBuffer);
        }
#endif
    }

#if WINDOWS
    [TestMethod]
    [DynamicData(nameof(TestData.VersionsAndSizes), typeof(TestData))]
    public void StructuredStorageWriteThenRead(Version version, int length)
    {
        byte[] expectedBuffer = TestData.CreateByteArray(length);

        using MemoryStream memoryStream = new();
        string fileName = Path.GetTempFileName();
        File.Delete(fileName);

        using (var rootStorage = StructuredStorage.Storage.Create(fileName, StructuredStorage.StorageModes.AccessReadWrite | StructuredStorage.StorageModes.ShareExclusive, version == Version.V4))
        {
            IEnumerable<string> entries = rootStorage.EnumerateEntries();
            using StructuredStorage.Stream stream = rootStorage.CreateStream("TestStream");

            stream.Write(expectedBuffer, 0, expectedBuffer.Length);
            Assert.AreEqual(length, stream.Length);
        }

        byte[] actualBuffer = new byte[length];
        using (var rootStorage = RootStorage.OpenRead(fileName))
        {
            using CfbStream stream = rootStorage.OpenStream("TestStream");
            rootStorage.Validate();
            Assert.AreEqual(length, stream.Length);

            stream.ReadExactly(actualBuffer);
            CollectionAssert.AreEqual(expectedBuffer, actualBuffer);
        }

        TestFile.TryDelete(fileName);
    }
#endif

    [TestMethod]
    [DynamicData(nameof(TestData.ShortVersionsAndSizes), typeof(TestData))]
    public void WriteMultiple(Version version, int length)
    {
        const int IterationCount = 2048;
        using MemoryStream memoryStream = new();
        using var rootStorage = RootStorage.Create(memoryStream, version);
        using CfbStream stream = rootStorage.CreateStream("TestStream");
        Assert.AreEqual(0, stream.Length);

        byte[] expectedBuffer = TestData.CreateByteArray(length);

        for (int i = 0; i < IterationCount; i++)
        {
            stream.Write(expectedBuffer, 0, expectedBuffer.Length);
            Assert.AreEqual(length * (i + 1), stream.Length);
        }

        stream.Flush();

        byte[] actualBuffer = new byte[length];
        stream.Position = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            actualBuffer.AsSpan().Clear();
            stream.ReadExactly(actualBuffer);
            CollectionAssert.AreEqual(expectedBuffer, actualBuffer);
        }
    }

    [TestMethod]
    [DataRow(Version.V3, 32)]
    [DataRow(Version.V4, 256)]
    public void CreateMultipleStreams(Version version, int streamCount)
    {
        using var rootStorage = RootStorage.CreateInMemory(version);
        for (int i = 0; i < streamCount; i++)
        {
            Assert.AreEqual(i, rootStorage.EnumerateEntries().Count());

            string streamName = $"TestStream{i}";
            using Stream stream = rootStorage.CreateStream(streamName);
        }
    }

    [TestMethod]
    [DynamicData(nameof(TestData.VersionsAndSizes), typeof(TestData))]
    public void Modify(Version version, int length)
    {
        byte[] expectedBuffer = TestData.CreateByteArray(length);

        using MemoryStream memoryStream = new();
        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            using CfbStream stream = rootStorage.CreateStream("TestStream1");
            Assert.AreEqual(0, stream.Length);

            stream.Write(expectedBuffer, 0, expectedBuffer.Length);
        }

        using (var rootStorage = RootStorage.Open(memoryStream, StorageModeFlags.LeaveOpen))
        {
            using CfbStream stream = rootStorage.CreateStream("TestStream2");
            Assert.AreEqual(0, stream.Length);

            stream.Write(expectedBuffer, 0, expectedBuffer.Length);
        }

        using (var rootStorage = RootStorage.Open(memoryStream))
        {
            rootStorage.Validate();

            using (CfbStream stream = rootStorage.OpenStream("TestStream1"))
            {
                Assert.AreEqual(length, stream.Length);

                byte[] actualBuffer = new byte[length];
                stream.ReadExactly(actualBuffer);
                CollectionAssert.AreEqual(expectedBuffer, actualBuffer);
            }

            using (CfbStream stream = rootStorage.OpenStream("TestStream2"))
            {
                Assert.AreEqual(length, stream.Length);

                byte[] actualBuffer = new byte[length];
                stream.ReadExactly(actualBuffer);
                CollectionAssert.AreEqual(expectedBuffer, actualBuffer);
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(TestData.VersionsAndSizes), typeof(TestData))]
    public void ModifyCommitArray(Version version, int length) => ModifyCommit(version, length, WriteMode.Array);

#if !NETSTANDARD2_0 && !NETFRAMEWORK
    [TestMethod]
    [DynamicData(nameof(TestData.VersionsAndSizes), typeof(TestData))]
    public void ModifyCommitSpan(Version version, int length) => ModifyCommit(version, length, WriteMode.Span);
#endif

#if !NETSTANDARD2_0 && !NETFRAMEWORK
    [TestMethod]
    [DynamicData(nameof(TestData.VersionsAndSizes), typeof(TestData))]
    public void ModifyCommitSingleByte(Version version, int length) => ModifyCommit(version, length, WriteMode.SingleByte);
#endif

    static void ModifyCommit(Version version, int length, WriteMode writeMode)
    {
        // Fill with bytes equal to their position modulo 256
        byte[] expectedBuffer = TestData.CreateByteArray(length);

        using MemoryStream memoryStream = new();
        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            using CfbStream stream = rootStorage.CreateStream("TestStream1");
            Assert.AreEqual(0, stream.Length);

            stream.Write(expectedBuffer, 0, expectedBuffer.Length);
        }

        using (var rootStorage = RootStorage.Open(memoryStream, StorageModeFlags.LeaveOpen | StorageModeFlags.Transacted))
        {
            using (CfbStream stream = rootStorage.CreateStream("TestStream2"))
            {
                Assert.AreEqual(0, stream.Length);

                stream.Write(expectedBuffer, writeMode);
            }

            rootStorage.Commit();
        }

        using (var rootStorage = RootStorage.Open(memoryStream))
        {
            rootStorage.Validate();

            using (CfbStream stream = rootStorage.OpenStream("TestStream1"))
            {
                Assert.AreEqual(length, stream.Length);

                byte[] actualBuffer = new byte[length];
                stream.ReadExactly(actualBuffer);
                CollectionAssert.AreEqual(expectedBuffer, actualBuffer);
            }

            using (CfbStream stream = rootStorage.OpenStream("TestStream2"))
            {
                Assert.AreEqual(length, stream.Length);

                byte[] actualBuffer = new byte[length];
                stream.ReadExactly(actualBuffer);
                CollectionAssert.AreEqual(expectedBuffer, actualBuffer);
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(TestData.VersionsAndSizes), typeof(TestData))]
    public void TransactedRead(Version version, int length)
    {
        byte[] expectedBuffer = TestData.CreateByteArray(length);

        using MemoryStream memoryStream = new();
        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            using CfbStream stream = rootStorage.CreateStream("TestStream1");
            Assert.AreEqual(0, stream.Length);

            stream.Write(expectedBuffer, 0, expectedBuffer.Length);
        }

        using (var rootStorage = RootStorage.Open(memoryStream, StorageModeFlags.Transacted))
        {
            using CfbStream stream = rootStorage.OpenStream("TestStream1");
            byte[] actualBuffer = new byte[length];
            stream.ReadExactly(actualBuffer);
            CollectionAssert.AreEqual(expectedBuffer, actualBuffer);
        }
    }

    [TestMethod]
    [DynamicData(nameof(TestData.VersionsAndSizes), typeof(TestData))]
    public void ModifyRevert(Version version, int length)
    {
        byte[] expectedBuffer = TestData.CreateByteArray(length);

        using MemoryStream memoryStream = new();
        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            using CfbStream stream = rootStorage.CreateStream("TestStream1");
            Assert.AreEqual(0, stream.Length);

            stream.Write(expectedBuffer, 0, expectedBuffer.Length);
        }

        using (var rootStorage = RootStorage.Open(memoryStream, StorageModeFlags.LeaveOpen | StorageModeFlags.Transacted))
        {
            using CfbStream stream = rootStorage.CreateStream("TestStream2");
            Assert.AreEqual(0, stream.Length);

            stream.Write(expectedBuffer, 0, expectedBuffer.Length);
            rootStorage.Revert();
        }

        using (var rootStorage = RootStorage.Open(memoryStream))
        {
            rootStorage.Validate();

            using (CfbStream stream = rootStorage.OpenStream("TestStream1"))
            {
                Assert.AreEqual(length, stream.Length);

                byte[] actualBuffer = new byte[length];
                stream.ReadExactly(actualBuffer);
                CollectionAssert.AreEqual(expectedBuffer, actualBuffer);
            }

            Assert.ThrowsExactly<FileNotFoundException>(() => rootStorage.OpenStream("TestStream2"));
        }
    }

    [TestMethod]
    [DynamicData(nameof(TestData.VersionsAndSizes), typeof(TestData))]
    public void Shrink(Version version, int length)
    {
        using MemoryStream memoryStream = new();
        using var rootStorage = RootStorage.Create(memoryStream, version);
        using CfbStream stream = rootStorage.CreateStream("Test");

        byte[] expectedBuffer = TestData.CreateByteArray(length);
        stream.Write(expectedBuffer, 0, expectedBuffer.Length);
        Assert.AreEqual(length, stream.Length);

        long baseStreamLength = memoryStream.Length;

        int newLength = length / 2;
        stream.SetLength(newLength);
        Assert.AreEqual(newLength, stream.Length);

        stream.Position = newLength;
        stream.Write(expectedBuffer, newLength, expectedBuffer.Length - newLength);
        Assert.AreEqual(length, stream.Length);

        byte[] actualBuffer = new byte[length];
        stream.Seek(0, SeekOrigin.Begin);
        stream.ReadExactly(actualBuffer);

        CollectionAssert.AreEqual(expectedBuffer, actualBuffer);
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void MiniFatToFat(Version version)
    {
        using MemoryStream memoryStream = new();
        using var rootStorage = RootStorage.Create(memoryStream, version);
        using CfbStream stream = rootStorage.CreateStream("Test");

        int length = 256;
        byte[] expectedBuffer = TestData.CreateByteArray(length);

        int iterations = (int)Header.MiniStreamCutoffSize / length;
        for (int i = 0; i < iterations; i++)
            stream.Write(expectedBuffer, 0, expectedBuffer.Length);

        Assert.AreEqual(length * iterations, stream.Length);

        byte[] actualBuffer = new byte[length];
        stream.Position = 0;
        for (int i = 0; i < iterations; i++)
        {
            actualBuffer.AsSpan().Clear();
            stream.ReadExactly(actualBuffer);
            CollectionAssert.AreEqual(expectedBuffer, actualBuffer);
        }
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void FatToMiniFat(Version version)
    {
        const int length = 256;

        using MemoryStream memoryStream = new();
        using var rootStorage = RootStorage.Create(memoryStream, version);
        using CfbStream stream = rootStorage.CreateStream("Test");

        byte[] expectedBuffer = TestData.CreateByteArray(length);
        int iterations = (int)Header.MiniStreamCutoffSize / length;
        for (int i = 0; i < iterations; i++)
            stream.Write(expectedBuffer, 0, expectedBuffer.Length);

        Assert.AreEqual(length * iterations, stream.Length);

        byte[] actualBuffer = new byte[length];

        // Check reading from the regular sectors
        stream.Position = 0;
        for (int i = 0; i < iterations; i++)
        {
            actualBuffer.AsSpan().Clear();
            stream.ReadExactly(actualBuffer);
            CollectionAssert.AreEqual(expectedBuffer, actualBuffer);
        }

        stream.SetLength(length);
        Assert.AreEqual(length, stream.Length);

        stream.Position = 0;
        stream.ReadExactly(actualBuffer);
        CollectionAssert.AreEqual(expectedBuffer, actualBuffer);
    }

    [TestMethod]
    [DynamicData(nameof(TestData.ShortVersionsAndSizes), typeof(TestData))]
    public void CopyFromStream(Version version, int length)
    {
        using MemoryStream memoryStream = new();
        using var rootStorage = RootStorage.Create(memoryStream, version);
        using CfbStream stream = rootStorage.CreateStream("TestStream");
        Assert.AreEqual(0, stream.Length);

        // Fill with bytes equal to their position modulo 256
        using MemoryStream expectedStream = new(length);
        for (int i = 0; i < length; i++)
            expectedStream.WriteByte((byte)i);

        expectedStream.Position = 0;
        expectedStream.CopyTo(stream);
        Assert.AreEqual(length, stream.Length);

        using MemoryStream actualStream = new();
        stream.Seek(0, SeekOrigin.Begin);
        stream.CopyTo(actualStream);

        StreamAssert.AreEqual(expectedStream, actualStream);
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void CreateDuplicateStreamThrowsException(Version version)
    {
        using MemoryStream memoryStream = new();
        using var rootStorage = RootStorage.Create(memoryStream, version);
        using CfbStream stream = rootStorage.CreateStream("Test");
        Assert.ThrowsExactly<IOException>(() => rootStorage.CreateStream("Test"));
    }

    [TestMethod]
    [DataRow(Version.V3, RootContext.MaximumV3StreamLength)]
    [DataRow(Version.V4, SectorType.Maximum * 4096L)]
    public void MaximumLength(Version version, long length)
    {
        using var rootStorage = RootStorage.CreateInMemory(version);
        using CfbStream stream = rootStorage.CreateStream("Test");
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.SetLength(length + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = length + 1);

        stream.Position = length;
        Assert.Throws<IOException>(() => stream.WriteByte(0));
    }
}
