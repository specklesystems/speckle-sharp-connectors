using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.TSDShared.HostApp;
using Speckle.Converters.TSDShared;
using Speckle.Converters.TSDShared.Results;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Pipelines.Progress;
using TSD.API.Remoting.Common;
using TSD.API.Remoting.Structure;
using TSD.API.Remoting.Units;

namespace Speckle.Connectors.TSDShared.Operations.Send;

internal sealed class TsdRootObjectBuilder : IRootObjectBuilder<IEntity>
{
  private static readonly Quantity[] s_propertyQuantities =
  {
    Quantity.Dimension,
    Quantity.Area,
    Quantity.Volume,
    Quantity.ShearModulus,
    Quantity.TemperatureCoefficient,
    Quantity.Angle,
    Quantity.Force,
    Quantity.Moment,
    Quantity.Deflection,
  };

  private readonly ITSDApplicationService _applicationService;
  private readonly TsdDisplayValueExtractor _displayValueExtractor;
  private readonly TsdMemberPropertyExtractor _memberPropertyExtractor;
  private readonly TsdSlabPropertyExtractor _slabPropertyExtractor;
  private readonly TsdWallPropertyExtractor _wallPropertyExtractor;
  private readonly TsdAnalysisResultsExtractor _analysisResultsExtractor;
  private readonly TsdConversionSettings _conversionSettings;
  private readonly ILogger<TsdRootObjectBuilder> _logger;

  public TsdRootObjectBuilder(
    ITSDApplicationService applicationService,
    TsdDisplayValueExtractor displayValueExtractor,
    TsdMemberPropertyExtractor memberPropertyExtractor,
    TsdSlabPropertyExtractor slabPropertyExtractor,
    TsdWallPropertyExtractor wallPropertyExtractor,
    TsdAnalysisResultsExtractor analysisResultsExtractor,
    TsdConversionSettings conversionSettings,
    ILogger<TsdRootObjectBuilder> logger
  )
  {
    _applicationService = applicationService;
    _displayValueExtractor = displayValueExtractor;
    _memberPropertyExtractor = memberPropertyExtractor;
    _slabPropertyExtractor = slabPropertyExtractor;
    _wallPropertyExtractor = wallPropertyExtractor;
    _analysisResultsExtractor = analysisResultsExtractor;
    _conversionSettings = conversionSettings;
    _logger = logger;
  }

  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<IEntity> entities,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var units = await _applicationService.GetUnitsAsync(s_propertyQuantities).ConfigureAwait(false);
    units.TryGetValue(Quantity.Dimension, out var lengthUnit);
    string speckleUnits = MapToSpeckleUnits(lengthUnit?.Abbreviation);
    if (lengthUnit is null)
    {
      _logger.LogWarning("Could not read TSD model units, defaulting to {Units}", speckleUnits);
    }

    Collection rootObjectCollection = new()
    {
      name = _applicationService.ApplicationTitle ?? "Tekla Structural Designer",
    };
    rootObjectCollection["units"] = speckleUnits;

    var slabDataByIndex = await GetSlabDataAsync(entities).ConfigureAwait(false);

    List<SendConversionResult> results = new(entities.Count);
    List<Dictionary<string, object?>> propertyTrees = new(entities.Count);
    int count = 0;

    foreach (var entity in entities)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var (result, converted) = await ConvertEntityAsync(entity, slabDataByIndex, lengthUnit, speckleUnits)
        .ConfigureAwait(false);
      results.Add(result);
      if (converted is not null)
      {
        rootObjectCollection.elements.Add(converted);
        if (converted is TsdObject tsdObject)
        {
          propertyTrees.Add(tsdObject.properties);
        }
      }

