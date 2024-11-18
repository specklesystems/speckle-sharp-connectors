// using System.Collections.Concurrent;
// using System.Diagnostics.CodeAnalysis;
// using Speckle.Sdk.Models;
//
// namespace Speckle.Connectors.Common.Caching;
//
// public class SendConversionCache2 : ISendConversionCache
// {
//   public SendConversionCache2() { }
//
//   private class CacheNode
//   {
//     public ObjectReference Value { get; }
//     public (string applicationId, string projectId) Key { get; } // Store the key in the node
//     public CacheNode? Parent { get; }
//     public ConcurrentDictionary<string, CacheNode> Children { get; } = new();
//
//     public CacheNode(ObjectReference value, (string applicationId, string projectId) key, CacheNode? parent = null)
//     {
//       Value = value;
//       Key = key;
//       Parent = parent;
//     }
//   }
//
//   private ConcurrentDictionary<(string applicationId, string projectId), CacheNode> Cache { get; set; } = new();
//
//   public void StoreSendResult(string projectId, IReadOnlyDictionary<string, ObjectReference> convertedReferences)
//   {
//     foreach (var kvp in convertedReferences)
//     {
//       var key = (kvp.Key, projectId);
//       var node = new CacheNode(kvp.Value, key);
//
//       Cache.AddOrUpdate(key, node, (k, existingNode) => node);
//     }
//   }
//
//   public void EvictObjects(IEnumerable<string> objectIds)
//   {
//     foreach (var objectId in objectIds)
//     {
//       var keysToRemove = Cache.Keys.Where(k => k.applicationId == objectId).ToList();
//
//       foreach (var key in keysToRemove)
//       {
//         if (Cache.TryRemove(key, out var node))
//         {
//           InvalidateParent(node);
//         }
//       }
//     }
//   }
//
//   private void InvalidateParent(CacheNode node)
//   {
//     var current = node.Parent;
//     while (current != null)
//     {
//       if (current.Children.IsEmpty)
//       {
//         if (Cache.TryRemove(current.Key, out var removedNode))
//         {
//           current = removedNode.Parent;
//         }
//         else
//         {
//           break;
//         }
//       }
//       else
//       {
//         break;
//       }
//     }
//   }
//
//   public void ClearCache() => Cache.Clear();
//
//   public bool TryGetValue(
//     string projectId,
//     string applicationId,
//     [NotNullWhen(true)] out ObjectReference? objectReference
//   )
//   {
//     if (Cache.TryGetValue((applicationId, projectId), out var node))
//     {
//       objectReference = node.Value;
//       return true;
//     }
//
//     objectReference = null;
//     return false;
//   }
// }
