using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace BenchmarkSpider
{
    public static class JsonElementExtensions
    {
        /// <summary>
        /// Compare récursivement deux éléments JSON pour déterminer s'ils sont équivalents.
        /// </summary>
        /// <param name="a">Premier élément JSON à comparer.</param>
        /// <param name="b">Deuxième élément JSON à comparer.</param>
        /// <returns><c>true</c> si les éléments JSON sont équivalents ; sinon, <c>false</c>.</returns>
        public static bool SameJson(this JsonElement a, JsonElement b)
        {
            if (a.ValueKind == JsonValueKind.True || a.ValueKind == JsonValueKind.False)
                return a.ValueKind == b.ValueKind;
            if (a.ValueKind != b.ValueKind)
                return false;
            switch (a.ValueKind)
            {
                case JsonValueKind.Number:
                    return a.GetDouble() == b.GetDouble();
                case JsonValueKind.String:
                    return a.GetString() == b.GetString();
                case JsonValueKind.Array:
                    return a.GetArrayLength() == b.GetArrayLength()
                        && a.EnumerateArray().Zip(b.EnumerateArray()).All(p => p.First.SameJson(p.Second));
                case JsonValueKind.Object:
                    var pa = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                    var pb = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                    return pa.Count == pb.Count && pa.All(p => pb.TryGetValue(p.Key, out var v) && p.Value.SameJson(v));
                default:
                    return true;
            }
        }
    }
}
