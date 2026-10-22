using Speckle.Converters.Common.Objects;
using Speckle.Sdk;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public interface IVolumetricBrepConverter : ITypedConverter<ABR.Brep, SOG.Mesh>;

public class BrepVolumetricToSpeckleRawConverter : IVolumetricBrepConverter
{
  private readonly INonVolumetricBrepConverter _baseConverter;

  public BrepVolumetricToSpeckleRawConverter(INonVolumetricBrepConverter baseConverter)
  {
    _baseConverter = baseConverter;
  }

  public SOG.Mesh Convert(ABR.Brep target)
  {
    var mesh = _baseConverter.Convert(target);

    try
    {
      mesh.volume = target.GetVolume();
    }
    catch (ABR.Exception e) when (!e.IsFatal()) { } // exceptions can be thrown for non-volumetric and open breps
    // Use INonVolumetricBrepConverter when the type is known to be non-volumetric

    return mesh;
  }
}
