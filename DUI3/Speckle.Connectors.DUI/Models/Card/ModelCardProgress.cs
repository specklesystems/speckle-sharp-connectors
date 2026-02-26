namespace Speckle.Connectors.DUI.Models.Card;

/// <summary>
/// Progress value between 0 and 1 to calculate UI progress bar width.
/// If it is null it will swooshing on UI.
/// </summary>
public readonly record struct ModelCardProgress(string ModelCardId, string Status, double? Progress)
{
  public override string ToString() => $"{ModelCardId} - {Status} - {Progress}";
}
