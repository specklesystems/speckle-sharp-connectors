using System.Runtime.Intrinsics;
using Ara3D.Buffers;
using Ara3D.Logging;
using Ara3D.Utils;

namespace Speckle.Connectors.Ifc.Ara3D.StepParser;

public sealed unsafe class StepDocument : IDisposable
{
  public readonly FilePath FilePath;
  public readonly byte* DataStart;
  public readonly byte* DataEnd;
  public readonly AlignedMemory Data;

  /// <summary>
  /// This is a list of raw step instance information.
  /// Each one has only a type and an ID.
  /// </summary>
  public readonly StepRawInstance[] RawInstances;

  /// <summary>
  /// The number of raw instance
  /// </summary>
  public readonly int NumRawInstances;

  /// <summary>
  /// This gives us a fast way to look up a StepInstance by their ID
  /// </summary>
  public readonly Dictionary<uint, int> InstanceIdToIndex = new();

  /// <summary>
  /// This tells us the byte offset of the start of each line in the file
  /// </summary>
  public readonly List<int> LineOffsets;

  public StepDocument(FilePath filePath, ILogger? logger = null)
  {
    FilePath = filePath;
    logger ??= Logger.Null;

    logger.Log($"Loading {filePath.GetFileSizeAsString()} of data from {filePath.GetFileName()}");
    Data = AlignedMemoryReader.ReadAllBytes(filePath);
    DataStart = Data.BytePtr;
    DataEnd = DataStart + Data.NumBytes;

    logger.Log($"Computing the start of each line");
    // NOTE: this estimates that the average line length is at least 32 characters.
    // This minimize the number of allocations that happen
    var cap = Data.NumBytes / 32;
    LineOffsets = new List<int>(cap);

    // We are going to report the beginning of the lines, while the "ComputeLines" function
    // will compute the ends of lines.
    var currentLine = 1;
    for (var i = 0; i < Data.NumVectors; i++)
    {
      StepLineParser.ComputeOffsets(((Vector256<byte>*)Data.BytePtr)[i], ref currentLine, LineOffsets);
    }

    logger.Log($"Found {LineOffsets.Count} lines");

    logger.Log($"Creating instance records");
    RawInstances = new StepRawInstance[LineOffsets.Count];

    for (var i = 0; i < LineOffsets.Count - 1; i++)
    {
      var lineStart = LineOffsets[i];
      var lineEnd = LineOffsets[i + 1];
      var inst = StepLineParser.ParseLine(DataStart + lineStart, DataStart + lineEnd);
      if (inst.IsValid())
      {
        InstanceIdToIndex.Add(inst.Id, NumRawInstances);
        RawInstances[NumRawInstances++] = inst;
      }
    }

    logger.Log($"Completed creation of STEP document from {filePath.GetFileName()}");
  }

  public void Dispose() => Data.Dispose();

  public StepInstance GetInstanceWithData(uint id) => GetInstanceWithDataFromIndex(InstanceIdToIndex[id]);

  public StepInstance GetInstanceWithDataFromIndex(int index) => GetInstanceWithData(RawInstances[index]);

  public StepInstance GetInstanceWithData(StepRawInstance inst)
  {
    var attr = inst.GetAttributes(DataEnd);
    var se = new StepEntity(inst.Type, attr);
    return new StepInstance(inst.Id, se);
  }

  public static StepDocument Create(FilePath fp) => new(fp);

  public IEnumerable<StepRawInstance> GetRawInstances(string typeCode) =>
    RawInstances.Where(inst => inst.Type.Equals(typeCode));

  public IEnumerable<StepInstance> GetInstances() => RawInstances.Select(GetInstanceWithData);

  public IEnumerable<StepInstance> GetInstances(string typeCode) =>
    GetRawInstances(typeCode).Select(GetInstanceWithData);
}
