using Speckle.Common.StructuralExtrusion;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Utils;
using Speckle.DoubleNumerics;
using Speckle.Objects.Geometry;
using Speckle.Sdk;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// Inflates the analytical display value into a solid: the section prism placed on the frame's line, or the shell
/// outline extruded half its thickness each way. Null means "keep the wireframe" and is always recorded (ENG-9048).
/// </summary>
public sealed class VolumetricDisplayValueExtractor
{
  private const double VERTICAL_TOLERANCE = 1e-3;
  private const string NO_SECTION = "None";

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
      var sapModel = _settingsStore.Current.SapModel;
      string sectionName = string.Empty,
        autoSelect = string.Empty;
      if (
        sapModel.FrameObj.GetSection(frame.Name, ref sectionName, ref autoSelect) != 0
        || string.IsNullOrEmpty(sectionName)
        || sectionName == NO_SECTION
      )
      {
        return Fallback(ModelObjectType.FRAME, "no-section");
      }

      var section = _profileResolver.Resolve(sectionName);
      if (section.Template is null)
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
      _ = sapModel.FrameObj.GetLocalAxes(frame.Name, ref angleDegrees, ref advanced);
      if (advanced)
      {
        return Fallback(ModelObjectType.FRAME, "advanced-axes");
      }

      // CSi default axes: local 2 lies in the vertical plane through the member, except for vertical members (sine of
      // the angle to global Z below 1e-3, CSi's own definition) where it follows global +X; the local-axis angle then
      // rotates 2 towards 3 about 1.
      double sineToVertical = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y) / length;
      var reference = sineToVertical < VERTICAL_TOLERANCE ? Vector3.UnitX : Vector3.UnitZ;
      var localFrame = LocalFrame.TryCreate(direction, reference, angleDegrees * Math.PI / 180);
      if (localFrame is null)
      {
        return Fallback(ModelObjectType.FRAME, "degenerate-axes");
      }

      return ToMesh(PrismBuilder.Place(section.Template, start, localFrame.Value, length));
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
      if (isOpening)
      {
        return Fallback(ModelObjectType.SHELL, "opening");
      }

      string sectionName = string.Empty;
      _ = areaObj.GetProperty(shell.Name, ref sectionName);

      double thickness = _thicknessResolver.GetThickness(sectionName);
      if (double.IsNaN(thickness) || thickness <= 0)
      {
        return Fallback(ModelObjectType.SHELL, sectionName == NO_SECTION ? NO_SECTION : "no-thickness");
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

  private Mesh? Fallback(ModelObjectType elementType, string reason)
  {
    _fallbacks.Record(elementType, reason);
    return null;
  }

  private Mesh ToMesh(PrismMesh prism) =>
    new()
    {
      vertices = prism.Vertices.ToList(),
      faces = prism.Faces.ToList(),
      units = _settingsStore.Current.SpeckleUnits,
    };
}
