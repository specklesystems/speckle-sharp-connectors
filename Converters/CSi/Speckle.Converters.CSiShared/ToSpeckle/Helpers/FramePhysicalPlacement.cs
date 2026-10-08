using Speckle.Common.StructuralExtrusion;
using Speckle.DoubleNumerics;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public readonly record struct FramePhysicalPlacement(Vector3 Start, Vector3 End, LocalFrame Frame)
{
  public double Length => (End - Start).Length();

  public static FramePhysicalPlacement? TryCreate(Vector3 start, Vector3 end, LocalFrame analyticalFrame)
  {
    // ENG-10487: projecting analytical local 2 preserves CSI's physical vertical axis. Profile width is the
    // negative IFC horizontal axis, so the existing depth/width convention keeps its handedness.
    var physicalFrame = LocalFrame.TryCreate(end - start, analyticalFrame.XAxis, 0);
    return physicalFrame is null ? null : new FramePhysicalPlacement(start, end, physicalFrame.Value);
  }
}
