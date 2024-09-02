using Speckle.Converters.Common;

namespace Speckle.Converters.RevitShared.Settings;

public class RevitConversionSettings(IHostToSpeckleUnitConverter<DB.ForgeTypeId> unitConverter) : IConverterSettings
{
  private string? _unitCache;

  public DB.Document Document { get; init; }
  public DetailLevelType DetailLevel { get; init; }
  public DB.Transform? ReferencePointTransform { get; init; }
  public double Tolerance { get; init; } = 0.0164042; // 5mm in ft

  public string SpeckleUnits
  {
    get
    {
      if (_unitCache is null)
      {
        _unitCache = unitConverter.ConvertOrThrow(
          Document.GetUnits().GetFormatOptions(DB.SpecTypeId.Length).GetUnitTypeId()
        );
      }

      return _unitCache;
    }
  }
}
