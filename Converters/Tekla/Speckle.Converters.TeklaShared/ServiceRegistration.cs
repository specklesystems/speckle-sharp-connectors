using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Converters.TeklaShared.ToSpeckle.Helpers;
using Speckle.Converters.TeklaShared.ToSpeckle.Raw;
using Speckle.Converters.TeklaShared.ToSpeckle.TopLevel;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Tekla.Structures.Datatype;

namespace Speckle.Converters.TeklaShared;

public static class ServiceRegistration
{
  public static IServiceCollection AddTeklaConverters(this IServiceCollection serviceCollection)
  {
    var converterAssembly = Assembly.GetExecutingAssembly();

    serviceCollection.AddTransient<ModelObjectToSpeckleConverter>();

    serviceCollection.AddScoped<DisplayValueExtractor>();
    serviceCollection.AddScoped<ClassPropertyExtractor>();
    serviceCollection.AddScoped<ReportPropertyExtractor>();

    serviceCollection.AddRootCommon<TeklaRootToSpeckleConverter>(converterAssembly);
    serviceCollection.AddApplicationConverters<TeklaToSpeckleUnitConverter, Distance.UnitType>(converterAssembly);
    serviceCollection.AddScoped<
      IConverterSettingsStore<TeklaConversionSettings>,
      ConverterSettingsStore<TeklaConversionSettings>
    >();

    serviceCollection.AddTransient<ITypedConverter<TSM.BooleanPart, IEnumerable<Base>>, OpeningToSpeckleConverter>();

    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);

    return serviceCollection;
  }
}
