using System.Collections.ObjectModel;
using System.Data;
using Autodesk.Navisworks.Api.Data;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Utils;

namespace Speckle.Connector.Navisworks.HostApp;

/// <summary>
/// Manages persistence of Speckle model states in Navisworks' embedded SQLite database
/// </summary>
public class NavisworksDocumentStore : DocumentModelStore
{
  // Constants for database table name, key name, and retry settings
  private const string TABLE_NAME = "speckle";
  private const string KEY_NAME = "Speckle_DUI3";
  private const int MAX_RETRIES = 3;
  private const int RETRY_DELAY_MS = 100;

  // Exception handler for capturing unhandled exceptions
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;

  /// <summary>
  /// Initialises a new instance of the NavisworksDocumentStore class
  /// </summary>
  /// <param name="jsonSerializer">JSON serializer</param>
  /// <param name="topLevelExceptionHandler">Exception handler</param>
  public NavisworksDocumentStore(IJsonSerializer jsonSerializer, ITopLevelExceptionHandler topLevelExceptionHandler)
    : base(jsonSerializer, true)
  {
    _topLevelExceptionHandler = topLevelExceptionHandler;
    ReadFromFile();
  }

  /// <summary>
  /// Attempts to safely persist current model state to Navisworks document database with retries
  /// </summary>
  public override void WriteToFile()
  {
    // Skip if document is invalid
    if (!IsDocumentValid())
    {
      return;
    }

    // Retry logic for database write operations
    for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
    {
      try
      {
        WriteStateToDatabase();
        return;
      }
      catch (DatabaseException ex)
      {
        // Handle final attempt failure
        if (attempt == MAX_RETRIES)
        {
          _topLevelExceptionHandler.CatchUnhandled(
            () => throw new InvalidOperationException("Failed to write Speckle state to database", ex)
          );
        }
        // Delay before retrying
        Thread.Sleep(RETRY_DELAY_MS);
      }
    }
  }

  /// <summary>
  /// Loads model state from Navisworks document database with retries
  /// </summary>
  public sealed override void ReadFromFile()
  {
    // Return empty model list if document is invalid
    if (!IsDocumentValid())
    {
      Models.Clear();
      return;
    }

    // Retry logic for database read operations
    for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
    {
      try
      {
        Models = ReadStateFromDatabase();
        return;
      }
      catch (DatabaseException ex)
      {
        // Handle final attempt failure
        if (attempt == MAX_RETRIES)
        {
          Models = [];
          _topLevelExceptionHandler.CatchUnhandled(
            () => throw new InvalidOperationException("Failed to read Speckle state from database", ex)
          );
        }
        // Delay before retrying
        Thread.Sleep(RETRY_DELAY_MS);
      }
    }
  }

  /// <summary>
  /// Validates that the Navisworks document and database are accessible
  /// </summary>
  private static bool IsDocumentValid()
  {
    try
    {
      var activeDoc = NavisworksApp.ActiveDocument;
      if (activeDoc == null)
      {
        return false;
      }

      // Check if we can access critical document properties
      return activeDoc.Database != null && activeDoc.Models.Count > 0 && activeDoc.ActiveSheet != null;
    }
    catch (ArgumentException)
    {
      // Handle case where document is disposed
      return false;
    }
    catch (ObjectDisposedException)
    {
      // Handle case where document is disposed
      return false;
    }
  }

  /// <summary>
  /// Serializes and writes the current model state to the database
  /// </summary>
  private void WriteStateToDatabase()
  {
    var activeDoc = NavisworksApp.ActiveDocument;
    if (activeDoc?.Database == null)
    {
      return;
    }

    // Serialize model state
    string serializedState = Serialize();
    var database = activeDoc.Database;

    // Ensure the database table exists
    using (var transaction = database.BeginTransaction(DatabaseChangedAction.Reset))
    {
      EnsureTableExists(transaction);
    }

    // Insert or update the state in the database
    using (var transaction = database.BeginTransaction(DatabaseChangedAction.Edited))
    {
      try
      {
        DeleteAndInsertState(transaction, serializedState);
        transaction.Commit();
      }
      catch
      {
        transaction.Rollback();
        throw;
      }
    }
  }

  /// <summary>
  /// Ensures the database table exists, creating it if necessary
  /// </summary>
  /// <param name="transaction">Active database transaction</param>
  private static void EnsureTableExists(NavisworksTransaction transaction)
  {
    var command = transaction.Connection.CreateCommand();
    command.CommandText = $"CREATE TABLE IF NOT EXISTS {TABLE_NAME}(key TEXT PRIMARY KEY, value TEXT)";
    command.ExecuteNonQuery();
    transaction.Commit();
  }

  /// <summary>
  /// Deletes the existing state and inserts the new serialized state into the database
  /// </summary>
  /// <param name="transaction">Active database transaction</param>
  /// <param name="serializedState">Serialized state to write</param>
  private static void DeleteAndInsertState(NavisworksTransaction transaction, string serializedState)
  {
    var command = transaction.Connection.CreateCommand();

    // Delete existing state
    command.CommandText = $"DELETE FROM {TABLE_NAME} WHERE key = @key";
    command.Parameters.AddWithValue("@key", KEY_NAME);
    command.ExecuteNonQuery();

    // Insert new state
    command.CommandText = $"INSERT INTO {TABLE_NAME}(key, value) VALUES(@key, @value)";
    command.Parameters.AddWithValue("@key", KEY_NAME);
    command.Parameters.AddWithValue("@value", serializedState);
    command.ExecuteNonQuery();
  }

  /// <summary>
  /// Reads the model state from the database
  /// </summary>
  /// <returns>Collection of model cards representing the state</returns>
  private ObservableCollection<ModelCard> ReadStateFromDatabase()
  {
    var database = NavisworksApp.ActiveDocument!.Database;
    using var table = new DataTable();

    // Execute query to fetch the serialized state
    using var dataAdapter = new NavisworksDataAdapter(
      $"SELECT value FROM {TABLE_NAME} WHERE key = @key",
      database.Value
    );
    dataAdapter.SelectCommand.Parameters.AddWithValue("@key", KEY_NAME);
    dataAdapter.Fill(table);

    // Handle missing or empty state
    if (table.Rows.Count <= 0)
    {
      return [];
    }

    string? stateString = table.Rows[0]["value"] as string;
    return !string.IsNullOrEmpty(stateString) ? Deserialize(stateString!) : [];
  }
}
