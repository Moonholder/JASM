﻿using System.Diagnostics;
using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Entities;
using Serilog;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;

namespace GIMI_ModManager.Core.Services;

// Extract process if archive file:
// 1. Copy archive to work folder in windows temp folder
// 2. Extract archive to windows temp folder
// 3. Move extracted files to work folder
// 4. Delete copied archive

public sealed class DragAndDropScanner
{
    private readonly ILogger _logger = Log.ForContext<DragAndDropScanner>();
    private readonly string _tmpFolder = Path.Combine(Path.GetTempPath(), "JASM_TMP");

    // Extracts files to this folder
    private string _workFolder = Path.Combine(Path.GetTempPath(), "JASM_TMP", Guid.NewGuid().ToString("N"));

    private ExtractTool _extractTool;

    public DragAndDropScanner()
    {
        _extractTool = GetExtractTool();
    }

    public DragAndDropScanResult ScanAndGetContents(string path, string? password = null)
    {
        PrepareWorkFolder();

        _workFolder = Path.Combine(_workFolder, Path.GetFileName(path));

        var exitCode = 0;

        if (IsArchive(path))
        {
            var copiedArchive = new FileInfo(path);
            copiedArchive = copiedArchive.CopyTo(Path.Combine(_tmpFolder, Path.GetFileName(path)), true);
            if (password is not null)
            {
                exitCode = Extract7Z(copiedArchive.FullName, password);
            }
            else
            {
                var result = Extractor(copiedArchive.FullName);
                exitCode = result?.Invoke(copiedArchive.FullName) ?? 0;
            }
        }
        else if (Directory.Exists(path)) // ModDragAndDropService handles loose folders, but this added just in case 
        {
            var modFolder = new Mod(new DirectoryInfo(path));
            modFolder.CopyTo(_workFolder);
        }
        else
            throw new Exception("没有找到有效的压缩文件或文件夹");


        return new DragAndDropScanResult()
        {
            ExtractedFolder = new Mod(new DirectoryInfo(_workFolder).Parent!),
            exitedCode = exitCode
        };
    }

    private void PrepareWorkFolder()
    {
        Directory.CreateDirectory(_tmpFolder);
        Directory.CreateDirectory(_workFolder);
    }

    private bool IsArchive(string path)
    {
        return Path.GetExtension(path) switch
        {
            ".zip" => true,
            ".rar" => true,
            ".7z" => true,
            _ => false
        };
    }

    private Func<string, int>? Extractor(string path, string? password = null)
    {
        Func<string, int>? action = null;

        if (_extractTool == ExtractTool.Bundled7Zip)
            action = (path) =>
            {
                return Extract7Z(path);
            };
        else if (_extractTool == ExtractTool.System7Zip) throw new NotImplementedException();

        return action;
    }

    private void ExtractEntries(IArchive archive)
    {
        _logger.Information("Extracting {ArchiveType} archive", archive.Type);
        foreach (var entry in archive.Entries)
        {
            _logger.Debug("Extracting {EntryName}", entry.Key);
            entry.WriteToDirectory(_workFolder, new ExtractionOptions()
            {
                ExtractFullPath = true,
                Overwrite = true,
                PreserveFileTime = false
            });
        }
    }

    private void SharpExtractZip(string path)
    {
        using var archive = ZipArchive.Open(path);
        ExtractEntries(archive);
    }


    private void SharpExtractRar(string path)
    {
        using var archive = RarArchive.Open(path);
        ExtractEntries(archive);
    }

    // ReSharper disable once InconsistentNaming
    private void SharpExtract7z(string path)
    {
        using var archive = ArchiveFactory.Open(path);
        ExtractEntries(archive);
    }


    private enum ExtractTool
    {
        Bundled7Zip, // 7zip bundled with JASM
        SharpCompress, // SharpCompress library
        System7Zip // 7zip installed on the system
    }

    private ExtractTool GetExtractTool()
    {
        var bundled7ZFolder = Path.Combine(AppContext.BaseDirectory, @"Assets\7z\");
        if (File.Exists(Path.Combine(bundled7ZFolder, "7z.exe")) &&
            File.Exists(Path.Combine(bundled7ZFolder, "7-zip.dll")) &&
            File.Exists(Path.Combine(bundled7ZFolder, "7z.dll")))
        {
            _logger.Debug("Using bundled 7zip");
            return ExtractTool.Bundled7Zip;
        }

        _logger.Information("Bundled 7zip not found, using SharpCompress library");
        return ExtractTool.SharpCompress;
    }

    private int Extract7Z(string path, string? password = null)
    {
        var sevenZipPath = Path.Combine(AppContext.BaseDirectory, @"Assets\7z\7z.exe");
        var args = $"x \"{path}\" -o\"{_workFolder}\" -y";
        args += password is not null ? $" -p\"{password}\"" : " -p-";
        // 先尝试无密码解压
        var process = new Process
        {
            StartInfo =
            {
                FileName = sevenZipPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            }
        };

        _logger.Information("Extracting 7z archive with command: {Command}", process.StartInfo.Arguments);
        process.Start();

        // 读取标准输出
        var output = new System.Text.StringBuilder();
        var passwordRequired = false;
        var passwordProvided = password != null;

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                output.AppendLine(e.Data);
                if (e.Data.Contains("Can't open as archive"))
                {
                    passwordRequired = true;
                    if (!passwordProvided)
                    {
                        // 密码为空，直接返回 exitcode = 1
                        _logger.Warning("Password is required but not provided");
                        process.Kill();
                    }
                }
            }
        };
        process.BeginOutputReadLine();
        process.WaitForExit();

        // 如果密码为空且密码被要求，返回1
        int exitCode = (passwordRequired && !passwordProvided) ? 1 : process.ExitCode;
        _logger.Information("7z extraction finished with exit code {ExitCode}", exitCode);
        if (exitCode != 0 && Directory.Exists(_workFolder))
        {
            Directory.Delete(_workFolder, true);
            _logger.Error("Failed to extract 7z archive, deleting work folder");
        }
        return exitCode;
    }

}

public class DragAndDropScanResult
{
    public IMod ExtractedFolder { get; init; } = null!;
    public string[] IgnoredMods { get; init; } = Array.Empty<string>();
    public int exitedCode { get; init; } = 0;
}