using System.Diagnostics;
using System.Text;
using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Entities;
using Serilog;

namespace GIMI_ModManager.Core.Services;

public sealed class DragAndDropScanner
{
    private readonly ILogger _logger = Log.ForContext<DragAndDropScanner>();
    private readonly string _baseTmpFolder = Path.Combine(Path.GetTempPath(), "JASM_TMP");
    private readonly ExtractTool _extractTool;

    public DragAndDropScanner()
    {
        _extractTool = GetExtractTool();
    }

    public DragAndDropScanResult ScanAndGetContents(string path, string? password = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or empty", nameof(path));

        Directory.CreateDirectory(_baseTmpFolder);

        // Allocate a dedicated unique session directory per scan to eliminate path collision and retry nesting
        var sessionFolder = Path.Combine(_baseTmpFolder, Guid.NewGuid().ToString("N"));
        var targetExtractionFolder = Path.Combine(sessionFolder, Path.GetFileName(path));

        Directory.CreateDirectory(sessionFolder);
        Directory.CreateDirectory(targetExtractionFolder);

        if (Directory.Exists(path))
        {
            var modFolder = new Mod(new DirectoryInfo(path));
            modFolder.CopyTo(targetExtractionFolder);

            return new DragAndDropScanResult
            {
                ExtractedFolder = new Mod(new DirectoryInfo(targetExtractionFolder).Parent!),
                exitedCode = 0,
                IsSuccess = true,
                IsPasswordRequired = false
            };
        }

        if (!File.Exists(path) || !IsArchive(path))
        {
            throw new FileNotFoundException("No valid mod archive or folder found", path);
        }

        var (exitCode, isPasswordRequired, output) = Extract7Z(path, targetExtractionFolder, password);

        bool hasExtractedFiles = Directory.Exists(targetExtractionFolder) &&
                                 Directory.EnumerateFileSystemEntries(targetExtractionFolder).Any();

        bool isSuccess = false;
        if (exitCode == 0)
        {
            isSuccess = true;
        }
        else if (exitCode == 1 && hasExtractedFiles && !isPasswordRequired)
        {
            // Exit code 1 indicates non-fatal 7-Zip warning(s) (e.g. file lock during timestamp/attribute setting).
            // Files were extracted and no password error occurred, so treat as successful extraction.
            _logger.Warning("7z completed with warning(s) (exit code 1), but files were extracted successfully: {Output}", output);
            isSuccess = true;
        }

        if (!isSuccess)
        {
            try
            {
                if (Directory.Exists(sessionFolder))
                {
                    Directory.Delete(sessionFolder, true);
                    _logger.Information("Cleaned up session folder after failed extraction: {Folder}", sessionFolder);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to clean up session folder: {Folder}", sessionFolder);
            }
        }

        return new DragAndDropScanResult
        {
            ExtractedFolder = new Mod(new DirectoryInfo(targetExtractionFolder).Parent!),
            exitedCode = exitCode,
            IsSuccess = isSuccess,
            IsPasswordRequired = isPasswordRequired,
            ErrorMessage = isSuccess ? null : GetUserFriendlyErrorMessage(output, exitCode)
        };
    }

    private static bool IsArchive(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".zip" => true,
            ".rar" => true,
            ".7z" => true,
            _ => false
        };
    }

    private static string? Resolve7ZipPath()
    {
        var bundledPath = Path.Combine(AppContext.BaseDirectory, @"Assets\7z\7z.exe");
        if (File.Exists(bundledPath))
            return bundledPath;

        var relativeTestPath = Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\GIMI-ModManager.WinUI\Assets\7z\7z.exe");
        if (File.Exists(relativeTestPath))
            return Path.GetFullPath(relativeTestPath);

        return null;
    }

    private ExtractTool GetExtractTool()
    {
        var sevenZipPath = Resolve7ZipPath();
        if (sevenZipPath != null &&
            File.Exists(Path.Combine(Path.GetDirectoryName(sevenZipPath)!, "7-zip.dll")) &&
            File.Exists(Path.Combine(Path.GetDirectoryName(sevenZipPath)!, "7z.dll")))
        {
            _logger.Debug("Using bundled 7zip: {Path}", sevenZipPath);
            return ExtractTool.Bundled7Zip;
        }

        _logger.Information("Bundled 7zip not found");
        return ExtractTool.SharpCompress;
    }

    private (int exitCode, bool isPasswordRequired, string output) Extract7Z(string archivePath, string destinationFolder, string? password = null)
    {
        var sevenZipPath = Resolve7ZipPath() ?? Path.Combine(AppContext.BaseDirectory, @"Assets\7z\7z.exe");
        var args = $"x \"{archivePath}\" -o\"{destinationFolder}\" -y";
        args += password is not null ? $" -p\"{password}\"" : " -p-";

        var process = new Process
        {
            StartInfo =
            {
                FileName = sevenZipPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        _logger.Information("Extracting archive with command: {Command}", process.StartInfo.Arguments);

        var outputBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                lock (outputBuilder)
                {
                    outputBuilder.AppendLine(e.Data);
                }
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                lock (outputBuilder)
                {
                    outputBuilder.AppendLine(e.Data);
                }
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        string fullOutput;
        lock (outputBuilder)
        {
            fullOutput = outputBuilder.ToString();
        }

        int exitCode = process.ExitCode;
        _logger.Information("7z extraction finished with exit code {ExitCode}", exitCode);

        bool isPasswordRequired = IsPasswordRequiredError(fullOutput);
        return (exitCode, isPasswordRequired, fullOutput);
    }

    public static bool IsPasswordRequiredError(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;

        return output.Contains("Wrong password", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Cannot open encrypted archive", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Data Error in encrypted file", StringComparison.OrdinalIgnoreCase)
            || output.Contains("encrypted file", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUserFriendlyErrorMessage(string output, int exitCode)
    {
        if (string.IsNullOrWhiteSpace(output))
            return $"Extraction failed with exit code {exitCode}";

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var errorLines = lines.Where(l => l.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase) ||
                                          l.Contains("Cannot open", StringComparison.OrdinalIgnoreCase) ||
                                          l.Contains("Can't open as archive", StringComparison.OrdinalIgnoreCase)).ToList();

        if (errorLines.Count > 0)
            return string.Join("; ", errorLines.Take(2));

        return $"Extraction failed with exit code {exitCode}";
    }

    private enum ExtractTool
    {
        Bundled7Zip,
        SharpCompress,
        System7Zip
    }
}

public class DragAndDropScanResult
{
    public IMod ExtractedFolder { get; init; } = null!;
    public string[] IgnoredMods { get; init; } = Array.Empty<string>();
    public int exitedCode { get; init; } = 0;
    public bool IsPasswordRequired { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
}