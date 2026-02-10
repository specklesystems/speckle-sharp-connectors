using Speckle.Connectors.DUI.Models.Card;

namespace Speckle.Connectors.DUI.Models;

public sealed class ModelCardsChangedEventArgs(IReadOnlyList<ModelCard> modelCards) : EventArgs
{
  public IReadOnlyList<ModelCard> ModelCards { get; } = modelCards;
}
