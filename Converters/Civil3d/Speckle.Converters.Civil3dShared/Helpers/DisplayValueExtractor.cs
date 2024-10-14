using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Civil3dShared.Helpers;

public sealed class DisplayValueExtractor
{
  private readonly ITypedConverter<ADB.Solid3d, SOG.Mesh> _solidConverter;
  private readonly ITypedConverter<CDB.TinSurface, SOG.Mesh> _tinSurfaceConverter;
  private readonly ITypedConverter<CDB.GridSurface, SOG.Mesh> _gridSurfaceConverter;
  private readonly ILogger<DisplayValueExtractor> _logger;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _converterSettings;

  public DisplayValueExtractor(
    ITypedConverter<ADB.Solid3d, SOG.Mesh> solidConverter,
    ITypedConverter<CDB.TinSurface, SOG.Mesh> tinSurfaceConverter,
    ITypedConverter<CDB.GridSurface, SOG.Mesh> gridSurfaceConverter,
    ILogger<DisplayValueExtractor> logger,
    IConverterSettingsStore<Civil3dConversionSettings> converterSettings
  )
  {
    _solidConverter = solidConverter;
    _tinSurfaceConverter = tinSurfaceConverter;
    _gridSurfaceConverter = gridSurfaceConverter;
    _logger = logger;
    _converterSettings = converterSettings;
  }

  public List<SOG.Mesh> GetDisplayValue(CDB.Entity entity)
  {
    List<SOG.Mesh> result = new();
    switch (entity)
    {
      // pipe networks: https://help.autodesk.com/view/CIV3D/2025/ENU/?guid=ade47b62-debf-f899-9b94-5645a620ab4f
      case CDB.Part part:
        SOG.Mesh partMesh = _solidConverter.Convert(part.Solid3dBody);
        result.Add(partMesh);
        break;

      // surfaces: https://help.autodesk.com/view/CIV3D/2025/ENU/?guid=d741aa49-e7da-9513-6b0b-226ebe3fa43f
      // POC: volume surfaces not supported
      case CDB.TinSurface tinSurface:
        SOG.Mesh tinSurfaceMesh = _tinSurfaceConverter.Convert(tinSurface);
        result.Add(tinSurfaceMesh);
        break;
      case CDB.GridSurface gridSurface:
        SOG.Mesh gridSurfaceMesh = _gridSurfaceConverter.Convert(gridSurface);
        result.Add(gridSurfaceMesh);
        break;
    }
    return result;
  }
}
