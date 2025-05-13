using System.Data;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Database = Autodesk.Navisworks.Api.DocumentParts.DocumentDatabase;

namespace Speckle.Connector.Navisworks.HostApp;

/// <summary>
/// Manages persistence of Speckle model states within Navisworks' embedded SQLite database.
/// Provides mechanisms for reliable read/write operations with retry handling and validation.
/// </summary>
public sealed class NavisworksDocumentModelStore : DocumentModelStore
{
  private const string TABLE_NAME = "speckle";
  private const string KEY_NAME = "Speckle_DUI3";

  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private string _lastSavedState = string.Empty;

  public NavisworksDocumentModelStore(
    ILogger<DocumentModelStore> logger,
    IJsonSerializer jsonSerializer,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
    : base(logger, jsonSerializer)
  {
    _topLevelExceptionHandler = topLevelExceptionHandler;
    LoadState();
  }

  protected override void HostAppSaveState(string modelCardState)
  {
    if (!IsActiveDocumentValid())
    {
      return;
    }

    // Compare current state with last saved state
    if (modelCardState == _lastSavedState)
    {
      return; // Skip save if states match
    }

    try
    {
      SaveStateToDatabase(modelCardState);
      _lastSavedState = modelCardState; // Update last saved state after successful save
    }
    catch (NAV.Data.DatabaseException ex)
    {
      _topLevelExceptionHandler.CatchUnhandled(
        () => throw new InvalidOperationException("Failed to write Speckle state to database", ex)
      );
    }
  }

  /// <summary>
  /// Public method to reload the state from storage.
  /// </summary>
  public void ReloadState() => LoadState();

  protected override void LoadState()
  {
    if (!IsActiveDocumentValid())
    {
      ClearAndSaveThisState();
      return;
    }

    try
    {
      string serializedState = RetrieveStateFromDatabase();
      LoadFromString(serializedState);
      _lastSavedState = serializedState; // Store initial state after loading
    }
    catch (NAV.Data.DatabaseException ex)
    {
      ClearAndSaveThisState(); // Clear models on failure to avoid stale data
      _topLevelExceptionHandler.CatchUnhandled(
        () => throw new InvalidOperationException("Failed to read Speckle state from database", ex)
      );
    }
  }

  private void ClearAndSaveThisState()
  {
    ClearAndSave();
    _lastSavedState = string.Empty; // Reset last saved state when clearing
  }

  private static bool IsActiveDocumentValid()
  {
    try
    {
      var activeDoc = NavisworksApp.ActiveDocument;
      return activeDoc?.Database != null && activeDoc.Models.Count > 0 && activeDoc.ActiveSheet != null;
    }
    catch (ArgumentException)
    {
      return false; // Handle invalid document access
    }
    catch (ObjectDisposedException)
    {
      return false; // Handle disposed document state
    }
  }

  private static void SaveStateToDatabase(string modelCardState)
  {
    var activeDoc = NavisworksApp.ActiveDocument;
    if (activeDoc?.Database == null)
    {
      return;
    }

    var database = activeDoc.Database;

    if (!DoesTableExist(database))
    {
      CreateTable(database);
    }

    using var transaction = database.BeginTransaction(NAV.Data.DatabaseChangedAction.Edited);
    try
    {
      ReplaceStateInDatabase(transaction, modelCardState);
      transaction.Commit();
    }
    catch
    {
      transaction.Rollback(); // Roll back transaction on failure
      throw;
    }
  }

  private static void ReplaceStateInDatabase(NAV.Data.NavisworksTransaction transaction, string modelCardState)
  {
    var command = transaction.Connection.CreateCommand();
    command.CommandText = $"REPLACE INTO {TABLE_NAME}(key, value) VALUES(@key, @value)";
    command.Parameters.AddWithValue("@key", KEY_NAME);
    command.Parameters.AddWithValue("@value", modelCardState);
    command.ExecuteNonQuery();
  }

  private static bool DoesTableExist(Database database)
  {
    var checkCommand = database.Value.CreateCommand();
    checkCommand.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{TABLE_NAME}'";
    return checkCommand.ExecuteScalar() != null;
  }

  private static void CreateTable(Database database)
  {
    using var transaction = database.BeginTransaction(NAV.Data.DatabaseChangedAction.Edited);
    try
    {
      var command = transaction.Connection.CreateCommand();
      command.CommandText = $"CREATE TABLE {TABLE_NAME}(key TEXT PRIMARY KEY, value TEXT)";
      command.ExecuteNonQuery();
      transaction.Commit();
    }
    catch
    {
      transaction.Rollback();
      throw;
    }
  }

  private static string RetrieveStateFromDatabase()
  {
    var database = NavisworksApp.ActiveDocument!.Database;
    using var table = new DataTable();

    if (!DoesTableExist(database))
    {
      return string.Empty;
    }

    using var dataAdapter = new NAV.Data.NavisworksDataAdapter(
      $"SELECT value FROM {TABLE_NAME} WHERE key = @key",
      database.Value
    );
    dataAdapter.SelectCommand.Parameters.AddWithValue("@key", KEY_NAME);
    dataAdapter.Fill(table);

    return table.Rows.Count <= 0 ? string.Empty : table.Rows[0]["value"] as string ?? string.Empty;
  }
}
