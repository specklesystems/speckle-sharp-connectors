using ArcGIS.Core.CIM;
using ArcGIS.Core.Data.Analyst3D;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Raw;

public class PointcloudToSpeckleConverter : ITypedConverter<LasDatasetLayer, SOG.Pointcloud>
{
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;
  private readonly ITypedConverter<Envelope, SOG.Box> _boxConverter;
  private readonly ITypedConverter<MapPoint, SOG.Point> _pointConverter;

  public PointcloudToSpeckleConverter(
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore,
    ITypedConverter<Envelope, SOG.Box> boxConverter,
    ITypedConverter<MapPoint, SOG.Point> pointConverter
  )
  {
    _settingsStore = settingsStore;
    _boxConverter = boxConverter;
    _pointConverter = pointConverter;
  }

  public SOG.Pointcloud Convert(LasDatasetLayer target)
  {
    // prepare data for pointcloud
    List<Objects.Geometry.Point> specklePts = new();
    List<double> values = new();
    List<int> speckleColors = new();

    var renderer = target.GetRenderers()[0];
    try
    {
      using (LasPointCursor ptCursor = target.SearchPoints(new LasPointFilter()))
      {
        while (ptCursor.MoveNext())
        {
          using (LasPoint pt = ptCursor.Current)
          {
            specklePts.Add(_pointConverter.Convert(pt.ToMapPoint()));
            values.Add(pt.ClassCode);
            int color = GetPointColor(pt, renderer);
            speckleColors.Add(color);
          }
        }
      }

      Objects.Geometry.Pointcloud cloud =
        new()
        {
          points = specklePts.SelectMany(pt => new List<double>() { pt.x, pt.y, pt.z }).ToList(),
          colors = speckleColors,
          sizes = values,
          bbox = _boxConverter.Convert(target.QueryExtent()),
          units = _settingsStore.Current.SpeckleUnits
        };
      return cloud;
    }
    catch (ArcGIS.Core.Data.Exceptions.TinException exception)
    {
      throw new SpeckleException("Pointcloud operations not enabled", exception);
    }
  }

  private int GetPointColor(LasPoint pt, object renderer)
  {
    // get color
    int color = 0;
    string classCode = pt.ClassCode.ToString();
    if (renderer is CIMTinUniqueValueRenderer uniqueRenderer)
    {
      foreach (CIMUniqueValueGroup group in uniqueRenderer.Groups)
      {
        if (color != 0)
        {
          break;
        }
        foreach (CIMUniqueValueClass groupClass in group.Classes)
        {
          if (color != 0)
          {
            break;
          }
          for (int i = 0; i < groupClass.Values.Length; i++)
          {
            if (classCode == groupClass.Values[i].FieldValues[0])
            {
              CIMColor symbolColor = groupClass.Symbol.Symbol.GetColor();
              color = symbolColor.CIMColorToInt();
              break;
            }
          }
        }
      }
    }
    else
    {
      color = pt.RGBColor.RGBToInt();
    }
    return color;
  }
}
