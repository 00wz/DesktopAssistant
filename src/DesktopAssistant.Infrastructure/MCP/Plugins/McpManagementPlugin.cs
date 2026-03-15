using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Infrastructure.MCP.Models;
using DesktopAssistant.Infrastructure.MCP.Search;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace DesktopAssistant.Infrastructure.MCP.Plugins;

/// <summary>
/// Plugin with tools for managing MCP servers (search, installation)
/// </summary>
public class McpManagementPlugin
{
    private readonly ILogger<McpManagementPlugin> _logger;
    private readonly HttpClient _httpClient;
    private readonly IMcpServerManager _serverManager;
    private readonly IMcpConfigurationService _configService;
    private readonly IMcpCatalogSearchService _catalogSearch;

    public McpManagementPlugin(
        ILogger<McpManagementPlugin> logger,
        IMcpServerManager serverManager,
        IMcpConfigurationService configService,
        IMcpCatalogSearchService catalogSearch,
        HttpClient? httpClient = null)
    {
        _logger = logger;
        _serverManager = serverManager;
        _configService = configService;
        _catalogSearch = catalogSearch;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DesktopAssistant", "1.0"));
    }
    
    /// <summary>
    /// Searches for MCP servers in the catalog by description or tags
    /// </summary>
    [KernelFunction("search_mcp_servers")]
    [Description("Search for MCP servers in the catalog by task description or keywords. Returns a list of matching servers with GitHub URLs. IMPORTANT: After finding a server, you MUST load the README via fetch_mcp_server_readme to get the EXACT installation command.")]
    public async Task<string> SearchMcpServersAsync(
        [Description("Search query: task description or keywords")] string query)
    {
        var results = await _catalogSearch.SearchAsync(query);

        if (results.Count == 0)
        {
            return $"No MCP servers found for query '{query}'. Try different keywords.";
        }

        var resultText = $"Found {results.Count} MCP servers:\n\n";

        foreach (var s in results)
        {
            resultText += $"**{s.Name}** (id: {s.Id})\n";
            resultText += $"Description: {s.Description}\n";
            resultText += $"GitHub: {s.GitHubUrl}\n";
            resultText += $"Tags: {string.Join(", ", s.Tags)}\n\n";
        }

        resultText += "\nTo install, load the README via fetch_mcp_server_readme and follow the instructions.\n";

        return resultText;
    }
    
    /// <summary>
    /// Loads README.md from the MCP server repository
    /// </summary>
    [KernelFunction("fetch_mcp_server_readme")]
    [Description("Loads README.md from the GitHub repository of an MCP server. The README contains the EXACT installation command (npx ...) — use it AS-IS, do not change the package name! Look for lines like 'npx -y package-name' or a configuration section.")]
    public async Task<string> FetchMcpServerReadmeAsync(
        [Description("GitHub repository URL (e.g. https://github.com/upstash/context7-mcp)")] string githubUrl)
    {
        try
        {
            // Convert GitHub URL to raw URL for README
            var rawUrl = ConvertToRawReadmeUrl(githubUrl);
            _logger.LogDebug("[TOOL fetch_mcp_server_readme] Converted to raw URL: {RawUrl}", rawUrl);
            
            var response = await _httpClient.GetAsync(rawUrl);
            _logger.LogDebug("[TOOL fetch_mcp_server_readme] Response status: {Status}", response.StatusCode);
            
            if (!response.IsSuccessStatusCode)
            {
                // Try alternative names
                var altUrls = new[]
                {
                    rawUrl.Replace("README.md", "readme.md"),
                    rawUrl.Replace("README.md", "Readme.md"),
                    rawUrl.Replace("/main/", "/master/"),
                    rawUrl.Replace("/main/", "/master/").Replace("README.md", "readme.md"),
                };
                
                foreach (var altUrl in altUrls)
                {
                    _logger.LogDebug("[TOOL fetch_mcp_server_readme] Trying alternative URL: {AltUrl}", altUrl);
                    response = await _httpClient.GetAsync(altUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogDebug("[TOOL fetch_mcp_server_readme] Success with URL: {AltUrl}", altUrl);
                        break;
                    }
                }
            }
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[TOOL fetch_mcp_server_readme] Failed to load README, status: {Status}", response.StatusCode);
                return $"Failed to load README from {githubUrl}. Response code: {response.StatusCode}";
            }
            
            var readme = await response.Content.ReadAsStringAsync();
            
            return $"README from {githubUrl}:\n\n{readme}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching README from {Url}", githubUrl);
            return $"Error loading README: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Returns the path to the MCP configuration file
    /// </summary>
    [KernelFunction("get_mcp_config_path")]
    [Description("Returns the path to the MCP server configuration file (mcp.json). Use for informational purposes — to add a server, use add_mcp_server instead.")]
    public string GetMcpConfigPath()
    {
        return _configService.ConfigFilePath;
    }
    
