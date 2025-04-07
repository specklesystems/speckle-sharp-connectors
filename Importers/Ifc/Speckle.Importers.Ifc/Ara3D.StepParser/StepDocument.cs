using System.Runtime.Intrinsics;
using Ara3D.Buffers;
using Ara3D.Logging;
using Ara3D.Utils;

namespace Speckle.Importers.Ifc.Ara3D.StepParser;

public sealed unsafe class StepDocument : IDisposable
{
  private readonly byte* _dataEnd;
  private readonly AlignedMemory _data;

  /// <summary>
  /// This is a list of raw step instance information.
  /// Each one has only a type and an ID.
  /// </summary>
  public IReadOnlyList<StepRawInstance> RawInstances { get; }

  private readonly int _numRawInstances;

  /// <summary>
  /// This gives us a fast way to look up a StepInstance by their ID
  /// </summary>
  private readonly Dictionary<uint, int> _instanceIdToIndex = new();

  public StepDocument(FilePath filePath, ILogger? logger = null)
  {
    logger ??= Logger.Null;

    logger.Log($"Loading {filePath.GetFileSizeAsString()} of data from {filePath.GetFileName()}");
    _data = AlignedMemoryReader.ReadAllBytes(filePath);
    byte* dataStart = _data.BytePtr;
    _dataEnd = dataStart + _data.NumBytes;

    logger.Log("Computing the start of each line");
    // NOTE: this estimates that the average line length is at least 32 characters.
    // This minimize the number of allocations that happen
    var cap = _data.NumBytes / 32;
    List<int> lineOffsets = new(cap);

    // We are going to report the beginning of the lines, while the "ComputeLines" function
    // will compute the ends of lines.
    var currentLine = 1;
    for (var i = 0; i < _data.NumVectors; i++)
    {
      StepLineParser.ComputeOffsets(((Vector256<byte>*)_data.BytePtr)[i], ref currentLine, lineOffsets);
    }

    logger.Log($"Found {lineOffsets.Count} lines");

    logger.Log($"Creating instance records");
    var rawInstances = new StepRawInstance[lineOffsets.Count];

    for (var i = 0; i < lineOffsets.Count - 1; i++)
    {
      var lineStart = lineOffsets[i];
      var lineEnd = lineOffsets[i + 1];
      var inst = StepLineParser.ParseLine(dataStart + lineStart, dataStart + lineEnd);
      if (inst.IsValid())
      {
        _instanceIdToIndex.Add(inst.Id, _numRawInstances);
        rawInstances[_numRawInstances++] = inst;
      }
    }

    RawInstances = rawInstances;

    logger.Log($"Completed creation of STEP document from {filePath.GetFileName()}");
  }

  public void Dispose() => _data.Dispose();

  public StepInstance GetInstanceWithData(uint id) => GetInstanceWithDataFromIndex(_instanceIdToIndex[id]);

  public StepInstance GetInstanceWithDataFromIndex(int index) => GetInstanceWithData(RawInstances[index]);

  public StepInstance GetInstanceWithData(StepRawInstance inst)
  {
    var attr = inst.GetAttributes(_dataEnd);
    var se = new StepEntity(inst.Type, attr);
    return new StepInstance(inst.Id, se);
  }

  public static StepDocument Create(FilePath fp) => new(fp);

  public IEnumerable<StepRawInstance> GetRawInstancesOfType(string typeCode) =>
    RawInstances.Where(inst => inst.Type.Equals(typeCode));

  public IEnumerable<StepInstance> GetInstances() => RawInstances.Select(GetInstanceWithData);

  public IEnumerable<StepInstance> GetInstances(string typeCode) =>
    GetRawInstancesOfType(typeCode).Select(GetInstanceWithData);
}
