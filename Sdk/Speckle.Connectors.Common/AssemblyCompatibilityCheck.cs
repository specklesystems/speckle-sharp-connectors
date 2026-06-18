using System.Reflection;
using Microsoft.Extensions.Logging;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Api;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Common;

[GenerateAutoInterface]
public class AssemblyCompatibilityCheck(ILogger<AssemblyCompatibilityCheck> logger, IOperations operations)
  : IAssemblyCompatibilityCheck
{
  private readonly HashSet<string> _targets = new()
  {
    "GraphQL.Client",
    "System.Text.Json",
    "System.Text.Encodings.Web",
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
  /// conflicts in the form of <see cref="MissingMethodException"/> when calling the constructor of UTsF8JsonWriter
  /// under runtime conditions where STJ binds to a different version of System.Buffers than our SDK
  /// </summary>
  /// <remarks>
  /// We're being extra careful introducing System.Text.Json SDK changes in connectors.
  /// This check will alert us to any conflicts... and help us avoid breaking sends.
  /// We're only really concerned with this being a possibility in .NET Framework apps that don't bundle STJ themselves.
  /// If we wish, we can remove this check at a later date if we're confident there's no issues.
  /// </remarks>
  /// <returns></returns>
  public bool ValidateStjCompatibility()
  {
    try
    {
      //ensures both UTF8JsonWriter.ctor and JsonWriterHelper.NeedsEscaping are hit
      operations.SerializeNew(
        new Base()
        {
          ["abcde"] = "This is a test",
          ["\"\u003C needs\nescaping® \\\u003E\""] = "\"\u003C needs\nescaping® \\\u003E\"",
        }
      );

      using (logger.BeginScope(GenerateLoadedAssemblyReport()))
      {
        logger.LogInformation("No STJ incompatibility detected");
      }
      return true;
    }
    catch (MissingMethodException ex)
    {
      using (logger.BeginScope(GenerateLoadedAssemblyReport()))
      {
        logger.LogWarning(ex, "Dll incompatibility detected");
      }
      return false;
    }
  }

  /// <summary>
  /// Returns the names, versions, and locations of <see cref="_targets"/> dlls, dlls that our plugin depends on.
  /// This may help when troubleshoot issues with potential DLL conflicts.
  /// </summary>
  /// <returns></returns>
  public List<KeyValuePair<string, object>> GenerateLoadedAssemblyReport()
  {
    List<KeyValuePair<string, object>> assemblyInfo = new();
    int i = 0;
    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
    {
      AssemblyName n = assembly.GetName();

      if (n.Name is not null && _targets.Contains(n.Name))
      {
        assemblyInfo.Add(new($"AssemblyReport.{i}", $"{n.Name} - {n.Version}, {assembly.Location}"));
      }

      i++;
    }

    return assemblyInfo;
  }
}
