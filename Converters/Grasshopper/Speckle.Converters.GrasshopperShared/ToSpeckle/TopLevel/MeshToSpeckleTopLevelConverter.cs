using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Grasshopper.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(RG.Mesh), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class MeshToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.Mesh, SOG.Mesh> _meshConverter;

  public MeshToSpeckleTopLevelConverter(ITypedConverter<RG.Mesh, SOG.Mesh> polylineConverter)
  {
    _meshConverter = polylineConverter;
  }

  public Base Convert(object target) => Convert((RG.Mesh)target);

  public SOG.Mesh Convert(RG.Mesh target) => _meshConverter.Convert(target);
}
