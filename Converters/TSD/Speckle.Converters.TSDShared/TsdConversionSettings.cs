namespace Speckle.Converters.TSDShared;

public sealed class TsdConversionSettings
{
  public IReadOnlyList<string> SelectedLoadings { get; set; } = Array.Empty<string>();
  public IReadOnlyList<string> SelectedResultTypes { get; set; } = Array.Empty<string>();
}
