using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenMcdf;

/// <summary>
/// The storage type of a <see cref="DirectoryEntry"/>.
/// </summary>
enum StorageType
{
    Unallocated = 0,
    Storage = 1,
    Stream = 2,
    Root = 5,
}

/// <summary>
/// Red-black node color.
/// </summary>
enum NodeColor
{
    Red = 0,
    Black = 1,
}

/// <summary>
/// Encapsulates data about a <see cref="Storage"/> or Stream.
/// </summary>
internal sealed class DirectoryEntry : IEquatable<DirectoryEntry?>
{
    internal const int Length = 128;
    internal const int NameFieldLength = 64;
    internal const uint MaxV3StreamLength = 0x80000000;

    internal static readonly byte[] Unallocated = new byte[128];

    public uint Id { get; set; }

    public byte[] Name { get; } = new byte[NameFieldLength];

    public ushort NameLength { get; set; }

    /// <summary>
    /// Gets or sets the type of the storage object.
    /// </summary>
    public StorageType Type { get; set; } = StorageType.Unallocated;

    public NodeColor Color { get; set; }

    /// <summary>
    /// Gets or sets stream ID of the left sibling.
    /// </summary>
    public uint LeftSiblingId { get; set; } = StreamId.NoStream;

    /// <summary>
    /// Gets or sets stream ID of the right sibling.
    /// </summary>
    public uint RightSiblingId { get; set; } = StreamId.NoStream;

    /// <summary>
    /// Gets or sets stream ID of the child.
    /// </summary>
    public uint ChildId { get; set; } = StreamId.NoStream;

    /// <summary>
    /// Gets or sets GUID for storage objects.
    /// </summary>
    public Guid CLSID { get; set; }

    /// <summary>
    /// Gets or sets user defined flags for storage objects.
    /// </summary>
    public uint StateBits { get; set; }

    /// <summary>
    /// Gets or sets the creation time of the storage object.
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// Gets or sets the modified time of the storage object.
    /// </summary>
    public DateTime ModifiedTime { get; set; }

    /// <summary>
    /// Gets or sets the starting sector location for a stream or the first sector of the mini-stream for the root storage object.
    /// </summary>
    public uint StartSectorId { get; set; } = StreamId.NoStream;

    /// <summary>
    /// Gets or sets the length of the stream.
    /// </summary>
    public long StreamLength { get; set; }

    internal char ColorChar => Color switch
    {
        NodeColor.Red => 'R',
        NodeColor.Black => 'B',
        _ => '?',
    };

    public ReadOnlySpan<byte> NameByteSpan
    {
        get
        {
            int clampedNameLength = Math.Max(0, Math.Min(NameFieldLength, NameLength - 2));
            return Name.AsSpan(0, clampedNameLength);
        }
    }

    public ReadOnlySpan<char> NameCharSpan => MemoryMarshal.Cast<byte, char>(NameByteSpan);

    public string NameString
    {
        get
        {
            int clampedNameLength = Math.Max(0, Math.Min(NameFieldLength, NameLength - 2));
            return Encoding.Unicode.GetString(Name, 0, clampedNameLength);
        }
        set => NameLength = (ushort)(Encoding.Unicode.GetBytes(value, 0, value.Length, Name, 0) + 2);
    }

    public uint GetSiblingId(SiblingType siblingType) => siblingType is SiblingType.Left ? LeftSiblingId : RightSiblingId;

    public override int GetHashCode()
    {
        HashCode code = default;
        code.Add(Id);
        code.Add(NameLength);
        foreach (byte b in Name)
            code.Add(b);
        return code.ToHashCode();
    }

    public override bool Equals(object? obj) => Equals(obj as DirectoryEntry);

    public bool Equals(DirectoryEntry? other)
    {
        return other is not null
            && Id == other.Id
            && Name.SequenceEqual(other.Name)
            && NameLength == other.NameLength
            && Type == other.Type
            && Color == other.Color
            && LeftSiblingId == other.LeftSiblingId
            && RightSiblingId == other.RightSiblingId
            && ChildId == other.ChildId
            && CLSID == other.CLSID
            && StateBits == other.StateBits
            && CreationTime == other.CreationTime
            && ModifiedTime == other.ModifiedTime
            && StartSectorId == other.StartSectorId
            && StreamLength == other.StreamLength;
    }

    public void RecycleRoot() => Recycle(StorageType.Root, "Root Entry");

    public void Recycle() => Recycle(StorageType.Unallocated, string.Empty);

    public void Recycle(StorageType storageType, string name)
    {
        Type = storageType;
        NameString = name;
        Color = NodeColor.Black;
        LeftSiblingId = StreamId.NoStream;
        RightSiblingId = StreamId.NoStream;
        ChildId = StreamId.NoStream;
        StartSectorId = storageType is StorageType.Stream or StorageType.Root ? StreamId.NoStream : 0;
        StreamLength = 0;

        if (storageType is StorageType.Root)
        {
            CreationTime = FileTime.UtcZero;
            ModifiedTime = DateTime.UtcNow;
        }
        else if (storageType is StorageType.Storage)
        {
            DateTime now = DateTime.UtcNow;
            CreationTime = now;
            ModifiedTime = now;
        }
        else
        {
            CreationTime = FileTime.UtcZero;
            ModifiedTime = FileTime.UtcZero;
        }
    }

    public EntryType EntryType => Type switch
    {
        StorageType.Stream => EntryType.Stream,
        StorageType.Storage => EntryType.Storage,
        StorageType.Root => EntryType.Storage,
        _ => throw new FileFormatException($"Invalid storage type: {Type}."),
    };

    public EntryInfo ToEntryInfo(string path) => new(EntryType, path, NameString, StreamLength, CLSID, CreationTime, ModifiedTime);

    [ExcludeFromCodeCoverage]
    public override string ToString() => $"{Id}: \"{NameString}\"";

    public DirectoryEntry CloneWithNoStream()
    {
        DirectoryEntry clone = new()
        {
            Id = Id,
            NameLength = NameLength,
            Type = Type,
            Color = Color,
            LeftSiblingId = LeftSiblingId,
            RightSiblingId = RightSiblingId,
            ChildId = ChildId,
            CLSID = CLSID,
            StateBits = StateBits,
            CreationTime = CreationTime,
            ModifiedTime = ModifiedTime,
            StartSectorId = StreamId.NoStream,
            StreamLength = 0,
        };

        Array.Copy(Name, clone.Name, Name.Length);

        return clone;
    }
}
