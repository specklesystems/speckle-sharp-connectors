using Autodesk.AutoCAD.Colors;
using Speckle.Converters.Autocad.Extensions;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

/// <summary>
/// The <see cref="ADB.PolyFaceMesh"/> class converter. Converts to <see cref="SOG.Mesh"/>.
/// </summary>
/// <remarks>
/// Accepts both database-resident and non database-resident <see cref="ADB.PolyFaceMesh"/> objects
/// (e.g. produced by <c>Explode()</c> in the Plant 3D converter).
/// Resident sub-entities are opened via the top transaction, which owns their lifetime.
/// Non-resident sub-entities are owned by this converter and disposed after reading.
/// Degenerate face records (fewer than 3 vertices) are skipped.
/// </remarks>
[NameAndRankValue(typeof(ADB.PolyFaceMesh), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class DBPolyfaceMeshToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly IReferencePointConverter _referencePointConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public DBPolyfaceMeshToSpeckleConverter(
    IReferencePointConverter referencePointConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _referencePointConverter = referencePointConverter;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target) => RawConvert((ADB.PolyFaceMesh)target);

  public SOG.Mesh RawConvert(ADB.PolyFaceMesh target)
  {
    List<double> vertices = new();
    List<int> faces = new();
    List<int> faceVisibility = new();
    List<int> colors = new();
    bool hasExplicitVertexColors = true;
    bool ownsSubEntities = target.Database is null;

    foreach (
      var obj in target.GetSubEntities<ADB.Vertex>(
        ADB.OpenMode.ForRead,
        _settingsStore.Current.Document.TransactionManager.TopTransaction
      )
    )
    {
      try
      {
        switch (obj)
        {
          case ADB.PolyFaceMeshVertex o:
            vertices.Add(o.Position.X);
            vertices.Add(o.Position.Y);
            vertices.Add(o.Position.Z);

            if (hasExplicitVertexColors && TryGetExplicitColor(o.Color, out var argb))
            {
              colors.Add(argb);
            }
            else
            {
              hasExplicitVertexColors = false;
            }
            break;
          case ADB.FaceRecord o:
            AddFace(o, faces, faceVisibility);
            break;
        }
      }
      finally
      {
        if (ownsSubEntities)
        {
          obj.Dispose();
        }
      }
    }

    SOG.Mesh speckleMesh = new()
    {
      vertices = _referencePointConverter.ConvertWCSDoublesToExternalCoordinates(vertices), // transform by reference point
      faces = faces,
      colors = hasExplicitVertexColors ? colors : [],
      units = _settingsStore.Current.SpeckleUnits,
      ["faceVisibility"] = faceVisibility,
    };

    return speckleMesh;
  }

  private static void AddFace(ADB.FaceRecord face, List<int> faces, List<int> faceVisibility)
  {
    List<int> indices = new(4);
    List<int> hidden = new(4);
    for (short i = 0; i < 4; i++)
    {
      short index = face.GetVertexAt(i);
      if (index == 0)
      {
        continue;
      }

      // vertices are 1 indexed, and can be negative (hidden)
      indices.Add(Math.Abs(index) - 1);
      // 0 indicates hidden vertex on the face: 1 indicates a visible vertex
      hidden.Add(index < 0 ? 0 : 1);
    }

    if (indices.Count < 3)
    {
      return;
    }

    faces.Add(indices.Count);
    faces.AddRange(indices);
    faceVisibility.Add(hidden.Count);
    faceVisibility.AddRange(hidden);
  }

  private static bool TryGetExplicitColor(Color color, out int argb)
  {
    if (color.ColorMethod is ColorMethod.ByAci or ColorMethod.ByColor)
    {
      argb = color.ColorValue.ToArgb();
      return true;
    }

    argb = 0;
    return false;
  }
}
