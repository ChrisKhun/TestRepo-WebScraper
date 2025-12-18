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
using System.Net.Http.Headers;
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
        Console.WriteLine();
        
        // Get account details
        await GetAccountDetails(http, dsn, body);
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

static async Task GetAccountDetails(HttpClient http, string dsn, string accountsJson)
{
    try
    {
        var doc = JsonDocument.Parse(accountsJson);
        var accounts = doc.RootElement.GetProperty("items");
        
        Console.WriteLine("Connected Accounts:");
        Console.WriteLine("-------------------");
        
        foreach (var account in accounts.EnumerateArray())
        {
            var accountId = account.GetProperty("id").GetString();
            var provider = account.GetProperty("provider").GetString();
            
            Console.WriteLine($"Account ID: {accountId}");
            Console.WriteLine($"Provider: {provider}");
            
            if (account.TryGetProperty("name", out var name))
                Console.WriteLine($"Name: {name.GetString()}");
            
            if (account.TryGetProperty("email", out var email))
                Console.WriteLine($"Email: {email.GetString()}");
            
            if (account.TryGetProperty("username", out var username))
                Console.WriteLine($"Username: {username.GetString()}");
            
            Console.WriteLine();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error parsing account details: {ex.Message}");
        Console.WriteLine($"Raw response: {accountsJson}");
    }
}

static string FindProjectRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && dir.GetFiles("*.csproj").Length == 0)
        dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}