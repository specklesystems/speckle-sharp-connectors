using System.Reflection;

namespace Speckle.Connectors.Logging.Internal;

/// <summary>
/// Helper class dedicated for Speckle specific Path operations.
/// </summary>
internal static class SpecklePathProvider
{
  private const string APPLICATION_NAME = "Speckle";

  private const string LOG_FOLDER_NAME = "Logs";

  private static string UserDataPathEnvVar => "SPECKLE_USERDATA_PATH";
  private static string? Path => Environment.GetEnvironmentVariable(UserDataPathEnvVar);

  /// <summary>
  /// Get the installation path.
  /// </summary>
  public static string InstallApplicationDataPath =>
    Assembly.GetExecutingAssembly().Location.Contains("ProgramData")
      ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
      : UserApplicationDataPath();

  /// <summary>
  /// Get the folder where the user's Speckle data should be stored.
  /// </summary>
  public static string UserSpeckleFolderPath => EnsureFolderExists(UserApplicationDataPath(), APPLICATION_NAME);

  /// <summary>
  /// Get the platform specific user configuration folder path.<br/>
  /// will be the <see cref="Environment.SpecialFolder.ApplicationData"/> path e.g.:
  /// In cases such as linux servers where the above path is not permissive, we will fall back to <see cref="Environment.SpecialFolder.UserProfile"/>
  /// </summary>
  /// <remarks>
  /// <see cref="Environment.SpecialFolder.ApplicationData"/> path usually maps to
  /// <ul>
  ///   <li>win: <c>%appdata%/</c></li>
  ///   <li>MacOS: <c>~/.config/</c></li>
  ///   <li>Linux: <c>~/.config/</c></li>
  /// </ul>
  /// </remarks>
  /// <exception cref="PlatformNotSupportedException">Both <see cref="Environment.SpecialFolder.ApplicationData"/> and <see cref="Environment.SpecialFolder.UserProfile"/> paths are inaccessible</exception>
  public static string UserApplicationDataPath()
  {
    // if we have an override, just return that
    var pathOverride = Path;
    if (pathOverride != null && !string.IsNullOrEmpty(pathOverride))
    {
      return pathOverride;
    }

    // on desktop linux and macos we use the appdata.
    // but we might not have write access to the disk
    // so the catch falls back to the user profile
    try
    {
      return Environment.GetFolderPath(
        Environment.SpecialFolder.ApplicationData,
        // if the folder doesn't exist, we get back an empty string on OSX,
        // which in turn, breaks other stuff down the line.
        // passing in the Create option ensures that this directory exists,
        // which is not a given on all OS-es.
        Environment.SpecialFolderOption.Create
      );
    }
    catch (SystemException ex) when (ex is PlatformNotSupportedException or ArgumentException)
    {
      //Adding this log just so we confidently know which Exception type to catch here.
      // TODO: Must re-add log call when (and if) this get's made as a service
      //SpeckleLog.Logger.Warning(ex, "Falling back to user profile path");

      // on server linux, there might not be a user setup, things can run under root
      // in that case, the appdata variable is most probably not set up
      // we fall back to the value of the home folder
      return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
  }

  private static string EnsureFolderExists(params string[] folderName)
  {
    var path = System.IO.Path.Combine(folderName);
    Directory.CreateDirectory(path);
    return path;
  }

  internal static string LogFolderPath(string applicationAndVersion) =>
    EnsureFolderExists(UserSpeckleFolderPath, LOG_FOLDER_NAME, applicationAndVersion);
}
