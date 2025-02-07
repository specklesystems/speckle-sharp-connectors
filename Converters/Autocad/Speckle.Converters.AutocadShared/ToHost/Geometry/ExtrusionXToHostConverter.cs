using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

/// <summary>
/// Converts a ExtrusionX to a List(PolyFaceMesh,Mesh)> as fallback conversion
/// </summary>
/// <remarks>
/// The return type is (Entity,Base) instead of the specific type (PolyfaceMesh, Mesh) so this result can be picked up by a generic list case in the SpeckleToHost connector object baking. This is essentially one-to-many fallback conversion.
/// </remarks>
[NameAndRankValue(typeof(SOG.ExtrusionX), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ExtrusionXToHostConverter
  : IToHostTopLevelConverter,
    ITypedConverter<SOG.ExtrusionX, List<(ADB.Entity a, Base b)>>
{
  private readonly ITypedConverter<SOG.Mesh, ADB.PolyFaceMesh> _meshConverter;

  public ExtrusionXToHostConverter(ITypedConverter<SOG.Mesh, ADB.PolyFaceMesh> meshConverter)
  {
    _meshConverter = meshConverter;
  }

  public object Convert(Base target) => Convert((SOG.ExtrusionX)target);

  /// <remarks>
  /// Unlikey case, but we need to handle multiple meshes inside of extrusionx displayvalue
  /// </remarks>
  public List<(ADB.Entity a, Base b)> Convert(SOG.ExtrusionX target)
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
