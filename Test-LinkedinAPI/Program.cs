using System.Net.Http.Headers;
using DotNetEnv;

Console.WriteLine("Loading environment variables...");

// 1) Find project root 
var projectRoot = FindProjectRoot();
var envPath = Path.Combine(projectRoot, ".env");

// 2) Load .env from the project root
Console.WriteLine("Base directory: " + AppContext.BaseDirectory);
Console.WriteLine("Project root:   " + projectRoot);
Console.WriteLine(".env path:      " + envPath);
Console.WriteLine(".env exists:    " + File.Exists(envPath));

if (File.Exists(envPath))
{
    try
    {
        Env.Load(envPath);
        Console.WriteLine("Loaded .env from project root");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠Failed to load .env: {ex.Message}");
    }
}
else
{
    Console.WriteLine("⚠.env not found in project root. Falling back to OS environment variables.");
}

// 3) Read env vars
string unipileDsn = (Environment.GetEnvironmentVariable("UNIPILE_DSN") ?? "").Trim().TrimEnd('/');
string unipileApiKey = (Environment.GetEnvironmentVariable("UNIPILE_API_KEY") ?? "").Trim();

Console.WriteLine("UNIPILE_DSN loaded: " + !string.IsNullOrWhiteSpace(unipileDsn));
Console.WriteLine("UNIPILE_API_KEY loaded: " + !string.IsNullOrWhiteSpace(unipileApiKey));

if (string.IsNullOrWhiteSpace(unipileDsn) || string.IsNullOrWhiteSpace(unipileApiKey))
{
    Console.WriteLine("Config error: Missing UNIPILE_DSN or UNIPILE_API_KEY.");
    Console.WriteLine("Create a .env in the SAME folder as your .csproj like:");
    Console.WriteLine("UNIPILE_DSN=https://api21.unipile.com:15198");
    Console.WriteLine("UNIPILE_API_KEY=[ENTER_REAL_KEY_HERE]");
    Environment.Exit(1);
}

// 4) Validate DSN is a valid URL
if (!Uri.TryCreate(unipileDsn, UriKind.Absolute, out var dsnUri) || 
    (dsnUri.Scheme != "http" && dsnUri.Scheme != "https"))
{
    Console.WriteLine("UNIPILE_DSN must be a valid HTTP/HTTPS URL");
    Environment.Exit(1);
}

// 5) Show sanitized credentials
Console.WriteLine($"UNIPILE_DSN: {unipileDsn}");
Console.WriteLine($"UNIPILE_API_KEY: {new string('*', Math.Min(unipileApiKey.Length, 8))}... (masked)");

// 6) Verify key by calling Unipile with retry logic
Console.WriteLine("Verifying Unipile API key...");
var verified = await VerifyWithRetryAsync(unipileDsn, unipileApiKey);

if (verified)
{
    Console.WriteLine("Unipile API key is VALID");
}
else
{
    Console.WriteLine("Unipile API key verification FAILED");
    Environment.Exit(1);
}

Console.WriteLine("Done.");


// ==================================================
//                     HELPERS
// ==================================================

static string FindProjectRoot()
{
    // Start from where the EXE is running (bin/Debug/net8.0)
    // Walk upward until we find a directory containing a *.csproj
    var dir = new DirectoryInfo(AppContext.BaseDirectory);

    while (dir != null)
    {
        if (dir.GetFiles("*.csproj").Length > 0)
            return dir.FullName;

        dir = dir.Parent;
    }

    // Fallback: current directory 
    return Directory.GetCurrentDirectory();
}

static async Task<bool> VerifyWithRetryAsync(string dsn, string apiKey, int maxRetries = 3)
{
    for (int attempt = 0; attempt < maxRetries; attempt++)
    {
        var (ok, statusCode, body, error) = await VerifyUnipileApiKeyAsync(dsn, apiKey);
        
        if (ok)
        {
            Console.WriteLine($"Verified on attempt {attempt + 1} (HTTP {statusCode})");
            Console.WriteLine("Response:");
            Console.WriteLine(body);
            return true;
        }
        
        if (statusCode >= 500 || !string.IsNullOrWhiteSpace(error))
        {
            if (attempt < maxRetries - 1)
            {
                var delayMs = 1000 * (attempt + 1);
                Console.WriteLine($"Attempt {attempt + 1} failed, retrying in {delayMs}ms...");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Console.WriteLine($"   Error: {error}");
                }
                else
                {
                    Console.WriteLine($"   HTTP {statusCode}");
                }
                await Task.Delay(delayMs);
                continue;
            }
        }
        
        // 4xx errors or final attempt - don't retry
        if (!string.IsNullOrWhiteSpace(error))
        {
            Console.WriteLine("Connection / request error:");
            Console.WriteLine(error);
        }
        else
        {
            Console.WriteLine($"Unipile API key FAILED (HTTP {statusCode})");
            Console.WriteLine("Response:");
            Console.WriteLine(body);
        }
        
        return false;
    }
    
    return false;
}

static async Task<(bool ok, int statusCode, string body, string error)> VerifyUnipileApiKeyAsync(string dsn, string apiKey)
{
    var url = $"{dsn}/api/v1/accounts";

    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-API-KEY", apiKey);

        var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        return (response.IsSuccessStatusCode, (int)response.StatusCode, body, "");
    }
    catch (Exception ex)
    {
        return (false, 0, "", ex.Message);
    }
}