      count++;
      onOperationProgressed.Report(new CardProgress("Converting", (double)count / entities.Count));
    }

    Dictionary<string, object?>? analysisResultsTree;
    try
    {
      analysisResultsTree = await _analysisResultsExtractor
        .ExtractAsync(_conversionSettings.SelectedLoadings, _conversionSettings.SelectedResultTypes, cancellationToken)
        .ConfigureAwait(false);
    }
    catch (SpeckleException)
    {
      throw;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      throw new SpeckleException("Analysis result extraction failed", ex);
    }

    if (analysisResultsTree is not null)
    {
      foreach (var resultBranch in analysisResultsTree.Values)
      {
        if (resultBranch is Dictionary<string, object?> branch)
        {
          propertyTrees.Add(branch);
        }
      }
    }

    await ApplyUnitsAsync(propertyTrees, units).ConfigureAwait(false);

    if (analysisResultsTree is not null)
    {
      var analysisResults = new Base();
      foreach (var (key, value) in analysisResultsTree)
      {
        analysisResults[key] = value;
      }

      rootObjectCollection[RootKeys.ANALYSIS_RESULTS] = analysisResults;
    }

    if (results.Count > 0 && results.TrueForAll(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    return new RootObjectBuilderResult(rootObjectCollection, results);
  }

  private async Task<(SendConversionResult result, Base? converted)> ConvertEntityAsync(
    IEntity entity,
    IReadOnlyDictionary<int, ISlabData> slabDataByIndex,
    IUnitBase? unit,
    string speckleUnits
  )
  {
    string applicationId = entity.Id.ToString();
    string type = GetEntityType(entity);

    try
    {
      var (displayValue, properties) = entity switch
      {
        IMember member => await ConvertMemberAsync(member, unit, speckleUnits).ConfigureAwait(false),
        ISlabItem slabItem => await ConvertSlabAsync(slabItem, slabDataByIndex, unit, speckleUnits)
          .ConfigureAwait(false),
        IStructuralWall wall => await ConvertWallAsync(wall, unit, speckleUnits).ConfigureAwait(false),
        _ => (new List<Base>(), new Dictionary<string, object?>()),
      };

      var tsdObject = new TsdObject
      {
        name = (entity as IHaveName)?.Name ?? type,
        type = type,
        elements = new List<TsdObject>(),
        displayValue = displayValue,
        properties = properties,
        units = speckleUnits,
        applicationId = applicationId,
      };

      return (new SendConversionResult(Status.SUCCESS, applicationId, type, tsdObject), tsdObject);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to convert TSD object {ApplicationId}", applicationId);
      return (new SendConversionResult(Status.ERROR, applicationId, type, null, ex), null);
    }
  }

  private static string GetEntityType(IEntity entity)
  {
    if (entity is IMember member)
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

    return entity.EntityType.ToString();
  }

  private async Task<(List<Base>, Dictionary<string, object?>)> ConvertMemberAsync(
    IMember member,
    IUnitBase? unit,
    string speckleUnits
  )
  {
    var spans = (await member.GetSpanAsync(null).ConfigureAwait(false))?.ToList() ?? new List<IMemberSpan>();
    var displayValue = await _displayValueExtractor
      .GetMemberDisplayValueAsync(spans, unit, speckleUnits)
      .ConfigureAwait(false);
    var properties = _memberPropertyExtractor.Extract(member, spans);
    return (displayValue, properties);
  }

  private async Task<(List<Base>, Dictionary<string, object?>)> ConvertSlabAsync(
    ISlabItem slabItem,
    IReadOnlyDictionary<int, ISlabData> slabDataByIndex,
    IUnitBase? unit,
    string speckleUnits
  )
  {
    var displayValue = await _displayValueExtractor
      .GetSlabDisplayValueAsync(slabItem, unit, speckleUnits)
      .ConfigureAwait(false);

    ISlabData? slabData = null;
    if (TryGetSlabIndex(slabItem) is int slabIndex)
    {
      slabDataByIndex.TryGetValue(slabIndex, out slabData);
    }

    var properties = _slabPropertyExtractor.Extract(slabItem, slabData);
    return (displayValue, properties);
  }

  private async Task<IReadOnlyDictionary<int, ISlabData>> GetSlabDataAsync(IReadOnlyList<IEntity> entities)
  {
    var slabIndices = new HashSet<int>();
    foreach (var slabItem in entities.OfType<ISlabItem>())
    {
      if (TryGetSlabIndex(slabItem) is int slabIndex)
      {
        slabIndices.Add(slabIndex);
      }
    }

    return slabIndices.Count == 0
      ? new Dictionary<int, ISlabData>()
      : await _applicationService.GetSlabDataAsync(slabIndices).ConfigureAwait(false);
  }

  private int? TryGetSlabIndex(ISlabItem slabItem)
  {
    try
    {
      return slabItem.SlabIndex.Value;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "Failed to read TSD slab index");
      return null;
    }
  }

  private async Task<(List<Base>, Dictionary<string, object?>)> ConvertWallAsync(
    IStructuralWall wall,
    IUnitBase? unit,
    string speckleUnits
  )
  {
    var panels = (await wall.GetSpanAsync(null).ConfigureAwait(false))?.ToList() ?? new List<IStructuralWallPanel>();
    var displayValue = await _displayValueExtractor
      .GetWallDisplayValueAsync(panels, unit, speckleUnits)
      .ConfigureAwait(false);
    var properties = _wallPropertyExtractor.Extract(wall, panels);
    return (displayValue, properties);
  }

  private async Task ApplyUnitsAsync(
    IReadOnlyList<Dictionary<string, object?>> propertyTrees,
    IReadOnlyDictionary<Quantity, IUnitBase> units
  )
  {
    var placeholders = new List<(Dictionary<string, object?> parent, string key, TsdQuantityValue value)>();
    foreach (var tree in propertyTrees)
    {
      CollectQuantities(tree, placeholders);
    }

    if (placeholders.Count == 0)
    {
      return;
    }

    foreach (var group in placeholders.GroupBy(p => p.value.Quantity))
    {
      var items = group.ToList();
      units.TryGetValue(group.Key, out var unit);

      IReadOnlyList<double> values;
      if (unit is null)
      {
        values = items.Select(i => i.value.BaseValue).ToList();
      }
      else
      {
        values = await _applicationService
          .ConvertFromBaseAsync(items.Select(i => i.value.BaseValue).ToList(), unit)
          .ConfigureAwait(false);
      }

      for (int i = 0; i < items.Count; i++)
      {
        var (parent, key, value) = items[i];
        double converted = i < values.Count ? values[i] : value.BaseValue;
        parent[key] = WithUnits(value.Name, converted, unit?.Abbreviation ?? value.BaseUnits);
      }
    }
  }

  private static void CollectQuantities(
    Dictionary<string, object?> dict,
    List<(Dictionary<string, object?> parent, string key, TsdQuantityValue value)> sink
  )
  {
    foreach (var kvp in dict)
    {
      if (kvp.Value is TsdQuantityValue quantityValue)
      {
        sink.Add((dict, kvp.Key, quantityValue));
      }
      else if (kvp.Value is Dictionary<string, object?> child)
      {
        CollectQuantities(child, sink);
      }
    }
  }

  private static Dictionary<string, object?> WithUnits(string key, object value, string? units) =>
    new()
    {
      ["name"] = key,
      ["value"] = value,
      ["units"] = units,
    };

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
