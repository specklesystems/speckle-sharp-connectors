using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;

namespace Speckle.Connectors.DUI.Bindings;

public interface IBasicConnectorBinding : IBinding
{
  public string GetSourceApplicationName();
  public string GetSourceApplicationVersion();
  public string GetConnectorVersion();
  public DocumentInfo? GetDocumentInfo();
  public DocumentModelStore GetDocumentState();
  public void AddModel(ModelCard model);
  public void UpdateModel(ModelCard model);
  public void RemoveModel(ModelCard model);
  public void RemoveModels(List<ModelCard> models);

  /// <summary>
  /// Highlights the objects attached to this sender in the host application.
  /// </summary>
  /// <param name="modelCardId"></param>
  public Task HighlightModel(string modelCardId);

  public Task HighlightObjects(IReadOnlyList<string> objectIds);

  public BasicConnectorBindingCommands Commands { get; }
}

public static class BasicConnectorBindingEvents
{
  public const string DISPLAY_TOAST_NOTIFICATION = "DisplayToastNotification";
  public const string DOCUMENT_CHANGED = "documentChanged";
}

public enum ToastNotificationType
{
  SUCCESS,
  WARNING,
  DANGER,
  INFO
}

public class BasicConnectorBindingCommands
{
  private const string NOTIFY_DOCUMENT_CHANGED_EVENT_NAME = "documentChanged";
  private const string SET_MODEL_ERROR_UI_COMMAND_NAME = "setModelError";
  public const string SET_GLOBAL_NOTIFICATION = "setGlobalNotification";

  public IBrowserBridge Bridge { get; }

  public BasicConnectorBindingCommands(IBrowserBridge bridge)
  {
    Bridge = bridge;
  }

  public async Task NotifyDocumentChanged() => await Bridge.Send(NOTIFY_DOCUMENT_CHANGED_EVENT_NAME);

  /// <summary>
  /// Use it whenever you want to send global toast notification to UI.
  /// </summary>
  /// <param name="type"> Level of notification, see <see cref="ToastNotificationType"/> for types</param>
  /// <param name="title"> Title of the notification</param>
  /// <param name="message"> Message in the toast notification.</param>
  /// <param name="autoClose"> Closes toast notification in set timeout in UI. Default is true.</param>
  public async Task SetGlobalNotification(
    ToastNotificationType type,
    string title,
    string message,
    bool autoClose = true
  ) =>
    await Bridge.Send(
      SET_GLOBAL_NOTIFICATION,
      new
      {
        type,
        title,
        description = message,
        autoClose
      }
    );

  public async Task SetModelError(string modelCardId, Exception error) =>
    await Bridge.Send(SET_MODEL_ERROR_UI_COMMAND_NAME, new { modelCardId, error = error.Message });
}
