using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

/// <summary>
/// The <see cref="ADB.PolyFaceMesh"/> class converter. Converts to <see cref="SOG.Mesh"/>.
/// </summary>
/// <remarks>
/// The IToSpeckleTopLevelConverter inheritance should only expect database-resident <see cref="ADB.PolyFaceMesh"/> objects. IRawConversion inheritance can expect non database-resident objects, when generated from other converters.
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
    using (ADB.Transaction tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
    {
      foreach (ADB.ObjectId id in target)
      {
        ADB.DBObject obj = tr.GetObject(id, ADB.OpenMode.ForRead);
        switch (obj)
        {
          case ADB.PolyFaceMeshVertex o:
            vertices.Add(o.Position.X);
            vertices.Add(o.Position.Y);
            vertices.Add(o.Position.Z);
            colors.Add(o.Color.ColorValue.ToArgb());
            break;
          case ADB.FaceRecord o:
            List<int> indices = new();
            List<int> hidden = new();
            for (short i = 0; i < 4; i++)
            {
              short index = o.GetVertexAt(i);
              if (index == 0)
              {
                continue;
              }

              // vertices are 1 indexed, and can be negative (hidden)
              int adjustedIndex = index > 0 ? index - 1 : Math.Abs(index) - 1;
              indices.Add(adjustedIndex);

              // 0 indicates hidden vertex on the face: 1 indicates a visible vertex
              hidden.Add(index < 0 ? 0 : 1);
            }

            if (indices.Count == 4)
            {
              faces.AddRange(new List<int> { 4, indices[0], indices[1], indices[2], indices[3] });
              faceVisibility.AddRange(new List<int> { 4, hidden[0], hidden[1], hidden[2], hidden[3] });
            }
            else
            {
              faces.AddRange(new List<int> { 3, indices[0], indices[1], indices[2] });
              faceVisibility.AddRange(new List<int> { 3, hidden[0], hidden[1], hidden[2] });
            }

            break;
        }
      }
      tr.Commit();
    }

    SOG.Mesh speckleMesh =
      new()
      {
        vertices = _referencePointConverter.ConvertDoublesToExternalCoordinates(vertices), // transform by reference point
        faces = faces,
        colors = colors,
        units = _settingsStore.Current.SpeckleUnits,
        ["faceVisibility"] = faceVisibility
      };

    return speckleMesh;
  }
}
