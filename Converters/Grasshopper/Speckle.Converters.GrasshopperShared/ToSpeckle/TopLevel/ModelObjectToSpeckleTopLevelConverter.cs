#if RHINO8_OR_GREATER
using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Grasshopper.ToSpeckle.TopLevel;

/// <summary>
/// Converts a ModelObject based on its rhino doc geometry.
/// The ModelObject is a wrapper class introduced in Rhino 8 Grasshopper dll.
/// See: https://developer.rhino3d.com/api/grasshopper/html/T_Grasshopper_Rhinoceros_Model_ModelObject.htm
/// </summary>
[NameAndRankValue(nameof(GM.ModelObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ModelObjectToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<GrasshopperConversionSettings> _settingsStore;
  private readonly ITypedConverter<RG.Point, SOG.Point> _pointConverter;
  private readonly ITypedConverter<RG.LineCurve, SOG.Line> _lineCurveConverter;
  private readonly ITypedConverter<RG.ArcCurve, Base> _arcCurveConverter;
  private readonly ITypedConverter<RG.Mesh, SOG.Mesh> _meshConverter;

  public ModelObjectToSpeckleTopLevelConverter(
    ITypedConverter<RG.Point, SOG.Point> pointConverter,
    ITypedConverter<RG.LineCurve, SOG.Line> lineCurveConverter,
    ITypedConverter<RG.ArcCurve, Base> arcCurveConverter,
    ITypedConverter<RG.Mesh, SOG.Mesh> meshConverter,
    IConverterSettingsStore<GrasshopperConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _lineCurveConverter = lineCurveConverter;
    _arcCurveConverter = arcCurveConverter;
    _meshConverter = meshConverter;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target) => Convert((GM.ModelObject)target);

  public Base Convert(GM.ModelObject target)
  {
    // retrieve this object from the rhino doc
    if (target.Id is Guid guid)
    {
      // POC: currently, the rhino top level converters create concrete classes based on the geometryBase value of a rhino doc object.
      // This should probably be updated to return some kind of RhinoObject:DataObject instead, including props like user strings, which will change this call in grasshopper
      if (_settingsStore.Current.Document.Objects.FindId(guid) is RhinoObject rhinoObj)
      {
        switch (rhinoObj.Geometry)
        {
          case RG.Point point:
            return _pointConverter.Convert(point);
          case RG.LineCurve lineCurve:
            return _lineCurveConverter.Convert(lineCurve);
          case RG.ArcCurve arcCurve:
            return _arcCurveConverter.Convert(arcCurve);
          case RG.Mesh mesh:
            return _meshConverter.Convert(mesh);
          default:
            throw new ConversionNotSupportedException($"No converter avilable for {target.ObjectType}");
        }
      }
      else
      {
        throw new ConversionException($"Could not find model object {guid} in the Rhino document.");
      }
    }
    else
    {
      throw new ConversionException("Could not find model object with null id in the Rhino document.");
    }
  }
}

#endif
