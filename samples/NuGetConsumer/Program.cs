using BaoBaoPaddleOCR;

if (args.Length == 0)
{
    Console.WriteLine("Usage: NuGetConsumer <imagePath>");
    return;
}

var imagePath = Path.GetFullPath(args[0]);
using var client = new BaoBaoPaddleOcrClient();
var result = client.Detect(imagePath);

Console.WriteLine(result.Text);
