namespace BaoBaoPaddleOCR;

public sealed record OcrPoint(float X, float Y);

public sealed record OcrColorSample(
    string Hex,
    float Red,
    float Green,
    float Blue,
    float Variance);

public sealed record OcrBlock(
    string Text,
    float Score,
    IReadOnlyList<OcrPoint> Box,
    OcrColorSample? ColorSample = null)
{
    public float Left => Box.Count == 0 ? 0 : Box.Min(static point => point.X);

    public float Top => Box.Count == 0 ? 0 : Box.Min(static point => point.Y);

    public float Right => Box.Count == 0 ? 0 : Box.Max(static point => point.X);

    public float Bottom => Box.Count == 0 ? 0 : Box.Max(static point => point.Y);
}

public sealed record OcrResult(string Text, string JsonText, IReadOnlyList<OcrBlock> Blocks);
