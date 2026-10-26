using Rhino.DocObjects;
using Rhino.DocObjects.Tables;
using Rhino.Geometry;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Connectors.Rhino.Extensions;

public static class ObjectTableExtensions
{
  /// <summary>
  /// Adds the geometry to the document, throwing when Rhino refuses it instead of returning <see cref="Guid.Empty"/>.
  /// </summary>
  public static Guid AddOrThrow(this ObjectTable objects, GeometryBase geometry, ObjectAttributes attributes)
  {
    var id = objects.Add(geometry, attributes);
    if (id != Guid.Empty)
    {
      return id;
    }

    var reason = geometry.IsValidWithLog(out string log)
      ? "Rhino did not add it to the document"
      : $"it is invalid ({log.Trim()})";
    throw new ConversionException($"Rhino rejected the {geometry.ObjectType} geometry: {reason}");
  }
}
