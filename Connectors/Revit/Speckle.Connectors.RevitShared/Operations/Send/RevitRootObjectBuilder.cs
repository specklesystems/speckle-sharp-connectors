using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Revit.Operations.Send;

/// <summary>
/// Orchestrates the building of the root Speckle object from Revit documents.
/// This class is now focused solely on coordination and delegates all business logic
/// to specialized components. Clean, testable, and maintainable.
/// </summary>
public class RevitRootObjectBuilder : IRootObjectBuilder<DocumentToConvert>
{
  private readonly DocumentProcessor _documentProcessor;
  private readonly ProxyManager _proxyManager;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly IThreadContext _threadContext;
  private readonly ILogger<RevitRootObjectBuilder> _logger;

  public RevitRootObjectBuilder(
    DocumentProcessor documentProcessor,
    ProxyManager proxyManager,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    IThreadContext threadContext,
    ILogger<RevitRootObjectBuilder> logger
  )
  {
    _documentProcessor = documentProcessor;
    _proxyManager = proxyManager;
    _converterSettings = converterSettings;
    _threadContext = threadContext;
    _logger = logger;
  }

  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<DocumentToConvert> documentElementContexts,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  ) =>
    _threadContext.RunOnMainAsync(
      () => Task.FromResult(BuildSync(documentElementContexts, projectId, onOperationProgressed, ct))
    );

  /// <summary>
  /// Synchronous implementation of the build process.
  /// Pure orchestration - delegates all work to specialized components.
  /// </summary>
  private RootObjectBuilderResult BuildSync(
    IReadOnlyList<DocumentToConvert> documentElementContexts,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    try
    {
      // 1. Validate environment and setup
      ValidateEnvironment();

      // 2. Create root collection
      var rootObject = CreateRootCollection();

      // 3. Create conversion context
      var context = CreateConversionContext(projectId, rootObject, onOperationProgressed, cancellationToken);

      // 4. Process all documents (main + linked models)
      _logger.LogInformation("Starting document processing for project {ProjectId}", projectId);
      var conversionResults = _documentProcessor.ProcessDocuments(documentElementContexts, context);

      // 5. Add all proxy objects to root
      _logger.LogInformation("Adding proxies to root object");
      _proxyManager.AddAllProxies(rootObject, conversionResults);

      // 6. Return final result
      _logger.LogInformation("Root object building completed successfully");
      return new RootObjectBuilderResult(rootObject, conversionResults.AllResults);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to build root object");
      throw;
    }
  }

  /// <summary>
  /// Validates that the Revit environment is suitable for conversion.
  /// </summary>
  private void ValidateEnvironment()
  {
    var document = _converterSettings.Current.Document;

    if (document.IsFamilyDocument)
    {
      throw new SpeckleException("Family Environment documents are not supported.");
    }
  }

  /// <summary>
  /// Creates the root collection with basic metadata.
  /// </summary>
  private Collection CreateRootCollection()
  {
    var document = _converterSettings.Current.Document;
    var documentName = document.PathName.Split('\\').Last().Split('.').First();

    return new Collection(documentName) { ["units"] = _converterSettings.Current.SpeckleUnits };
  }

  /// <summary>
  /// Creates the conversion context with all necessary parameters.
  /// </summary>
  private ConversionContext CreateConversionContext(
    string projectId,
    Collection rootObject,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    bool sendWithLinkedModels = _converterSettings.Current.SendLinkedModels;

    return new ConversionContext(projectId, rootObject, sendWithLinkedModels, onOperationProgressed, cancellationToken);
  }
}
