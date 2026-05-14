using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;

namespace StructuredStorage;

#pragma warning disable CA1069 // Enums values should not be duplicated
#pragma warning disable CA1724 // Type names should not match namespaces
#pragma warning disable CA1028 // Enum storage should be Int32
#pragma warning disable CA1008 // Enums should have zero value

/// <summary>
/// STGC constants.
/// </summary>
[Flags]
public enum CommitFlags : uint
{
    Default = STGC.STGC_DEFAULT,
    Overwrite = STGC.STGC_OVERWRITE,
    OnlyIfCurrent = STGC.STGC_ONLYIFCURRENT,
    DangerouslyCommitMerelyToDiskCache = STGC.STGC_DANGEROUSLYCOMMITMERELYTODISKCACHE,
    Consolidate = STGC.STGC_CONSOLIDATE,
}

/// <summary>
/// STGM constants.
/// </summary>
[Flags]
public enum StorageModes : uint
{
    FailIfThere = STGM.STGM_FAILIFTHERE,
    Direct = STGM.STGM_DIRECT,
    AccessRead = STGM.STGM_READ,
    AccessWrite = STGM.STGM_WRITE,
    AccessReadWrite = STGM.STGM_READWRITE,
    ShareExclusive = STGM.STGM_SHARE_EXCLUSIVE,
    ShareDenyWrite = STGM.STGM_SHARE_DENY_WRITE,
    ShareDenyRead = STGM.STGM_SHARE_DENY_READ,
    ShareDenyNone = STGM.STGM_SHARE_DENY_NONE,
    Create = STGM.STGM_CREATE,
    Transacted = STGM.STGM_TRANSACTED,
    Convert = STGM.STGM_CONVERT,
    Priority = STGM.STGM_PRIORITY,
    NoScratch = STGM.STGM_NOSCRATCH,
    NoSnapShot = STGM.STGM_NOSNAPSHOT,
    DirectSWMR = STGM.STGM_DIRECT_SWMR,
    DeleteOnRelease = STGM.STGM_DELETEONRELEASE,
    ModeSimple = STGM.STGM_SIMPLE,
}

/// <summary>
/// Wraps a COM structured storage object.
/// </summary>
public sealed class Storage : IDisposable
{
    static readonly Guid IStorageGuid = typeof(IStorage).GUID;

    static STGOPTIONS V3Options => new()
    {
        usVersion = 2,
        reserved = 0,
        ulSectorSize = 512,
        pwcsTemplateFile = null,
    };

    static STGOPTIONS V4Options => new()
    {
        usVersion = 2,
        reserved = 0,
        ulSectorSize = 4096,
        pwcsTemplateFile = null,
    };

    readonly IStorage storage;
    readonly LockBytes? lockBytes; // Prevents garbage collection of in-memory storage

    public Storage? Parent { get; }

    public PropertySetStorage PropertySetStorage { get; }

    internal StatStgCollection StatStgCollection { get; }

    bool disposed;

    // Methods
    internal Storage(IStorage storage, Storage? parent = null, LockBytes? lockBytes = null)
    {
        this.storage = storage;
        Parent = parent;
        this.lockBytes = lockBytes;
        PropertySetStorage = new(storage);
        StatStgCollection = new StatStgCollection(storage);
    }

    public static unsafe Storage Create(string fileName, StorageModes modes = StorageModes.ShareExclusive | StorageModes.AccessReadWrite, bool v4 = true)
    {
        STGOPTIONS opts = v4 ? V4Options : V3Options;
        HRESULT hr = PInvoke.StgCreateStorageEx(fileName, (STGM)modes, STGFMT.STGFMT_DOCFILE, 0, ref opts, (PSECURITY_DESCRIPTOR)null, IStorageGuid, out void* ptr);
        hr.ThrowOnFailure();

        var iStorage = (IStorage)Marshal.GetObjectForIUnknown((nint)ptr);
        Marshal.Release((nint)ptr);
        return new(iStorage);
    }

    public static Storage CreateInMemory(int capacity)
    {
        LockBytes lockBytes = new(capacity);
        HRESULT hr = PInvoke.StgCreateDocfileOnILockBytes(lockBytes.ILockBytes, STGM.STGM_READWRITE | STGM.STGM_SHARE_EXCLUSIVE | STGM.STGM_CREATE, 0, out IStorage storage);
        hr.ThrowOnFailure();
        return new(storage, null, lockBytes);
    }

    public static unsafe Storage Open(MemoryStream stream, StorageModes modes = StorageModes.ShareExclusive | StorageModes.AccessReadWrite)
    {
        stream.Position = 0;

        LockBytes lockBytes = new(stream);
        HRESULT hr = PInvoke.StgOpenStorageOnILockBytes(lockBytes.ILockBytes, null, (STGM)modes, null, out IStorage storage);
        hr.ThrowOnFailure();
        return new(storage);
    }

