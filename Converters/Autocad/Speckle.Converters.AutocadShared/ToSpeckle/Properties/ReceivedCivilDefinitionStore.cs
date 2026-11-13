using System.Text;

namespace Speckle.Converters.AutocadShared.ToSpeckle;

/// <summary>Source civil definitions retained with a display-only receive. Never presented as current native design.</summary>
public static class ReceivedCivilDefinitionStore
{
  private const string RECORD = "SPECKLE_RECEIVED_CIVIL_V1";
  private const int CHUNK_LENGTH = 1024;
  private const int MAX_LENGTH = 16 * 1024 * 1024;

  public static void Write(ADB.Entity entity, string json, ADB.Transaction tr)
  {
    if (json.Length > MAX_LENGTH)
    {
      throw new ArgumentException("Civil definition exceeds the 16 MiB retention limit.", nameof(json));
    }
    entity.CreateExtensionDictionary();
    var dictionary = (ADB.DBDictionary)tr.GetObject(entity.ExtensionDictionary, ADB.OpenMode.ForWrite);
    if (dictionary.Contains(RECORD))
    {
      throw new InvalidOperationException("Received civil definition already exists; it must not be overwritten silently.");
    }
    var chunks = new List<ADB.TypedValue>();
    for (int i = 0; i < json.Length; i += CHUNK_LENGTH)
    {
      chunks.Add(new ADB.TypedValue((int)ADB.DxfCode.Text, json.Substring(i, Math.Min(CHUNK_LENGTH, json.Length - i))));
    }
    using var data = new ADB.ResultBuffer(chunks.ToArray());
    var record = new ADB.Xrecord { Data = data };
    dictionary.SetAt(RECORD, record);
    tr.AddNewlyCreatedDBObject(record, true);
  }

  public static string? Read(ADB.Entity entity)
  {
    if (entity.ExtensionDictionary.IsNull)
    {
      return null;
    }
    using var tr = entity.Database.TransactionManager.StartOpenCloseTransaction();
    var dictionary = (ADB.DBDictionary)tr.GetObject(entity.ExtensionDictionary, ADB.OpenMode.ForRead);
    if (!dictionary.Contains(RECORD))
    {
      return null;
    }
    var record = (ADB.Xrecord)tr.GetObject(dictionary.GetAt(RECORD), ADB.OpenMode.ForRead);
    using var data = record.Data;
    var result = new StringBuilder();
    foreach (var value in data)
    {
      if (value.TypeCode != (int)ADB.DxfCode.Text || value.Value is not string chunk || result.Length + chunk.Length > MAX_LENGTH)
      {
        throw new InvalidOperationException("Invalid retained civil definition.");
      }
      result.Append(chunk);
    }
    return result.ToString();
  }
}
