using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Plant3dShared.ToSpeckle.Geometry;

/// <summary>
/// Converts P&amp;ID LineSegment (SLINE) entities to Speckle.
/// Unlike 3D Plant objects (Pipe, Equipment) which are block references needing Explode(),
/// LineSegment is a Curve-based entity. We read its Vertices directly and produce a Polyline,
/// applying the same coordinate transforms as the AutoCAD converters.
/// </summary>
[NameAndRankValue(typeof(PP.PnIDObjects.LineSegment), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class LineSegmentToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;
  private readonly IReferencePointConverter _referencePointConverter;

  public LineSegmentToSpeckleConverter(
    IConverterSettingsStore<AutocadConversionSettings> settingsStore,
    IReferencePointConverter referencePointConverter
  )
  {
    _settingsStore = settingsStore;
    _referencePointConverter = referencePointConverter;
  }

  public Base Convert(object target)
  {
    var lineSegment = (PP.PnIDObjects.LineSegment)target;

    // Build a flat list of xyz coordinates from the Vertices collection
    var vertices = lineSegment.Vertices;
    List<double> value = new(vertices.Count * 3);
    foreach (AG.Point3d pt in vertices)
    {
      value.Add(pt.X);
      value.Add(pt.Y);
      value.Add(pt.Z);
    }

    // Apply the same WCS → external coordinate transform that all AutoCAD converters use
    value = _referencePointConverter.ConvertWCSDoublesToExternalCoordinates(value);

    SOG.Polyline polyline =
      new()
      {
        value = value,
        closed = lineSegment.Closed,
        units = _settingsStore.Current.SpeckleUnits
      };

    DataObject dataObject =
      new()
      {
        name = lineSegment.GetType().Name,
        displayValue = new List<Base> { polyline },
        properties = new Dictionary<string, object?>(),
        applicationId = lineSegment.Handle.Value.ToString()
      };

    return dataObject;
  }
}
