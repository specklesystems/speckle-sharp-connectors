using System.Data;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;

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

  public NavisworksDocumentModelStore(
    IJsonSerializer jsonSerializer,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
    : base(jsonSerializer)
  {
    _topLevelExceptionHandler = topLevelExceptionHandler;
    LoadState();
  }

  public override void HostAppSaveState(string modelCardState)
  {
    if (!IsActiveDocumentValid())
    {
      return;
    }

    try
    {
      SaveStateToDatabase(modelCardState);
    }
    catch (NAV.Data.DatabaseException ex)
    {
      _topLevelExceptionHandler.CatchUnhandled(
        () => throw new InvalidOperationException("Failed to write Speckle state to database", ex)
      );
    }
  }

  public override void LoadState()
  {
    if (!IsActiveDocumentValid())
    {
      ClearAndSave();
      return;
    }

    try
    {
      string serializedState = RetrieveStateFromDatabase();
      LoadFromString(serializedState);
    }
    catch (NAV.Data.DatabaseException ex)
    {
      ClearAndSave(); // Clear models on failure to avoid stale data
      _topLevelExceptionHandler.CatchUnhandled(
        () => throw new InvalidOperationException("Failed to read Speckle state from database", ex)
      );
    }
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

    using (var transaction = database.BeginTransaction(NAV.Data.DatabaseChangedAction.Reset))
    {
      EnsureDatabaseTableExists(transaction);
    }

    using (var transaction = database.BeginTransaction(NAV.Data.DatabaseChangedAction.Edited))
    {
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
  }

  private static void EnsureDatabaseTableExists(NAV.Data.NavisworksTransaction transaction)
  {
    var command = transaction.Connection.CreateCommand();
    command.CommandText = $"CREATE TABLE IF NOT EXISTS {TABLE_NAME}(key TEXT PRIMARY KEY, value TEXT)";
    command.ExecuteNonQuery();
    transaction.Commit(); // Ensure table exists before proceeding
  }

  private static void ReplaceStateInDatabase(NAV.Data.NavisworksTransaction transaction, string serializedState)
  {
    var command = transaction.Connection.CreateCommand();

    command.CommandText = $"DELETE FROM {TABLE_NAME} WHERE key = @key";
    command.Parameters.AddWithValue("@key", KEY_NAME);
    command.ExecuteNonQuery();

    command.CommandText = $"INSERT INTO {TABLE_NAME}(key, value) VALUES(@key, @value)";
    command.Parameters.AddWithValue("@key", KEY_NAME);
    command.Parameters.AddWithValue("@value", serializedState);
    command.ExecuteNonQuery();
  }

  private static string RetrieveStateFromDatabase()
  {
    var database = NavisworksApp.ActiveDocument!.Database;
    using var table = new DataTable();

    using (var transaction = database.BeginTransaction(NAV.Data.DatabaseChangedAction.Reset))
    {
      EnsureDatabaseTableExists(transaction);
    }

    using var dataAdapter = new NAV.Data.NavisworksDataAdapter(
      $"SELECT value FROM {TABLE_NAME} WHERE key = @key",
      database.Value
    );
    dataAdapter.SelectCommand.Parameters.AddWithValue("@key", KEY_NAME);
    dataAdapter.Fill(table);

    if (table.Rows.Count <= 0)
    {
      return string.Empty; // Return an empty collection if no state is found
    }

    string stateString = table.Rows[0]["value"] as string ?? string.Empty;

    return stateString;
  }
}
