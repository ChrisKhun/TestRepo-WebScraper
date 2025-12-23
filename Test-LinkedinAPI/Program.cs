// ============================================================================
// Unipile API Connection Test
// Author: Christopher Khun
// Date: 12/17/25
// 
// Description:
// Tests connection to Unipile API and retrieves connected account details.
// Loads credentials through API from .env file
// ============================================================================
using DotNetEnv;
using System.Text.Json;

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
    //          https://{subdomain}.unipile.com:{port}/api/v1/users/{identifier}
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

            await SearchEmployeesByCompanyName(http, dsn, unipileAccountId, companyInput);
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
//                      Find Company Employee w/ Linkedin
// ============================================================================
static async Task SearchEmployeesByCompanyName(HttpClient http, string dsn, string accountId, string companyName)
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

    await SearchLinkedInByUrl(http, dsn, accountId, employeesUrl, companyName);
}

// ============================================================================
//                        Searches w/ Pages & export to .csv
// ============================================================================
static async Task SearchLinkedInByUrl(HttpClient http, string dsn, string accountId, string searchUrl, string companyName)
{
    try
    {
        // get user input
        Console.Write("Enter keyword(s) to filter results (ex: software engineering). Leave blank for no filter: ");
        var keywordInput = Console.ReadLine();

        // set keyword
        // Split by spaces, keep unique terms, lowercase for matching
        var keywords = string.IsNullOrWhiteSpace(keywordInput)
            ? Array.Empty<string>()
            : keywordInput
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(w => w.ToLowerInvariant())
                .Distinct()
                .ToArray();

        string? cursor = null;
        int page = 0;

        var endpoint =
            $"{dsn.TrimEnd('/')}/api/v1/linkedin/search" +
            $"?account_id={Uri.EscapeDataString(accountId)}";

        // gets profiles for CSV export
        var profiles = new List<LinkedInProfile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // create cancel signal
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        Console.WriteLine("Press ANY key to stop and save progress...\n");

        // background key listener
        _ = Task.Run(() =>
        {
            Console.ReadKey(true);
            cts.Cancel();
        });
        
        int emptyMatchPages = 0;
        const int maxEmptyMatchPages = 50; // change if needed
        
        while (true)
        {
            // allow stopping between pages
            if (token.IsCancellationRequested)
            {
                Console.WriteLine("\nStopping! Saving progress...");
                break;
            }

            page++;

            // checks for cursor
            object payload = string.IsNullOrWhiteSpace(cursor)
                ? new { url = searchUrl }
                : new { url = searchUrl, cursor = cursor };

            // converts payload into json for httpclient
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            // sends http post request to server & wait for response
            using var response = await http.PostAsync(endpoint, content);
            var body = await response.Content.ReadAsStringAsync();

            // checks if response is valid
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"LinkedIn search failed (HTTP {(int)response.StatusCode} {response.ReasonPhrase})");
                Console.WriteLine($"Response: {body}");
                break;
            }

            // parses json
            using var doc = JsonDocument.Parse(body);

            // ensures api response has items
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                Console.WriteLine("LinkedIn search succeeded, but response didn't contain an 'items' array.");
                Console.WriteLine($"Raw response: {body}");
                break;
            }

            int pageAdded = 0;
            
            // loops through and sends profile information to csv
            foreach (var item in items.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var t) ? t.GetString() : "unknown";
                var id = item.TryGetProperty("id", out var i) ? i.GetString() : null;
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;

                var profileUrl =
                    item.TryGetProperty("profile_url", out var p) ? p.GetString() :
                    item.TryGetProperty("url", out var u) ? u.GetString() :
                    null;

                var headline =
                    item.TryGetProperty("headline", out var h) ? h.GetString() :
                    item.TryGetProperty("title", out var tt) ? tt.GetString() :
                    null;
                
                // must have url
                if (string.IsNullOrWhiteSpace(profileUrl))
                    continue;

                // keyword filter
                if (keywords.Length > 0)
                {
                    var text = $"{name} {headline}".ToLowerInvariant();
                    if (!keywords.Any(word => text.Contains(word)))
                        continue;
                }
                
                // gets linkedin url from url
                var identifier = ExtractLinkedInPublicIdentifier(profileUrl);
                
                string? email = null;
                string? phone = null;

                // best-effort contact fetch
                if (!string.IsNullOrWhiteSpace(identifier))
                {
                    (email, phone) = await TryGetContactAsync(http, dsn, accountId, identifier);
                }
                
                // prints to see if info found
                Console.WriteLine(
                    $"account_id={accountId} | identifier={identifier} | email={email ?? "null"} | phone={phone ?? "null"}"
                );
                
                profiles.Add(new LinkedInProfile(type, id, name, headline, profileUrl, email, phone));
                pageAdded++;

            }
            
            if (pageAdded == 0)
            {
                emptyMatchPages++;
                Console.WriteLine($"No matches this page ({emptyMatchPages}/{maxEmptyMatchPages}).");
                if (emptyMatchPages >= maxEmptyMatchPages)
                {
                    Console.WriteLine("\nStopping early: too many pages with no matches.");
                    break;
                }
            }
            else
            {
                emptyMatchPages = 0;
            }

            Console.WriteLine($"Page {page} complete | added {pageAdded} | total {profiles.Count}");

            cursor = doc.RootElement.TryGetProperty("cursor", out var c) ? c.GetString() : null;

            if (string.IsNullOrWhiteSpace(cursor))
            {
                Console.WriteLine("\nSearch complete.");
                break;
            }
        }

        // Export whatever is collected to CSV
        var fileName = $"{companyName}_results_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        var filePath = Path.Combine(Environment.CurrentDirectory, fileName);

        if (profiles.Count > 0)
        {
        await ExportProfilesToCsvAsync(profiles, filePath);
        Console.WriteLine($"\nExported {profiles.Count} profiles to: " + filePath);
        } else
        {
            Console.WriteLine("Not exporting. 0 profiles found.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error running LinkedIn search: {ex.Message}");
    }
}

