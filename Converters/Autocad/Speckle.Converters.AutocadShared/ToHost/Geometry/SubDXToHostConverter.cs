using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

[NameAndRankValue(typeof(SOG.SubDX), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SubDXToHostConverter
  : IToHostTopLevelConverter,
    ITypedConverter<SOG.SubDX, List<(ADB.PolyFaceMesh a, SOG.Mesh b)>>
{
  private readonly ITypedConverter<SOG.Mesh, ADB.PolyFaceMesh> _meshConverter;

  public SubDXToHostConverter(ITypedConverter<SOG.Mesh, ADB.PolyFaceMesh> meshConverter)
  {
    _meshConverter = meshConverter;
  }

  public object Convert(Base target) => Convert((SOG.SubDX)target);

  /// <remarks>
  /// Unlikey case, but we need to handle multiple meshes inside of subdx displayvalue
  /// </remarks>
  public List<(ADB.PolyFaceMesh a, SOG.Mesh b)> Convert(SOG.SubDX target)
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
