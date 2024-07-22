namespace Speckle.Connectors.DUI.Bindings;

public interface ISelectionBinding : IBinding
{
  public SelectionInfo GetSelection();
}

public static class SelectionBindingEvents
{
  public const string SET_SELECTION = "setSelection";
}

public record SelectionInfo(IReadOnlyCollection<string> SelectedObjectIds, string Summary);
