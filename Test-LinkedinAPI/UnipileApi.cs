// ============================================================================
// Unipile API 
// Author: Christopher Khun
// Date: 12/17/25
//
// Description:
// Tests connection to Unipile API and retrieves connected account details.
// Loads credentials through API from .env file
// ============================================================================
using System.Text.Json;

namespace Test_LinkedinAPI;

public class UnipileApi : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _dsn;
    private readonly string _accountId;
    
    // 
    public UnipileApi(string dsn, string apiKey, string accountId)
    {
        _dsn = dsn.TrimEnd('/');
        _accountId = accountId;
        
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("X-API-KEY", apiKey);
    }
    
    // ==================================================================
    // Test Api Connection
    // ==================================================================
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var response = await _http.GetAsync($"{_dsn}/api/v1/accounts");
            var body = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("API key is VALID.");
                return true;
            } else
            {
                Console.WriteLine($"API key is INVALID (HTTP {response.StatusCode}");
                Console.WriteLine($"Response: {body}");
                return false;
            }
        } catch (Exception ex)
        {
            Console.WriteLine($"Connection failed: {ex.Message}");
            return false;
        }
    }
    
    // ==================================================================
    // Search Employees by Company Name
    // ==================================================================
    public async Task SearchEmployeesByCompanyNameAsync(string companyName)
    {
        var companyId = await GetCompanyIdFromNameAsync(companyName);
        if (string.IsNullOrWhiteSpace(companyId))
        {
            Console.WriteLine("Could not determine company id from company search.");
            return;
        }

        // Ask for keywords after showing company info
        Console.Write("Enter keyword(s) to filter results (ex: software engineering). Leave blank for no filter: ");
        var keywordInput = Console.ReadLine();

        var encoded = Uri.EscapeDataString(companyName.Trim());
        var employeesUrl = $"https://www.linkedin.com/search/results/people/?keywords={encoded}" +
                           $"&currentCompany=%5B{Uri.EscapeDataString(companyId)}%5D";

        await SearchLinkedInByUrlAsync(employeesUrl, companyName, keywordInput);
    }
    
    // ==================================================================
    // Search Employees by Company Name
    // ==================================================================
    public async Task SearchLinkedInByUrlAsync(string searchUrl, string companyName, string? keywordFilter = null)
    {
        try
        {
            string[] keywords;
            if (string.IsNullOrWhiteSpace(keywordFilter))
            {
                keywords = Array.Empty<string>();
            } else
            {
                keywords = keywordFilter
                    .ToLower()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Distinct()
                    .ToArray();
            }
            
            string? cursor = null;
            int page = 0;
            var endpoint = $"{_dsn}/api/v1/linkedin/search?account_id={Uri.EscapeDataString((_accountId))}";
            
            var profiles = new List<LinkedInProfile>();
            
            using var cts = new CancellationTokenSource();
            var token = cts.Token;
            
            Console.WriteLine("Press ANY key to stop and save progress...\n");
            
            _ = Task.Run(() =>
            {
                Console.ReadKey(true);
                cts.Cancel();
            });
            
            int emptyMatchPages = 0;
            const int maxEmptyMatchPages = 150; // can change in future if want to searh longer
            
            while (true)
            {
                if (token.IsCancellationRequested)
                {
                    Console.WriteLine("\nStopping! Saving Progress...");
                    break;
                }
                
                page++;
                
                object payload = string.IsNullOrWhiteSpace(cursor)
                    ?  new { url = searchUrl }
                    : new  { url = searchUrl, cursor = cursor};
                
                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                using var response = await _http.PostAsync(endpoint, content);
                var body = await response.Content.ReadAsStringAsync();
                
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"LinkedIn search failed (HTTP {(int)response.StatusCode} {response.ReasonPhrase})");
                    Console.WriteLine($"Response: {body}");
                    break;
                }
                
                using var doc = JsonDocument.Parse(body);
                
                if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("LinkedIn search succeeded, but response didn't contain an 'items' array.");
                    Console.WriteLine($"Raw response: {body}");
                    break;
                }

                int pageAdded = 0;

                foreach (var item in items.EnumerateArray())
                {
                    var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var profileUrl = item.TryGetProperty("profile_url", out var p) ? p.GetString() :
                                    item.TryGetProperty("url", out var u) ? u.GetString() : null;
                    var headline = item.TryGetProperty("headline", out var h) ? h.GetString() :
                                  item.TryGetProperty("title", out var tt) ? tt.GetString() : null;

                    if (string.IsNullOrWhiteSpace(profileUrl)) continue;

                    // Keyword filter
                    if (keywords.Length > 0)
                    {
                        var text = $"{name} {headline}".ToLowerInvariant();
                        if (!keywords.Any(word => text.Contains(word))) continue;
                    }

                    var identifier = ExtractLinkedInPublicIdentifier(profileUrl);

                    string? email = null;
                    string? phone = null;
                    string? company = null;
                    string? jobTitle = null;

                    if (!string.IsNullOrWhiteSpace(identifier))
                    {
                        var profileData = await GetContactInfoAsync(identifier);
                        email = profileData.Email;
                        phone = profileData.Phone;
                        company = profileData.Company;
                        jobTitle = profileData.JobTitle;
                    }

                    var hasEmail = !string.IsNullOrWhiteSpace(email);
                    var hasPhone = !string.IsNullOrWhiteSpace(phone);

                    if (!(hasEmail || hasPhone))
                    {
                        continue;
                    }

                    // if found prints prf details w/ headline
                    Console.WriteLine(
                        $"name={name ?? "null"} | headline={headline ?? "null"} | company={company ?? "null"} | title={jobTitle ?? "null"} | " +
                        $"email={(hasEmail ? email : "null")} | phone={(hasPhone ? phone : "null")} | url={profileUrl}"
                    );

                    profiles.Add(new LinkedInProfile(name, company, jobTitle, profileUrl, email, phone));
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

            if (profiles.Count > 0)
            {
                var fileName = $"{companyName.Trim()}_results_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                var filePath = Path.Combine(Environment.CurrentDirectory, fileName);
                await ExportProfilesToCsvAsync(profiles, filePath);
                Console.WriteLine($"\nExported {profiles.Count} profiles to: {filePath}");
            }
            else
            {
                Console.WriteLine("Not exporting. 0 profiles with contact info found.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error running LinkedIn search: {ex.Message}");
        }
    }
    // ==================================================================
    // Get Company ID from Company Name
    // ==================================================================
    public async Task<string?> GetCompanyIdFromNameAsync(string companyName)
    {
        // makes company name safe for URL use
        var encoded = Uri.EscapeDataString(companyName.Trim());
        var companySearchUrl = $"https://www.linkedin.com/search/results/companies/?keywords={encoded}";
        
        // builds api endpoint w/ acc id
        var endpoint = $"{_dsn}/api/v1/linkedin/search?account_id={Uri.EscapeDataString(_accountId)}";
        
        // packages search url in json
        var payload = new {url = companySearchUrl};
        var json = JsonSerializer.Serialize((payload));
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        
        // sends request & read repsonse
        using var response = await _http.PostAsync(endpoint, content);
        var body = await response.Content.ReadAsStringAsync();
        
        // fail check
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Company search failed (HTTP {(int)response.StatusCode} {response.ReasonPhrase})");
            Console.WriteLine($"Response: {body}");
            return null;
        }
        
        // verify if array exists
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine("Company search succeeded, but response didn't contain an 'items' array.");
            return null;
        }

        // prints searched company's info
        Console.WriteLine("\n\nCompany Results:");
        Console.WriteLine("----------------");
        int shown = 0;
        foreach (var item in items.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            var id = item.TryGetProperty("id", out var i) ? i.GetString() : null;
            var url = item.TryGetProperty("profile_url", out var p) ? p.GetString() :
                item.TryGetProperty("url", out var u) ? u.GetString() : null;

            if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(id))
            {
                Console.WriteLine($"[{shown + 1}] {name ?? "(no name)"}");
                if (!string.IsNullOrWhiteSpace(id)) Console.WriteLine($"     ID: {id}");
                if (!string.IsNullOrWhiteSpace(url)) Console.WriteLine($"     URL: {url}");
                Console.WriteLine();
                shown++;
            }
            if (shown >= 1) break;
        }

        // get first company's id
        var first = items.GetArrayLength() > 0 ? items[0] : default;
        if (first.ValueKind == JsonValueKind.Undefined) return null;

        return first.TryGetProperty("id", out var firstId) ? firstId.GetString() : null;
    }
    
    // ==================================================================
    // Get Contact Info from Linkedin Profile
    // ==================================================================
     public async Task<ProfileData> GetContactInfoAsync(string identifier)
    {
        // builds profile fetch url with identifier
        var url = $"{_dsn}/api/v1/users/{Uri.EscapeDataString(identifier)}" +
                  $"?linkedin_sections=%2A&account_id={Uri.EscapeDataString(_accountId)}";

        // creates get request and gets json response
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/json");

        // delay + sends request to read profile data
        await Task.Delay(150);
        using var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();

        // return null if empty
        if (!resp.IsSuccessStatusCode)
            return new ProfileData(null, null, null, null);

        // parses json gets root element
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // gets first string from array
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
        string? company = null;
        string? jobTitle = null;

        // gets contact info
        if (root.TryGetProperty("contact_info", out var contact) &&
            contact.ValueKind == JsonValueKind.Object)
        {
            if (contact.TryGetProperty("emails", out var emails))
                email = FirstStringFromArray(emails);
            if (contact.TryGetProperty("phones", out var phones))
                phone = FirstStringFromArray(phones);
        }

        // gets current company from experience 
        if (root.TryGetProperty("experience", out var exp) &&
            exp.ValueKind == JsonValueKind.Array)
        {
            foreach (var job in exp.EnumerateArray())
            {
                var hasEndDate = job.TryGetProperty("end_date", out var endDate) &&
                               endDate.ValueKind != JsonValueKind.Null &&
                               !string.IsNullOrWhiteSpace(endDate.GetString());

                if (!hasEndDate)
                {
                    if (job.TryGetProperty("company", out var comp))
                        company = comp.GetString();
                    if (job.TryGetProperty("title", out var title))
                        jobTitle = title.GetString();
                    break;
                }
            }
        }

        // parse from headline
        if (string.IsNullOrWhiteSpace(company) &&
            root.TryGetProperty("headline", out var headline))
        {
            var headlineText = headline.GetString();
            if (!string.IsNullOrWhiteSpace(headlineText) && headlineText.Contains(" at "))
            {
                var parts = headlineText.Split(new[] { " at " }, StringSplitOptions.None);
                if (parts.Length >= 2)
                {
                    jobTitle ??= parts[0].Trim();
                    company = parts[1].Trim();
                }
            }
        }

        return new ProfileData(email, phone, company, jobTitle);
    }
     
    // ==================================================================
    // Helper Methods
    // ==================================================================
    
    // get username from profile url
    private static string? ExtractLinkedInPublicIdentifier(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var uri = new Uri(url);
            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Equals("in", StringComparison.OrdinalIgnoreCase))
                return parts[1];
            return null;
        }
        catch
        {
            return null;
        }
    }

    // formats text for csv
    private static string CsvEscape(string? value)
    {
        value ??= "";
        var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        value = value.Replace("\"", "\"\"");
        return needsQuotes ? $"\"{value}\"" : value;
    }

    // writes profile data to csv
    private static async Task ExportProfilesToCsvAsync(IEnumerable<LinkedInProfile> profiles, string filePath)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("name,company,title,email,phone,url");

        foreach (var p in profiles)
        {
            sb.Append(CsvEscape(p.Name)).Append(',')
                .Append(CsvEscape(p.Company)).Append(',')
                .Append(CsvEscape(p.JobTitle)).Append(',')
                .Append(CsvEscape(p.Email)).Append(',')
                .Append(CsvEscape(p.Phone)).Append(',')
                .Append(CsvEscape(p.Url)).AppendLine();
        }

        await File.WriteAllTextAsync(filePath, sb.ToString());
    }

    // cleanup method 
    public void Dispose()
    {
        _http?.Dispose();
    }

    // ==================================================================
    // Records
    // ==================================================================
    public record ProfileData(string? Email, string? Phone, string? Company, string? JobTitle);

    public record LinkedInProfile(
        string? Name,
        string? Company,
        string? JobTitle,
        string? Url,
        string? Email,
        string? Phone
            );
}