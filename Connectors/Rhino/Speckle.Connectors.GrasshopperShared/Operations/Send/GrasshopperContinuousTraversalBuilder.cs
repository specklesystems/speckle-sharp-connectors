using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Pipelines.Send;
using DataObject = Speckle.Objects.Data.DataObject;

namespace Speckle.Connectors.GrasshopperShared.Operations.Send;

/// <summary>
/// Continuous traversal builder for Grasshopper that processes each object through the <see cref="SendPipeline"/>
/// as it unwraps. This enables the packfile send path (streaming objects to S3 during build).
/// </summary>
public class GrasshopperContinuousTraversalBuilder(
  IInstanceObjectsManager<SpeckleGeometryWrapper, List<string>> instanceObjectsManager
) : IRootContinuousTraversalBuilder<SpeckleCollectionWrapperGoo>
{
  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<SpeckleCollectionWrapperGoo> objects,
    string projectId,
    SendPipeline sendPipeline,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    // create root collection
    var rootCollectionGoo = (SpeckleRootCollectionWrapperGoo)objects[0].Duplicate();
    rootCollectionGoo.Value.Name = "Grasshopper Model";
    RootCollection rootCollection =
      new(rootCollectionGoo.Value.Name)
      {
        applicationId = rootCollectionGoo.Value.ApplicationId,
        properties = rootCollectionGoo.Value.Properties ?? new()
      };

    // create packers for colors and render materials
    GrasshopperColorPacker colorPacker = new();
    GrasshopperMaterialPacker materialPacker = new();
    GrasshopperBlockPacker blockPacker = new(instanceObjectsManager);

    // unwrap the input collection, processing each object through the send pipeline
    await Unwrap(
        rootCollectionGoo.Value,
        rootCollection,
        colorPacker,
        materialPacker,
        blockPacker,
        sendPipeline,
        cancellationToken
      )
      .ConfigureAwait(false);

    // add proxies
    rootCollection[ProxyKeys.COLOR] = colorPacker.ColorProxies.Values.ToList();
    rootCollection[ProxyKeys.RENDER_MATERIAL] = materialPacker.RenderMaterialProxies.Values.ToList();
    rootCollection[ProxyKeys.INSTANCE_DEFINITION] = blockPacker.InstanceDefinitionProxies.Values.ToList();

    // process the root collection through the pipeline and wait for all uploads
    await sendPipeline.Process(rootCollection).ConfigureAwait(false);
    await sendPipeline.WaitForUpload().ConfigureAwait(false);

    // TODO: Not getting any conversion results yet
    return new RootObjectBuilderResult(rootCollection, []);
  }

  private async Task Unwrap(
    SpeckleCollectionWrapper wrapper,
    Collection targetCollection,
    GrasshopperColorPacker colorPacker,
    GrasshopperMaterialPacker materialPacker,
    GrasshopperBlockPacker blockPacker,
    SendPipeline sendPipeline,
    CancellationToken cancellationToken
  )
  {
    colorPacker.ProcessColor(wrapper.ApplicationId, wrapper.Color);
    materialPacker.ProcessMaterial(wrapper.ApplicationId, wrapper.Material);

    int skippedNulls = 0;

    foreach (ISpeckleCollectionObject? element in wrapper.Elements)
    {
      cancellationToken.ThrowIfCancellationRequested();

      switch (element)
      {
        case null:
          skippedNulls++;
          continue;

        case SpeckleCollectionWrapper collWrapper:
          collWrapper.ApplicationId ??= collWrapper.GetSpeckleApplicationId();
          targetCollection.elements.Add(collWrapper.Collection);
          await Unwrap(
              collWrapper,
              collWrapper.Collection,
              colorPacker,
              materialPacker,
              blockPacker,
              sendPipeline,
              cancellationToken
            )
            .ConfigureAwait(false);
          break;

        case SpeckleGeometryWrapper so:
          Base objectBase = UnwrapGeometry(so);
          string applicationId = objectBase.applicationId!;

          // NOTE: This is how it differentiate from 'GrasshopperSendOperation'
          // It process through send pipeline before adding to collection
          var reference = await sendPipeline.Process(objectBase).ConfigureAwait(false);
          targetCollection.elements.Add(reference);

          if (so is SpeckleBlockInstanceWrapper blockInstance)
          {
            await ProcessBlockInstanceDefinition(
                blockInstance,
                colorPacker,
                materialPacker,
                blockPacker,
                targetCollection,
                sendPipeline,
                cancellationToken
              )
              .ConfigureAwait(false);
          }

          colorPacker.ProcessColor(applicationId, so.Color);
          materialPacker.ProcessMaterial(applicationId, so.Material);
          break;

        case SpeckleDataObjectWrapper dataObjectWrapper:
          DataObject dataObject = UnwrapDataObject(dataObjectWrapper, colorPacker, materialPacker);

          // process data object through send pipeline
          var dataRef = await sendPipeline.Process(dataObject).ConfigureAwait(false);
          targetCollection.elements.Add(dataRef);
          break;
      }
    }

    // clear topology when nulls are present (CNX-2855)
    if (skippedNulls > 0)
    {
      targetCollection[Constants.TOPOLOGY_PROP] = null;
    }
  }

  private Base UnwrapGeometry(SpeckleGeometryWrapper wrapper)
  {
    Dictionary<string, object?> props = [];
    Base baseObject = wrapper.Base;
    if (wrapper.Properties.CastTo(ref props))
    {
      baseObject["properties"] = props;
    }

    return baseObject;
  }

  private async Task ProcessBlockInstanceDefinition(
    SpeckleBlockInstanceWrapper blockInstance,
    GrasshopperColorPacker colorPacker,
    GrasshopperMaterialPacker materialPacker,
    GrasshopperBlockPacker blockPacker,
    Collection currentColl,
    SendPipeline sendPipeline,
    CancellationToken cancellationToken
  )
  {
    cancellationToken.ThrowIfCancellationRequested();

    var definitionObjects = blockPacker.ProcessInstance(blockInstance);

    if (definitionObjects != null)
    {
      foreach (var definitionObject in definitionObjects)
      {
        Base defObjectBase = UnwrapGeometry(definitionObject);
        string applicationId = defObjectBase.applicationId!;

        var reference = await sendPipeline.Process(defObjectBase).ConfigureAwait(false);
        currentColl.elements.Add(reference);

        colorPacker.ProcessColor(applicationId, definitionObject.Color);
        materialPacker.ProcessMaterial(applicationId, definitionObject.Material);
      }
    }
  }

  private DataObject UnwrapDataObject(
    SpeckleDataObjectWrapper wrapper,
    GrasshopperColorPacker colorPacker,
    GrasshopperMaterialPacker materialPacker
  )
  {
    DataObject dataObject = wrapper.DataObject;

    var displayValue = new List<Base>();
    foreach (var geometryWrapper in wrapper.Geometries)
    {
      Base geometryBase = UnwrapGeometry(geometryWrapper);
      displayValue.Add(geometryBase);

      if (geometryWrapper.ApplicationId != null)
      {
        colorPacker.ProcessColor(geometryWrapper.ApplicationId, geometryWrapper.Color);
        materialPacker.ProcessMaterial(geometryWrapper.ApplicationId, geometryWrapper.Material);
      }
    }

    dataObject.displayValue = displayValue;

    return dataObject;
  }
}
