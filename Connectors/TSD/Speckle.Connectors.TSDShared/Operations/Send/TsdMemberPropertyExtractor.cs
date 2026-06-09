using Microsoft.Extensions.Logging;
using Speckle.Sdk;
using TSD.API.Remoting.Materials;
using TSD.API.Remoting.Sections;
using TSD.API.Remoting.Structure;

namespace Speckle.Connectors.TSDShared.Operations.Send;

internal sealed class TsdMemberPropertyExtractor
{
  private readonly ILogger<TsdMemberPropertyExtractor> _logger;

  public TsdMemberPropertyExtractor(ILogger<TsdMemberPropertyExtractor> logger)
  {
    _logger = logger;
  }

  public Dictionary<string, object?> Extract(IMember member, IReadOnlyList<IMemberSpan> spans)
  {
    IMemberData? memberData = null;
    try
    {
      memberData = member.Data.Value;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "Failed to read TSD member data");
    }

    var properties = new Dictionary<string, object?>();

    var objectId = new Dictionary<string, object?>();
    TryAdd(objectId, "GUID", () => member.Id.ToString());
    TryAdd(objectId, "Label", () => member.Name);
    TryAdd(objectId, "Design Orientation", () => memberData?.MemberType.Value.ToString());
    if (objectId.Count > 0)
    {
      properties["Object ID"] = objectId;
    }

    var geometry = ExtractGeometry(spans);
    if (geometry.Count > 0)
    {
      properties["Geometry"] = geometry;
    }

    var assignments = ExtractAssignments(member, memberData, spans);
    if (assignments.Count > 0)
    {
      properties["Assignments"] = assignments;
    }

    return properties;
  }

  private Dictionary<string, object?> ExtractGeometry(IReadOnlyList<IMemberSpan> spans)
  {
    var geometry = new Dictionary<string, object?>();

    double totalLength = 0;
    double totalVolume = 0;
    bool hasLength = false;
    bool hasVolume = false;

    foreach (var span in spans)
    {
      var length = TryGetValue(() => (double?)span.Length.Value);
      var area = TryGetArea(span);

      if (length is double spanLength)
      {
        totalLength += spanLength;
        hasLength = true;

        if (area is double spanArea)
        {
          totalVolume += spanLength * spanArea;
          hasVolume = true;
        }
      }
    }

    if (hasLength)
    {
      geometry["Length"] = WithUnits("Length", totalLength, "mm");
    }

    if (spans.Count > 0 && TryGetArea(spans[0]) is double crossSectionalArea)
    {
      geometry["Cross-Sectional Area"] = WithUnits("Cross-Sectional Area", crossSectionalArea, "mm²");
    }

    if (hasVolume)
    {
      geometry["Volume"] = WithUnits("Volume", totalVolume, "mm³");
    }

    if (spans.Count > 0)
    {
      TryAdd(geometry, "I-End Node", () => spans[0].StartMemberNode.ConstructionPointIndex.Value);
      TryAdd(geometry, "J-End Node", () => spans[spans.Count - 1].EndMemberNode.ConstructionPointIndex.Value);
    }

    return geometry;
  }

  private Dictionary<string, object?> ExtractAssignments(
    IMember member,
    IMemberData? memberData,
    IReadOnlyList<IMemberSpan> spans
  )
  {
    var assignments = new Dictionary<string, object?>();

    TryAdd(assignments, "Groups", () => member.ElementGroupName);

    if (memberData is not null)
    {
      TryAddWithUnits(
        assignments,
        "Local Axis Angle",
        () => RadiansToDegrees(memberData.RotationAngle.Value),
        "Degrees"
      );
    }

    if (spans.Count > 0)
    {
      TryAdd(assignments, "Section", () => TryGetSectionName(spans[0]));
      TryAdd(assignments, "Material", () => ExtractMaterial(spans[0].Material.Value));

      var endReleases = new Dictionary<string, object?>();
      TryAdd(endReleases, "I-End", () => ExtractReleases(spans[0].StartReleases.Value));
      TryAdd(endReleases, "J-End", () => ExtractReleases(spans[spans.Count - 1].EndReleases.Value));
      if (endReleases.Count > 0)
      {
        assignments["End Releases"] = endReleases;
      }
    }

    return assignments;
  }

  private static Dictionary<string, object?>? ExtractMaterial(IMaterial? material)
  {
    if (material is null)
    {
      return null;
    }

    return new Dictionary<string, object?>
    {
      ["Name"] = material.Name,
      ["Type"] = material.Type.ToString(),
      ["Poisson's Ratio"] = material.PoissonsRatio,
      ["Shear Modulus"] = WithUnits("Shear Modulus", material.ShearModulus, "N/mm²"),
      ["Thermal Expansion Coefficient"] = WithUnits(
        "Thermal Expansion Coefficient",
        material.ThermalExpansionCoefficient,
        "1/K"
      ),
    };
  }

  private static Dictionary<string, object?>? ExtractReleases(ISpanReleases? releases)
  {
    if (releases is null)
    {
      return null;
    }

    return new Dictionary<string, object?>
    {
      ["Axial Release"] = releases.AxialRelease.Value,
      ["Torsional Release"] = releases.TorsionalRelease.Value,
      ["Cantilever"] = releases.Cantilever.Value,
      ["Degree of Freedom"] = releases.DegreeOfFreedom.Value.ToString(),
    };
  }

  private double? TryGetArea(IMemberSpan span) =>
    TryGetValue(() =>
      span.ElementSection.Value is ISolverElementSection solverSection
        ? solverSection.CrossSectionalArea.Value
        : (double?)null
    );

  private static string? TryGetSectionName(IMemberSpan span) =>
    span.ElementSection.Value is IMemberSection memberSection ? memberSection.PhysicalSection.Value?.LongName : null;

  private static Dictionary<string, object?> WithUnits(string key, object value, string? units) =>
    new()
    {
      ["name"] = key,
      ["value"] = value,
      ["units"] = units,
    };

  private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;

  private void TryAdd(Dictionary<string, object?> target, string key, Func<object?> getter)
  {
    try
    {
      var value = getter();
      if (value is not null)
      {
        target[key] = value;
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "Failed to extract TSD member property {Key}", key);
    }
  }

  private void TryAddWithUnits(Dictionary<string, object?> target, string key, Func<double?> getter, string units)
  {
    try
    {
      if (getter() is double value)
      {
        target[key] = WithUnits(key, value, units);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "Failed to extract TSD member property {Key}", key);
    }
  }

  private T? TryGetValue<T>(Func<T?> getter)
    where T : struct
  {
    try
    {
      return getter();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return null;
    }
  }
}
