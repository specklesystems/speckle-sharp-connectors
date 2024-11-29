namespace Speckle.Connectors.CSiShared.Utils;

// NOTE: All API methods are based on the objectType and objectName, not the GUID
// We will obviously manage the GUIDs but for all method calls we need a concatenated version of the objectType and objectName
// Since objectType >= 1 and <= 7, we know first index will always be the objectType
// Remaining string represents objectName and since the user can add any string (provided it is unique), this is safer
// than using a delimiting character (which could clash with user string)
public static class ObjectIdentifier
{
  public static string Encode(int objectType, string objectName)
  {
    if (objectType < 1 || objectType > 7)
    {
      throw new ArgumentException($"Invalid object type: {objectType}. Must be between 1 and 7.");
    }
    return $"{objectType}{objectName}";
  }

  public static (int type, string name) Decode(string encodedId)
  {
    if (string.IsNullOrEmpty(encodedId) || encodedId.Length < 2)
    {
      throw new ArgumentException($"Invalid encoded ID: {encodedId}");
    }

    int objectType = int.Parse(encodedId[0].ToString());
    string objectName = encodedId[1..];

    return (objectType, objectName);
  }
}
