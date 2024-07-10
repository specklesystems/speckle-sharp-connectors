using System.Diagnostics;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;
using Objects.GIS;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.ArcGIS3;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Core.Logging;
using Speckle.Core.Models;

namespace Speckle.Connectors.ArcGis.Operations.Send;

/// <summary>
/// Stateless builder object to turn an ISendFilter into a <see cref="Base"/> object
/// </summary>
public class ArcGISRootObjectBuilder : IRootObjectBuilder<MapMember>
{
  private readonly IUnitOfWorkFactory _unitOfWorkFactory;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IConversionContextStack<ArcGISDocument, Unit> _contextStack;

  public ArcGISRootObjectBuilder(
    IUnitOfWorkFactory unitOfWorkFactory,
    ISendConversionCache sendConversionCache,
    IConversionContextStack<ArcGISDocument, Unit> contextStack
  )
  {
    _unitOfWorkFactory = unitOfWorkFactory;
    _sendConversionCache = sendConversionCache;
    _contextStack = contextStack;
  }

  public RootObjectBuilderResult Build(
    IReadOnlyList<MapMember> objects,
    SendInfo sendInfo,
    Action<string, double?>? onOperationProgressed = null,
    CancellationToken ct = default
  )
  {
    // set active CRS & offsets on Send, add offsets if we find a way to set them up
    CRSoffsetRotation crsOffsetRotation =
      new(_contextStack.Current.Document.Map.SpatialReference, _contextStack.Current.Document.Map);
    _contextStack.Current.Document.ActiveCRSoffsetRotation = crsOffsetRotation;

    // POC: does this feel like the right place? I am wondering if this should be called from within send/rcv?
    // begin the unit of work
    using var uow = _unitOfWorkFactory.Resolve<IRootToSpeckleConverter>();
    var converter = uow.Service;

    int count = 0;

    Collection rootObjectCollection = new(); //TODO: Collections

    List<SendConversionResult> results = new(objects.Count);
    var cacheHitCount = 0;

    foreach (MapMember mapMember in objects)
    {
      ct.ThrowIfCancellationRequested();
      var collectionHost = rootObjectCollection;
      var applicationId = mapMember.URI;

      try
      {
        Base converted;
        if (_sendConversionCache.TryGetValue(sendInfo.ProjectId, applicationId, out ObjectReference value))
        {
          converted = value;
          cacheHitCount++;
        }
        else
        {
          converted = converter.Convert(mapMember);

          // get Active CRS (for writing geometry coords)
          var spatialRef = _contextStack.Current.Document.ActiveCRSoffsetRotation.SpatialReference;
          converted["crs"] = new CRS
          {
            wkt = spatialRef.Wkt,
            name = spatialRef.Name,
            offset_y = System.Convert.ToSingle(_contextStack.Current.Document.ActiveCRSoffsetRotation.LatOffset),
            offset_x = System.Convert.ToSingle(_contextStack.Current.Document.ActiveCRSoffsetRotation.LonOffset),
            rotation = System.Convert.ToSingle(_contextStack.Current.Document.ActiveCRSoffsetRotation.TrueNorthRadians),
            units_native = _contextStack.Current.Document.ActiveCRSoffsetRotation.SpeckleUnitString,
          };

          // other properties
          converted["name"] = mapMember.Name;
          converted["units"] = _contextStack.Current.Document.ActiveCRSoffsetRotation.SpeckleUnitString;
          converted.applicationId = applicationId;
        }

        // add to host
        collectionHost.elements.Add(converted);
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
