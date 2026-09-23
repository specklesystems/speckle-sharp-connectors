using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class DisplayValueExtractor
{
  private readonly ITypedConverter<CsiJointWrapper, Point> _jointConverter;
  private readonly ITypedConverter<CsiFrameWrapper, Line> _frameConverter;
  private readonly ITypedConverter<CsiShellWrapper, Mesh> _shellConverter;
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly VolumetricDisplayValueExtractor _volumetricExtractor;

  public DisplayValueExtractor(
    ITypedConverter<CsiJointWrapper, Point> jointConverter,
    ITypedConverter<CsiFrameWrapper, Line> frameConverter,
    ITypedConverter<CsiShellWrapper, Mesh> shellConverter,
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    VolumetricDisplayValueExtractor volumetricExtractor
  )
  {
    _jointConverter = jointConverter;
    _frameConverter = frameConverter;
    _shellConverter = shellConverter;
    _settingsStore = settingsStore;
    _volumetricExtractor = volumetricExtractor;
  }

  public IEnumerable<Base> GetDisplayValue(ICsiWrapper wrapper)
  {
    return wrapper switch
    {
      CsiJointWrapper joint => ExtractJoint(joint),
      CsiFrameWrapper frame => ExtractFrame(frame),
      CsiShellWrapper shell => ExtractShell(shell),
      _ => Enumerable.Empty<Base>(),
    };
  }

  private IEnumerable<Base> ExtractJoint(CsiJointWrapper target)
  {
    yield return _jointConverter.Convert(target);
  }

  private IEnumerable<Base> ExtractFrame(CsiFrameWrapper target)
  {
    var line = _frameConverter.Convert(target);
    if (
      _settingsStore.Current.SendVolumetricGeometry && _volumetricExtractor.TryExtrudeFrame(target, line) is { } solid
    )
    {
      yield return solid;
      yield break;
    }
    yield return line;
  }

  private IEnumerable<Base> ExtractShell(CsiShellWrapper target)
  {
    var outline = _shellConverter.Convert(target);
    if (
      _settingsStore.Current.SendVolumetricGeometry
      && _volumetricExtractor.TryExtrudeShell(target, outline) is { } solid
    )
    {
      yield return solid;
      yield break;
    }
    yield return outline;
  }
}
