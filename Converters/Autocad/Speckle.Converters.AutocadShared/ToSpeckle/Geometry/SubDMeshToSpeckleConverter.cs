using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Geometry;

[NameAndRankValue(typeof(ADB.SubDMesh), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SubDMeshToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<ADB.SubDMesh, SOG.Mesh> _subDMeshConverter;

  public SubDMeshToSpeckleConverter(ITypedConverter<ADB.SubDMesh, SOG.Mesh> subDMeshConverter)
  {
    _subDMeshConverter = subDMeshConverter;
  }

  public Base Convert(object target) => Convert((ADB.SubDMesh)target);

  public SOG.Mesh Convert(ADB.SubDMesh target) => _subDMeshConverter.Convert(target);
}
