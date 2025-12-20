// ============================================================================
// Unipile API Connection Test
// Author: Christopher Khun
// Date: 12/17/25
// 
// Description:
// Tests connection to Unipile API and retrieves connected account details.
// Loads credentials through API from .env file
// ============================================================================

using System.Net;
using DotNetEnv;
using System.Text.Json;


// ============================================================================
//                                    Setup
// ============================================================================

// Load .env from project root
var projectRoot = FindProjectRoot();
Env.Load(Path.Combine(projectRoot, ".env"));

// gets key from .env and sets as string
var dsn = Environment.GetEnvironmentVariable("UNIPILE_DSN")?.TrimEnd('/');
var apiKey = Environment.GetEnvironmentVariable("UNIPILE_API_KEY");

Console.WriteLine($"Testing connection to: {dsn}");

// create new http client & give it header name
using var http = new HttpClient();
http.DefaultRequestHeaders.Add("X-API-KEY", apiKey);

// ============================================================================
//                                     Main
// ============================================================================
try
{
    // send and read response from unipile
    var response = await http.GetAsync($"{dsn}/api/v1/accounts");
    var body = await response.Content.ReadAsStringAsync();

    
    if (response.IsSuccessStatusCode)
    {
        Console.WriteLine("API key is VALID");
        
        // Get search url details
        var unipileAccountId = Environment.GetEnvironmentVariable("UNIPILE_ACCOUNT_ID");

        if (string.IsNullOrWhiteSpace(unipileAccountId))
        {
            // exception check
            Console.WriteLine("Missing UNIPILE_ACCOUNT_ID in .env (needed for LinkedIn search).");
        }
        else
        {
            Console.Write("Enter company name (ex: Pfizer): ");
            var companyInput = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(companyInput))
            {
                Console.WriteLine("No company entered.");
                Environment.Exit(0);
            }

            await SearchEmployeesByCompanyName(http, dsn, unipileAccountId, companyInput, topN: 10);

        }

    }
    else
    {
        // exception check
        Console.WriteLine($"API key is INVALID (HTTP {response.StatusCode})");
        Console.WriteLine($"Response: {body}");
    }
}
catch (Exception ex)
{
    // exception check
    Console.WriteLine($"Connection failed: {ex.Message}");
}

// ============================================================================
//                            File Helper Function (to find .env)
// ============================================================================
static string FindProjectRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && dir.GetFiles("*.csproj").Length == 0)
        dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

// ============================================================================
//                  Search For LinkedIn Company People By URL
// ============================================================================

static async Task<string?> GetCompanyIdFromName(HttpClient http, string dsn, string accountId, string companyName)
{
    var encoded = Uri.EscapeDataString(companyName.Trim());
    var companySearchUrl = $"https://www.linkedin.com/search/results/companies/?keywords={encoded}";

    var endpoint =
        $"{dsn.TrimEnd('/')}/api/v1/linkedin/search" +
        $"?account_id={Uri.EscapeDataString(accountId)}";

    // packages object to send over to http
    var payload = new { url = companySearchUrl };
    var json = JsonSerializer.Serialize(payload);
    using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

    // execute request & read response
    using var response = await http.PostAsync(endpoint, content);
    var body = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        Console.WriteLine($"Company search failed (HTTP {(int)response.StatusCode} {response.ReasonPhrase})");
        Console.WriteLine($"Response: {body}");
        return null;
    }

    // parse json response
    using var doc = JsonDocument.Parse(body);

    if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
    {
        Console.WriteLine("Company search succeeded, but response didn't contain an 'items' array.");
        return null;
    }

    // Print top 5 companies and auto-pick 1
    Console.WriteLine("\nCompany Results:");
    Console.WriteLine("----------------");
    int shown = 0;

    foreach (var item in items.EnumerateArray())
    {
        var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
        var id = item.TryGetProperty("id", out var i) ? i.GetString() : null;
        var url =
            item.TryGetProperty("profile_url", out var p) ? p.GetString() :
            item.TryGetProperty("url", out var u) ? u.GetString() :
            null;

        if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(id))
        {
            Console.WriteLine($"[{shown + 1}] {name ?? "(no name)"}");
            if (!string.IsNullOrWhiteSpace(id)) Console.WriteLine($"    ID: {id}");
            if (!string.IsNullOrWhiteSpace(url)) Console.WriteLine($"    URL: {url}");
            Console.WriteLine();
            shown++;
        }

        if (shown >= 5) break;
    }

    // Pick the first company id
    var first = items.GetArrayLength() > 0 ? items[0] : default;
    if (first.ValueKind == JsonValueKind.Undefined) return null;

    return first.TryGetProperty("id", out var firstId) ? firstId.GetString() : null;
}


static async Task SearchEmployeesByCompanyName(HttpClient http, string dsn, string accountId, string companyName, int topN = 10)
{
    var companyId = await GetCompanyIdFromName(http, dsn, accountId, companyName);

    if (string.IsNullOrWhiteSpace(companyId))
    {
        Console.WriteLine("Could not determine company id from company search.");
        return;
    }

    var encoded = Uri.EscapeDataString(companyName.Trim());

    // currentCompany expects a JSON-like array in the URL
    var employeesUrl =
        $"https://www.linkedin.com/search/results/people/?keywords={encoded}" +
        $"&currentCompany=%5B{Uri.EscapeDataString(companyId)}%5D";

    await SearchLinkedInByUrl(http, dsn, accountId, employeesUrl);
}

// ============================================================================
//                             Search For LinkIn By URL
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
        // exception check
        Console.WriteLine($"Error running LinkedIn search: {ex.Message}");
    }
}