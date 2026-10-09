using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public interface INonVolumetricBrepConverter : ITypedConverter<ABR.Brep, SOG.Mesh>;

public class BrepToSpeckleRawConverter : INonVolumetricBrepConverter
{
  private readonly IReferencePointConverter _referencePointConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public BrepToSpeckleRawConverter(
    IReferencePointConverter referencePointConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _referencePointConverter = referencePointConverter;
    _settingsStore = settingsStore;
  }

  public SOG.Mesh Convert(ABR.Brep target)
  {
    if (target.IsNull)
    {
      throw new ConversionException("Brep was null.");
    }

    List<int> faces = new();
    List<double> vertices = new();
    int vertexCount = 0;

    using (var control = new ABR.Mesh2dControl())
    {
      // These settings may need adjusting
      control.MaxSubdivisions = 10000;

      // create mesh filters
      using (var filter = new ABR.Mesh2dFilter())
      {
        filter.Insert(target, control);
        using (ABR.Mesh2d m = new(filter))
        {
          foreach (ABR.Element2d? e in m.Element2ds)
          {
            // add number of vertices for this face
            int nodeCount = e.Nodes.Count();
            faces.Add(nodeCount);

            foreach (var n in e.Nodes)
            {
              // add index of current vertex to face
              faces.Add(vertexCount);
              vertexCount++;

              // add vertex coords
              vertices.Add(n.Point.X);
              vertices.Add(n.Point.Y);
              vertices.Add(n.Point.Z);
              n.Dispose();
            }

            e.Dispose();
          }
        }
      }

      // create speckle mesh
      SOG.Mesh mesh = new()
      {
        faces = faces,
        vertices = _referencePointConverter.ConvertWCSDoublesToExternalCoordinates(vertices), // transform by reference point
        units = _settingsStore.Current.SpeckleUnits,
        area = target.GetSurfaceArea(),
      };

      return mesh;
    }
  }
}
