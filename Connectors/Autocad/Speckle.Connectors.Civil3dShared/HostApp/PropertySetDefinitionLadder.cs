#if SDK_BUNDLE_VOCAB_ADDITIONS
// Whole file is gated: it references the SDK's ArtefactPropertySetField, which the pinned Speckle.Objects
// package predates. Define SDK_BUNDLE_VOCAB_ADDITIONS after the pin bump (speckle-sharp-sdk@oguzhan/
// bundle-vocab-additions); until then the file compiles to nothing and the branch builds as big-truck.
using Speckle.Sdk.Pipelines.Receive.Artifacts;

namespace Speckle.Connectors.Civil3dShared.HostApp;

/// <summary>One field of a property-set schema, host-API-free (see <see cref="PropertySetDefinitionLadder"/>).</summary>
public sealed record PropertySetFieldSchema(
  string Name,
  string? BucketId,
  string? DataType,
  string? DefaultString,
  double? DefaultDouble,
  bool? DefaultBoolean,
  string? Unit,
  string? Description
);

/// <summary>One property-set schema to recreate, host-API-free. Fields are in authored (row) order.</summary>
public sealed record PropertySetSchema(
  string SetName,
  string? SetKey,
  string? SetDescription,
  string? AppliesTo,
  IReadOnlyList<PropertySetFieldSchema> Fields
);

/// <summary>
/// The receive-side definition ladder, kept free of AutoCAD/AEC types so the tier selection and matching
/// logic is unit-testable without a running host. Tiers, most- to least-faithful:
/// <list type="number">
/// <item>the <c>eav.property_set_definitions</c> file (full fidelity: types, defaults, descriptions, bucket ids),</item>
/// <item>the legacy carrier pseudo-object (handled by the caller — this class only signals absence),</item>
/// <item>synthesis from the received objects' own value rows (unblocks producers that ship neither, e.g.
/// dwgextract today: names from path leaves, types from the value shapes, bucket ids from
/// <c>internalDefinitionName</c> — descriptions and defaults are unrecoverable).</item>
/// </list>
/// </summary>
public static class PropertySetDefinitionLadder
{
  /// <summary>Tier 1: schemas from the definitions file. Null when the bundle ships no file (→ try tier 2).
  /// Rows arrive in field order and are grouped by set_key (set_name when absent) — two same-named sets stay
  /// separate schemas here; the baker's name-keyed map takes the first and logs the collision.</summary>
  public static IReadOnlyList<PropertySetSchema>? FromSpecRows(IReadOnlyList<ArtefactPropertySetField> rows)
  {
    if (rows.Count == 0)
    {
      return null;
    }
    var bySet = new List<PropertySetSchema>();
    var index = new Dictionary<string, int>(); // set_key ?? set_name → position in bySet
    var fields = new List<List<PropertySetFieldSchema>>();
    foreach (var r in rows)
    {
      string key = string.IsNullOrEmpty(r.SetKey) ? r.SetName : r.SetKey!;
      if (!index.TryGetValue(key, out int at))
      {
        at = bySet.Count;
        index[key] = at;
        fields.Add(new List<PropertySetFieldSchema>());
        bySet.Add(new PropertySetSchema(r.SetName, r.SetKey, r.SetDescription, r.AppliesTo, fields[at]));
      }
      fields[at]
        .Add(
          new PropertySetFieldSchema(
            r.FieldName,
            r.FieldBucketId,
            r.DataType,
            r.DefaultString,
            r.DefaultDouble,
            r.DefaultBoolean,
            r.Unit,
            r.Description
          )
        );
    }
    return bySet;
  }

