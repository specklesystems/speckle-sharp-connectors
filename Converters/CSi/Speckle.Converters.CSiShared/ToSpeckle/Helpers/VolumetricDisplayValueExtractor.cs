using Speckle.Common.StructuralExtrusion;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Utils;
using Speckle.DoubleNumerics;
using Speckle.Objects.Geometry;
using Speckle.Sdk;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// Inflates the analytical display value into a solid: the section prism placed on the frame's line, or the shell
/// outline extruded half its thickness each way. Null means "keep the wireframe"; it is recorded as a fallback
/// unless the element has no solid to begin with (openings, null sections) (ENG-9048).
/// </summary>
public sealed class VolumetricDisplayValueExtractor
{
  // CSi's own definition of a vertical member: sine of the angle to global Z below this.
  private const double VERTICAL_TOLERANCE = 1e-3;
  private const int CARDINAL_POINT_CENTROID = 10;
  private const string LOCAL_COORDINATE_SYSTEM = "Local";

  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly FrameSectionProfileResolver _profileResolver;
  private readonly IShellThicknessResolver _thicknessResolver;
  private readonly ExtrusionFallbackTracker _fallbacks;

  public VolumetricDisplayValueExtractor(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    FrameSectionProfileResolver profileResolver,
    IShellThicknessResolver thicknessResolver,
    ExtrusionFallbackTracker fallbacks
  )
  {
    _settingsStore = settingsStore;
    _profileResolver = profileResolver;
    _thicknessResolver = thicknessResolver;
    _fallbacks = fallbacks;
  }

