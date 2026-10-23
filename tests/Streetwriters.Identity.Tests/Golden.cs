using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Streetwriters.Identity.Tests;

/// <summary>
/// Snapshots of what clients and the other servers see from Identity, with values that change on
/// every run (tokens, ids, times) replaced by what kind of value they are. Recorded against
/// IdentityServer4, they are the contract any replacement must keep. Set UPDATE_GOLDEN=1 to re-record.
/// </summary>
public static partial class Golden
{
    private static readonly HashSet<string> TokenKeys = new(["access_token", "refresh_token", "id_token", "token", "accessToken", "refreshToken"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> IdKeys = new(["sub", "jti", "sid", "kid", "id", "user_id", "userId", "originTokenId"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> TimeKeys = new(["exp", "iat", "nbf", "auth_time"], StringComparer.OrdinalIgnoreCase);
    private static readonly string[] Headers = ["Content-Type", "Cache-Control", "Pragma", "WWW-Authenticate", "Location"];

    private static string Directory([CallerFilePath] string path = "") => Path.Combine(Path.GetDirectoryName(path)!, "Golden");

    public static async Task<JsonObject> SnapshotAsync(HttpResponseMessage response)
    {
        var headers = new JsonObject();
        foreach (var name in Headers)
        {
            if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values))
                headers[name] = string.Join(", ", values);
        }
        var text = await response.Content.ReadAsStringAsync();
        JsonNode? body = null;
        if (text.Length > 0)
        {
            try { body = Parse(text); }
            catch (JsonException) { body = text; }
        }
        return new JsonObject
        {
            ["status"] = (int)response.StatusCode,
            ["headers"] = headers,
            ["body"] = Normalize(body, null),
        };
    }

    /// <summary>
    /// Like <see cref="JsonNode.Parse(string, JsonNodeOptions?, JsonDocumentOptions)"/>, but keeps duplicate
    /// properties (IdentityServer4 writes some), numbering the later ones. Clients read the last one.
    /// </summary>
    private static JsonNode? Parse(string text)
    {
        using var document = JsonDocument.Parse(text);
        return Convert(document.RootElement);

        static JsonNode? Convert(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var obj = new JsonObject();
                    foreach (var property in element.EnumerateObject())
                    {
                        var name = property.Name;
                        for (var i = 2; obj.ContainsKey(name); i++) name = $"{property.Name}#{i}";
                        obj[name] = Convert(property.Value);
                    }
                    return obj;
                case JsonValueKind.Array:
                    return new JsonArray([.. element.EnumerateArray().Select(Convert)]);
                default:
                    return JsonValue.Create(element.Clone());
            }
        }
    }

    public static JsonNode? Normalize(JsonNode? node, string? key)
    {
        switch (node)
        {
            case JsonObject obj:
                var sorted = new JsonObject();
                foreach (var (k, v) in obj.OrderBy((p) => p.Key, StringComparer.Ordinal))
                    sorted[k] = Normalize(v?.DeepClone(), k);
                return sorted;
            case JsonArray array:
                return new JsonArray([.. array.Select((v) => Normalize(v?.DeepClone(), key))]);
            case JsonValue value when value.TryGetValue(out string? s):
                return NormalizeString(s, key);
            case JsonValue value when key != null && TimeKeys.Contains(key):
                return "<time>";
            default:
                return node;
        }
    }

    private static JsonNode NormalizeString(string s, string? key)
    {
        if (key != null && TokenKeys.Contains(key)) return DescribeToken(s);
        if (key != null && IdKeys.Contains(key)) return ObjectId().IsMatch(s) ? "<objectid>" : "<id>";
        if (key != null && TimeKeys.Contains(key)) return "<time>";
        // JSON serialized into a string, e.g. the data of an mfa_required error
        if (s.StartsWith('{'))
        {
            try { return new JsonObject { ["<json>"] = Normalize(JsonNode.Parse(s), null) }; }
            catch (JsonException) { }
        }
        s = Email().Replace(s, "<email>");
        s = ObjectId().Replace(s, "<objectid>");
        s = Number().Replace(s, "<n>");
        return s;
    }

    /// <summary>Opaque tokens are reduced to their shape; JWTs to their (normalized) header and claims.</summary>
    public static JsonNode DescribeToken(string token)
    {
        var parts = token.Split('.');
        if (parts.Length == 3)
        {
            return new JsonObject
            {
                ["<jwt>"] = new JsonObject
                {
                    ["header"] = Normalize(JsonNode.Parse(Base64Url(parts[0])), null),
                    ["payload"] = Normalize(JsonNode.Parse(Base64Url(parts[1])), null),
                }
            };
        }
        return "<opaque>";
    }

    private static string Base64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    public static async Task AssertMatchesAsync(string name, HttpResponseMessage response) => AssertMatches(name, await SnapshotAsync(response));

    public static void AssertMatches(string name, JsonNode actual)
    {
        var file = Path.Combine(Directory(), name + ".json");
        var text = actual.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            System.IO.Directory.CreateDirectory(Directory());
            File.WriteAllText(file, text);
            return;
        }
        Assert.True(File.Exists(file), $"No snapshot for {name}. Record it with UPDATE_GOLDEN=1.");
        Assert.Equal(File.ReadAllText(file), text);
    }

    [GeneratedRegex(@"user[0-9a-f]{32}@example\.com")]
    private static partial Regex Email();

    [GeneratedRegex("[0-9a-f]{24}")]
    private static partial Regex ObjectId();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Number();
}
