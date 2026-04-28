using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.WriteIndented = true;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "RegistryCors",
        policy =>
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
    );
});

var app = builder.Build();
app.UseCors("RegistryCors");

var logger = app.Logger;
var configPath = builder.Configuration["Registry:DataFilePath"] ?? "Data/servers.json";
var dataFilePath = Path.IsPathRooted(configPath)
    ? configPath
    : Path.Combine(app.Environment.ContentRootPath, configPath);

IReadOnlyList<ServerResponse> registryEntries = LoadEntries(dataFilePath, logger);

app.MapGet(
    "/v0.1/servers",
    (HttpRequest request) =>
    {
        var search = request.Query["search"].ToString();
        var versionFilter = request.Query["version"].ToString();

        IEnumerable<ServerResponse> entries = registryEntries;

        if (!string.IsNullOrWhiteSpace(search))
        {
            entries = entries.Where(x =>
                x.Server.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            );
        }

        if (!string.IsNullOrWhiteSpace(versionFilter))
        {
            if (string.Equals(versionFilter, "latest", StringComparison.OrdinalIgnoreCase))
            {
                entries = LatestVersions(entries);
            }
            else
            {
                entries = entries.Where(x =>
                    string.Equals(
                        x.Server.Version,
                        versionFilter,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            }
        }

        var list = entries.ToList();

        return Results.Ok(
            new ServerListResponse
            {
                Servers = list,
                Metadata = new Metadata { Count = list.Count },
            }
        );
    }
);

app.MapGet(
    "/v0.1/servers/{*serverPath}",
    (string? serverPath) =>
    {
        if (string.IsNullOrWhiteSpace(serverPath))
        {
            return Results.NotFound(Problem("Missing server path."));
        }

        // Expected path shape: {serverName}/versions/{version}
        var marker = "/versions/";
        var markerIndex = serverPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return Results.NotFound(Problem("Expected '/versions/{version}' in request path."));
        }

        var rawServerName = serverPath[..markerIndex].Trim('/');
        var version = serverPath[(markerIndex + marker.Length)..].Trim('/');

        if (string.IsNullOrWhiteSpace(rawServerName) || string.IsNullOrWhiteSpace(version))
        {
            return Results.NotFound(Problem("Server name or version is missing."));
        }

        var decodedServerName = Uri.UnescapeDataString(rawServerName);

        var matching = registryEntries.Where(x =>
            string.Equals(x.Server.Name, decodedServerName, StringComparison.OrdinalIgnoreCase)
        );
        if (!matching.Any())
        {
            return Results.NotFound(Problem($"Server '{decodedServerName}' was not found."));
        }

        ServerResponse? selected;
        if (string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase))
        {
            selected = LatestVersions(matching).FirstOrDefault();
        }
        else
        {
            selected = matching.FirstOrDefault(x =>
                string.Equals(x.Server.Version, version, StringComparison.OrdinalIgnoreCase)
            );
        }

        if (selected is null)
        {
            return Results.NotFound(
                Problem($"Version '{version}' for server '{decodedServerName}' was not found.")
            );
        }

        return Results.Ok(selected);
    }
);

app.MapMethods("/v0.1/servers", new[] { "OPTIONS" }, () => Results.Ok());
app.MapMethods("/v0.1/servers/{*path}", new[] { "OPTIONS" }, () => Results.Ok());

app.Run();

static IReadOnlyList<ServerResponse> LoadEntries(string dataFilePath, ILogger logger)
{
    if (!File.Exists(dataFilePath))
    {
        logger.LogWarning(
            "Registry data file not found at {Path}. Returning empty registry.",
            dataFilePath
        );
        return [];
    }

    var json = File.ReadAllText(dataFilePath);
    var entries = JsonSerializer.Deserialize<List<ServerResponse>>(
        json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
    );

    return entries ?? [];
}

static IEnumerable<ServerResponse> LatestVersions(IEnumerable<ServerResponse> entries)
{
    return entries
        .GroupBy(x => x.Server.Name, StringComparer.OrdinalIgnoreCase)
        .Select(group =>
        {
            // Prefer explicit isLatest flag when present.
            var flagged = group.FirstOrDefault(x => x.Meta?.Official?.IsLatest == true);
            if (flagged is not null)
            {
                return flagged;
            }

            return group
                .OrderByDescending(x => TryParseVersion(x.Server.Version))
                .ThenByDescending(x => x.Server.Version, StringComparer.OrdinalIgnoreCase)
                .First();
        });
}

static Version TryParseVersion(string? value)
{
    return Version.TryParse(value, out var parsed) ? parsed : new Version(0, 0);
}

static ProblemDetailsBody Problem(string detail)
{
    return new ProblemDetailsBody
    {
        Type = "about:blank",
        Title = "Not Found",
        Status = 404,
        Detail = detail,
    };
}

public sealed class ServerListResponse
{
    [JsonPropertyName("servers")]
    public required List<ServerResponse> Servers { get; init; }

    [JsonPropertyName("metadata")]
    public required Metadata Metadata { get; init; }
}

public sealed class Metadata
{
    [JsonPropertyName("count")]
    public required int Count { get; init; }

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; init; }
}

public sealed class ServerResponse
{
    [JsonPropertyName("server")]
    public required ServerJson Server { get; init; }

    [JsonPropertyName("_meta")]
    public ResponseMeta? Meta { get; init; }
}

public sealed class ServerJson
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("websiteUrl")]
    public string? WebsiteUrl { get; init; }

    [JsonPropertyName("remotes")]
    public List<Transport>? Remotes { get; init; }

    [JsonPropertyName("packages")]
    public List<Package>? Packages { get; init; }
}

public sealed class Transport
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

public sealed class Package
{
    [JsonPropertyName("registryType")]
    public required string RegistryType { get; init; }

    [JsonPropertyName("registryBaseUrl")]
    public string? RegistryBaseUrl { get; init; }

    [JsonPropertyName("identifier")]
    public required string Identifier { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("runtimeHint")]
    public string? RuntimeHint { get; init; }

    [JsonPropertyName("transport")]
    public required Transport Transport { get; init; }

    [JsonPropertyName("environmentVariables")]
    public List<KeyValueInput>? EnvironmentVariables { get; init; }
}

public sealed class KeyValueInput
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("isRequired")]
    public bool? IsRequired { get; init; }

    [JsonPropertyName("isSecret")]
    public bool? IsSecret { get; init; }
}

public sealed class ResponseMeta
{
    [JsonPropertyName("io.modelcontextprotocol.registry/official")]
    public OfficialMeta? Official { get; init; }
}

public sealed class OfficialMeta
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("statusChangedAt")]
    public string? StatusChangedAt { get; init; }

    [JsonPropertyName("publishedAt")]
    public string? PublishedAt { get; init; }

    [JsonPropertyName("isLatest")]
    public bool? IsLatest { get; init; }
}

public sealed class ProblemDetailsBody
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("status")]
    public required int Status { get; init; }

    [JsonPropertyName("detail")]
    public required string Detail { get; init; }
}