// ============================================================================
//                       Company Search & Return First ID
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

    // Displays searched company's details
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

        if (shown >= 1) break;
    }
    
    var first = items.GetArrayLength() > 0 ? items[0] : default;
    if (first.ValueKind == JsonValueKind.Undefined) return null;

    return first.TryGetProperty("id", out var firstId) ? firstId.GetString() : null;
}

// ============================================================================
//                   profile fetch helper 
// ============================================================================
static async Task<(string? Email, string? Phone)> TryGetContactAsync( HttpClient http, string dsn, string accountId, string identifier)
{
    // this pulls the full profile including contacts
    var url =
        $"{dsn.TrimEnd('/')}/api/v1/users/{Uri.EscapeDataString(identifier)}" +
        $"?linkedin_sections=%2A&account_id={Uri.EscapeDataString(accountId)}";

    using var req = new HttpRequestMessage(HttpMethod.Get, url);
    req.Headers.Accept.ParseAdd("application/json");

    // delay
    await Task.Delay(250);
    using var resp = await http.SendAsync(req);
    var body = await resp.Content.ReadAsStringAsync();

    if (!resp.IsSuccessStatusCode)
        return (null, null);

    using var doc = JsonDocument.Parse(body);
    var root = doc.RootElement;

    string? FirstStringFromArray(JsonElement arr)
    {
        if (arr.ValueKind != JsonValueKind.Array) return null;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        return null;
    }

    string? email = null;
    string? phone = null;

    if (root.TryGetProperty("contact_info", out var contact) && contact.ValueKind == JsonValueKind.Object)
    {
        if (contact.TryGetProperty("emails", out var emails))
            email = FirstStringFromArray(emails);

        if (contact.TryGetProperty("phones", out var phones))
            phone = FirstStringFromArray(phones);
    }

    return (email, phone);
}

// gets linkedin url from url
static string? ExtractLinkedInPublicIdentifier(string? url)
{
    if (string.IsNullOrWhiteSpace(url))
    {
        return null;
    }
    try
    {
        var uri = new Uri(url);
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // ["in", "<slug>"]
        if (parts.Length >= 2 && parts[0].Equals("in", StringComparison.OrdinalIgnoreCase))
            return parts[1];

        return null;
    }
    catch
    {
        return null;
    }
}

// ============================================================================
//                              csv helper
// ============================================================================

static string CsvEscape(string? value)
{
    value ??= "";
    var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
    value = value.Replace("\"", "\"\"");
    return needsQuotes ? $"\"{value}\"" : value;
}

static async Task ExportProfilesToCsvAsync(
    IEnumerable<LinkedInProfile> profiles,
    string filePath)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("type,id,name,headline,url,email,phone");

    foreach (var p in profiles)
    {
        // this is temp just to make sure csv works will format to [name, company, url, contact etc..]
        // need to figure out how to get contacts
        sb.Append(CsvEscape(p.Type)).Append(',')
            .Append(CsvEscape(p.Id)).Append(',')
            .Append(CsvEscape(p.Name)).Append(',')
            .Append(CsvEscape(p.Headline)).Append(',')
            .Append(CsvEscape(p.Url)).Append(',')
            .Append(CsvEscape(p.Email)).Append(',')
            .Append(CsvEscape(p.Phone)).AppendLine();
    }

    await File.WriteAllTextAsync(filePath, sb.ToString());
}

record LinkedInProfile(string? Type, string? Id, string? Name, string? Headline, string? Url, string? Email, string? Phone);

