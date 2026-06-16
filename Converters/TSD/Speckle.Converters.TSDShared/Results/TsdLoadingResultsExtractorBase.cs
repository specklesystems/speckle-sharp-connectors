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

  protected abstract Dictionary<string, object?> Build(IEnumerable<TResult> items);

  public async Task<Dictionary<string, object?>> GetResultsAsync(
    IAnalysis3DResults analysisResults,
    IReadOnlyList<TsdLoadingRef> loadings,
    CancellationToken cancellationToken
  )
  {
    var results = new Dictionary<string, object?>();

    foreach (var loading in loadings)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var items = await FetchAsync(analysisResults, loading.Id, cancellationToken).ConfigureAwait(false);
      if (items is null)
      {
        continue;
      }

      var entries = Build(items);
      if (entries.Count > 0)
      {
        results[loading.Name] = entries;
      }
    }

    return results;
  }
}