  /// <summary>Tier 3: synthesize minimal schemas from per-object property trees (each tree is one object's
  /// <c>properties</c> dict; the walk looks under <c>Property Sets.{set}.{field}</c>). Field order is
  /// first-seen; types are inferred from the value shape (double → Real, bool → TrueFalse, else Text — the
  /// eav ships every number as double, so Integer is unrecoverable); bucket ids come from
  /// <c>internalDefinitionName</c>. Null when no object carries any property set.</summary>
  public static IReadOnlyList<PropertySetSchema>? SynthesizeFromValues(
    IEnumerable<Dictionary<string, object?>> propertyTrees
  )
  {
    var sets = new List<PropertySetSchema>();
    var setIndex = new Dictionary<string, int>();
    var fieldLists = new List<List<PropertySetFieldSchema>>();
    var fieldIndex = new List<Dictionary<string, int>>();

    foreach (var tree in propertyTrees)
    {
      if (!tree.TryGetValue("Property Sets", out var setsObj) || setsObj is not Dictionary<string, object?> propertySets)
      {
        continue;
      }
      foreach (var setEntry in propertySets)
      {
        if (setEntry.Value is not Dictionary<string, object?> setData)
        {
          continue;
        }
        if (!setIndex.TryGetValue(setEntry.Key, out int si))
        {
          si = sets.Count;
          setIndex[setEntry.Key] = si;
          fieldLists.Add(new List<PropertySetFieldSchema>());
          fieldIndex.Add(new Dictionary<string, int>());
          sets.Add(new PropertySetSchema(setEntry.Key, null, null, null, fieldLists[si]));
        }
        foreach (var fieldEntry in setData)
        {
          object? raw = fieldEntry.Value;
          string? bucketId = null;
          string? unit = null;
          object? value = raw;
          if (raw is Dictionary<string, object?> leaf)
          {
            value = leaf.TryGetValue("value", out var v) ? v : null;
            bucketId = leaf.TryGetValue("internalDefinitionName", out var idn) ? idn as string : null;
            unit = leaf.TryGetValue("units", out var u) ? u as string : null;
          }
          string dataType = value switch
          {
            bool _ => "TrueFalse",
            double _ => "Real",
            float _ => "Real",
            int _ => "Real", // eav numbers round-trip as double; Real is the safe recreate
            long _ => "Real",
            _ => "Text",
          };
          if (!fieldIndex[si].TryGetValue(fieldEntry.Key, out int fi))
          {
            fieldIndex[si][fieldEntry.Key] = fieldLists[si].Count;
            fieldLists[si]
              .Add(new PropertySetFieldSchema(fieldEntry.Key, bucketId, dataType, null, null, null, unit, null));
          }
          else if (fieldLists[si][fi].BucketId is null && bucketId is not null)
          {
            // A later object supplied the bucket id an earlier one lacked — upgrade in place.
            fieldLists[si][fi] = fieldLists[si][fi] with { BucketId = bucketId };
          }
        }
      }
    }
    return sets.Count == 0 ? null : sets;
  }

  /// <summary>THE set_key recipe — the single implementation both send (emission) and receive (existing-def
  /// comparison) use, so the two sides can never drift. Cross-producer, byte-exact (keep in sync with
  /// dwgextract): sha256_hex_uppercase( utf8( set_name + "\n" + join("\n", for each field in AUTHORED
  /// order: field_name + "|" + data_type + "|" + (unit ?? "")) ) ). Unit is the raw captured display text —
  /// deliberately NOT "(none)"-filtered, mirroring PropertySetDefinitionHandler's capture.</summary>
  public static string ComputeSetKey(string setName, IEnumerable<(string Name, string? DataType, string? Unit)> fields)
  {
    var parts = new List<string> { setName };
    foreach (var f in fields)
    {
      parts.Add($"{f.Name}|{f.DataType}|{f.Unit}");
    }
    using var sha = System.Security.Cryptography.SHA256.Create();
    byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", parts)));
    return BitConverter.ToString(hash).Replace("-", ""); // net48-safe hex
  }

  /// <summary>The schema's identity: the shipped set_key when present (tier 1 — sender-computed with the
  /// same recipe), else computed locally (tier 3 — types are inferred there, so a mismatch against a real
  /// existing definition is expected and safely degrades to a disambiguated create).</summary>
  public static string EffectiveSetKey(PropertySetSchema schema)
  {
    if (!string.IsNullOrEmpty(schema.SetKey))
    {
      return schema.SetKey!;
    }
    var fields = new List<(string Name, string? DataType, string? Unit)>();
    foreach (var f in schema.Fields)
    {
      fields.Add((f.Name, f.DataType, f.Unit));
    }
    return ComputeSetKey(schema.SetName, fields);
  }

  /// <summary>Resolves which authored field a received value row belongs to: bucket id first (the join key
  /// the producer shipped in <c>internalDefinitionName</c>), display/field name as the fallback.</summary>
  public static string ResolveFieldName(
    string entryKey,
    string? internalDefinitionName,
    Dictionary<string, string>? bucketToFieldName
  ) =>
    internalDefinitionName is not null
    && bucketToFieldName is not null
    && bucketToFieldName.TryGetValue(internalDefinitionName, out string? mapped)
      ? mapped
      : entryKey;
}
#endif
