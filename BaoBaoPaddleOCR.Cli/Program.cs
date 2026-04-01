using System.Text.Json;
using BaoBaoPaddleOCR;

var exitCode = CliRunner.Run(args);
return exitCode;

internal static class CliRunner
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || HasFlag(args, "--help", "-h"))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var imagePath = args[0];
        var modelRoot = GetOption(args, "--model-root");
        var nativeDir = GetOption(args, "--native-dir");
        var outputJson = HasFlag(args, "--json");
        var outputFull = HasFlag(args, "--full");

        try
        {
            using var client = new BaoBaoPaddleOcrClient(modelRoot, nativeDir);
            var result = client.Detect(imagePath);

            if (outputFull)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                return 0;
            }

            if (outputJson)
            {
                Console.WriteLine(result.JsonText);
                return 0;
            }

            Console.WriteLine(result.Text);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"OCR failed: {ex.Message}");
            return 2;
        }
    }

    private static bool HasFlag(IEnumerable<string> args, params string[] flags)
    {
        return args.Any(arg => flags.Any(flag => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)));
    }

    private static string? GetOption(string[] args, string optionName)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  BaoBaoPaddleOCR.Cli <imagePath> [--model-root <dir>] [--native-dir <dir>] [--json|--full]");
        Console.WriteLine();
        Console.WriteLine("Environment:");
        Console.WriteLine("  BAOBAO_PADDLEOCR_NATIVE_DIR  Native DLL 目录（可选）");
        Console.WriteLine("  BAOBAO_PADDLEOCR_MODEL_ROOT  模型目录（可选）");
        Console.WriteLine("  BAOBAO_PADDLEOCR_MOCK_TEXT   Mock 输出文本（可选）");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}
