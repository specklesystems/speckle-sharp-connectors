using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Microsoft.Extensions.Logging;
using Speckle.Sdk;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>What a previous receive of one model left behind in this document.</summary>
/// <param name="Storage">The <see cref="DataStorage"/> element the record lives on, so the caller can update it in
/// place instead of accumulating one per receive.</param>
/// <param name="GroupUniqueId">UniqueId of the top-level Group the bake was collected into, or null when the bake
/// predates grouping (or grouping failed).</param>
public sealed record RevitReceiveRecord(DataStorage Storage, string? GroupUniqueId);

/// <summary>
/// The per-model receive manifest: a hidden <see cref="DataStorage"/> element carrying, in Extensible Storage, what
/// the last receive of one model put into this document — its top-level Group.
/// </summary>
/// <remarks>
/// <para>This is the tracking key that replaces the <c>Comments</c> parameter marker [ENG-8805]. Two properties matter:
/// it is <b>not user-facing</b> (Comments is user-visible, schedulable and commonly edited — an edit used to orphan the
/// element and duplicate it on the next receive), and it is <b>rename-proof</b> — lookup prefers the project/model
/// <i>ids</i>, so renaming either on the web no longer strands the prior bake.</para>
/// <para>Lookup falls back to the marker name (<c>Project {name}: Model {name}</c>) when ids are unavailable: the v1
/// <c>IHostObjectBuilder</c> path has no ids to pass, and records written by earlier builds carry none.</para>
/// <para>Element tracking itself stays with the Group, exactly as in v1 — a manifest listing every baked element would
/// mean a multi-megabyte entity on a large model for no gain over <c>PurgeGroups</c>. The known consequence, inherited
/// from v1: if a user explicitly ungroups a received model, its elements are no longer tracked and the next receive
/// bakes alongside them.</para>
/// <para>Every operation is defensive — a manifest failure degrades cleanup to the name-based fallbacks rather than
/// costing the user their receive.</para>
/// </remarks>
public class RevitReceiveManifest
{
  // Stable across versions — changing it strands every manifest already written into user documents.
  private static readonly Guid s_schemaGuid = new("8f3d5c21-9b74-4f0e-9a6d-2c1e7b4a3d58");
  private const string SCHEMA_NAME = "SpeckleReceiveManifest";
  private const string FIELD_PROJECT_ID = "projectId";
  private const string FIELD_MODEL_ID = "modelId";
  private const string FIELD_MARKER = "marker";
  private const string FIELD_GROUP = "groupUniqueId";

  // Reserved. Materials are deliberately never purged (see RevitMaterialBaker), so nothing is recorded here — but the
  // field stays in the schema, written empty, because the schema shape must keep matching records already in the wild.
  private const string FIELD_MATERIALS = "materialUniqueIds";

  private readonly ILogger<RevitReceiveManifest> _logger;

  public RevitReceiveManifest(ILogger<RevitReceiveManifest> logger)
  {
    _logger = logger;
  }

  /// <summary>The record left by the previous receive of this model, or null if there is none (or it can't be read).
  /// Matches on (projectId, modelId) when both are supplied, else on <paramref name="marker"/>.</summary>
  public RevitReceiveRecord? Find(Document doc, string? projectId, string? modelId, string marker)
  {
    try
    {
      // GetOrCreate, not Lookup: a schema is only registered in the session once someone builds it, so a plain
      // Lookup can miss records written by an earlier session — and a miss here would silently orphan the prior
      // bake and mint a second DataStorage on write. Building it needs no transaction.
      var schema = GetOrCreateSchema();

      bool byId = !string.IsNullOrEmpty(projectId) && !string.IsNullOrEmpty(modelId);
      using var collector = new FilteredElementCollector(doc);
      foreach (var element in collector.OfClass(typeof(DataStorage)))
      {
        if (element is not DataStorage storage)
        {
          continue;
        }

        var entity = storage.GetEntity(schema);
        if (entity is null || !entity.IsValid())
        {
          continue;
        }

        // Ids win when we have them (rename-proof); the marker is the fallback for id-less records and the v1 path.
        bool match = byId
          ? string.Equals(entity.Get<string>(FIELD_PROJECT_ID), projectId, StringComparison.Ordinal)
            && string.Equals(entity.Get<string>(FIELD_MODEL_ID), modelId, StringComparison.Ordinal)
          : string.Equals(entity.Get<string>(FIELD_MARKER), marker, StringComparison.Ordinal);

        if (!match)
        {
          continue;
        }

        var group = entity.Get<string>(FIELD_GROUP);
        return new RevitReceiveRecord(storage, string.IsNullOrEmpty(group) ? null : group);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Could not read the Speckle receive manifest; falling back to name-based cleanup");
    }

    return null;
  }

  /// <summary>Records what this receive produced, updating <paramref name="existing"/> in place when the previous
  /// receive left one. Must be called inside an open transaction.</summary>
  public void Write(
    Document doc,
    DataStorage? existing,
    string? projectId,
    string? modelId,
    string marker,
    string? groupUniqueId
  )
  {
    try
    {
      var schema = GetOrCreateSchema();
      // DataStorage is a document element, owned by the document — same non-disposal as View3D in RevitViewBaker.
#pragma warning disable CA2000
      var storage = existing is { IsValidObject: true } ? existing : DataStorage.Create(doc);
#pragma warning restore CA2000

      var entity = new Entity(schema);
      entity.Set(FIELD_PROJECT_ID, projectId ?? string.Empty);
      entity.Set(FIELD_MODEL_ID, modelId ?? string.Empty);
      entity.Set(FIELD_MARKER, marker);
      entity.Set(FIELD_GROUP, groupUniqueId ?? string.Empty);
      entity.Set(FIELD_MATERIALS, string.Empty);
      storage.SetEntity(entity);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // A missing manifest costs cleanup precision on the next receive, not this receive's geometry.
      _logger.LogWarning(ex, "Could not write the Speckle receive manifest for '{Marker}'", marker);
    }
  }

  private static Schema GetOrCreateSchema()
  {
    var existing = Schema.Lookup(s_schemaGuid);
    if (existing is not null)
    {
      return existing;
    }

    using var builder = new SchemaBuilder(s_schemaGuid);
    builder.SetSchemaName(SCHEMA_NAME);
    builder.SetVendorId("SPKL");
    builder.SetReadAccessLevel(AccessLevel.Public);
    builder.SetWriteAccessLevel(AccessLevel.Public);
    builder.AddSimpleField(FIELD_PROJECT_ID, typeof(string));
    builder.AddSimpleField(FIELD_MODEL_ID, typeof(string));
    builder.AddSimpleField(FIELD_MARKER, typeof(string));
    builder.AddSimpleField(FIELD_GROUP, typeof(string));
    builder.AddSimpleField(FIELD_MATERIALS, typeof(string));
    return builder.Finish();
  }
}
