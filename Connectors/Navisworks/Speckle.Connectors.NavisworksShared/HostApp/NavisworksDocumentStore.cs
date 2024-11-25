using System.Collections.ObjectModel;
using System.Data;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Utils;

namespace Speckle.Connector.Navisworks.HostApp;

/// <summary>
/// Manages persistence of Speckle model states within Navisworks' embedded SQLite database.
/// Provides mechanisms for reliable read/write operations with retry handling and validation.
/// </summary>
public class NavisworksDocumentStore : DocumentModelStore
{
  private const string TABLE_NAME = "speckle";
  private const string KEY_NAME = "Speckle_DUI3";

  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;

  public NavisworksDocumentStore(IJsonSerializer jsonSerializer, ITopLevelExceptionHandler topLevelExceptionHandler)
    : base(jsonSerializer, true)
  {
    _topLevelExceptionHandler = topLevelExceptionHandler;
    ReadFromFile();
  }

  public override void WriteToFile()
  {
    if (!IsActiveDocumentValid())
    {
      return;
    }

    try
    {
      SaveStateToDatabase();
    }
    catch (NAV.Data.DatabaseException ex)
    {
      _topLevelExceptionHandler.CatchUnhandled(
        () => throw new InvalidOperationException("Failed to write Speckle state to database", ex)
      );
    }
  }

  public sealed override void ReadFromFile()
  {
    if (!IsActiveDocumentValid())
    {
      Models.Clear();
      return;
    }

    try
    {
      Models = RetrieveStateFromDatabase();
    }
    catch (NAV.Data.DatabaseException ex)
    {
      Models = []; // Clear models on failure to avoid stale data
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

  private void SaveStateToDatabase()
  {
    var activeDoc = NavisworksApp.ActiveDocument;
    if (activeDoc?.Database == null)
    {
      return;
    }

    string serializedState = Serialize();
    var database = activeDoc.Database;

    using (var transaction = database.BeginTransaction(NAV.Data.DatabaseChangedAction.Reset))
    {
      EnsureDatabaseTableExists(transaction);
    }

    using (var transaction = database.BeginTransaction(NAV.Data.DatabaseChangedAction.Edited))
    {
      try
      {
        ReplaceStateInDatabase(transaction, serializedState);
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

  private ObservableCollection<ModelCard> RetrieveStateFromDatabase()
  {
    var database = NavisworksApp.ActiveDocument!.Database;
    using var table = new DataTable();

    using var dataAdapter = new NAV.Data.NavisworksDataAdapter(
      $"SELECT value FROM {TABLE_NAME} WHERE key = @key",
      database.Value
    );
    dataAdapter.SelectCommand.Parameters.AddWithValue("@key", KEY_NAME);
    dataAdapter.Fill(table);

    if (table.Rows.Count <= 0)
    {
      return []; // Return an empty collection if no state is found
    }

    string? stateString = table.Rows[0]["value"] as string;
    return !string.IsNullOrEmpty(stateString) ? Deserialize(stateString!) : [];
  }
}
