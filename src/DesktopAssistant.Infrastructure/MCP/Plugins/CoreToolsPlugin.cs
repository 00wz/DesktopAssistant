using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace DesktopAssistant.Infrastructure.MCP.Plugins;

/// <summary>
/// Базовые tools для работы с файлами и командами
/// Используются AI для самостоятельной установки MCP серверов
/// </summary>
public class CoreToolsPlugin
{
    private readonly ILogger<CoreToolsPlugin> _logger;
    
    public CoreToolsPlugin(ILogger<CoreToolsPlugin> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Выполняет команду в терминале
    /// </summary>
    [KernelFunction("execute_command")]
    [Description("Выполняет команду в терминале и возвращает результат. Используется для git clone, npm install, npm run build и других команд установки.")]
    public async Task<string> ExecuteCommandAsync(
        [Description("Команда для выполнения (например: git clone https://github.com/repo)")] string command,
        [Description("Рабочая директория для выполнения команды (опционально)")] string? workingDirectory = null)
    {
        _logger.LogInformation("Executing command: {Command} in {Directory}", command, workingDirectory ?? "current directory");
        
        try
        {
            // Определяем shell в зависимости от ОС
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
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
            };
            
            // Создаём рабочую директорию если не существует
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
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            // Таймаут 5 минут для длинных операций (npm install, build)
            var completed = await Task.Run(() => process.WaitForExit(300000));
            
            if (!completed)
            {
                process.Kill();
                return $"Ошибка: команда превысила таймаут (5 минут)\nВывод до таймаута:\n{output}\n\nОшибки:\n{error}";
            }
            
            var result = new StringBuilder();
            result.AppendLine($"Exit code: {process.ExitCode}");
            
            if (output.Length > 0)
            {
                result.AppendLine("\nВывод:");
                result.AppendLine(output.ToString());
            }
            
            if (error.Length > 0)
            {
                result.AppendLine("\nОшибки/предупреждения:");
                result.AppendLine(error.ToString());
            }
            
            // Ограничиваем размер вывода
            var resultStr = result.ToString();
            if (resultStr.Length > 10000)
            {
                resultStr = resultStr.Substring(0, 10000) + "\n\n[Вывод обрезан из-за размера]";
            }
            
            _logger.LogDebug("Command completed with exit code {ExitCode}", process.ExitCode);
            return resultStr;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command: {Command}", command);
            return $"Ошибка выполнения команды: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Читает содержимое файла
    /// </summary>
    [KernelFunction("read_file")]
    [Description("Читает содержимое файла и возвращает его текст. Используется для чтения текущей конфигурации mcp.json и других файлов.")]
    public async Task<string> ReadFileAsync(
        [Description("Путь к файлу для чтения")] string path)
    {
        _logger.LogDebug("Reading file: {Path}", path);
        
        try
        {
            // Расширяем ~ до домашней директории
            path = ExpandPath(path);
            
            if (!File.Exists(path))
            {
                return $"Файл не найден: {path}";
            }
            
            var content = await File.ReadAllTextAsync(path);
            
            // Ограничиваем размер
            if (content.Length > 50000)
            {
                content = content.Substring(0, 50000) + "\n\n[Файл обрезан из-за размера]";
            }
            
            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file: {Path}", path);
            return $"Ошибка чтения файла: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Записывает содержимое в файл
    /// </summary>
    [KernelFunction("write_to_file")]
    [Description("Записывает содержимое в файл. Создаёт директории если нужно. Используется для редактирования конфигурации mcp.json.")]
    public async Task<string> WriteToFileAsync(
        [Description("Путь к файлу для записи")] string path,
        [Description("Содержимое для записи в файл")] string content)
    {
        _logger.LogDebug("Writing to file: {Path}", path);
        
        try
        {
            // Расширяем ~ до домашней директории
            path = ExpandPath(path);
            
            // Создаём директорию если не существует
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            await File.WriteAllTextAsync(path, content);
            
            return $"Файл успешно записан: {path}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing to file: {Path}", path);
            return $"Ошибка записи файла: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Проверяет существование файла или директории
    /// </summary>
    [KernelFunction("path_exists")]
    [Description("Проверяет существование файла или директории по указанному пути.")]
    public string PathExists(
        [Description("Путь для проверки")] string path)
    {
        path = ExpandPath(path);
        
        if (File.Exists(path))
        {
            return $"Файл существует: {path}";
        }
        
        if (Directory.Exists(path))
        {
            return $"Директория существует: {path}";
        }
        
        return $"Путь не существует: {path}";
    }
    
    /// <summary>
    /// Список файлов в директории
    /// </summary>
    [KernelFunction("list_directory")]
    [Description("Возвращает список файлов и поддиректорий в указанной директории.")]
    public string ListDirectory(
        [Description("Путь к директории")] string path)
    {
        path = ExpandPath(path);
        
        if (!Directory.Exists(path))
        {
            return $"Директория не существует: {path}";
        }
        
        try
        {
            var result = new StringBuilder();
            result.AppendLine($"Содержимое директории: {path}\n");
            
            var dirs = Directory.GetDirectories(path);
            if (dirs.Length > 0)
            {
                result.AppendLine("Директории:");
                foreach (var dir in dirs.Take(50))
                {
                    result.AppendLine($"  [DIR] {Path.GetFileName(dir)}");
                }
                if (dirs.Length > 50)
                {
                    result.AppendLine($"  ... и ещё {dirs.Length - 50} директорий");
                }
            }
            
            var files = Directory.GetFiles(path);
            if (files.Length > 0)
            {
                result.AppendLine("\nФайлы:");
                foreach (var file in files.Take(50))
                {
                    var info = new FileInfo(file);
                    result.AppendLine($"  {Path.GetFileName(file)} ({info.Length} bytes)");
                }
                if (files.Length > 50)
                {
                    result.AppendLine($"  ... и ещё {files.Length - 50} файлов");
                }
            }
            
            return result.ToString();
        }
        catch (Exception ex)
        {
            return $"Ошибка чтения директории: {ex.Message}";
        }
    }
    
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
