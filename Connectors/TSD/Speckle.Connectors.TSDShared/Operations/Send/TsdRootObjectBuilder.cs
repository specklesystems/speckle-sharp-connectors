using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.TSDShared.HostApp;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Pipelines.Progress;
using TSD.API.Remoting.Geometry;
using TSD.API.Remoting.Structure;
using TSD.API.Remoting.Units;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Connectors.TSDShared.Operations.Send;

internal sealed class TsdRootObjectBuilder : IRootObjectBuilder<IMember>
{
  private readonly ITSDApplicationService _applicationService;
  private readonly TsdMemberPropertyExtractor _propertyExtractor;
  private readonly ILogger<TsdRootObjectBuilder> _logger;

  public TsdRootObjectBuilder(
    ITSDApplicationService applicationService,
    TsdMemberPropertyExtractor propertyExtractor,
    ILogger<TsdRootObjectBuilder> logger
  )
  {
    _applicationService = applicationService;
    _propertyExtractor = propertyExtractor;
    _logger = logger;
  }

  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<IMember> members,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var unit = await _applicationService.GetLengthUnitAsync().ConfigureAwait(false);
    string speckleUnits = MapToSpeckleUnits(unit?.Abbreviation);
    if (unit is null)
    {
      _logger.LogWarning("Could not read TSD model units, defaulting to {Units}", speckleUnits);
    }

    Collection rootObjectCollection = new()
    {
      name = _applicationService.ApplicationTitle ?? "Tekla Structural Designer",
    };
    rootObjectCollection["units"] = speckleUnits;

    List<SendConversionResult> results = new(members.Count);
    int count = 0;

    foreach (var member in members)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var (result, converted) = await ConvertMemberAsync(member, unit, speckleUnits).ConfigureAwait(false);
      results.Add(result);
      if (converted is not null)
      {
        rootObjectCollection.elements.Add(converted);
      }

      count++;
      onOperationProgressed.Report(new CardProgress("Converting", (double)count / members.Count));
    }

    if (results.Count > 0 && results.TrueForAll(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    return new RootObjectBuilderResult(rootObjectCollection, results);
  }

  private async Task<(SendConversionResult result, Base? converted)> ConvertMemberAsync(
    IMember member,
    IUnitBase? unit,
    string speckleUnits
  )
  {
    string applicationId = member.Id.ToString();
    string type = GetMemberType(member);

    try
    {
      var spans = (await member.GetSpanAsync(null).ConfigureAwait(false))?.ToList() ?? new List<IMemberSpan>();
      var displayValue = await GetDisplayValueAsync(spans, unit, speckleUnits).ConfigureAwait(false);

      var tsdObject = new TsdObject
      {
        name = member.Name ?? type,
        type = type,
        elements = new List<TsdObject>(),
        displayValue = displayValue,
        properties = _propertyExtractor.Extract(member, spans),
        units = speckleUnits,
        applicationId = applicationId,
      };

      return (new SendConversionResult(Status.SUCCESS, applicationId, type, tsdObject), tsdObject);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to convert TSD member {ApplicationId}", applicationId);
      return (new SendConversionResult(Status.ERROR, applicationId, type, null, ex), null);
    }
  }

  private static string GetMemberType(IMember member)
  {
    try
    {
      return member.Data.Value.MemberType.Value.ToString();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return nameof(IMember);
    }
  }

  private async Task<List<Base>> GetDisplayValueAsync(
    IReadOnlyList<IMemberSpan> spans,
    IUnitBase? unit,
    string speckleUnits
  )
  {
    var displayValue = new List<Base>();

    if (spans.Count == 0)
    {
      return displayValue;
    }

    List<double> baseCoordinates = new();
    foreach (var span in spans)
    {
      var segment = span.DesignSegment.Value;
      if (segment is null)
      {
        continue;
      }

      var start = segment.GetPoint(Location.Start);
      var end = segment.GetPoint(Location.End);

      baseCoordinates.Add(start.X);
      baseCoordinates.Add(start.Y);
      baseCoordinates.Add(start.Z);
      baseCoordinates.Add(end.X);
      baseCoordinates.Add(end.Y);
      baseCoordinates.Add(end.Z);
    }

    if (baseCoordinates.Count == 0)
    {
      return displayValue;
    }

    var coordinates = unit is null
      ? baseCoordinates
      : await _applicationService.ConvertFromBaseAsync(baseCoordinates, unit).ConfigureAwait(false);

    for (int i = 0; i + 5 < coordinates.Count; i += 6)
    {
      displayValue.Add(
        new SOG.Line
        {
          start = new SOG.Point(coordinates[i], coordinates[i + 1], coordinates[i + 2], speckleUnits),
          end = new SOG.Point(coordinates[i + 3], coordinates[i + 4], coordinates[i + 5], speckleUnits),
          units = speckleUnits,
        }
      );
    }

    return displayValue;
  }

  private static string MapToSpeckleUnits(string? abbreviation)
  {
    if (string.IsNullOrWhiteSpace(abbreviation))
    {
      return Units.Meters;
    }

    try
    {
      return Units.GetUnitsFromString(abbreviation) ?? Units.Meters;
    }
    catch (ArgumentOutOfRangeException)
    {
      return Units.Meters;
    }
  }
}
