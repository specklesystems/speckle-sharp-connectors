using Autodesk.Revit.DB;
using Speckle.Autofac.DependencyInjection;
using Speckle.Converters.Common;
using Speckle.Converters.Common.DependencyInjection;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.ToSpeckle;

namespace Speckle.Converters.RevitShared.DependencyInjection;

public class RevitConverterModule : ISpeckleModule
{
  public void Load(SpeckleContainerBuilder builder)
  {
    //register types by default
    builder.ScanAssemblyOfType<SlopeArrowExtractor>();
    // Register single root
    builder.AddRootCommon<RevitRootToSpeckleConverter>();

    // register all application converters
    builder.AddApplicationConverters<RevitToSpeckleUnitConverter, ForgeTypeId>();

    builder.AddScoped<IRootToHostConverter, RevitRootToHostConverter>();
    builder.AddSingleton(new RevitContext());

    // POC: do we need ToSpeckleScalingService as is, do we need to interface it out?
    builder.AddScoped<ScalingServiceToSpeckle>();
    builder.AddScoped<ScalingServiceToHost>();

    // POC: the concrete type can come out if we remove all the reference to it
    builder.AddScoped<IRevitConversionContextStack, RevitConversionContextStack>();

    builder.AddScoped<IReferencePointConverter, ReferencePointConverter>();
    builder.AddScoped<RevitConversionSettings>();

    builder.AddScoped<IRevitVersionConversionHelper, RevitVersionConversionHelper>();

    builder.AddScoped<ParameterValueExtractor>();
    builder.AddScoped<ParameterValueSetter>();
    builder.AddScoped<DisplayValueExtractor>();
    builder.AddScoped<HostedElementConversionToSpeckle>();
    builder.AddScoped<ParameterObjectAssigner>();
    builder.AddScoped<ISlopeArrowExtractor, SlopeArrowExtractor>();

    builder.AddScoped<IRevitCategories, RevitCategories>();
  }
}