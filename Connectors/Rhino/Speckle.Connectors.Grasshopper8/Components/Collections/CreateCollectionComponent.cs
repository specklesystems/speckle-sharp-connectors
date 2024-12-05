using Grasshopper.Kernel;
using Speckle.Connectors.Grasshopper8.Components.BaseComponents;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.Components.Collections;

public record CreateCollectionComponentInput(
  Collection? Collection,
  string? Name,
  List<Base>? Elements,
  List<Collection>? Collections
);

public record CreateCollectionComponentOutput(Collection Collection);

public class CreateCollectionComponent
  : SpeckleTaskCapableComponent<CreateCollectionComponentInput, CreateCollectionComponentOutput>
{
  public CreateCollectionComponent()
    : base("Create Collection", "CrCol", "Creates a new collection", "Speckle", "Collections") { }

  public override Guid ComponentGuid => new("6A9EDFDE-8AC4-4E28-B455-45DF42E2172B");

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    var colIndex = pManager.AddParameter(
      new SpeckleCollectionParam(GH_ParamAccess.item),
      "collection",
      "Collection",
      "Collection",
      GH_ParamAccess.item
    );
    var nameIndex = pManager.AddTextParameter("Name", "Name", "Name of the collection", GH_ParamAccess.item);

    var elementsIndex = pManager.AddParameter(
      new SpeckleObjectParam(GH_ParamAccess.list),
      "elements",
      "Elements",
      "Elements of the collection",
      GH_ParamAccess.list
    );
    var collectionsIndex = pManager.AddParameter(
      new SpeckleCollectionParam(GH_ParamAccess.list),
      "collections",
      "Collections",
      "Sub-collections of the collection",
      GH_ParamAccess.list
    );

    pManager[colIndex].Optional = true;
    pManager[nameIndex].Optional = true;
    pManager[elementsIndex].Optional = true;
    pManager[collectionsIndex].Optional = true;
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    var colIndex = pManager.AddParameter(
      new SpeckleCollectionParam(GH_ParamAccess.item),
      "collection",
      "Collection",
      "Collection",
      GH_ParamAccess.item
    );
    var nameIndex = pManager.AddTextParameter("Name", "Name", "Name of the collection", GH_ParamAccess.item);

    var elementsIndex = pManager.AddParameter(
      new SpeckleObjectParam(GH_ParamAccess.list),
      "elements",
      "Elements",
      "Elements of the collection",
      GH_ParamAccess.list
    );
    var collectionsIndex = pManager.AddParameter(
      new SpeckleCollectionParam(GH_ParamAccess.list),
      "collections",
      "Collections",
      "Sub-collections of the collection",
      GH_ParamAccess.list
    );

    pManager[colIndex].Optional = true;
    pManager[nameIndex].Optional = true;
    pManager[elementsIndex].Optional = true;
    pManager[collectionsIndex].Optional = true;
  }

  protected override CreateCollectionComponentInput GetInput(IGH_DataAccess da)
  {
    Collection? collection = null;
    string? name = "";
    List<Base>? elements = new List<Base>();
    List<Collection>? collections = new List<Collection>();

    da.GetData(0, ref collection);
    da.GetData(1, ref name);
    da.GetDataList(2, elements);
    da.GetDataList(3, collections);

    return new CreateCollectionComponentInput(collection, name, elements, collections);
  }

  protected override void SetOutput(IGH_DataAccess da, CreateCollectionComponentOutput result)
  {
    da.SetData(0, result.Collection);
    da.SetData(1, result.Collection.name);
    da.SetDataList(2, result.Collection.elements.Where(e => e is not Collection));
    da.SetDataList(3, result.Collection.elements.Where(e => e is Collection));
  }

  protected override Task<CreateCollectionComponentOutput> PerformTask(
    CreateCollectionComponentInput input,
    CancellationToken cancellationToken = default
  )
  {
    if (input.Collection is null)
    {
      // Create new collection
      if (input.Name is null)
      {
        throw new SpeckleException("New collections must have a name");
      }

      var collection = new Collection(input.Name) { elements = input.Elements ?? new List<Base>() };
      var result = new CreateCollectionComponentOutput(collection);

      return Task.FromResult(result);
    }
    else
    {
      var collection = new Collection(input.Collection.name) { elements = input.Collection.elements };

      // Create new collection
      if (input.Name is not null && input.Name.Length != 0)
      {
        collection.name = input.Name;
      }
      var elements = new List<Base>();
      if (input.Elements is not null && input.Elements.Count != 0)
      {
        elements.AddRange(input.Elements);
      }
      else
      {
        elements.AddRange(collection.elements.Where(e => e is not Collection));
      }

      if (input.Collections is not null && input.Collections.Count != 0)
      {
        elements.AddRange(input.Collections);
      }
      else
      {
        elements.AddRange(collection.elements.Where(e => e is Collection));
      }

      var result = new CreateCollectionComponentOutput(collection);

      return Task.FromResult(result);
    }
  }
}