    public static unsafe Storage Open(string fileName, StorageModes modes = StorageModes.ShareExclusive | StorageModes.AccessReadWrite)
    {
        STGOPTIONS opts = V4Options;
        HRESULT hr = PInvoke.StgOpenStorageEx(fileName, (STGM)modes, STGFMT.STGFMT_DOCFILE, 0, ref opts, (PSECURITY_DESCRIPTOR)null, IStorageGuid, out void* ptr);
        if (hr == HRESULT.STG_E_FILENOTFOUND)
            throw new FileNotFoundException(null, fileName);
        if (hr == HRESULT.STG_E_FILEALREADYEXISTS)
            hr = HRESULT.STG_E_DOCFILECORRUPT;
        hr.ThrowOnFailure();

        var iStorage = (IStorage)Marshal.GetObjectForIUnknown((nint)ptr);
        Marshal.Release((nint)ptr);
        return new(iStorage);
    }

    #region IDisposable Members

    public void Dispose()
    {
        if (disposed)
            return;

        int count = Marshal.ReleaseComObject(storage);
        Debug.Assert(count == 0);

        lockBytes?.Dispose();

        disposed = true;
    }

    #endregion

    public unsafe Storage CreateStorage(string name, StorageModes flags = StorageModes.Create | StorageModes.ShareExclusive | StorageModes.AccessReadWrite)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        fixed (char* namePtr = name)
        {
            storage.CreateStorage(namePtr, (STGM)flags, 0, 0, out IStorage childStorage);
            return new Storage(childStorage, this);
        }
    }

    public unsafe Stream CreateStream(string name, StorageModes flags = StorageModes.Create | StorageModes.ShareExclusive | StorageModes.AccessReadWrite)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        fixed (char* namePtr = name)
        {
            storage.CreateStream(namePtr, (STGM)flags, 0, 0, out IStream stm);
            return new Stream(stm, this);
        }
    }

    internal StatStgEnumerator CreateStatStgEnumerator() => new(storage);

    public IEnumerable<string> EnumerateEntries()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        using StatStgEnumerator enumerator = CreateStatStgEnumerator();
        while (enumerator.MoveNext())
        {
            yield return enumerator.Current.pwcsName.ToString();
        }
    }

    public void DestroyElement(string name)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        storage.DestroyElement(name);
    }

    public void DestroyElementIfExists(string name)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (ContainsElement(name))
            storage.DestroyElement(name);
    }

    public bool ContainsElement(string name)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        return StatStgCollection.Any(s => s.pwcsName.AsSpan().SequenceEqual(name));
    }

    public bool ContainsStream(string name)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        return StatStgCollection.Any(s => (STGTY)s.type == STGTY.STGTY_STREAM && s.pwcsName.AsSpan().SequenceEqual(name));
    }

    public void Commit(CommitFlags flags = CommitFlags.Default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        storage.Commit((uint)flags);
    }

    public void MoveElement(string name, Storage destination) => MoveElement(name, destination, name);

    public void MoveElement(string name, Storage destination, string newName)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        storage.MoveElementTo(name, destination.storage, newName, 0);
    }

    public unsafe Storage OpenStorage(string name, StorageModes flags = StorageModes.AccessReadWrite | StorageModes.ShareExclusive)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        fixed (char* namePtr = name)
        {
            storage.OpenStorage(namePtr, null, (STGM)flags, null, 0, out IStorage childStorage);
            return new Storage(childStorage, this);
        }
    }

    public unsafe Stream OpenStream(string name, StorageModes flags = StorageModes.AccessReadWrite | StorageModes.ShareExclusive)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        fixed (char* namePtr = name)
        {
            storage.OpenStream(namePtr, null, (STGM)flags, 0, out IStream iStream);
            return new Stream(iStream, this);
        }
    }

    public Stream OpenOrCreateStream(string name, StorageModes flags = StorageModes.AccessReadWrite | StorageModes.ShareExclusive)
        => ContainsStream(name) ? OpenStream(name, flags) : CreateStream(name, flags);

    public void Revert()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        storage.Revert();
    }

    public unsafe void SwitchToFile(string fileName)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        fixed (char* fileNamePtr = fileName)
        {
            if (storage is not IRootStorage rootStorage)
                throw new InvalidOperationException("Not file storage.");
            rootStorage.SwitchToFile(fileNamePtr);
        }
    }

    // Properties
    public Guid Id
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            HRESULT hr = PInvoke.ReadClassStg(storage, out Guid guid);
            hr.ThrowOnFailure();
            return guid;
        }

        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            HRESULT hr = PInvoke.WriteClassStg(storage, value);
            hr.ThrowOnFailure();
        }
    }
}
