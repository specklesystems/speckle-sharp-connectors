namespace Speckle.Connectors.DUI.Models.Card.SendFilter;

public record SendFilterSelectItem(string Id, string Name);

public interface ISendFilterSelect : ISendFilter
{
  public bool IsMultiSelectable { get; set; }
  public List<SendFilterSelectItem> SelectedItems { get; set; }
  public List<SendFilterSelectItem> Items { get; set; }
}
