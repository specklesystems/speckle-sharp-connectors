using Microsoft.Data.Sqlite;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.DUI.Testing;

[GenerateAutoInterface]
public class TestStorage : ITestStorage
{
  private readonly string _connectionString;

  public TestStorage(string rootPath)
  {
    _connectionString = $"Data Source={rootPath};";
    Initialize();
  }

  private void Initialize()
  {
    // NOTE: used for creating partioned object tables.
    //string[] HexChars = new string[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "a", "b", "c", "d", "e", "f" };
    //var cart = new List<string>();
    //foreach (var str in HexChars)
    //  foreach (var str2 in HexChars)
    //    cart.Add(str + str2);

    using var c = new SqliteConnection(_connectionString);
    c.Open();
    const string COMMAND_TEXT =
      @"
            CREATE TABLE IF NOT EXISTS results(
              name TEXT PRIMARY KEY,
              test TEXT,
              timestamp TEXT,
              results TEXT                                
            ) WITHOUT ROWID;
          ";
    using (var command = new SqliteCommand(COMMAND_TEXT, c))
    {
      command.ExecuteNonQuery();
    }

    // Insert Optimisations

    using SqliteCommand cmd0 = new("PRAGMA journal_mode='wal';", c);
    cmd0.ExecuteNonQuery();

    //Note / Hack: This setting has the potential to corrupt the db.
    //cmd = new SqliteCommand("PRAGMA synchronous=OFF;", Connection);
    //cmd.ExecuteNonQuery();

    using SqliteCommand cmd1 = new("PRAGMA count_changes=OFF;", c);
    cmd1.ExecuteNonQuery();

    using SqliteCommand cmd2 = new("PRAGMA temp_store=MEMORY;", c);
    cmd2.ExecuteNonQuery();

    using SqliteCommand cmd3 = new("PRAGMA mmap_size = 30000000000;", c);
    cmd3.ExecuteNonQuery();

    using SqliteCommand cmd4 = new("PRAGMA page_size = 32768;", c);
    cmd4.ExecuteNonQuery();
  }


  public IEnumerable<TestResults> GetResults(string modelName)
  {
    using var c = new SqliteConnection(_connectionString);
    c.Open();
    using var command = new SqliteCommand(@"SELECT name, test, results, timestamp
                                FROM results 
                                WHERE name = @modelName 
                                ORDER BY timestamp DESC LIMIT 1;", c);
    command.Parameters.AddWithValue("@modelName", modelName);
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
      yield return new TestResults(reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetDateTime(4));
    }
  }
  
  public void Save(TestResults results)
  {
    using var c = new SqliteConnection(_connectionString);
    c.Open();
    const string COMMAND_TEXT = @"INSERT OR IGNORE INTO results(name, test, results, timestamp) 
                                VALUES(@name, @test, @results, @timestamp)";

    using var command = new SqliteCommand(COMMAND_TEXT, c);
    command.Parameters.AddWithValue("@name", results.ModelName);
    command.Parameters.AddWithValue("@test", results.TestName);
    command.Parameters.AddWithValue("@timestamp", results.TimeStamp ?? DateTime.UtcNow);
    command.Parameters.AddWithValue("@results", results.Results);
    command.ExecuteNonQuery();
  }
}

