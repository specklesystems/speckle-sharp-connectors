using ArcGIS.Core.CIM;
using ArcGIS.Core.Data.Analyst3D;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(LasDatasetLayer), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PointCloudToSpeckleConverter
  : IToSpeckleTopLevelConverter,
    ITypedConverter<LasDatasetLayer, SGIS.VectorLayer>
{
  private readonly ITypedConverter<ACG.MapPoint, SOG.Point> _pointConverter;
  private readonly ITypedConverter<ACG.Envelope, SOG.Box> _boxConverter;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public PointCloudToSpeckleConverter(
    ITypedConverter<ACG.MapPoint, SOG.Point> pointConverter,
    ITypedConverter<ACG.Envelope, SOG.Box> boxConverter,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _boxConverter = boxConverter;
    _settingsStore = settingsStore;
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

  public Base Convert(object target)
  {
    return Convert((LasDatasetLayer)target);
  }

  public SGIS.VectorLayer Convert(LasDatasetLayer target)
  {
    SGIS.VectorLayer speckleLayer =
      new() { nativeGeomType = target.MapLayerType.ToString(), geomType = GISLayerGeometryType.POINTCLOUD };

    // prepare data for pointcloud
    List<SOG.Point> specklePts = new();
    List<double> values = new();
    List<int> speckleColors = new();
    var renderer = target.GetRenderers()[0];

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

    SOG.Pointcloud cloud =
      new()
      {
        points = specklePts.SelectMany(pt => new List<double>() { pt.x, pt.y, pt.z }).ToList(),
        colors = speckleColors,
        sizes = values,
        bbox = _boxConverter.Convert(target.QueryExtent()),
        units = _settingsStore.Current.SpeckleUnits
      };

    speckleLayer.elements.Add(cloud);
    return speckleLayer;
  }
}
