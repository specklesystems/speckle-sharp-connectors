using Microsoft.Extensions.Logging;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Pipelines.Send;

namespace Speckle.Connectors.Common;

[GenerateAutoInterface]
public class AssemblyCompatibilityCheck(ILogger<AssemblyCompatibilityCheck> logger) : IAssemblyCompatibilityCheck
{
  private readonly HashSet<string> _targets = new()
  {
    "GraphQL.Client",
    "System.Text.Json",
    "System.Memory",
    "System.Buffers",
    "System.Reactive",
    "System.Threading.Tasks.Extensions",
    "System.Runtime.CompilerServices.Unsafe",
    "System.Net.WebSockets.Client.Managed",
    "Microsoft.Data.Sqlite",
    "Microsoft.Bcl.AsyncInterfaces",
    "Microsoft.Extensions.DependencyInjection",
    "Microsoft.Extensions.DependencyInjection.Abstractions",
    "Microsoft.Extensions.Logging",
    "Microsoft.Extensions.Logging.Abstractions",
    "Microsoft.Extensions.ObjectPool",
  };

  /// <summary>
  /// Validates that we are able to use System.Text.Json parts of our SDK without any runtime dll
  /// conflicts in the form of <see cref="MissingMethodException"/> when calling the constructor of UTF8JsonWriter
  /// under runtime conditions where STJ binds to a different version of System.Buffers than our SDK
  /// </summary>
  /// <remarks>
  /// We're being extra careful introducing System.Text.Json SDK changes in connectors.
  /// This check will alert us to any conflicts... and help us avoid breaking sends.
  /// We're only really concerned with this being a possibility in .NET Framework apps that don't bundle STJ themselves.
  /// If we wish, we can remove this check at a later date if we're confident there's no issues.
  /// </remarks>
  /// <returns></returns>
  public bool ValidateStjCompatiblity()
  {
    try
    {
      using var t = new Utf8Json();
      var report = string.Join("\n", GenerateLoadedAssemblyReport().ToArray());
      logger.LogInformation("No STJ incompatibility detected {Report}", report);
      return true;
    }
    catch (MissingMethodException ex)
    {
      var report = string.Join("\n", GenerateLoadedAssemblyReport().ToArray());
      logger.LogWarning(ex, "Dll incompatibility detected {Report}", report);
      return false;
    }
  }

  /// <summary>
  /// Returns the names, versions, and locations of <see cref="_targets"/> dlls, dlls that our plugin depends on.
  /// This may help when troubleshoot issues with potential DLL conflicts.
  /// </summary>
  /// <returns></returns>
  public IEnumerable<string> GenerateLoadedAssemblyReport()
  {
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
    {
      var n = asm.GetName();

      if (_targets.Contains(n.Name))
      {
        yield return $"{n.Name} {n.Version} {asm.Location}";
      }
    }
  }
}
