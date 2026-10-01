using System.Text.Json;

namespace Server.Tests;

/// <summary>
/// Loads the golden fixtures generated from the POC by `npm run fixtures`.
///
/// These are the oracle for the C# port: the TypeScript implementation is the specification,
/// and a disagreement means the port is wrong until proven otherwise. See AGENTS.md.
/// </summary>
public static class Fixtures
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static T Load<T>(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Missing fixture '{name}'. Regenerate with `npm run fixtures` in the POC repo " +
                $"and copy it to tests/server.tests/Fixtures/.", path);
        }

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
               ?? throw new InvalidOperationException($"Fixture '{name}' deserialized to null.");
    }
}
