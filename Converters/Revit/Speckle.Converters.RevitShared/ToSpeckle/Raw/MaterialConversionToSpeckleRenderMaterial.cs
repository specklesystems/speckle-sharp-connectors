using Speckle.Converters.Common.Objects;
using Speckle.Objects.Other;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class MaterialConversionToSpeckleRenderMaterial : ITypedConverter<DB.Material, RenderMaterial>
{
  public RenderMaterial Convert(DB.Material target) =>
    // POC: not sure we should be pulling in System.Drawing -
    // maybe this isn't a problem as it's part of the netstandard Fwk
    // ideally we'd have serialiser of our own colour class, i.e. to serialise to an uint
    new()
    {
      name = target.Name,
      opacity = 1 - target.Transparency / 100d,
      diffuse = System.Drawing.Color.FromArgb(target.Color.Red, target.Color.Green, target.Color.Blue).ToArgb(),
      applicationId = target.Id.ToString()
      //metalness = revitMaterial.Shininess / 128d, //Looks like these are not valid conversions
      //roughness = 1 - (revitMaterial.Smoothness / 100d)
    };
}
