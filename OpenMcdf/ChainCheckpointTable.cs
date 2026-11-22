using System.Diagnostics;

namespace OpenMcdf;

/// <summary>
/// A self-thinning table of sector IDs indexed by chain position.
/// Records every sector initially (stride = 1) and doubles the stride by compacting in-place whenever capacity is reached,
/// bounding memory to a fixed maximum while keeping backward seeks within at most <c>Stride - 1</c> steps.
/// </summary>
internal struct ChainCheckpointTable
{
    // 32,768 entries = 128 KB max per table. At stride 1 this covers 16 MB chains in V3 (512B sectors)
    // and 128 MB chains in V4 (4096B sectors) with zero seek hops.
    internal const int MaxCount = 32768;
    private const int InitialCapacity = 16;

    private uint[]? sectors;
    private int count;
    private uint stride;

    public int Count => count;

    public uint Stride => stride > 0 ? stride : 1;

    public void Add(uint chainIndex, uint sectorId)
    {
        if (stride == 0)
            stride = 1;

        if (chainIndex % stride != 0)
            return;

        if (count == MaxCount)
        {
            int newCount = count / 2;
            for (int i = 1; i < newCount; i++)
                sectors![i] = sectors[i * 2];

            count = newCount;
            stride *= 2;

            if (chainIndex % stride != 0)
                return;
        }

        if (sectors is null)
        {
            sectors = new uint[InitialCapacity];
        }
        else if (count == sectors.Length)
        {
            Array.Resize(ref sectors, Math.Min(sectors.Length * 2, MaxCount));
        }

        Debug.Assert((uint)count * stride == chainIndex);
        sectors[count++] = sectorId;
    }

    public bool TryGetCheckpoint(uint targetIndex, out uint checkpointIndex, out uint sectorId)
    {
        if (count == 0)
        {
            checkpointIndex = uint.MaxValue;
            sectorId = uint.MaxValue;
            return false;
        }

        uint currentStride = Stride;
        uint slot = targetIndex / currentStride;
        if (slot >= (uint)count)
            slot = (uint)count - 1;

        checkpointIndex = slot * currentStride;
        sectorId = sectors![(int)slot];
        return true;
    }

    public void Clear()
    {
        sectors = null;
        count = 0;
        stride = 1;
    }
}
