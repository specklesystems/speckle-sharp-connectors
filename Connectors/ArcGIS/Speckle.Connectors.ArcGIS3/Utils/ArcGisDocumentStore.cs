using System.Xml.Linq;
using ArcGIS.Desktop.Core.Events;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Mapping.Events;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;

namespace Speckle.Connectors.ArcGIS.Utils;

public class ArcGISDocumentStore : DocumentModelStore
{
  public ArcGISDocumentStore(
    ILogger<DocumentModelStore> logger,
    IJsonSerializer jsonSerializer,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
    : base(logger, jsonSerializer)
  {
    ActiveMapViewChangedEvent.Subscribe(a => topLevelExceptionHandler.CatchUnhandled(() => OnMapViewChanged(a)), true);
    ProjectSavingEvent.Subscribe(
      _ =>
      {
        topLevelExceptionHandler.CatchUnhandled(OnProjectSaving);
        return Task.CompletedTask;
      },
      true
    );
    ProjectClosingEvent.Subscribe(
      _ =>
      {
        topLevelExceptionHandler.CatchUnhandled(OnProjectClosing);
        return Task.CompletedTask;
      },
      true
    );

    // in case plugin was loaded into already opened Map, read metadata from the current Map
    if (!IsDocumentInit && MapView.Active != null)
    {
      IsDocumentInit = true;
      LoadState();
      OnDocumentChanged();
    }
  }

  private void OnProjectClosing()
  {
    if (MapView.Active is null)
    {
      return;
    }

    SaveState();
  }

  private void OnProjectSaving()
  {
    if (MapView.Active is not null)
    {
      SaveState();
    }
  }

  /// <summary>
  /// On map view switch, this event trigger twice, first for outgoing view, second for incoming view.
  /// </summary>
  private void OnMapViewChanged(ActiveMapViewChangedEventArgs args)
  {
    if (args.IncomingView is null)
    {
      return;
    }

    IsDocumentInit = true;
    LoadState();
    OnDocumentChanged();
  }

  protected override void HostAppSaveState(string modelCardState) =>
    QueuedTask
      .Run(() =>
      {
        Map map = MapView.Active.Map;
        // Read existing metadata - To prevent messing existing metadata. 🤞 Hope other add-in developers will do same :D
        var existingMetadata = map.GetMetadata();

        // Parse existing metadata
        XDocument existingXmlDocument = !string.IsNullOrEmpty(existingMetadata)
          ? XDocument.Parse(existingMetadata)
          : new XDocument(new XElement("metadata"));

        XElement xmlModelCards = new("SpeckleModelCards", modelCardState);

        // Check if SpeckleModelCards element already exists at root and update it
        var speckleModelCardsElement = existingXmlDocument.Root?.Element("SpeckleModelCards");
        if (speckleModelCardsElement != null)
        {
          speckleModelCardsElement.ReplaceWith(xmlModelCards);
        }
        else
        {
          existingXmlDocument.Root?.Add(xmlModelCards);
        }

        map.SetMetadata(existingXmlDocument.ToString());
      })
      .FireAndForget();

  protected override void LoadState() =>
    QueuedTask
      .Run(() =>
      {
        Map map = MapView.Active.Map;
        var metadata = map.GetMetadata();
        var root = XDocument.Parse(metadata).Root;
        var element = root?.Element("SpeckleModelCards");
        if (element is null)
        {
          ClearAndSave();
          return;
        }

        string modelsString = element.Value;
        LoadFromString(modelsString);
      })
      .FireAndForget();
}
