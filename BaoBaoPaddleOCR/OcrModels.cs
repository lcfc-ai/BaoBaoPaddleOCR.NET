using System.Collections.Generic;
using System.Linq;

namespace BaoBaoPaddleOCR
{
    public sealed class OcrPoint
    {
        public OcrPoint(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; private set; }

        public float Y { get; private set; }
    }

    public sealed class OcrColorSample
    {
        public OcrColorSample(string hex, float red, float green, float blue, float variance)
        {
            Hex = hex;
            Red = red;
            Green = green;
            Blue = blue;
            Variance = variance;
        }

        public string Hex { get; private set; }

        public float Red { get; private set; }

        public float Green { get; private set; }

        public float Blue { get; private set; }

        public float Variance { get; private set; }
    }

    public sealed class OcrBlock
    {
        public OcrBlock(string text, float score, IReadOnlyList<OcrPoint> box, OcrColorSample colorSample)
        {
            Text = text;
            Score = score;
            Box = box ?? new OcrPoint[0];
            ColorSample = colorSample;
        }

        public string Text { get; private set; }

        public float Score { get; private set; }

        public IReadOnlyList<OcrPoint> Box { get; private set; }

        public OcrColorSample ColorSample { get; private set; }

        public float Left
        {
            get { return Box.Count == 0 ? 0 : Box.Min(point => point.X); }
        }

        public float Top
        {
            get { return Box.Count == 0 ? 0 : Box.Min(point => point.Y); }
        }

        public float Right
        {
            get { return Box.Count == 0 ? 0 : Box.Max(point => point.X); }
        }

        public float Bottom
        {
            get { return Box.Count == 0 ? 0 : Box.Max(point => point.Y); }
        }
    }

    public sealed class OcrResult
    {
        public OcrResult(string text, string jsonText, IReadOnlyList<OcrBlock> blocks)
        {
            Text = text;
            JsonText = jsonText;
            Blocks = blocks ?? new OcrBlock[0];
        }

        public string Text { get; private set; }

        public string JsonText { get; private set; }

        public IReadOnlyList<OcrBlock> Blocks { get; private set; }
    }
}
