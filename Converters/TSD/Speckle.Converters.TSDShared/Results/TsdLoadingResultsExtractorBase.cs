using TSD.API.Remoting.Solver;

namespace Speckle.Converters.TSDShared.Results;

public abstract class TsdLoadingResultsExtractorBase<TResult> : ITsdResultsExtractor
{
  public abstract string ResultsKey { get; }

  protected abstract Task<IEnumerable<TResult>?> FetchAsync(
    IAnalysis3DResults analysisResults,
    Guid loadingId,
    CancellationToken cancellationToken
  );

  protected abstract Dictionary<string, object?> Build(IEnumerable<TResult> items, TsdResultsContext context);

  public async Task<Dictionary<string, object?>> GetResultsAsync(
    TsdResultsContext context,
    CancellationToken cancellationToken
  )
  {
    var results = new Dictionary<string, object?>();

    foreach (var loading in context.Loadings)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var items = await FetchAsync(context.AnalysisResults, loading.Id, cancellationToken).ConfigureAwait(false);
      if (items is null)
      {
        continue;
      }

      var entries = Build(items, context);
      if (entries.Count > 0)
      {
        results[loading.Name] = entries;
      }
    }

    return results;
  }
}
