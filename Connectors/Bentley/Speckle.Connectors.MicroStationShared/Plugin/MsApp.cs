using System.Runtime.InteropServices;

namespace Speckle.Connectors.MicroStation.Plugin;

/// <summary>
/// Provides cached access to the running MicroStation 2026 COM Application object.
/// MicroStation registers itself as a COM server (ProgID "MicroStationDGN.Application")
/// when it starts; we obtain the running instance via <see cref="Marshal.GetActiveObject"/>.
/// </summary>
internal static class MsApp
{
  private static Application? s_instance;

  /// <summary>Gets the running MicroStation application. Throws if MicroStation is not running.</summary>
  public static Application Instance =>
    s_instance ??= (Application)Marshal.GetActiveObject("MicroStationDGN.Application");

  /// <summary>Returns the application instance or null if MicroStation is not available.</summary>
  public static Application? TryGetInstance()
  {
    if (s_instance != null)
    {
      return s_instance;
    }

    try
    {
      s_instance = (Application)Marshal.GetActiveObject("MicroStationDGN.Application");
      return s_instance;
    }
    catch (COMException)
    {
      return null;
    }
  }

  /// <summary>Returns the active model or null when no file is open.</summary>
  public static ModelReference? ActiveModel
  {
    get
    {
      var app = TryGetInstance();
      return app?.HasActiveModelReference == true ? app.ActiveModelReference : null;
    }
  }
}
