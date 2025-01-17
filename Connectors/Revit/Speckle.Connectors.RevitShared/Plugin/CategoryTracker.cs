using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Speckle.Connectors.Revit.Plugin;

public class VisibilityUpdater : IUpdater
{
  private static UpdaterId s_updaterId;
  private readonly CategoryVisibilityTracker _tracker;

  public VisibilityUpdater(AddInId addInId, Document doc)
  {
    s_updaterId = new UpdaterId(addInId, new Guid("B1234567-ABCD-1234-5678-9ABCDEF01234")); // Unique GUID
    _tracker = new CategoryVisibilityTracker(doc);
  }

  public void Execute(UpdaterData data)
  {
    Document doc = data.GetDocument();

    foreach (Category category in doc.Settings.Categories)
    {
      if (category.get_AllowsVisibilityControl(doc.ActiveView) && _tracker.HasVisibilityChanged(category))
      {
        TaskDialog.Show(
          "Visibility Changed",
          $"Category {category.Name} visibility has changed to {category.get_Visible(doc.ActiveView)}."
        );
      }
    }
  }

  public string GetAdditionalInformation() => "Tracks visibility changes for categories.";

  public ChangePriority GetChangePriority() => ChangePriority.Views;

  public UpdaterId GetUpdaterId() => s_updaterId;

  public string GetUpdaterName() => "VisibilityUpdater";
}
