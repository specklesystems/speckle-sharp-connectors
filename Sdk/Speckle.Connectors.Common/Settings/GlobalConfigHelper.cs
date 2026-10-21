using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Common;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace Speckle.Connectors.Common.Settings;

[GenerateAutoInterface]
public class GlobalConfigHelper(ILogger<GlobalConfigHelper>? logger) : IGlobalConfigHelper
{
  private const string REGISTRY_PATH = @"Software\Speckle\Connector Config\Global";

  /// <summary>
  /// Gets a configuration value using the priority:
  /// envvar > HKLM > HKCU > fallback
  /// </summary>
  public bool GetConfigValue(string key, [NotNullWhen(true)] out string? value)
  {
    // 1. Environment variable
    value = Environment.GetEnvironmentVariable(key);
    if (!string.IsNullOrWhiteSpace(value))
    {
      return true;
    }

#if NET5_0_OR_GREATER
    if (OperatingSystem.IsWindows())
#endif
    {
      // 2. HKEY_LOCAL_MACHINE
      value = ReadRegistry(RegistryHive.LocalMachine, REGISTRY_PATH, key);
      if (!string.IsNullOrWhiteSpace(value))
      {
        value.NotNull();
        return true;
      }

      // 3. HKEY_CURRENT_USER
      value = ReadRegistry(RegistryHive.CurrentUser, REGISTRY_PATH, key);
      if (!string.IsNullOrEmpty(value))
      {
        value.NotNull();
        return true;
      }
    }

    return false;
  }

  public bool GetConfigUrl(string key, [NotNullWhen(true)] out Uri? value)
  {
    if (!GetConfigValue(key, out string? rawValue))
    {
      value = null;
      return false;
    }

    if (!Uri.TryCreate(rawValue, UriKind.Absolute, out Uri? endpoint))
    {
      throw new UriFormatException(
        $"Invalid Url in connector configuration {key}={rawValue}, please contact your IT administrator"
      );
    }
    value = endpoint;
    return true;
  }

  public bool GetConfigBool(string key, out bool value)
  {
    if (!GetConfigValue(key, out string? rawValue))
    {
      value = default;
      return false;
    }

    value = rawValue.ToLowerInvariant() switch
    {
      "false" => false,
      "true" => true,
      _ => throw new InvalidOperationException(
        $"Invalid boolean in connector configuration {key}={rawValue}, please contact your IT administrator"
      ),
    };

    return true;
  }

#if NET5_0_OR_GREATER
  [SupportedOSPlatform("windows")]
#endif
  private string? ReadRegistry(RegistryHive hive, string path, string key)
  {
    try
    {
      using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
      using var subKey = baseKey.OpenSubKey(path);

      return subKey?.GetValue(key)?.ToString();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger?.LogWarning(ex, "Failed to read {Hive} {Path} {Key}", hive, path, key);
      // Swallow exceptions (e.g., permissions issues) and continue fallback chain
      return null;
    }
  }
}
