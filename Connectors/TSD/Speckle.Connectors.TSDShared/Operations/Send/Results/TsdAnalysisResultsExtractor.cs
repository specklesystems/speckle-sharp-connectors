using Speckle.Connectors.TSDShared.HostApp;
using Speckle.Sdk;
using TSD.API.Remoting.Solver;
using TSD.API.Remoting.Structure.Analysis;
using StructureModel = TSD.API.Remoting.Structure.IModel;

namespace Speckle.Connectors.TSDShared.Operations.Send.Results;

internal sealed class TsdAnalysisResultsExtractor
{
  private static readonly AnalysisType[] s_analysisTypePriority =
  {
    AnalysisType.SecondOrderNonLinear,
    AnalysisType.SecondOrderLinear,
    AnalysisType.FirstOrderNonLinear,
    AnalysisType.FirstOrderLinear,
    AnalysisType.FEChaseDown,
    AnalysisType.GrillageChaseDown,
    AnalysisType.SecondOrderNonLinearStagedConstruction,
    AnalysisType.SecondOrderLinearStagedConstruction,
    AnalysisType.FirstOrderNonLinearStagedConstruction,
    AnalysisType.FirstOrderLinearStagedConstruction,
    AnalysisType.SequentialLoading,
  };

  private readonly ITSDApplicationService _applicationService;
  private readonly TsdResultsExtractorFactory _factory;

  public TsdAnalysisResultsExtractor(ITSDApplicationService applicationService, TsdResultsExtractorFactory factory)
  {
    _applicationService = applicationService;
    _factory = factory;
  }

  public async Task<Dictionary<string, object?>?> ExtractAsync(
    IReadOnlyList<string> selectedLoadings,
    IReadOnlyList<string> selectedResultTypes,
    CancellationToken cancellationToken
  )
  {
    if (selectedLoadings.Count == 0 || selectedResultTypes.Count == 0)
    {
      return null;
    }

    var model = await _applicationService.GetModelAsync().ConfigureAwait(false);
    if (model is null)
    {
      throw new SpeckleException("Could not access the TSD model to extract analysis results.");
    }

    var analysis = await ResolveAnalysis3DAsync(model, cancellationToken).ConfigureAwait(false);
    if (analysis is null)
    {
      throw new SpeckleException(
        "No completed analysis with results was found in the TSD model. Run the analysis first."
      );
    }

    var (analysis3D, solvedIdSet) = analysis.Value;

    var loadings = (await ResolveLoadingsAsync(model, selectedLoadings, cancellationToken).ConfigureAwait(false))
      .Where(loading => solvedIdSet.Contains(loading.Id))
      .ToList();

    if (loadings.Count == 0)
    {
      throw new SpeckleException(
        "None of the selected load cases or combinations have analysis results. Run the analysis in TSD first."
      );
    }

    var tree = new Dictionary<string, object?>();
    foreach (var resultType in selectedResultTypes)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var extractor = _factory.GetExtractor(resultType);
      tree[extractor.ResultsKey] = await extractor
        .GetResultsAsync(analysis3D, loadings, cancellationToken)
        .ConfigureAwait(false);
    }

    return tree;
  }

  private static async Task<(IAnalysis3DResults Results, HashSet<Guid> SolvedLoadingIds)?> ResolveAnalysis3DAsync(
    StructureModel model,
    CancellationToken cancellationToken
  )
  {
    foreach (var analysisType in await GetCandidateAnalysisTypesAsync(model, cancellationToken).ConfigureAwait(false))
    {
      cancellationToken.ThrowIfCancellationRequested();

      var solverModels = await model
        .GetSolverModelsAsync(new[] { analysisType }, cancellationToken)
        .ConfigureAwait(false);
      var solverModel = solverModels?.FirstOrDefault();
      if (solverModel is null)
      {
        continue;
      }

      var results = await solverModel.GetResultsAsync(cancellationToken).ConfigureAwait(false);
      var analysis3D =
        results is null ? null : await results.GetAnalysis3DAsync(cancellationToken).ConfigureAwait(false);
      if (analysis3D is null)
      {
        continue;
      }

      var solvedIds = await analysis3D.GetSolvedLoadingIdsAsync(cancellationToken).ConfigureAwait(false);
      var solvedIdSet = solvedIds?.ToHashSet() ?? new HashSet<Guid>();
      if (solvedIdSet.Count > 0)
      {
        return (analysis3D, solvedIdSet);
      }
    }

    return null;
  }

  private static async Task<IReadOnlyList<AnalysisType>> GetCandidateAnalysisTypesAsync(
    StructureModel model,
    CancellationToken cancellationToken
  )
  {
    var ordered = new List<AnalysisType>();

    void Add(AnalysisType analysisType)
    {
      if (
        analysisType != AnalysisType.Unknown
        && analysisType != AnalysisType.None
        && !ordered.Contains(analysisType)
      )
      {
        ordered.Add(analysisType);
      }
    }

    Add(await model.GetSelectedAnalysisTypeAsync(cancellationToken).ConfigureAwait(false));

    var condition = await model.GetAnalysisConditionAsync(cancellationToken).ConfigureAwait(false);
    if (condition is not null)
    {
      foreach (var analysisType in s_analysisTypePriority)
      {
        if (condition.GetStatus(analysisType) == AnalysisStatus.Actual)
        {
          Add(analysisType);
        }
      }
    }

    foreach (var analysisType in s_analysisTypePriority)
    {
      Add(analysisType);
    }

    return ordered;
  }

  private static async Task<IReadOnlyList<TsdLoadingRef>> ResolveLoadingsAsync(
    StructureModel model,
    IReadOnlyList<string> selectedLoadings,
    CancellationToken cancellationToken
  )
  {
    var selected = new HashSet<string>(selectedLoadings);
    var loadings = new List<TsdLoadingRef>();

    var loadcases = await model.GetLoadcasesAsync(null, cancellationToken).ConfigureAwait(false);
    if (loadcases is not null)
    {
      loadings.AddRange(
        loadcases
          .Where(loadcase => selected.Contains(loadcase.Name))
          .Select(loadcase => new TsdLoadingRef(loadcase.Id, loadcase.Name))
      );
    }

    var combinations = await model.GetCombinationsAsync(null, cancellationToken).ConfigureAwait(false);
    if (combinations is not null)
    {
      loadings.AddRange(
        combinations
          .Where(combination => selected.Contains(combination.Name))
          .Select(combination => new TsdLoadingRef(combination.Id, combination.Name))
      );
    }

    return loadings;
  }
}
