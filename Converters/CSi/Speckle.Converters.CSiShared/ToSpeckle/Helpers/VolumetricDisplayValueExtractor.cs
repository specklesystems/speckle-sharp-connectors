using Speckle.Common.StructuralExtrusion;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Utils;
using Speckle.DoubleNumerics;
using Speckle.Objects.Geometry;
using Speckle.Sdk;
using Speckle.Sdk.Common;

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
  private readonly ShellGeometryAssignmentReader _shellAssignments;

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
    _shellAssignments = new ShellGeometryAssignmentReader(settingsStore);
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
      var template = ReferenceEquals(insertion.Outline, section.Outline)
        ? section.Template
        : PrismBuilder.TryBuildUnitPrism(insertion.Outline);
      if (template is null)
      {
        return Fallback(ModelObjectType.FRAME, $"{section.ShapeKey}/tessellation-failed");
      }

      double millimeter = Units.GetConversionFactor(Units.Millimeters, _settingsStore.Current.SpeckleUnits);
      string? curveDiagnostic = ReadCurve(frameObj, frame.Name, start, end, 1e-6 * millimeter, out var circularPath);
      if (curveDiagnostic is not null)
      {
        return Fallback(ModelObjectType.FRAME, curveDiagnostic);
      }
      if (circularPath is not null)
      {
        if ((insertion.StartOffset - insertion.EndOffset).Length() > 1e-6 * millimeter)
        {
          return Fallback(ModelObjectType.FRAME, "unsupported-curve-offsets");
        }
        var swept = circularPath.TrySweep(
          template,
          localFrame.Value,
          insertion.StartOffset,
          insertion.CardinalShift,
          0.25 * millimeter
        );
        return swept is null ? Fallback(ModelObjectType.FRAME, "degenerate-curve-sweep") : ToMesh(swept);
      }

      var physicalStart = start + insertion.StartOffset;
      var physicalEnd = end + insertion.EndOffset;
      if ((physicalEnd - physicalStart).LengthSquared() < 1e-12)
      {
        return Fallback(ModelObjectType.FRAME, "zero-length");
      }
      var placement = FramePhysicalPlacement.TryCreate(physicalStart, physicalEnd, localFrame.Value);
      if (placement is null)
      {
        return Fallback(ModelObjectType.FRAME, "degenerate-axes");
      }

      return ToMesh(
        PrismBuilder.Place(
          template,
          placement.Value.Start,
          placement.Value.Frame,
          placement.Value.Length,
          insertion.CardinalShift,
          insertion.CardinalShift
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

      int count = 0;
      double[] offsets = [],
        matrix = [];
      if (
        areaObj.GetOffsets3(shell.Name, ref count, ref offsets) != 0
        || count != points.Count
        || offsets.Length != count
        || offsets.Any(offset => double.IsNaN(offset) || double.IsInfinity(offset))
      )
      {
        return Fallback(ModelObjectType.SHELL, "invalid-insertion");
      }
      if (
        areaObj.GetTransformationMatrix(shell.Name, ref matrix, true) != 0
        || matrix.Length != 9
        || matrix.Any(value => double.IsNaN(value) || double.IsInfinity(value))
      )
      {
        return Fallback(ModelObjectType.SHELL, "invalid-axes");
      }
      var normal = new Vector3(matrix[2], matrix[5], matrix[8]);
      if (normal.LengthSquared() < 1e-12)
      {
        return Fallback(ModelObjectType.SHELL, "invalid-axes");
      }
      normal = Vector3.Normalize(normal);
      var assignments = _shellAssignments.Read(shell.Name, offsets, matrix, thickness);
      PrismMesh? prism;
      if (
        assignments.Displacements.All(displacement => displacement == assignments.Displacements[0])
        && assignments.Thicknesses.All(value => value == thickness)
      )
      {
        prism = PrismBuilder.TryExtrudeOutline(points, thickness);
        if (prism is not null)
        {
          var shift = assignments.Displacements[0];
          for (int i = 0; i < prism.Vertices.Count; i += 3)
          {
            prism.Vertices[i] += shift.X;
            prism.Vertices[i + 1] += shift.Y;
            prism.Vertices[i + 2] += shift.Z;
          }
        }
      }
      else
      {
        prism = ShellMeshBuilder.TryBuild(points, normal, assignments);
      }
      return prism is null ? Fallback(ModelObjectType.SHELL, "degenerate-outline") : ToMesh(prism);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return Fallback(ModelObjectType.SHELL, ex.GetType().Name);
    }
  }

  private sealed record Insertion(
    ProfileOutline Outline,
    Vector3 StartOffset,
    Vector3 EndOffset,
    Vector2 CardinalShift
  );

  private static string? ReadCurve(
    cFrameObj frameObj,
    string name,
    Vector3 start,
    Vector3 end,
    double tolerance,
    out CircularFramePath? path
  )
  {
    path = null;
    int type = 0,
      count = 0;
    double tension = 0;
    double[] x = [],
      y = [],
      z = [];
    int result = frameObj.GetCurved_2(name, ref type, ref tension, ref count, ref x, ref y, ref z);
    if (
      type == 0
      && count == 0
      && (x is null || x.Length == 0)
      && (y is null || y.Length == 0)
      && (z is null || z.Length == 0)
    )
    {
      // ENG-10486: ETABS reports return code 1 with empty curve data for ordinary straight frames.
      return result is 0 or 1 ? null : "curve-read-failed";
    }
    if (result != 0)
    {
      return "curve-read-failed";
    }
    if (type != 1)
    {
      return "unsupported-curve";
    }
    if (
      count != 3
      || x is null
      || y is null
      || z is null
      || x.Length != count
      || y.Length != count
      || z.Length != count
    )
    {
      return "invalid-curve-controls";
    }
    var first = new Vector3(x[0], y[0], z[0]);
    var last = new Vector3(x[1], y[1], z[1]);
    if (!((first - start).Length() <= tolerance) || !((last - end).Length() <= tolerance))
    {
      return "curve-endpoint-mismatch";
    }
    path = CircularFramePath.TryCreate(first, last, new Vector3(x[2], y[2], z[2]));
    return path is null ? "degenerate-curve-controls" : null;
  }

  private static Insertion ReadInsertion(
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
      outline = outline.MirrorAboutDepth();
    }

    var cardinalShift = CardinalPointShift(outline, cardinalPoint);
    var startJoint = ToWorldOffset(jointOffset1, offsetSystem, frame);
    var endJoint = ToWorldOffset(jointOffset2, offsetSystem, frame);
    return new Insertion(outline, startJoint, endJoint, cardinalShift);
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

  private static Vector3 ToWorldOffset(double[] offset, string coordinateSystem, LocalFrame frame)
  {
    if (offset.Length < 3)
    {
      return Vector3.Zero;
    }
    if (string.Equals(coordinateSystem, LOCAL_COORDINATE_SYSTEM, StringComparison.OrdinalIgnoreCase))
    {
      return offset[0] * frame.ZAxis + offset[1] * frame.XAxis + offset[2] * frame.YAxis;
    }

    return new Vector3(offset[0], offset[1], offset[2]);
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
