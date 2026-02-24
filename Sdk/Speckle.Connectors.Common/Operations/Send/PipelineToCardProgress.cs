using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.Common.Operations.Send;

public sealed class PipelineToCardProgress(IProgress<CardProgress> cardProgress) : IProgress<PipelineProgressArgs>
{
  public void Report(PipelineProgressArgs value) => cardProgress.Report(new(value.StatusMessage, value.Progress));
}
