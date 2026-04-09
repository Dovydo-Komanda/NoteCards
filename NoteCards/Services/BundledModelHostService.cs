using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using NoteCards.Localization;
using NoteCards.Models;

namespace NoteCards.Services;

public sealed class BundledModelHostService
{
    public sealed record FlashcardProgress(string StatusKey, int? Percent = null, int? GeneratedChars = null);

    private sealed record ModelInfo(
        string Key,
        string FileName,
        string DisplayName,
        string DescriptionResourceKey,
        string DefaultDescription,
        string DownloadStatusKey,
        long MinimumBytes,
        long MinimumRecommendedMemoryBytes,
        string? Sha256,
        string[] Urls);

    private readonly SemaphoreSlim _sync = new(1, 1);
    private static readonly HttpClient DownloadClient = new()
    {
        Timeout = TimeSpan.FromMinutes(60)
    };

    public static BundledModelHostService Instance { get; } = new();

    private const int MaxOutputChars = 20000;
    private const int MaxCapturedStdoutChars = 250_000;
    private const int MaxCapturedStderrChars = 120_000;
    private static readonly TimeSpan PromptFileRetention = TimeSpan.FromHours(24);
    private static readonly TimeSpan InferenceTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan InferenceInactivityTimeout = TimeSpan.FromSeconds(90);

    private const string RuntimeExeName = "llama-completion.exe";
    private const string DefaultModelKey = "Qwen3.5-0.8B";
    private static readonly ModelInfo[] SupportedModels =
    [
        new(
            "Qwen3.5-0.8B",
            "Qwen3.5-0.8B-Q4_K_M.gguf",
            "Qwen3.5-0.8B",
            "FlashcardModel08BDescription",
            "faster, light",
            "ConvertToFlashcardsStatusDownloadingModel08B",
            100L * 1024L * 1024L,
            0,
            "de9bcb3f1b16e6e33ab42b3851e80945d4153631418c8494a77bfa46b3ac70ec",
            [
                "https://huggingface.co/unsloth/Qwen3.5-0.8B-GGUF/resolve/main/Qwen3.5-0.8B-Q4_K_M.gguf",
                "https://huggingface.co/unsloth/Qwen3.5-0.8B-GGUF/resolve/main/Qwen3.5-0.8B-Q4_K_M.gguf?download=true",
                "https://hf-mirror.com/unsloth/Qwen3.5-0.8B-GGUF/resolve/main/Qwen3.5-0.8B-Q4_K_M.gguf?download=true"
            ]),
        new(
            "Qwen3.5-2B",
            "Qwen3.5-2B-Q4_K_M.gguf",
            "Qwen3.5-2B",
            "FlashcardModel2BDescription",
            "balanced, better quality",
            "ConvertToFlashcardsStatusDownloadingModel2B",
            300L * 1024L * 1024L,
            6L * 1024L * 1024L * 1024L,
            "125b05d833c1decf4fb55f5f12e97737245cab9711841fc4cdbcd3afcfd39aae",
            [
                "https://huggingface.co/unsloth/Qwen3.5-2B-GGUF/resolve/main/Qwen3.5-2B-Q4_K_M.gguf",
                "https://huggingface.co/unsloth/Qwen3.5-2B-GGUF/resolve/main/Qwen3.5-2B-Q4_K_M.gguf?download=true",
                "https://hf-mirror.com/unsloth/Qwen3.5-2B-GGUF/resolve/main/Qwen3.5-2B-Q4_K_M.gguf?download=true"
            ]),
        new(
            "Qwen3.5-4B",
            "Qwen3.5-4B-Q4_K_M.gguf",
            "Qwen3.5-4B",
            "FlashcardModel4BDescription",
            "more quality, slower",
            "ConvertToFlashcardsStatusDownloadingModel4B",
            600L * 1024L * 1024L,
            12L * 1024L * 1024L * 1024L,
            "1d203c2196991da08bc5b191ab4727516f476f3167e3276f75a0c5257493aadb",
            [
                "https://huggingface.co/unsloth/Qwen3.5-4B-GGUF/resolve/main/Qwen3.5-4B-Q4_K_M.gguf",
                "https://huggingface.co/unsloth/Qwen3.5-4B-GGUF/resolve/main/Qwen3.5-4B-Q4_K_M.gguf?download=true",
                "https://hf-mirror.com/unsloth/Qwen3.5-4B-GGUF/resolve/main/Qwen3.5-4B-Q4_K_M.gguf?download=true"
            ])
    ];

