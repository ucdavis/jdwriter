using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Server.Core.Ai;

/// <summary>
/// Derives a JSON Schema from a C# type, so the type stays the single source of truth for a
/// structured-output call.
///
/// This is the Zod-parity piece. In the POC every call passed a Zod schema, which gave three
/// things at once: the wire schema, the parsed result type, and per-field descriptions. The C# SDK
/// takes a raw JSON Schema dictionary and has no equivalent, so without this each call site would
/// hand-maintain a schema alongside its POCO and the two would drift.
///
/// Three transformations are applied on top of what <see cref="JsonSchemaExporter"/> emits:
///
///   1. <c>additionalProperties: false</c> on every object, so the model cannot invent fields.
///   2. <c>required</c> listing every non-nullable property. The exporter does not do this, and a
///      schema without it lets the model omit fields that the deserializer then leaves at default —
///      a silently truncated result rather than an error.
///   3. <c>[Description]</c> attributes copied onto the property schema. These carry real
///      prompt-engineering weight: they are the ported text of Zod's <c>.describe()</c> calls, which
///      is where instructions like "across all responsibilities these must sum to 100" live.
///      Dropping them would change model behavior while every type still compiled.
/// </summary>
public static class StructuredSchema
{
    // Web defaults give camelCase, matching the property names the POC's Zod schemas produced.
    // The wire contract is those names, so this is not cosmetic.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // Required explicitly: the schema exporter marks the options read-only, and an instance
        // without a resolver throws at that point rather than when it was configured. Left
        // implicit, whether it worked depended on which call touched the options first.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static readonly ConcurrentDictionary<Type, JsonNode> Cache = new();

    /// <summary>The serializer options a response must be deserialized with to match the schema.</summary>
    public static JsonSerializerOptions SerializerOptions => Options;

    public static JsonNode For<T>() => For(typeof(T));

    public static JsonNode For(Type type) =>
        // Deep-cloned on the way out: callers hand this to the SDK, which may mutate or take
        // ownership, and a shared mutated schema would corrupt every later call for that type.
        Cache.GetOrAdd(type, static t =>
        {
            var exported = Options.GetJsonSchemaAsNode(t, new JsonSchemaExporterOptions
            {
                // A non-nullable reference type means required. Without this every property is
                // treated as nullable and nothing lands in `required`.
                TreatNullObliviousAsNonNullable = true,
                TransformSchemaNode = Transform,
            });
            return InlineRefs(exported, exported, 0)!;
        }).DeepClone();

    /// <summary>
    /// Replace every local <c>$ref</c> with a copy of the schema it points at.
    ///
    /// The exporter writes the second use of a type as a pointer to its first occurrence —
    /// <c>{"$ref": "#/properties/functionGroups/items"}</c> — and the API rejects any reference
    /// that is not under <c>$defs</c>. That rejection was caught by ingest's deterministic
    /// fallback, so consolidation and envelope synthesis silently never ran: every ingested class
    /// got a deterministic envelope, with only a log warning to say so. Inlining gives the API a
    /// self-contained schema it accepts, and leaves the exporter's output otherwise untouched.
    /// </summary>
    private static JsonNode? InlineRefs(JsonNode? node, JsonNode root, int refDepth)
    {
        if (node is JsonObject obj)
        {
            if (obj["$ref"] is JsonValue refValue
                && refValue.TryGetValue<string>(out var pointer)
                && pointer.StartsWith("#", StringComparison.Ordinal))
            {
                // A structured-output type that contains itself has no finite inline form.
                if (refDepth >= 32)
                {
                    throw new InvalidOperationException(
                        $"Schema reference {pointer} is recursive; structured output types must not contain themselves.");
                }

                var target = Resolve(root, pointer)
                             ?? throw new InvalidOperationException($"Schema reference {pointer} does not resolve.");
                var inlined = InlineRefs(target, root, refDepth + 1);
                if (inlined is JsonObject inlinedObj)
                {
                    // Keywords beside the $ref (a description, say) still apply.
                    foreach (var (key, value) in obj)
                    {
                        if (key != "$ref")
                        {
                            inlinedObj[key] = InlineRefs(value, root, refDepth);
                        }
                    }
                }

                return inlined;
            }

            var copy = new JsonObject();
            foreach (var (key, value) in obj)
            {
                copy[key] = InlineRefs(value, root, refDepth);
            }

            return copy;
        }

        if (node is JsonArray array)
        {
            var copy = new JsonArray();
            foreach (var item in array)
            {
                copy.Add(InlineRefs(item, root, refDepth));
            }

            return copy;
        }

        return node?.DeepClone();
    }

