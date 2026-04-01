using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace BaoBaoPaddleOCR;

public interface IBaoBaoPaddleOcrClient
{
    OcrResult Detect(string imagePath);
}

public sealed class BaoBaoPaddleOcrClient : IBaoBaoPaddleOcrClient, IDisposable
{
    private readonly nint _handle;
    private bool _disposed;

    public BaoBaoPaddleOcrClient(string? modelRoot = null, string? nativeDir = null)
    {
        NativeMethods.EnsureNativeLibraryLoaded(nativeDir);

        var resolvedModelRoot = ResolveModelRoot(modelRoot);
        var code = NativeMethods.BaoBaoPaddleOcr_Create(resolvedModelRoot, out _handle, out var errorPtr);
        var error = NativeMethods.ReadUtf8AndFree(errorPtr);

        if (code != 0 || _handle == nint.Zero)
        {
            throw new InvalidOperationException($"初始化 OCR 引擎失败 (code={code})。{error}");
        }
    }

    public static BaoBaoPaddleOcrClient Shared { get; } = new();

    public OcrResult Detect(string imagePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new ArgumentException("imagePath 不能为空。", nameof(imagePath));
        }

        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"图片文件不存在: {fullPath}", fullPath);
        }

        var code = NativeMethods.BaoBaoPaddleOcr_Detect(_handle, fullPath, out var jsonPtr, out var errorPtr);
        var json = NativeMethods.ReadUtf8AndFree(jsonPtr);
        var error = NativeMethods.ReadUtf8AndFree(errorPtr);

        if (code != 0)
        {
            throw new InvalidOperationException($"OCR 识别失败 (code={code})。{error}");
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("OCR 返回了空结果。");
        }

        return OcrResultParser.Parse(json);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_handle != nint.Zero)
        {
            NativeMethods.BaoBaoPaddleOcr_Destroy(_handle);
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static string ResolveModelRoot(string? modelRoot)
    {
        if (!string.IsNullOrWhiteSpace(modelRoot))
        {
            return Path.GetFullPath(modelRoot);
        }

        var fromEnv = Environment.GetEnvironmentVariable("BAOBAO_PADDLEOCR_MODEL_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return Path.GetFullPath(fromEnv);
        }

        return Path.Combine(AppContext.BaseDirectory, "models");
    }
}

internal static class OcrResultParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static OcrResult Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<NativeOcrResultDto>(json, JsonOptions)
                  ?? throw new InvalidOperationException("OCR JSON 解析失败：结果为空。");

        var blocks = dto.Blocks is null
            ? Array.Empty<OcrBlock>()
            : dto.Blocks.Select(x => new OcrBlock(x.Text ?? string.Empty, x.Score)).ToArray();

        var text = string.IsNullOrWhiteSpace(dto.Text)
            ? string.Join(Environment.NewLine, blocks.Select(x => x.Text).Where(x => !string.IsNullOrWhiteSpace(x)))
            : dto.Text;

        return new OcrResult(text, json, blocks);
    }
}

internal sealed class NativeOcrResultDto
{
    public string? Text { get; set; }

    public NativeOcrBlockDto[]? Blocks { get; set; }
}

internal sealed class NativeOcrBlockDto
{
    public string? Text { get; set; }

    public float Score { get; set; }
}

internal static partial class NativeMethods
{
    private const string NativeDllName = "BaoBaoPaddleOCR.Native";
    private static bool _loaded;

    public static void EnsureNativeLibraryLoaded(string? nativeDir = null)
    {
        if (_loaded)
        {
            return;
        }

        foreach (var dir in GetNativeSearchPaths(nativeDir))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            var fullDir = Path.GetFullPath(dir);
            var dllPath = Path.Combine(fullDir, NativeDllName + ".dll");
            if (!File.Exists(dllPath))
            {
                continue;
            }

            TryRegisterDllDirectory(fullDir);
            NativeLibrary.Load(dllPath);
            _loaded = true;
            return;
        }

        throw new DllNotFoundException(
            $"未找到 {NativeDllName}.dll。请将其放到程序目录、native 子目录，或设置 BAOBAO_PADDLEOCR_NATIVE_DIR。");
    }

    public static string ReadUtf8AndFree(nint ptr)
    {
        if (ptr == nint.Zero)
        {
            return string.Empty;
        }

        try
        {
            var len = 0;
            while (Marshal.ReadByte(ptr, len) != 0)
            {
                len++;
            }

            var bytes = new byte[len];
            Marshal.Copy(ptr, bytes, 0, len);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            BaoBaoPaddleOcr_Free(ptr);
        }
    }

    private static void TryRegisterDllDirectory(string fullDir)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        SetDefaultDllDirectories(LoadLibrarySearchFlags.LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LoadLibrarySearchFlags.LOAD_LIBRARY_SEARCH_USER_DIRS);
        AddDllDirectory(fullDir);
        SetDllDirectory(fullDir);
    }

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(LoadLibrarySearchFlags directoryFlags);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint AddDllDirectory(string newDirectory);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string? lpPathName);

    [Flags]
    private enum LoadLibrarySearchFlags : uint
    {
        LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000,
        LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400
    }

    [DllImport(NativeDllName, EntryPoint = "BaoBaoPaddleOcr_Create", CallingConvention = CallingConvention.Cdecl)]
    public static extern int BaoBaoPaddleOcr_Create(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string modelRoot,
        out nint handle,
        out nint errorMessage);

    [DllImport(NativeDllName, EntryPoint = "BaoBaoPaddleOcr_Detect", CallingConvention = CallingConvention.Cdecl)]
    public static extern int BaoBaoPaddleOcr_Detect(
        nint handle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string imagePath,
        out nint jsonResult,
        out nint errorMessage);

    [DllImport(NativeDllName, EntryPoint = "BaoBaoPaddleOcr_Destroy", CallingConvention = CallingConvention.Cdecl)]
    public static extern void BaoBaoPaddleOcr_Destroy(nint handle);

    [DllImport(NativeDllName, EntryPoint = "BaoBaoPaddleOcr_Free", CallingConvention = CallingConvention.Cdecl)]
    public static extern void BaoBaoPaddleOcr_Free(nint textPtr);

    private static IEnumerable<string> GetNativeSearchPaths(string? nativeDir)
    {
        if (!string.IsNullOrWhiteSpace(nativeDir))
        {
            yield return nativeDir;
        }

        var fromEnv = Environment.GetEnvironmentVariable("BAOBAO_PADDLEOCR_NATIVE_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            yield return fromEnv;
        }

        yield return AppContext.BaseDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, "native");
        yield return Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native");

        var assemblyDir = Path.GetDirectoryName(typeof(BaoBaoPaddleOcrClient).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(assemblyDir))
        {
            yield return assemblyDir;
            yield return Path.Combine(assemblyDir, "native");
            yield return Path.Combine(assemblyDir, "runtimes", "win-x64", "native");
        }
    }
}
