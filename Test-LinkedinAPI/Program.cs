// ============================================================================
// Unipile API Connection Test
// Author: Christopher Khun
// Date: 12/17/25
// 
// Description:
// Tests connection to Unipile API and retrieves connected account details.
// Loads credentials from .env file
// ============================================================================
using DotNetEnv;
using System.Text.Json;

// Load .env from project root
var projectRoot = FindProjectRoot();
Env.Load(Path.Combine(projectRoot, ".env"));

var dsn = Environment.GetEnvironmentVariable("UNIPILE_DSN")?.TrimEnd('/');
var apiKey = Environment.GetEnvironmentVariable("UNIPILE_API_KEY");

Console.WriteLine($"Testing connection to: {dsn}");

using var http = new HttpClient();
http.DefaultRequestHeaders.Add("X-API-KEY", apiKey);

try
{
    var response = await http.GetAsync($"{dsn}/api/v1/accounts");
    var body = await response.Content.ReadAsStringAsync();

    if (response.IsSuccessStatusCode)
    {
        Console.WriteLine("API key is VALID");
        
        // Get search url details
        var unipileAccountId = Environment.GetEnvironmentVariable("UNIPILE_ACCOUNT_ID");

        if (string.IsNullOrWhiteSpace(unipileAccountId))
        {
            Console.WriteLine("Missing UNIPILE_ACCOUNT_ID in .env (needed for LinkedIn search).");
        }
        else
        {
            // user input convert to url
            Console.Write("Enter LinkedIn keyword: ");
            Console.WriteLine("");
            Console.WriteLine("");
            var keywordsInput = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(keywordsInput))
            {
                Console.WriteLine("No keywords entered.");
                Environment.Exit(0);
            }
            else
            {
                // convert keywords to url
                var encodedKeywords = Uri.EscapeDataString(keywordsInput.Trim());
                
                var linkedinSearchUrl = $"https://www.linkedin.com/search/results/people/?keywords={encodedKeywords}";
                
                await SearchLinkedInByUrl(http, dsn, unipileAccountId, linkedinSearchUrl);
            }
        }

    }
    else
    {
        Console.WriteLine($"API key is INVALID (HTTP {response.StatusCode})");
        Console.WriteLine($"Response: {body}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Connection failed: {ex.Message}");
}

static string FindProjectRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && dir.GetFiles("*.csproj").Length == 0)
        dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

// ============================================================================
//                             Search For Link By URL
// ============================================================================

static async Task SearchLinkedInByUrl(HttpClient http, string dsn, string accountId, string searchUrl)
{
    try
    {
        var endpoint =
            $"{dsn.TrimEnd('/')}/api/v1/linkedin/search" +
            $"?account_id={Uri.EscapeDataString(accountId)}";
        var payload = new { url = searchUrl };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var response = await http.PostAsync(endpoint, content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"LinkedIn search failed (HTTP {(int)response.StatusCode} {response.ReasonPhrase})");
            Console.WriteLine($"Response: {body}");
            return;
        }

        // if successful returns an object with array of results 
        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine("LinkedIn search succeeded, but response didn't contain an 'items' array.");
            Console.WriteLine($"Raw response: {body}");
            return;
        }

        Console.WriteLine("\nLinkedIn Search Results:");
        Console.WriteLine("------------------------");
        Console.WriteLine($"Count: {items.GetArrayLength()}");
        Console.WriteLine();

        foreach (var item in items.EnumerateArray())
        {
            // type id name
            var type = item.TryGetProperty("type", out var t) ? t.GetString() : "unknown";
            var id = item.TryGetProperty("id", out var i) ? i.GetString() : null;
            var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;

            // url
            var profileUrl =
                item.TryGetProperty("profile_url", out var p) ? p.GetString() :
                item.TryGetProperty("url", out var u) ? u.GetString() :
                null;

            // headline 
            var headline =
                item.TryGetProperty("headline", out var h) ? h.GetString() :
                item.TryGetProperty("title", out var tt) ? tt.GetString() :
                null;

            Console.WriteLine($"Type: {type}");
            if (!string.IsNullOrWhiteSpace(id)) Console.WriteLine($"ID: {id}");
            if (!string.IsNullOrWhiteSpace(name)) Console.WriteLine($"Name: {name}");
            if (!string.IsNullOrWhiteSpace(headline)) Console.WriteLine($"Headline/Title: {headline}");
            if (!string.IsNullOrWhiteSpace(profileUrl)) Console.WriteLine($"URL: {profileUrl}");
            Console.WriteLine();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error running LinkedIn search: {ex.Message}");
    }
}