    /// <summary>A JSON Pointer ("#/a/b/0") resolved against the schema root.</summary>
    private static JsonNode? Resolve(JsonNode root, string pointer)
    {
        JsonNode? current = root;
        foreach (var raw in pointer.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = raw.Replace("~1", "/").Replace("~0", "~");
            current = current switch
            {
                JsonObject o => o[segment],
                JsonArray a when int.TryParse(segment, out var i) && i < a.Count => a[i],
                _ => null,
            };
            if (current == null)
            {
                return null;
            }
        }

        return current;
    }

    private static JsonNode Transform(JsonSchemaExporterContext context, JsonNode schema)
    {
        if (schema is not JsonObject obj)
        {
            return schema;
        }

        // Per-property description, from [Description] on the property or its type.
        var description = DescriptionOf(context);
        if (description is not null && !obj.ContainsKey("description"))
        {
            obj["description"] = description;
        }

        if (obj["properties"] is JsonObject properties)
        {
            // Closed objects. An open schema lets the model return extra keys that deserialize to
            // nothing, which looks like success and loses data.
            obj["additionalProperties"] = false;

            // Everything the exporter did not mark nullable is required. The model must return the
            // whole shape or fail loudly, rather than omitting a field that silently defaults.
            var required = new JsonArray();
            foreach (var (name, value) in properties)
            {
                if (!IsNullable(value))
                {
                    required.Add(name);
                }
            }

            if (required.Count > 0)
            {
                obj["required"] = required;
            }
        }

        return obj;
    }

    /// <summary>
    /// A property the exporter considered nullable shows up either as <c>"type": ["x", "null"]</c>
    /// or as a union containing a null branch.
    /// </summary>
    private static bool IsNullable(JsonNode? schema)
    {
        if (schema is not JsonObject obj)
        {
            return false;
        }

        // `type` is either a single string or an array of them. Reading the array form as a
        // string throws, so each shape is handled explicitly rather than falling through.
        return obj["type"] switch
        {
            JsonArray types => types.Any(t => t is JsonValue v && v.TryGetValue<string>(out var s) && s == "null"),
            JsonValue value => value.TryGetValue<string>(out var single) && single == "null",
            _ => false,
        };
    }

    private static string? DescriptionOf(JsonSchemaExporterContext context)
    {
        // Property-level first: that is where the ported .describe() text lives.
        var fromProperty = context.PropertyInfo?.AttributeProvider
            ?.GetCustomAttributes(typeof(DescriptionAttribute), inherit: true)
            .OfType<DescriptionAttribute>()
            .FirstOrDefault()?.Description;

        if (!string.IsNullOrEmpty(fromProperty))
        {
            return fromProperty;
        }

        return context.TypeInfo.Type
            .GetCustomAttributes(typeof(DescriptionAttribute), inherit: true)
            .OfType<DescriptionAttribute>()
            .FirstOrDefault()?.Description;
    }

    /// <summary>
    /// The schema as the SDK wants it: a dictionary of top-level keyword to value.
    /// </summary>
    public static Dictionary<string, JsonElement> AsDictionary(Type type)
    {
        var node = For(type);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        if (node is JsonObject obj)
        {
            foreach (var (key, value) in obj)
            {
                result[key] = JsonSerializer.Deserialize<JsonElement>(
                    value?.ToJsonString() ?? "null", Options);
            }
        }

        return result;
    }
}
