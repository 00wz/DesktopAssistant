using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace DesktopAssistant.Infrastructure.MCP.Plugins;

/// <summary>
/// Basic tools for file system operations and command execution
/// </summary>
public class CoreToolsPlugin
{
    private readonly ILogger<CoreToolsPlugin> _logger;
    
    public CoreToolsPlugin(ILogger<CoreToolsPlugin> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Executes a command in the terminal
    /// </summary>
    [KernelFunction("execute_command")]
    [Description("Executes a command in the terminal and returns the execution result including stdout, stderr, and exit code.")]
    public async Task<string> ExecuteCommandAsync(
        [Description("Command to execute (e.g. git clone https://github.com/repo)")] string command,
        [Description("Working directory for the command (optional)")] string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine shell based on OS
            string shell, shellArgs;
            if (OperatingSystem.IsWindows())
            {
                shell = "cmd.exe";
                shellArgs = $"/c {command}";
            }
            else
            {
                shell = "/bin/bash";
                shellArgs = $"-c \"{command.Replace("\"", "\\\"")}\"";
            }
            
            var startInfo = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = shellArgs,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
            };
            
            // Create working directory if it does not exist
            if (!string.IsNullOrEmpty(workingDirectory) && !Directory.Exists(workingDirectory))
            {
                Directory.CreateDirectory(workingDirectory);
            }
            
            using var process = new Process { StartInfo = startInfo };
            var output = new StringBuilder();
            var error = new StringBuilder();
            
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    output.AppendLine(e.Data);
                }
            };
            
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    error.AppendLine(e.Data);
                }
            };
            
            process.Start();
            process.StandardInput.Close(); // prevent interactive prompts from blocking
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            // 5-minute timeout for long operations (npm install, build)
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                var reason = cancellationToken.IsCancellationRequested ? "was cancelled" : "exceeded timeout (5 minutes)";
                return $"Error: command {reason}\nOutput before cancellation:\n{output}\n\nErrors:\n{error}";
            }
            
            var result = new StringBuilder();
            result.AppendLine($"Exit code: {process.ExitCode}");
            
            if (output.Length > 0)
            {
                result.AppendLine("\nOutput:");
                result.AppendLine(output.ToString());
            }

            if (error.Length > 0)
            {
                result.AppendLine("\nErrors/warnings:");
                result.AppendLine(error.ToString());
            }
            
            return result.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command: {Command}", command);
            return $"Error executing command: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Reads file contents
    /// </summary>
    [KernelFunction("read_file")]
    [Description("Reads the contents of a text file and returns its text.")]
    public async Task<string> ReadFileAsync(
        [Description("Path to the file to read")] string path)
    {
        try
        {
            // Expand ~ to home directory
            path = ExpandPath(path);

            if (!File.Exists(path))
            {
                return $"File not found: {path}";
            }
            
            var content = await File.ReadAllTextAsync(path);
            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file: {Path}", path);
            return $"Error reading file: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Writes content to a file
    /// </summary>
    [KernelFunction("write_to_file")]
    [Description("Writes content to a file. Creates directories if they do not exist.")]
    public async Task<string> WriteToFileAsync(
        [Description("Path to the file to write")] string path,
        [Description("Content to write to the file")] string content)
    {
        try
        {
            // Expand ~ to home directory
            path = ExpandPath(path);

            // Create directory if it does not exist
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            await File.WriteAllTextAsync(path, content);
            
            return $"File written successfully: {path}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing to file: {Path}", path);
            return $"Error writing file: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Checks whether a file or directory exists
    /// </summary>
    [KernelFunction("path_exists")]
    [Description("Checks whether a file or directory exists at the specified path.")]
    public string PathExists(
        [Description("Path to check")] string path)
    {
        path = ExpandPath(path);
        
        if (File.Exists(path))
        {
            return $"File exists: {path}";
        }

        if (Directory.Exists(path))
        {
            return $"Directory exists: {path}";
        }

        return $"Path does not exist: {path}";
    }
    
    /// <summary>
    /// Lists files in a directory
    /// </summary>
    [KernelFunction("list_directory")]
    [Description("Returns a list of files and subdirectories in the specified directory.")]
    public string ListDirectory(
        [Description("Path to the directory")] string path)
    {
        path = ExpandPath(path);
        
        if (!Directory.Exists(path))
        {
            return $"Directory does not exist: {path}";
        }

        try
        {
            var result = new StringBuilder();
            result.AppendLine($"Directory contents: {path}\n");

            var dirs = Directory.GetDirectories(path);
            if (dirs.Length > 0)
            {
                result.AppendLine("Directories:");
                foreach (var dir in dirs)
                {
                    result.AppendLine($"  [DIR] {Path.GetFileName(dir)}");
                }
            }

            var files = Directory.GetFiles(path);
            if (files.Length > 0)
            {
                result.AppendLine("\nFiles:");
                foreach (var file in files)
                {
                    var info = new FileInfo(file);
                    result.AppendLine($"  {Path.GetFileName(file)} ({info.Length} bytes)");
                }
            }

            return result.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing directory: {Path}", path);
            return $"Error reading directory: {ex.Message}";
        }
    }
    
#if DEBUG
    /// <summary>
    /// Test function: waits 10 seconds
    /// </summary>
    [KernelFunction("test_wait")]
    [Description("Test function: waits 10 seconds and returns a message. Used for testing cancellation and timeouts.")]
    public async Task<string> TestWaitAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("TestWait: starting 10-second wait");
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        _logger.LogInformation("TestWait: wait complete");
        return "10-second wait completed successfully.";
    }
#endif

    private static string ExpandPath(string path)
    {
        if (path.StartsWith("~"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(home, path.Substring(1).TrimStart('/', '\\'));
        }
        
        return Path.GetFullPath(path);
    }
}
