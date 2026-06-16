namespace Speckle.Connectors.TSDShared.Operations.Send.Results;

internal sealed class TsdConversionSettings
{
  public IReadOnlyList<string> SelectedLoadings { get; set; } = Array.Empty<string>();
  public IReadOnlyList<string> SelectedResultTypes { get; set; } = Array.Empty<string>();
}
