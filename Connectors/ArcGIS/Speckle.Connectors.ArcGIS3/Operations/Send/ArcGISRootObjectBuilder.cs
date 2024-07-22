using System.Diagnostics;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;
using Objects.GIS;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.ArcGIS3;
using Speckle.Converters.Common;
using Speckle.Core.Logging;
using Speckle.Core.Models;
using Speckle.Core.Models.Collections;

namespace Speckle.Connectors.ArcGis.Operations.Send;

/// <summary>
/// Stateless builder object to turn an ISendFilter into a <see cref="Base"/> object
/// </summary>
public class ArcGISRootObjectBuilder : IRootObjectBuilder<MapMember>
{
  private readonly IRootToSpeckleConverter _rootToSpeckleConverter;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IConversionContextStack<ArcGISDocument, Unit> _contextStack;

  public ArcGISRootObjectBuilder(
    ISendConversionCache sendConversionCache,
    IConversionContextStack<ArcGISDocument, Unit> contextStack,
    IRootToSpeckleConverter rootToSpeckleConverter
  )
  {
    _sendConversionCache = sendConversionCache;
    _contextStack = contextStack;
    _rootToSpeckleConverter = rootToSpeckleConverter;
  }

  public RootObjectBuilderResult Build(
    IReadOnlyList<MapMember> objects,
    SendInfo sendInfo,
    Action<string, double?>? onOperationProgressed = null,
    CancellationToken ct = default
  )
  {
    // TODO: add a warning if Geographic CRS is set
    // "Data has been sent in the units 'degrees'. It is advisable to set the project CRS to Projected type (e.g. EPSG:32631) to be able to receive geometry correctly in CAD/BIM software"

    int count = 0;

    Collection rootObjectCollection = new(); //TODO: Collections

    List<SendConversionResult> results = new(objects.Count);
    var cacheHitCount = 0;
    List<(GroupLayer, Collection)> nestedGroups = new();

    foreach (MapMember mapMember in objects)
    {
      ct.ThrowIfCancellationRequested();
      var collectionHost = rootObjectCollection;
      var applicationId = mapMember.URI;
      Base converted;

      try
      {
        // if the layer is not a part of the group, reset groups
        for (int i = 0; i < nestedGroups.Count; i++)
        {
          if (nestedGroups.Count > 0 && !nestedGroups[0].Item1.Layers.Select(x => x.URI).Contains(applicationId))
          {
            nestedGroups.RemoveAt(0);
          }
          else
          {
            // break at the first group, which contains current layer
            break;
          }
        }

        // don't use cache for group layers
        if (
          mapMember is not GroupLayer
          && _sendConversionCache.TryGetValue(sendInfo.ProjectId, applicationId, out ObjectReference value)
        )
        {
          converted = value;
          cacheHitCount++;
        }
        else
        {
          if (mapMember is GroupLayer group)
          {
            // group layer will always come before it's contained layers
            // keep active group last in the list
            converted = new Collection();
            nestedGroups.Insert(0, (group, (Collection)converted));
          }
          else
          {
            converted = _rootToSpeckleConverter.Convert(mapMember);

            // get units & Active CRS (for writing geometry coords)
            converted["units"] = _contextStack.Current.Document.ActiveCRSoffsetRotation.SpeckleUnitString;

            var spatialRef = _contextStack.Current.Document.ActiveCRSoffsetRotation.SpatialReference;
            converted["crs"] = new CRS
            {
              wkt = spatialRef.Wkt,
              name = spatialRef.Name,
              offset_y = System.Convert.ToSingle(_contextStack.Current.Document.ActiveCRSoffsetRotation.LatOffset),
              offset_x = System.Convert.ToSingle(_contextStack.Current.Document.ActiveCRSoffsetRotation.LonOffset),
              rotation = System.Convert.ToSingle(
                _contextStack.Current.Document.ActiveCRSoffsetRotation.TrueNorthRadians
              ),
              units_native = _contextStack.Current.Document.ActiveCRSoffsetRotation.SpeckleUnitString,
            };
          }

          // other common properties for layers and groups
          converted["name"] = mapMember.Name;
          converted.applicationId = applicationId;
        }

        if (
          nestedGroups.Count == 0
          || (nestedGroups.Count == 1 && nestedGroups[0].Item2.applicationId == applicationId)
        )
        {
          // add to host if no groups, or current root group
          collectionHost.elements.Add(converted);
        }
        else
        {
          // if we are adding a layer inside the group
          var parentCollection = nestedGroups.FirstOrDefault(x =>
            x.Item1.Layers.Select(y => y.URI).Contains(applicationId)
          );
          parentCollection.Item2.elements.Add(converted);
        }

        results.Add(new(Status.SUCCESS, applicationId, mapMember.GetType().Name, converted));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        results.Add(new(Status.ERROR, applicationId, mapMember.GetType().Name, null, ex));
        // POC: add logging
      }

      onOperationProgressed?.Invoke("Converting", (double)++count / objects.Count);
    }

    // POC: Log would be nice, or can be removed.
    Debug.WriteLine(
      $"Cache hit count {cacheHitCount} out of {objects.Count} ({(double)cacheHitCount / objects.Count})"
    );

    return new(rootObjectCollection, results);
  }
}
