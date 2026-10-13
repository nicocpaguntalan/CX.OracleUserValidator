using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var usersDirectory = Path.Combine(AppContext.BaseDirectory, "Users");

var environment = args.Length > 0
    ? args[0]
    : Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Test";

var baseUrl = LoadBaseUrl(AppContext.BaseDirectory, environment);
if (string.IsNullOrWhiteSpace(baseUrl))
{
    Console.Error.WriteLine("OracleCrmApi:BaseUrl is not configured.");
    return 1;
}

if (!Directory.Exists(usersDirectory))
{
    Console.Error.WriteLine($"Users directory not found: {usersDirectory}");
    return 1;
}

var inputFiles = Directory.GetFiles(usersDirectory, "*.csv")
    .Where(f => !Path.GetFileName(f).StartsWith("UserValidationResults-", StringComparison.OrdinalIgnoreCase))
    .ToArray();

if (inputFiles.Length == 0)
{
    Console.Error.WriteLine($"No user CSV files found in {usersDirectory}");
    return 1;
}

var users = inputFiles.SelectMany(ReadUsers).ToList();
Console.WriteLine($"Environment: {environment}");
Console.WriteLine($"Base URL:    {baseUrl}");
Console.WriteLine($"Users found: {users.Count}");
Console.WriteLine();

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
    Timeout = TimeSpan.FromSeconds(60)
};
httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

var results = new List<ValidationResult>(users.Count);
foreach (var user in users)
{
    var result = await ValidateUserAsync(httpClient, user);
    results.Add(result);
    Console.WriteLine($"{result.Username,-50} {result.StatusCode,5} {result.Status}");
}

var outputPath = Path.Combine(usersDirectory, $"UserValidationResults-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
WriteResults(outputPath, results);

Console.WriteLine();
Console.WriteLine($"Has access:   {results.Count(r => r.Status == ValidationResult.HasAccess)}");
Console.WriteLine($"Unauthorized: {results.Count(r => r.Status == ValidationResult.Unauthorized)}");
Console.WriteLine($"Other:        {results.Count(r => r.Status is not (ValidationResult.HasAccess or ValidationResult.Unauthorized))}");
Console.WriteLine($"Results written to: {outputPath}");
return 0;

static string? LoadBaseUrl(string directory, string environment)
{
    string? baseUrl = null;
    foreach (var fileName in new[] { "appsettings.json", $"appsettings.{environment}.json" })
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            continue;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.TryGetProperty("OracleCrmApi", out var api) &&
            api.TryGetProperty("BaseUrl", out var url) &&
            url.ValueKind == JsonValueKind.String)
        {
            baseUrl = url.GetString();
        }
    }

    return string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim();
}

static IEnumerable<UserCredential> ReadUsers(string path)
{
    var fileName = Path.GetFileName(path);
    var rows = ParseCsv(File.ReadAllText(path));
    var isFirst = true;

    foreach (var row in rows)
    {
        if (isFirst)
        {
            isFirst = false;
            if (row.Count > 0 && row[0].Equals("Username", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
        }

        if (row.Count < 2 || string.IsNullOrWhiteSpace(row[0]))
        {
            continue;
        }

        yield return new UserCredential(row[0].Trim(), row[1], fileName);
    }
}

static List<List<string>> ParseCsv(string content)
{
    var rows = new List<List<string>>();
    var row = new List<string>();
    var field = new StringBuilder();
    var inQuotes = false;

    for (var i = 0; i < content.Length; i++)
    {
        var c = content[i];

        if (inQuotes)
        {
            if (c == '"')
            {
                if (i + 1 < content.Length && content[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = false;
                }
            }
            else
            {
                field.Append(c);
            }
            continue;
        }

        switch (c)
        {
            case '"':
                inQuotes = true;
                break;
            case ',':
                row.Add(field.ToString());
                field.Clear();
                break;
            case '\r':
                break;
            case '\n':
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = new List<string>();
                break;
            default:
                field.Append(c);
                break;
        }
    }

    if (field.Length > 0 || row.Count > 0)
    {
        row.Add(field.ToString());
        rows.Add(row);
    }

    return rows;
}

static async Task<ValidationResult> ValidateUserAsync(HttpClient httpClient, UserCredential user)
{
    var timestamp = DateTime.Now;
    try
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, string.Empty);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user.Username}:{user.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var status = response.StatusCode switch
        {
            HttpStatusCode.OK => ValidationResult.HasAccess,
            HttpStatusCode.Unauthorized => ValidationResult.Unauthorized,
            _ => $"Unexpected ({(int)response.StatusCode} {response.ReasonPhrase})"
        };

        return new ValidationResult(user.Username, user.SourceFile, (int)response.StatusCode, status, string.Empty, timestamp);
    }
    catch (Exception ex)
    {
        return new ValidationResult(user.Username, user.SourceFile, 0, "Error", ex.Message, timestamp);
    }
}

static void WriteResults(string path, IEnumerable<ValidationResult> results)
{
    var sb = new StringBuilder();
    sb.AppendLine("Username,SourceFile,StatusCode,Status,Error,CheckedAt");
    foreach (var r in results)
    {
        sb.AppendLine(string.Join(",",
            Csv(r.Username),
            Csv(r.SourceFile),
            r.StatusCode.ToString(),
            Csv(r.Status),
            Csv(r.Error),
            r.CheckedAt.ToString("yyyy-MM-dd HH:mm:ss")));
    }

    File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    static string Csv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}

record UserCredential(string Username, string Password, string SourceFile);

record ValidationResult(string Username, string SourceFile, int StatusCode, string Status, string Error, DateTime CheckedAt)
{
    public const string HasAccess = "Has Access";
    public const string Unauthorized = "Unauthorized Access";
}
