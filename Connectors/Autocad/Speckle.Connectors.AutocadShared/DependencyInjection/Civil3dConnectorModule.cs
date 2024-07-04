#if CIVIL3D
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.Autocad.Bindings;
using Speckle.Connectors.Autocad.Filters;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Operations;

namespace Speckle.Connectors.Autocad.DependencyInjection;

public class Civil3dConnectorModule : ISpeckleModule
{
  public void Load(SpeckleContainerBuilder builder)
  {
    SharedConnectorModule.LoadShared(builder);

    // Operations
    builder.AddScoped<SendOperation<AutocadRootObject>>();

    // Object Builders
    builder.AddScoped<IRootObjectBuilder<AutocadRootObject>, AutocadRootObjectBuilder>();

    // Register bindings
    builder.AddSingleton<IBinding, ConfigBinding>("connectorName", "Civil3d"); // POC: Easier like this for now, should be cleaned up later
    builder.AddSingleton<IBinding, AutocadSendBinding>();

    // register send filters
    builder.AddTransient<ISendFilter, AutocadSelectionFilter>();

    // register send conversion cache
    builder.AddSingleton<ISendConversionCache, SendConversionCache>();
  }
}
#endif
