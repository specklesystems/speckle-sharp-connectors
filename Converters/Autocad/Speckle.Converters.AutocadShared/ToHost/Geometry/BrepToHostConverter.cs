using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

[NameAndRankValue(typeof(SOG.Brep), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class BrepToHostConverter
  : IToHostTopLevelConverter,
    ITypedConverter<SOG.Brep, List<(ADB.PolyFaceMesh a, SOG.Mesh b)>>
{
  private readonly ITypedConverter<SOG.Mesh, ADB.PolyFaceMesh> _meshConverter;

  public BrepToHostConverter(ITypedConverter<SOG.Mesh, ADB.PolyFaceMesh> meshConverter)
  {
    _meshConverter = meshConverter;
  }

  public object Convert(Base target) => Convert((SOG.Brep)target);

  /// <remarks>
  /// Unlikey case, but we need to handle multiple meshes inside of brepx displayvalue
  /// </remarks>
  public List<(ADB.PolyFaceMesh a, SOG.Mesh b)> Convert(SOG.Brep target)
  {
    var result = new List<ADB.PolyFaceMesh>();
    foreach (SOG.Mesh mesh in target.displayValue)
    {
      ADB.PolyFaceMesh convertedMesh = _meshConverter.Convert(mesh);
      result.Add(convertedMesh);
    }

    return result.Zip(target.displayValue, (a, b) => (a, b)).ToList();
  }
}
