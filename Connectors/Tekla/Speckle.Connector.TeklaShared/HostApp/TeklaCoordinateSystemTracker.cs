using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Caching;
using Speckle.Sdk;

namespace Speckle.Connectors.TeklaShared.HostApp;

/// <summary>
/// Detects changes to the coordinate frame Tekla reports geometry in, and clears the send conversion cache
/// when it moves.
/// </summary>
/// <remarks>
/// All geometry we read from the Tekla API is expressed in the model's current coordinate frame: the work plane,
/// plus the current base point and its angle to north. Moving that frame rewrites the coordinates of every object
/// without changing any object, so <c>ModelObjectChanged</c> never fires and cached objects are re-published with
/// the old orientation. Tekla raises no event for frame changes either, so we re-read the frame before every send.
/// </remarks>
public class TeklaCoordinateSystemTracker
{
  private readonly ISendConversionCache _sendConversionCache;
  private readonly ILogger<TeklaCoordinateSystemTracker> _logger;

  private double[]? _lastKnownFrame;

  public TeklaCoordinateSystemTracker(
    ISendConversionCache sendConversionCache,
    ILogger<TeklaCoordinateSystemTracker> logger
  )
  {
    _sendConversionCache = sendConversionCache;
    _logger = logger;
  }

  /// <summary>
  /// Clears the send conversion cache unless the model's coordinate frame is provably unchanged since the last
  /// send. Must be called before objects are converted.
  /// </summary>
  public void ClearCacheIfCoordinateSystemChanged(TSM.Model model)
  {
    double[]? currentFrame = null;
    try
    {
      currentFrame = GetCoordinateFrame(model);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to read the Tekla coordinate frame, clearing the send conversion cache.");
    }

    // an unknown frame on either side means we cannot prove cached objects are still valid, so we assume they are
    // not. The frame is model wide, so a change invalidates every cached object, not just the current card's.
    if (currentFrame is null || _lastKnownFrame is null || !_lastKnownFrame.SequenceEqual(currentFrame))
    {
      _sendConversionCache.ClearCache();
    }

    _lastKnownFrame = currentFrame;
  }

  private static double[] GetCoordinateFrame(TSM.Model model)
  {
    var workPlane = model.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal;
    TSM.BasePoint? basePoint = TSM.ProjectInfo.GetCurrentCoordsysBasePoint();

    // a base point shifts and rotates reported coordinates by where it sits in the model, what coordinates it
    // assigns to that spot, and how far it is turned from north. Compared by value, so swapping between two
    // identically defined base points counts as no change.
    return
    [
      .. GetMatrixValues(workPlane),
      model.GetInfo().NorthDirection,
      basePoint?.LocationInModelX ?? 0,
      basePoint?.LocationInModelY ?? 0,
      basePoint?.LocationInModelZ ?? 0,
      basePoint?.EastWest ?? 0,
      basePoint?.NorthSouth ?? 0,
      basePoint?.Elevation ?? 0,
      basePoint?.AngleToNorth ?? 0,
    ];
  }

  // Tekla matrices are 4 rows by 3 columns (rotation then translation) and do not implement value equality
  private static IEnumerable<double> GetMatrixValues(Tekla.Structures.Geometry3d.Matrix matrix)
  {
    for (int row = 0; row < 4; row++)
    {
      for (int column = 0; column < 3; column++)
      {
        yield return matrix[row, column];
      }
    }
  }
}