    private const string CacheFolderName = "AiCache";

    static BundledModelHostService()
    {
        DownloadClient.DefaultRequestHeaders.UserAgent.ParseAdd("NoteCards/1.0 (+https://github.com/Dovydo-Komanda/NoteCards)");
    }

    public void Stop() { }

    public async Task<string> CompleteAsync(
        string prompt,
        int nPredict,
        double temperature,
        IProgress<FlashcardProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return string.Empty;

        await _sync.WaitAsync(cancellationToken);
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var runtimeAssetsDir = Path.Combine(baseDir, "Assets", "ModelRuntime");
            var modelsAssetsDir = Path.Combine(baseDir, "Assets", "Models");
            var runtimeCacheDir = Path.Combine(GetCacheRootDirectory(), "runtime");
            var modelsCacheDir = Path.Combine(GetCacheRootDirectory(), "models");
            Directory.CreateDirectory(runtimeAssetsDir);
            Directory.CreateDirectory(modelsAssetsDir);
            Directory.CreateDirectory(runtimeCacheDir);
            Directory.CreateDirectory(modelsCacheDir);

            progress?.Report(new FlashcardProgress("ConvertToFlashcardsStatusPreparingAssets"));
            var selectedModel = GetSelectedModelInfo();
            await EnsureBundledAssetsAsync(runtimeAssetsDir, modelsAssetsDir, runtimeCacheDir, modelsCacheDir, selectedModel, progress, cancellationToken);

            var cliPath = Path.Combine(runtimeCacheDir, RuntimeExeName);
            var modelPath = Path.Combine(modelsCacheDir, selectedModel.FileName);

            if (!File.Exists(cliPath))
                throw new FileNotFoundException("Bundled model runtime was not found.", cliPath);
            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"Bundled Qwen model file '{selectedModel.DisplayName}' was not found.", modelPath);

