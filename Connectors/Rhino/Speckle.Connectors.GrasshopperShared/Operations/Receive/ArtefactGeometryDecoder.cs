using Rhino;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Converters.Rhino.ToHost.Helpers;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Pipelines.Receive.Artifacts;
using RG = Rhino.Geometry;

namespace Speckle.Connectors.GrasshopperShared.Operations.Receive;

/// <summary>
/// Turns one geometry index of an artefact bundle into Rhino geometry, in the active document's units.
/// </summary>
/// <remarks>
/// Shared with <see cref="GrasshopperArtefactObjectBuilder"/> so Explore decodes the geometry receive does NOT
/// bake — a CENTERLINE curve — through the same path.
/// </remarks>
internal static class ArtefactGeometryDecoder
{
  internal static List<RG.GeometryBase> DecodeGeometryIndex(
    int geomK,
    ArtefactBundle bundle,
    string fallbackUnits,
    string? sourceType,
    List<string> warnings
  )
  {
    if (!bundle.Geometries.TryGetValue(geomK, out var g))
    {
      return new List<RG.GeometryBase>();
    }
    var geoms = ArtefactGeometryToHost.Decode(
      g.Content,
      g.Type,
      fallbackUnits,
      DocUnits(),
      sourceType,
      ConvertSpeckleGeometry,
      out var failure
    );
    if (failure is { } f)
    {
      warnings.Add(
        f.Exception is { } ex
          ? $"Geometry {geomK} ({g.Type}) failed to {f.Stage}: {ex.Message}"
          : $"Geometry {geomK} ({g.Type}) {f.Stage} did not produce any native geometry."
      );
    }
    return geoms;
  }

  private static IEnumerable<RG.GeometryBase> ConvertSpeckleGeometry(Base decoded) =>
    SpeckleConversionContext.Current.ConvertToHost(decoded).Select(pair => pair.Item1).OfType<RG.GeometryBase>();

  // ConvertToSpeckle stamps these units onto the converted Base without rescaling, so decoded geometry must land here
  internal static string DocUnits() => RhinoDoc.ActiveDoc?.ModelUnitSystem.ToSpeckleString() ?? Units.Meters;
}
