using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Rhino.Runtime.InProcess;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.Rhino.Internal;

internal sealed class ImportRunner : IDisposable
{
  private static readonly JsonSerializerOptions s_serializerOptions =
    new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, };

  private readonly RhinoCore _rhinoInstance;
  private readonly ImporterInstance _importer;
  private readonly ILogger<ImportRunner> _logger;

  public ImportRunner(ImporterInstance importer, ILogger<ImportRunner> logger)
  {
    _importer = importer;
    _logger = logger;
    _rhinoInstance = new RhinoCore(["/netcore-8"], WindowStyle.NoWindow);
  }

  public async Task Run(string[] args)
  {
    var a = JsonSerializer.Deserialize<ImporterArgs>(args[0], s_serializerOptions);
    var result = await TryImport(a);
    var serializedResult = JsonSerializer.Serialize(result, s_serializerOptions);
    File.WriteAllLines(a.ResultsPath, [serializedResult]);
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "IPC")]
  private async Task<ImporterResponse> TryImport(ImporterArgs args)
  {
    try
    {
      var version = await Import(args);
      return new ImporterResponse { Version = version, ErrorMessage = null };
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Import attempt failed with exception");
      return new ImporterResponse { ErrorMessage = ex.Message, Version = null };
    }
  }

  private async Task<Version> Import(ImporterArgs args)
  {
    return await _importer.RunRhinoImport(args, CancellationToken.None);
  }

  public void Dispose()
  {
    _rhinoInstance.Dispose();
  }
}