  public Mesh? TryExtrudeFrame(CsiFrameWrapper frame, Line axis)
  {
    try
    {
      var frameObj = _settingsStore.Current.SapModel.FrameObj;
      string sectionName = string.Empty,
        autoSelect = string.Empty;
      if (frameObj.GetSection(frame.Name, ref sectionName, ref autoSelect) != 0)
      {
        return Fallback(ModelObjectType.FRAME, "no-section");
      }
      if (string.IsNullOrEmpty(sectionName) || sectionName == CsiName.NONE)
      {
        return null;
      }

      var section = _profileResolver.Resolve(sectionName);
      if (section.Template is null || section.Outline is null)
      {
        return Fallback(ModelObjectType.FRAME, section.ShapeKey);
      }

      var start = new Vector3(axis.start.x, axis.start.y, axis.start.z);
      var end = new Vector3(axis.end.x, axis.end.y, axis.end.z);
      var direction = end - start;
      double length = direction.Length();
      if (double.IsNaN(length) || length <= 0)
      {
        return Fallback(ModelObjectType.FRAME, "zero-length");
      }

      double angleDegrees = 0;
      bool advanced = false;
      _ = frameObj.GetLocalAxes(frame.Name, ref angleDegrees, ref advanced);
      if (advanced)
      {
        return Fallback(ModelObjectType.FRAME, "advanced-axes");
      }

      // CSi default axes: local 2 lies in the vertical plane through the member, except for vertical members where it
      // follows global +X; the local-axis angle then rotates 2 towards 3 about 1.
      double sineToVertical = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y) / length;
      var reference = sineToVertical < VERTICAL_TOLERANCE ? Vector3.UnitX : Vector3.UnitZ;
      var localFrame = LocalFrame.TryCreate(direction, reference, angleDegrees * Math.PI / 180);
      if (localFrame is null)
      {
        return Fallback(ModelObjectType.FRAME, "degenerate-axes");
      }

      var insertion = ReadInsertion(frameObj, frame.Name, section.Outline, localFrame.Value);
      if (insertion is null)
      {
        return Fallback(ModelObjectType.FRAME, "mirrored-section");
      }

      double physicalLength = length + insertion.EndAxial - insertion.StartAxial;
      if (physicalLength <= 0)
      {
        return Fallback(ModelObjectType.FRAME, "zero-length");
      }

      return ToMesh(
        PrismBuilder.Place(
          section.Template,
          start + insertion.StartAxial * localFrame.Value.ZAxis,
          localFrame.Value,
          physicalLength,
          insertion.StartOffset,
          insertion.EndOffset
        )
      );
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return Fallback(ModelObjectType.FRAME, ex.GetType().Name);
    }
  }

  public Mesh? TryExtrudeShell(CsiShellWrapper shell, Mesh outline)
  {
    try
    {
      var areaObj = _settingsStore.Current.SapModel.AreaObj;
      bool isOpening = false;
      _ = areaObj.GetOpening(shell.Name, ref isOpening);
      string sectionName = string.Empty;
      _ = areaObj.GetProperty(shell.Name, ref sectionName);
      if (isOpening || string.IsNullOrEmpty(sectionName) || sectionName == CsiName.NONE)
      {
        return null;
      }

      double thickness = _thicknessResolver.GetThickness(sectionName);
      if (double.IsNaN(thickness) || thickness <= 0)
      {
        return Fallback(ModelObjectType.SHELL, "no-thickness");
      }

      var points = new List<Vector3>(outline.vertices.Count / 3);
      for (int i = 0; i + 2 < outline.vertices.Count; i += 3)
      {
        points.Add(new Vector3(outline.vertices[i], outline.vertices[i + 1], outline.vertices[i + 2]));
      }

      var prism = PrismBuilder.TryExtrudeOutline(points, thickness);
      return prism is null ? Fallback(ModelObjectType.SHELL, "degenerate-outline") : ToMesh(prism);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return Fallback(ModelObjectType.SHELL, ex.GetType().Name);
    }
  }

  /// <summary>Profile shifts in the section plane and axial shifts per end, in the profile frame; null when mirrored.</summary>
  private sealed record Insertion(Vector2 StartOffset, Vector2 EndOffset, double StartAxial, double EndAxial);

  private static Insertion? ReadInsertion(
    cFrameObj frameObj,
    string frameName,
    ProfileOutline outline,
    LocalFrame frame
  )
  {
    int cardinalPoint = CARDINAL_POINT_CENTROID;
    bool mirror2 = false,
      stiffTransform = false;
    double[] jointOffset1 = [],
      jointOffset2 = [];
    string offsetSystem = string.Empty;
    _ = frameObj.GetInsertionPoint(
      frameName,
      ref cardinalPoint,
      ref mirror2,
      ref stiffTransform,
      ref jointOffset1,
      ref jointOffset2,
      ref offsetSystem
    );
    if (mirror2)
    {
      return null;
    }

    var cardinalShift = CardinalPointShift(outline, cardinalPoint);
    var startJoint = ToLocalOffset(jointOffset1, offsetSystem, frame);
    var endJoint = ToLocalOffset(jointOffset2, offsetSystem, frame);
    return new Insertion(
      cardinalShift + new Vector2(startJoint.X, startJoint.Y),
      cardinalShift + new Vector2(endJoint.X, endJoint.Y),
      startJoint.Z,
      endJoint.Z
    );
  }

  // CSi cardinal points 1-9 sit on the section's bounding box (rows bottom/middle/top along local 2, columns
  // left/centre/right along local 3, "left" being the +3 side); 10 is the centroid and 11 the shear centre, which the
  // catalog approximates by the centroid. The analytical line passes through the cardinal point, so the centroid-based
  // profile shifts by the opposite of that point.
  private static Vector2 CardinalPointShift(ProfileOutline outline, int cardinalPoint)
  {
    if (cardinalPoint is < 1 or > 9)
    {
      return Vector2.Zero;
    }

    int row = (cardinalPoint - 1) / 3;
    int column = (cardinalPoint - 1) % 3;
    double depth = row switch
    {
      0 => outline.MinDepth,
      1 => (outline.MinDepth + outline.MaxDepth) / 2,
      _ => outline.MaxDepth,
    };
    double width = column switch
    {
      0 => outline.MaxWidth,
      1 => (outline.MinWidth + outline.MaxWidth) / 2,
      _ => outline.MinWidth,
    };
    return new Vector2(-depth, -width);
  }

  // Joint offsets come as (1, 2, 3) components in the local system or (X, Y, Z) in a global one; returned as
  // (depth, width, axial) to match the profile frame.
  private static Vector3 ToLocalOffset(double[] offset, string coordinateSystem, LocalFrame frame)
  {
    if (offset.Length < 3)
    {
      return Vector3.Zero;
    }
    if (string.Equals(coordinateSystem, LOCAL_COORDINATE_SYSTEM, StringComparison.OrdinalIgnoreCase))
    {
      return new Vector3(offset[1], offset[2], offset[0]);
    }

    var global = new Vector3(offset[0], offset[1], offset[2]);
    return new Vector3(
      Vector3.Dot(global, frame.XAxis),
      Vector3.Dot(global, frame.YAxis),
      Vector3.Dot(global, frame.ZAxis)
    );
  }

  private Mesh? Fallback(ModelObjectType elementType, string reason)
  {
    _fallbacks.Record(elementType.ToString(), reason);
    return null;
  }

  private Mesh ToMesh(PrismMesh prism) =>
    new()
    {
      vertices = prism.Vertices,
      faces = prism.Faces.ToList(),
      units = _settingsStore.Current.SpeckleUnits,
    };
}
