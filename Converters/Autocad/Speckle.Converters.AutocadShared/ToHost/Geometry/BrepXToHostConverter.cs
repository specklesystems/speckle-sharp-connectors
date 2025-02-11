using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

/// <summary>
/// Converts a BrepX to a List(PolyFaceMesh,Mesh)> as fallback conversion
/// </summary>
/// <remarks>
/// The return type is (Entity,Base) instead of the specific type (PolyfaceMesh, Mesh) so this result can be picked up by a generic list case in the SpeckleToHost connector object baking. This is essentially one-to-many fallback conversion.
/// </remarks>
[NameAndRankValue(typeof(SOG.BrepX), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class BrepXToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.BrepX, List<(ADB.Entity a, Base b)>>
{
  private readonly ITypedConverter<SOG.Mesh, ADB.PolyFaceMesh> _meshConverter;

  public BrepXToHostConverter(ITypedConverter<SOG.Mesh, ADB.PolyFaceMesh> meshConverter)
  {
    _meshConverter = meshConverter;
  }

  public object Convert(Base target) => Convert((SOG.BrepX)target);

  /// <remarks>
  /// Unlikey case, but we need to handle multiple meshes inside of brepx displayvalue.
  /// </remarks>
  public List<(ADB.Entity a, Base b)> Convert(SOG.BrepX target)
  {
    var result = new List<ADB.PolyFaceMesh>();
    foreach (SOG.Mesh mesh in target.displayValue)
    {
      ADB.PolyFaceMesh convertedMesh = _meshConverter.Convert(mesh);
      result.Add(convertedMesh);
    }

    return result.Zip(target.displayValue, (a, b) => ((ADB.Entity)a, (Base)b)).ToList();
  }
}
