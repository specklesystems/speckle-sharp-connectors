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
/// </summary>
public class RevitRootObjectBuilder : IRootObjectBuilder<DocumentToConvert>
{
  private readonly DocumentProcessor _documentProcessor;
  private readonly ProxyManager _proxyManager;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly IThreadContext _threadContext;

  public RevitRootObjectBuilder(
    DocumentProcessor documentProcessor,
    ProxyManager proxyManager,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    IThreadContext threadContext
  )
  {
    _documentProcessor = documentProcessor;
    _proxyManager = proxyManager;
    _converterSettings = converterSettings;
    _threadContext = threadContext;
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

  private RootObjectBuilderResult BuildSync(
    IReadOnlyList<DocumentToConvert> documentElementContexts,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var document = _converterSettings.Current.Document;
    if (document.IsFamilyDocument)
    {
      throw new SpeckleException("Family Environment documents are not supported.");
    }

    var documentName = document.PathName.Split('\\').Last().Split('.').First();
    var rootObject = new Collection(documentName) { ["units"] = _converterSettings.Current.SpeckleUnits };

    var context = new ConversionContext(
      projectId,
      rootObject,
      _converterSettings.Current.SendLinkedModels,
      onOperationProgressed,
      cancellationToken
    );

    var conversionResults = _documentProcessor.ProcessDocuments(documentElementContexts, context);
    _proxyManager.AddAllProxies(rootObject, conversionResults);

    return new RootObjectBuilderResult(rootObject, conversionResults.AllResults);
  }
}
