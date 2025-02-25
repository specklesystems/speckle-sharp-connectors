namespace Speckle.Connectors.DUI.Models.Card.SendFilter;

public record SendFilterSelectItem(string Id, string Name);

/// <summary>
/// UI data type to make have FilterFormSelect component as send filter.
/// </summary>
public interface ISendFilterSelect : ISendFilter
{
  public bool IsMultiSelectable { get; set; }
  public List<SendFilterSelectItem> SelectedItems { get; set; }
  public List<SendFilterSelectItem> Items { get; set; }
}
