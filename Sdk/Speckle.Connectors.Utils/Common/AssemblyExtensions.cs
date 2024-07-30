using System.Reflection;

namespace Speckle.Connectors.Utils.Common;

public static class AssemblyExtensions
{
  public static string GetVersion(this Assembly assembly)
  {
    try
    {
      var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "No version";
      if (!string.IsNullOrEmpty(informationalVersion))
      {
        return informationalVersion;
      }
    }
#pragma warning disable CA1031
    catch (Exception)
#pragma warning restore CA1031
    {
      // Note: on full .NET FX, checking the AssemblyInformationalVersionAttribute could throw an exception,
      // therefore this method uses a try/catch to make sure this method always returns a value
    }

    return assembly.GetName().Version?.ToString() ?? "No version";
  }
}