    /// <summary>
    /// Returns the path for cloning MCP servers
    /// </summary>
    [KernelFunction("get_mcp_servers_directory")]
    [Description("Returns the path to the directory for cloning MCP servers")]
    public string GetMcpServersDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".desktopasst",
            "mcp-servers");
    }
    
    /// <summary>
    /// Universal tool for adding an MCP server to the configuration.
    /// Works with any server: npx, node, python, etc.
    /// </summary>
    [KernelFunction("add_mcp_server")]
    [Description(@"Adds an MCP server to the configuration and attempts to connect.
Works with ANY server: npx, node, python, etc.

Examples:
- npx server: command='npx', args='[""-y"", ""tavily-mcp@0.2.1""]'
- node server: command='node', args='[""/path/to/server/build/index.js""]'
- python server: command='python', args='[""/path/to/server/main.py""]'

Automatically:
1. For npx — validates the npm package exists
2. Writes configuration to mcp.json
3. Attempts to connect to the server
4. Returns the result with available tools or an error

IMPORTANT: For npx servers the package name must be EXACTLY from the README!")]
    public async Task<string> AddMcpServerAsync(
        [Description("Unique server ID for the configuration (e.g. 'tavily', 'weather-server')")] string serverId,
        [Description("Launch command: 'npx', 'node', 'python', etc.")] string command,
        [Description("Command-line arguments as a JSON array")] string argsJson,
        [Description("Environment variables as a JSON object. Can be empty: {}")] string envJson)
    {
        try
        {
            // 1. Parse arguments
            List<string> args;
            try
            {
                args = JsonSerializer.Deserialize<List<string>>(argsJson) ?? new List<string>();
            }
            catch (JsonException ex)
            {
                return $"❌ ERROR: Invalid args JSON format: {ex.Message}\n" +
                       "Format must be a JSON array: [\"-y\", \"package-name@version\"]";
            }
            
            // 2. Parse env
            Dictionary<string, string> env;
            try
            {
                env = JsonSerializer.Deserialize<Dictionary<string, string>>(envJson) ?? new Dictionary<string, string>();
            }
            catch (JsonException ex)
            {
                return $"❌ ERROR: Invalid env JSON format: {ex.Message}\n" +
                       "Format must be a JSON object: {\"KEY\": \"value\"}";
            }
            
            // 3. Universal validation before writing configuration
            var validationError = await ValidateServerConfigAsync(command, args);
            if (validationError != null)
            {
                return validationError;
            }
            
            // 4. Write configuration
            var serverConfig = new McpServerConfigDto
            {
                Command = command,
                Args = args,
                Env = env,
                Enabled = true
            };
            
            await _configService.AddServerAsync(serverId, serverConfig);
            _logger.LogInformation("MCP server config written for '{ServerId}'", serverId);
            
            // 6. Wait briefly and attempt to connect
            await Task.Delay(500); // Give FileWatcher time to process the change

            // 7. Check connection status with several attempts
            const int maxAttempts = 120;
            const int delayMs = 1000;
            
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                // Use GetServerInfo to get information including errors
                var serverInfo = _serverManager.GetServerInfo(serverId);
                
                if (serverInfo != null)
                {
                    // Check for successful connection
                    if (serverInfo.Status == McpServerStatusDto.Connected)
                    {
                        var toolsList = serverInfo.Tools.Any()
                            ? string.Join("\n", serverInfo.Tools.Select(t => $"  - {t.Name}: {t.Description ?? "no description"}"))
                            : "  (no tools)";

                        return $"✅ MCP SERVER '{serverId}' SUCCESSFULLY INSTALLED AND CONNECTED!\n\n" +
                               $"Configuration:\n" +
                               $"  Command: {command}\n" +
                               $"  Args: {string.Join(" ", args)}\n" +
                               (env.Any() ? $"  Env: {env.Keys.Count} variables\n" : "") +
                               $"\nAvailable tools ({serverInfo.Tools.Count}):\n{toolsList}\n\n" +
                               "You can now use these tools to perform user tasks.";
                    }
                    
                    // Check for connection error — return immediately, don't wait
                    if (serverInfo.Status == McpServerStatusDto.Error)
                    {
                        return $"❌ CONNECTION ERROR for MCP server '{serverId}'!\n\n" +
                               $"Configuration written, but the server failed to start.\n" +
                               $"Error: {serverInfo.ErrorMessage}\n\n" +
                               "Possible reasons:\n" +
                               "1. Invalid command or argument format\n" +
                               "2. Missing required environment variables (API keys)\n" +
                               "3. The package requires additional configuration\n\n" +
                               "SOLUTION: Check the README and make sure all parameters are correct.";
                    }
                }
                
                if (attempt < maxAttempts)
                {
                    await Task.Delay(delayMs);
                }
            }
            
            // 8. Timeout — server still in Connecting status
            return $"⚠️ MCP server '{serverId}' — configuration written, but connection did not complete within {maxAttempts} seconds.\n\n" +
                   "Configuration saved to mcp.json.\n\n" +
                   "Possible reason: server is starting slowly";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error installing MCP server '{ServerId}'", serverId);
            return $"❌ CRITICAL ERROR installing MCP server: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Universal validation of server configuration before writing
    /// </summary>
    private async Task<string?> ValidateServerConfigAsync(string command, List<string> args)
    {
        var commandLower = command.ToLowerInvariant();
        
        // 1. For npx — validate npm package
        if (commandLower == "npx")
        {
            string? packageName = null;
            foreach (var arg in args)
            {
                if (!arg.StartsWith("-") && !arg.StartsWith("/"))
                {
                    packageName = arg;
                    break;
                }
            }
            
            if (!string.IsNullOrEmpty(packageName))
            {
                var validationResult = await ValidateNpmPackageInternalAsync(packageName);
                if (!validationResult.IsValid)
                {
                    return $"❌ npm PACKAGE VALIDATION ERROR!\n\n" +
                           $"Package: {packageName}\n" +
                           $"Error: {validationResult.Error}\n\n" +
                           "Possible reasons:\n" +
                           "1. Package name is misspelled\n" +
                           "2. You INVENTED the package name instead of copying it from the README\n\n" +
                           "SOLUTION: Return to README (fetch_mcp_server_readme) and find the EXACT package name!";
                }
            }
            return null;
        }
        
        // 2. For node/python — check script file existence
        if (commandLower == "node" || commandLower == "python" || commandLower == "python3")
        {
            // Find script file path in arguments
            string? scriptPath = null;
            foreach (var arg in args)
            {
                if (!arg.StartsWith("-") && (arg.EndsWith(".js") || arg.EndsWith(".py") || arg.EndsWith(".mjs")))
                {
                    scriptPath = arg;
                    break;
                }
            }
            
            if (!string.IsNullOrEmpty(scriptPath) && !File.Exists(scriptPath))
            {
                return $"❌ ERROR: Script file not found!\n\n" +
                       $"Path: {scriptPath}\n\n" +
                       "Possible reasons:\n" +
                       "1. The server was not cloned or not built\n" +
                       "2. Path is incorrect\n\n" +
                       "SOLUTION:\n" +
                       "1. Make sure the repository is cloned in get_mcp_servers_directory()\n" +
                       "2. Run the build: execute_command('npm install && npm run build')\n" +
                       "3. Verify the path to the built file";
            }
            return null;
        }
        
        // 3. Check that the command is available on the system
        var commandExists = await CheckCommandExistsAsync(command);
        if (!commandExists)
        {
            return $"⚠️ WARNING: Command '{command}' may not be available on the system.\n\n" +
                   "Configuration will be written, but the server may fail to start.\n" +
                   "Make sure the program is installed and available in PATH.";
        }
        
        return null;
    }
    
    /// <summary>
    /// Checks whether a command is available on the system
    /// </summary>
    private async Task<bool> CheckCommandExistsAsync(string command)
    {
        try
        {
            string shell, shellArgs;
            if (OperatingSystem.IsWindows())
            {
                shell = "cmd.exe";
                shellArgs = $"/c where {command}";
            }
            else
            {
                shell = "/bin/bash";
                shellArgs = $"-c \"which {command}\"";
            }
            
            var startInfo = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = shellArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.WaitForExitAsync();
            
            return process.ExitCode == 0;
        }
        catch
        {
            return true; // If check fails — skip
        }
    }
    
    /// <summary>
    /// Internal method for npm package validation
    /// </summary>
    private async Task<(bool IsValid, string? Error)> ValidateNpmPackageInternalAsync(string packageName)
    {
        try
        {
            // Strip version from package name for validation
            var cleanPackageName = packageName;
            
            // Handle scoped packages (@scope/name@version)
            if (packageName.StartsWith("@"))
            {
                var parts = packageName.Split('@');
                if (parts.Length >= 3)
                {
                    // @scope/name@version → @scope/name
                    cleanPackageName = "@" + parts[1];
                }
            }
            else if (packageName.Contains("@"))
            {
                // name@version → name
                cleanPackageName = packageName.Split('@')[0];
            }
            
            string shell, shellArgs;
            if (OperatingSystem.IsWindows())
            {
                shell = "cmd.exe";
                shellArgs = $"/c npm view {cleanPackageName} name --json 2>&1";
            }
            else
            {
                shell = "/bin/bash";
                shellArgs = $"-c \"npm view {cleanPackageName} name --json 2>&1\"";
            }
            
            var startInfo = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = shellArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            
            var completed = await Task.Run(() => process.WaitForExit(30000)); // 30 sec timeout
            
            if (!completed)
            {
                process.Kill();
                return (false, "Timeout while checking npm package");
            }
            
            if (process.ExitCode != 0)
            {
                return (false, $"Package not found in npm registry. Output: {output} {error}");
            }
            
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Validation error: {ex.Message}");
        }
    }
    
    private static string ConvertToRawReadmeUrl(string githubUrl)
    {
        // Handle URLs with subdirectories:
        // https://github.com/owner/repo/tree/main/src/subdir -> README from subdirectory
        // https://github.com/owner/repo/blob/main/src/subdir/README.md -> direct link
        // https://github.com/owner/repo -> README from root

        // Pattern for URL with tree (subdirectory)
        var treeMatch = Regex.Match(githubUrl, @"github\.com/([^/]+)/([^/]+)/tree/([^/]+)/(.+)");
        if (treeMatch.Success)
        {
            var owner = treeMatch.Groups[1].Value;
            var repo = treeMatch.Groups[2].Value;
            var branch = treeMatch.Groups[3].Value;
            var path = treeMatch.Groups[4].Value.TrimEnd('/');
            return $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{path}/README.md";
        }
        
        // Pattern for URL with blob (direct file link)
        var blobMatch = Regex.Match(githubUrl, @"github\.com/([^/]+)/([^/]+)/blob/([^/]+)/(.+)");
        if (blobMatch.Success)
        {
            var owner = blobMatch.Groups[1].Value;
            var repo = blobMatch.Groups[2].Value;
            var branch = blobMatch.Groups[3].Value;
            var filePath = blobMatch.Groups[4].Value;
            return $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{filePath}";
        }
        
        // Base pattern for repository root
        var baseMatch = Regex.Match(githubUrl, @"github\.com/([^/]+)/([^/]+)");
        if (baseMatch.Success)
        {
            var owner = baseMatch.Groups[1].Value;
            var repo = baseMatch.Groups[2].Value.TrimEnd('/');
            return $"https://raw.githubusercontent.com/{owner}/{repo}/main/README.md";
        }
        
        return githubUrl;
    }
}