            var promptDir = Path.Combine(GetCacheRootDirectory(), "temp");
            Directory.CreateDirectory(promptDir);
            CleanupOldPromptFiles(promptDir);
            var promptFilePath = Path.Combine(promptDir, $"prompt-{Guid.NewGuid():N}.txt");
            try
            {
                progress?.Report(new FlashcardProgress("ConvertToFlashcardsStatusProcessing"));
                await File.WriteAllTextAsync(promptFilePath, prompt, Encoding.UTF8, cancellationToken);

                var threadCount = Math.Max(1, Environment.ProcessorCount - 1);
                var temperatureText = temperature.ToString(System.Globalization.CultureInfo.InvariantCulture);

                // === ANTI-THINKING + STABILITY PARAMETRAI ===
                var commandArgs = new[]
                {
                    "-m", modelPath,
                    "-f", promptFilePath,
                    "-n", nPredict.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--temp", temperatureText,
                    "--top-p", "0.95",
                    "--top-k", "40",
                    "--presence-penalty", "2.0",
                    "--repeat-penalty", "1.08",
                    "-c", "16384",
                    "-t", threadCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--no-display-prompt",
                    "-st",
                    "--simple-io"
                };

                var args = string.Join(" ", commandArgs.Select(QuoteArgumentForLog));

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = cliPath,
                        WorkingDirectory = runtimeCacheDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    }
                };

                foreach (var arg in commandArgs)
                    process.StartInfo.ArgumentList.Add(arg);

                if (!process.Start())
                    throw new InvalidOperationException("Failed to start bundled model runtime process.");

                try
                {
                    try { process.StandardInput.Close(); } catch { }

                    var outputBuilder = new StringBuilder();
                    var errorBuilder = new StringBuilder();
                    var streamLock = new object();
                    long lastActivityTicks = DateTime.UtcNow.Ticks;
                    var lastCpuTime = TimeSpan.Zero;
                    int generatedChars = 0;
                    var lastCharReportAt = DateTime.UtcNow;

                    var outputTask = PumpStreamAsync(process.StandardOutput.BaseStream, outputBuilder, streamLock, chunkLen =>
                    {
                        Interlocked.Exchange(ref lastActivityTicks, DateTime.UtcNow.Ticks);
                        var chars = Interlocked.Add(ref generatedChars, chunkLen);
                        if ((DateTime.UtcNow - lastCharReportAt) >= TimeSpan.FromMilliseconds(800))
                        {
                            lastCharReportAt = DateTime.UtcNow;
                            progress?.Report(new FlashcardProgress("ConvertToFlashcardsStatusProcessing", null, chars));
                        }
                    }, MaxCapturedStdoutChars, cancellationToken);

                    var errorTask = PumpStreamAsync(process.StandardError.BaseStream, errorBuilder, streamLock, _ =>
                        Interlocked.Exchange(ref lastActivityTicks, DateTime.UtcNow.Ticks), MaxCapturedStderrChars, cancellationToken);

                    var startedAt = DateTime.UtcNow;

                    while (!process.HasExited)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (TryGetTotalProcessorTime(process, out var cpuTime) && cpuTime > lastCpuTime)
                        {
                            lastCpuTime = cpuTime;
                            Interlocked.Exchange(ref lastActivityTicks, DateTime.UtcNow.Ticks);
                        }

                        if (DateTime.UtcNow - startedAt > InferenceTimeout)
                        {
                            TryKillProcess(process);
                            var logPath = WriteInferenceDiagnosticLog(args, outputBuilder.ToString(), errorBuilder.ToString(), generatedChars, null);
                            throw new TimeoutException($"AI generation timed out. Diagnostic log: {logPath}");
                        }

                        var lastActivity = new DateTime(Interlocked.Read(ref lastActivityTicks), DateTimeKind.Utc);
                        if (DateTime.UtcNow - lastActivity > InferenceInactivityTimeout)
                        {
                            TryKillProcess(process);
                            var logPath = WriteInferenceDiagnosticLog(args, outputBuilder.ToString(), errorBuilder.ToString(), generatedChars, null);
                            throw new TimeoutException($"AI generation stalled. Diagnostic log: {logPath}");
                        }

                        await Task.Delay(250, cancellationToken);
                    }

                    await process.WaitForExitAsync(cancellationToken);
                    await Task.WhenAll(outputTask, errorTask);

                    var output = outputBuilder.ToString();
                    var error = errorBuilder.ToString();

                    var normalizedOutput = NormalizeOutput(output);

                    if (process.ExitCode != 0)
                    {
                        if (!(process.ExitCode == 130 && normalizedOutput.Contains("q:", StringComparison.OrdinalIgnoreCase)))
                        {
                            var logPath = WriteInferenceDiagnosticLog(args, output, error, generatedChars, process.ExitCode);
                            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                                ? $"Bundled model runtime returned non-zero exit code. Diagnostic: {logPath}"
                                : $"{error.Trim()}\nDiagnostic: {logPath}");
                        }
                    }

                    progress?.Report(new FlashcardProgress("ConvertToFlashcardsStatusFinalizing"));
                    return normalizedOutput;
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        try { process.Kill(true); } catch { }
                    }
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(promptFilePath))
                        File.Delete(promptFilePath);
                }
                catch { }
            }
        }
        finally
        {
            _sync.Release();
        }
    }

    private static async Task PumpStreamAsync(
        Stream source,
        StringBuilder target,
        object streamLock,
        Action<int> onActivity,
        int maxChars,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            onActivity(read);
            var chunk = Encoding.UTF8.GetString(buffer, 0, read);

            lock (streamLock)
            {
                if (target.Length >= maxChars)
                    continue;

                var remaining = maxChars - target.Length;
                if (chunk.Length <= remaining)
                {
                    target.Append(chunk);
                }
                else
                {
                    target.Append(chunk.AsSpan(0, remaining));
                }
            }
        }
    }

    private static string WriteInferenceDiagnosticLog(string args, string output, string error, int generatedChars, int? exitCode)
    {
        try
        {
            var diagnosticsDir = Path.Combine(GetCacheRootDirectory(), "diagnostics");
            Directory.CreateDirectory(diagnosticsDir);
            var filePath = Path.Combine(diagnosticsDir, $"inference-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");

            var body = new StringBuilder();
            body.AppendLine($"TimestampUtc: {DateTime.UtcNow:O}");
            body.AppendLine($"ExitCode: {(exitCode.HasValue ? exitCode.Value.ToString() : "n/a")}");
            body.AppendLine($"GeneratedChars: {generatedChars}");
            body.AppendLine($"Args: {args}");
            body.AppendLine();
            body.AppendLine("--- STDERR ---");
            body.AppendLine(error);
            body.AppendLine();
            body.AppendLine("--- STDOUT (preview) ---");
            body.AppendLine(output.Length > 4000 ? output[..4000] : output);

            File.WriteAllText(filePath, body.ToString(), Encoding.UTF8);
            return filePath;
        }
        catch
        {
            return "(failed to write diagnostic log)";
        }
    }

    private static void TryKillProcess(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); } catch { }
    }

    private static bool TryGetTotalProcessorTime(Process process, out TimeSpan totalProcessorTime)
    {
        try
        {
            totalProcessorTime = process.TotalProcessorTime;
            return true;
        }
        catch
        {
            totalProcessorTime = TimeSpan.Zero;
            return false;
        }
    }

    private static ModelInfo GetSelectedModelInfo()
    {
        var key = AppSettingsService.Load().FlashcardModelKey;
        return SupportedModels.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? SupportedModels[0];
    }

    public static string GetRecommendedFlashcardModelKeyForCurrentMachine()
    {
        return GetRecommendedFlashcardModelKey(GetTotalPhysicalMemoryBytesForCurrentMachine());
    }

    public static string GetRecommendedFlashcardModelKey(long totalPhysicalMemoryBytes)
    {
        if (totalPhysicalMemoryBytes >= 12L * 1024L * 1024L * 1024L)
            return "Qwen3.5-4B";

        if (totalPhysicalMemoryBytes >= 6L * 1024L * 1024L * 1024L)
            return "Qwen3.5-2B";

        return DefaultModelKey;
    }

    public static string GetFlashcardModelDisplayLabel(string key, bool includeWarningPrefix = false, bool isCompatible = true)
    {
        var model = SupportedModels.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? SupportedModels[0];

        var label = $"{model.DisplayName} - {GetFlashcardModelDescription(key)}";
        if (includeWarningPrefix && !isCompatible)
            label = $"⚠️ {label}";

        return label;
    }

    public static string GetFlashcardModelDisplayName(string key)
    {
        return SupportedModels.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? SupportedModels[0].DisplayName;
    }

    public static string GetFlashcardModelDescription(string key)
    {
        var model = SupportedModels.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? SupportedModels[0];

        var localized = LocalizationService.GetString(model.DescriptionResourceKey);
        if (!string.IsNullOrWhiteSpace(localized) && !string.Equals(localized, model.DescriptionResourceKey, StringComparison.Ordinal))
            return localized;

        return model.DefaultDescription;
    }

    public static long GetFlashcardModelRequiredMemoryBytes(string key)
    {
        return SupportedModels.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase))?.MinimumRecommendedMemoryBytes
            ?? 0;
    }

    public static bool IsFlashcardModelCompatibleWithMemory(string key, long totalPhysicalMemoryBytes)
    {
        return totalPhysicalMemoryBytes >= GetFlashcardModelRequiredMemoryBytes(key);
    }

    public static long GetTotalPhysicalMemoryBytesForCurrentMachine()
    {
        try
        {
            var memoryStatus = new MEMORYSTATUSEX();
            memoryStatus.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            if (GlobalMemoryStatusEx(ref memoryStatus))
                return (long)memoryStatus.ullTotalPhys;
        }
        catch
        {
        }

        return 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public string GetSelectedModelDisplayName()
    {
        return GetSelectedModelInfo().DisplayName;
    }

    private static async Task EnsureBundledAssetsAsync(
        string runtimeAssetsDir,
        string modelsAssetsDir,
        string runtimeCacheDir,
        string modelsCacheDir,
        ModelInfo selectedModel,
        IProgress<FlashcardProgress>? progress,
        CancellationToken cancellationToken)
    {
        await EnsureRuntimeAssetsAsync(runtimeAssetsDir, runtimeCacheDir, cancellationToken);
        await EnsureModelAssetAsync(modelsAssetsDir, modelsCacheDir, selectedModel, progress, cancellationToken);
        progress?.Report(new FlashcardProgress("ConvertToFlashcardsStatusAssetsReady"));
    }

    private static async Task EnsureRuntimeAssetsAsync(string runtimeAssetsDir, string runtimeCacheDir, CancellationToken cancellationToken)
    {
        var cacheRuntimePath = Path.Combine(runtimeCacheDir, RuntimeExeName);
        if (IsRuntimePayloadAvailable(cacheRuntimePath)
            && await IsRuntimeRunnableAsync(cacheRuntimePath, cancellationToken))
        {
            return;
        }

        var bundledRuntimePath = Path.Combine(runtimeAssetsDir, RuntimeExeName);
        if (!IsRuntimePayloadAvailable(bundledRuntimePath))
            throw new FileNotFoundException("Bundled llama runtime is missing. Reinstall the app package to restore AI runtime assets.", bundledRuntimePath);

        TryDeleteDirectory(runtimeCacheDir);
        CopyRuntimePayload(bundledRuntimePath, cacheRuntimePath);

        if (!await IsRuntimeRunnableAsync(cacheRuntimePath, cancellationToken))
            throw new InvalidOperationException("Bundled llama runtime is present but could not be started from cache.");
    }

    private static async Task EnsureModelAssetAsync(
        string modelsAssetsDir,
        string modelsCacheDir,
        ModelInfo selectedModel,
        IProgress<FlashcardProgress>? progress,
        CancellationToken cancellationToken)
    {
        var cacheModelPath = Path.Combine(modelsCacheDir, selectedModel.FileName);
        if (IsModelFileValid(cacheModelPath, selectedModel))
            return;

        var bundledModelPath = Path.Combine(modelsAssetsDir, selectedModel.FileName);
        if (IsModelFileValid(bundledModelPath, selectedModel))
        {
            CopyToCache(bundledModelPath, cacheModelPath);
            if (IsModelFileValid(cacheModelPath, selectedModel))
                return;
        }

        progress?.Report(new FlashcardProgress(selectedModel.DownloadStatusKey, 0));
        await DownloadModelAsync(cacheModelPath, selectedModel, progress, cancellationToken);
    }

    private static async Task DownloadModelAsync(
        string modelPath,
        ModelInfo model,
        IProgress<FlashcardProgress>? progress,
        CancellationToken cancellationToken)
    {
        var urls = ResolveModelDownloadUrls(model.Urls);

        Exception? lastError = null;
        foreach (var modelUrl in urls)
        {
            var tempPath = $"{modelPath}.{Guid.NewGuid():N}.download";
            try
            {
                await DownloadFileWithRetryAsync(modelUrl, tempPath, model.DownloadStatusKey, progress, cancellationToken);
                ValidateDownloadedModelFile(tempPath, model);
                ValidateModelChecksum(tempPath, model);
                File.Move(tempPath, modelPath, overwrite: true);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                TryDeleteFile(tempPath);
            }
        }
        throw new InvalidOperationException("Failed to download bundled Qwen model automatically.", lastError);
    }

    private static IReadOnlyList<string> ResolveModelDownloadUrls(IEnumerable<string> modelUrls)
    {
        var safeDefaults = modelUrls
            .Where(IsSafeModelDownloadUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (safeDefaults.Length == 0)
            throw new InvalidOperationException("No allowlisted model download URLs are configured for the selected model.");

        var overrideUrl = Environment.GetEnvironmentVariable("NOTECARDS_QWEN_MODEL_URL");
        if (string.IsNullOrWhiteSpace(overrideUrl))
            return safeDefaults;

#if DEBUG
        if (Uri.TryCreate(overrideUrl, UriKind.Absolute, out var debugUri)
            && (string.Equals(debugUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || string.Equals(debugUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(debugUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)))
        {
            return [overrideUrl];
        }

        throw new InvalidOperationException("NOTECARDS_QWEN_MODEL_URL must be an HTTPS URL (or localhost) in Debug builds.");
#else
        return safeDefaults;
#endif
    }

    private static bool IsSafeModelDownloadUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        return string.Equals(uri.Host, "huggingface.co", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "hf-mirror.com", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateModelChecksum(string filePath, ModelInfo model)
    {
        var expectedHash = Environment.GetEnvironmentVariable("NOTECARDS_QWEN_MODEL_SHA256");
        if (string.IsNullOrWhiteSpace(expectedHash))
            expectedHash = model.Sha256;
        if (string.IsNullOrWhiteSpace(expectedHash))
            return;

        using var stream = File.OpenRead(filePath);
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(stream));
        var normalizedExpected = NormalizeSha256(expectedHash);

        if (!string.Equals(actualHash, normalizedExpected, StringComparison.Ordinal))
            throw new InvalidDataException("Downloaded model checksum does not match NOTECARDS_QWEN_MODEL_SHA256.");
    }

    private static string NormalizeSha256(string hash)
    {
        var cleaned = hash.Trim().ToLowerInvariant();
        if (cleaned.StartsWith("sha256:", StringComparison.Ordinal))
            cleaned = cleaned[7..];
        cleaned = cleaned.Replace("-", string.Empty, StringComparison.Ordinal);

        if (cleaned.Length != 64 || cleaned.Any(c => !IsHexChar(c)))
            throw new InvalidDataException("Model SHA-256 value must be a 64-character hexadecimal string.");

        return cleaned;
    }

    private static string QuoteArgumentForLog(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        return value.Any(char.IsWhiteSpace) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static async Task<FileStream> AcquireAssetsLockAsync(CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(GetCacheRootDirectory(), "assets.lock");
        Directory.CreateDirectory(GetCacheRootDirectory());

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(150, cancellationToken);
            }
        }
    }

    private static string? TryGetBundledModelChecksum(ModelInfo model)
    {
        try
        {
            var bundledModelPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Models", model.FileName);
            if (!IsModelFileValid(bundledModelPath, model))
                return null;

            using var stream = File.OpenRead(bundledModelPath);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        catch
        {
            return null;
        }
    }

    private static bool IsHexChar(char value)
    {
        return (value >= '0' && value <= '9')
            || (value >= 'a' && value <= 'f');
    }

    private static async Task DownloadFileWithRetryAsync(
        string url,
        string destinationPath,
        string statusKey,
        IProgress<FlashcardProgress>? progress,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await DownloadFileAsync(url, destinationPath, statusKey, progress, cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < 3)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException($"Failed to download file from '{url}'.", lastError);
    }

    private static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        string statusKey,
        IProgress<FlashcardProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        using var response = await DownloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var totalBytes = response.Content.Headers.ContentLength;
        var buffer = new byte[81920];
        long readBytes = 0;
        var lastPercent = -1;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            readBytes += read;

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                var percent = (int)Math.Clamp((readBytes * 100L) / totalBytes.Value, 0, 100);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    progress?.Report(new FlashcardProgress(statusKey, percent));
                }
            }
        }

        if (lastPercent < 100)
            progress?.Report(new FlashcardProgress(statusKey, 100));
    }


    private static string GetCacheRootDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NoteCards",
            CacheFolderName);
    }

    private static bool IsRuntimeExecutableValid(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length > 100 * 1024;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRuntimePayloadAvailable(string runtimeExePath)
    {
        if (!IsRuntimeExecutableValid(runtimeExePath))
            return false;

        var runtimeDir = Path.GetDirectoryName(runtimeExePath);
        if (string.IsNullOrWhiteSpace(runtimeDir) || !Directory.Exists(runtimeDir))
            return false;

        // Basic payload check: runtime folder should contain several dll dependencies.
        var dllCount = Directory.GetFiles(runtimeDir, "*.dll", SearchOption.TopDirectoryOnly).Length;
        return dllCount > 0;
    }

    private static void CopyRuntimePayload(string sourceExePath, string destinationExePath)
    {
        var sourceDir = Path.GetDirectoryName(sourceExePath);
        var destinationDir = Path.GetDirectoryName(destinationExePath);
        if (string.IsNullOrWhiteSpace(sourceDir) || string.IsNullOrWhiteSpace(destinationDir))
            throw new InvalidOperationException("Invalid runtime source or destination path.");

        Directory.CreateDirectory(destinationDir);

        foreach (var sourcePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, sourcePath);
            var destinationPath = Path.Combine(destinationDir, relativePath);
            var destinationSubDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationSubDir))
                Directory.CreateDirectory(destinationSubDir);

            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private static async Task<bool> IsRuntimeRunnableAsync(string runtimeExePath, CancellationToken cancellationToken)
    {
        try
        {
            var runtimeDir = Path.GetDirectoryName(runtimeExePath);
            if (string.IsNullOrWhiteSpace(runtimeDir))
                return false;

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = runtimeExePath,
                    Arguments = "--version",
                    WorkingDirectory = runtimeDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            if (!process.Start())
                return false;

            var waitTask = process.WaitForExitAsync(cancellationToken);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            var completed = await Task.WhenAny(waitTask, timeoutTask);
            if (completed == timeoutTask)
            {
                try { process.Kill(true); } catch { }
                return false;
            }

            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            if (error.Contains(".dll", StringComparison.OrdinalIgnoreCase)
                && error.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void CopyToCache(string sourcePath, string cachePath)
    {
        try
        {
            var cacheDir = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrWhiteSpace(cacheDir))
                Directory.CreateDirectory(cacheDir);

            File.Copy(sourcePath, cachePath, overwrite: true);
        }
        catch
        {
        }
    }

    private static void ValidateDownloadedModelFile(string filePath, ModelInfo? model = null)
    {
        var fileInfo = new FileInfo(filePath);
        var minBytes = model?.MinimumBytes ?? (1024L * 1024L);
        if (!fileInfo.Exists || fileInfo.Length < minBytes)
            throw new InvalidDataException("Downloaded Qwen model file is invalid or too small.");

        using var stream = File.OpenRead(filePath);
        Span<byte> header = stackalloc byte[4];
        var bytesRead = stream.Read(header);
        if (bytesRead < 4 || header[0] != (byte)'G' || header[1] != (byte)'G' || header[2] != (byte)'U' || header[3] != (byte)'F')
            throw new InvalidDataException("Downloaded file is not a valid GGUF model.");
    }

    private static bool IsModelFileValid(string filePath, ModelInfo? model = null)
    {
        try
        {
            ValidateDownloadedModelFile(filePath, model);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CleanupOldPromptFiles(string promptDir)
    {
        try
        {
            var cutoffUtc = DateTime.UtcNow - PromptFileRetention;
            foreach (var file in Directory.EnumerateFiles(promptDir, "prompt-*.txt", SearchOption.TopDirectoryOnly))
            {
                DateTime lastWriteUtc;
                try
                {
                    lastWriteUtc = File.GetLastWriteTimeUtc(file);
                }
                catch
                {
                    continue;
                }

                if (lastWriteUtc < cutoffUtc)
                    TryDeleteFile(file);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private static string NormalizeOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return string.Empty;

        var text = output.Trim();
        if (text.Length > MaxOutputChars)
            text = text[..MaxOutputChars];

        return text;
    }
}
