using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Autocad.ToSpeckle.Encoding;

/// <summary>
/// Extracts display meshes from AutoCAD geometry for visualization in the Speckle viewer.
/// Follows the BrepX pattern/
/// </summary>
internal static class DisplayMeshExtractor
{
  public static List<SOG.Mesh> GetSpeckleMeshes(ADB.Solid3d solid, ITypedConverter<ABR.Brep, SOG.Mesh> meshConverter)
  {
    ArgumentNullException.ThrowIfNull(solid);

    ArgumentNullException.ThrowIfNull(meshConverter);

    // Extract Brep from Solid3d
    using ABR.Brep brep = new(solid);
    if (brep.IsNull)
    {
      throw new ValidationException("Could not extract Brep from Solid3d for display mesh generation.");
    }

    // Convert Brep to Speckle mesh
    SOG.Mesh displayMesh = meshConverter.Convert(brep);

    return new List<SOG.Mesh> { displayMesh };
  }
}
