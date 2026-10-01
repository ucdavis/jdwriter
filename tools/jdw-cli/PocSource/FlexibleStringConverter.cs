using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jdw.Cli.PocSource;

/// <summary>
/// Reads a JSON value that may be a string, a boolean, a number, or null into a nullable string.
///
/// One distribution table serves six fields, three of which are string-valued (salary grade, FLSA,
/// union) and three of which are tri-state booleans (supervises, leads, works outdoors). The POC's
/// TypeScript writes real JSON booleans for the latter — `{"value": false, "count": 2}` — so a
/// plain string binding fails on 60 of the 65 profiles.
///
/// Booleans render as "true"/"false" to match the schema's stated convention. JSON null stays
/// null, and that distinction carries weight: a profile can record six JDs where the export did
/// not say alongside two that said no, and collapsing those would skew the consensus computed
/// from them.
/// </summary>
public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return reader.GetString();

            case JsonTokenType.True:
                return "true";

            case JsonTokenType.False:
                return "false";

            case JsonTokenType.Number:
                // Not expected in this data, but a number silently becoming an exception during a
                // corpus load is worse than a number becoming its literal text.
                return reader.TryGetInt64(out var l)
                    ? l.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture);

            default:
                throw new JsonException(
                    $"Cannot read a {reader.TokenType} as a distribution value.");
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value);
    }
}
