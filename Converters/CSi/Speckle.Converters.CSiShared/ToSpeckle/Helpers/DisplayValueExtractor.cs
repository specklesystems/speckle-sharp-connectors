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
  private readonly CsiToSpeckleCacheSingleton _cache;

  public DisplayValueExtractor(
    ITypedConverter<CsiJointWrapper, Point> jointConverter,
    ITypedConverter<CsiFrameWrapper, Line> frameConverter,
    ITypedConverter<CsiShellWrapper, Mesh> shellConverter,
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    VolumetricDisplayValueExtractor volumetricExtractor,
    CsiToSpeckleCacheSingleton cache
  )
  {
    _jointConverter = jointConverter;
    _frameConverter = frameConverter;
    _shellConverter = shellConverter;
    _settingsStore = settingsStore;
    _volumetricExtractor = volumetricExtractor;
    _cache = cache;
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
    // Recorded for every frame, volumetric or not and even when the extrusion falls back: the analytical axis is a
    // CENTERLINE in its own right, the same edge etabsextract writes without any setting (ENG-11149).
    _cache.FrameCenterlineCache[target.Name] = line;
    if (!_settingsStore.Current.SendVolumetricGeometry)
    {
      yield return line;
      yield break;
    }

    yield return (Base?)_volumetricExtractor.TryExtrudeFrame(target, line) ?? line;
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
