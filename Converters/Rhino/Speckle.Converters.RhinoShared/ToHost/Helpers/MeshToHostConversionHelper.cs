using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToHost.Helpers;

public interface IMeshToHostConversionHelper
{
  RG.GeometryBase ConvertMesh(SOG.Mesh mesh);
}

public class MeshToHostConversionHelper(
  ITypedConverter<SOG.Mesh, RG.Mesh> meshConverter,
  IConverterSettingsStore<RhinoConversionSettings> settingsStore
) : IMeshToHostConversionHelper
{
#pragma warning disable CA1508 // Brep.CreateFromMesh can return null for degenerate meshes
  public RG.GeometryBase ConvertMesh(SOG.Mesh mesh)
  {
    var rhinoMesh = meshConverter.Convert(mesh);

    if (settingsStore.Current.ConvertMeshesToBreps && mesh["fromSolid"] is true)
    {
      var brep = RG.Brep.CreateFromMesh(rhinoMesh, true);
      if (brep is not null)
      {
        brep.MergeCoplanarFaces(
          settingsStore.Current.Document.ModelAbsoluteTolerance,
          settingsStore.Current.Document.ModelAngleToleranceRadians
        );
        return brep;
      }
    }

    return rhinoMesh;
  }
#pragma warning restore CA1508
}
