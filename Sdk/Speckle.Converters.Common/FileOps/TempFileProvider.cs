namespace Speckle.Converters.Common.FileOps;

public static class TempFileProvider // note should be in connector, and connector should nuke its folder on startup  
{
  public static string GetTempFile(string appSlug, string extension)
  {
    var folderPath = GetTempFolderPath(appSlug);
    var filePath = Path.Combine(folderPath, $"{Guid.NewGuid():N}.{extension}");
    return filePath;
  }

  public static void CleanTempFolder(string appSlug) // note, not used? 
  {
    var folderPath = GetTempFolderPath(appSlug);
    Directory.Delete(folderPath, true);
  }

  private static string GetTempFolderPath(string appSlug)
  {
    var folderPath = Path.Combine(Path.GetTempPath(), "Speckle", appSlug);
    Directory.CreateDirectory(folderPath);
    return folderPath;
  } 
}
