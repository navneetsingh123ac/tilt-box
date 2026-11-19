using NUnit.Framework;
using TiltBox.Core;

namespace TiltBox.Core.Tests
{
    public class LevelParserTests
    {
        private const string Valid = @"{
  ""id"": ""t_01"", ""world"": 1, ""w"": 3, ""h"": 3,
  ""cells"": [""..."", // a comment
              "".#."",
              ""..R""],
  ""blocks"": [ { ""x"": 0, ""y"": 0, ""c"": ""r"" } ],
  ""par"": 2, ""limit"": 8, ""kind"": ""teach""
}";

        private static string With(string find, string replace)
        {
            Assert.IsTrue(Valid.Contains(find), "test setup: '" + find + "' not in sample");
            return Valid.Replace(find, replace);
        }

        private static void AssertRejected(string json, string expectedFragment)
        {
            var e = Assert.Throws<LevelFormatException>(() => LevelParser.Parse(json));
            StringAssert.Contains(expectedFragment, e.Message);
        }

        [Test]
        public void ParsesValidLevel()
        {
            LevelDefinition level = LevelParser.Parse(Valid);
            Assert.AreEqual("t_01", level.Id);
            Assert.AreEqual(1, level.World);
            Assert.AreEqual(3, level.Layout.Width);
            Assert.AreEqual(3, level.Layout.Height);
            Assert.AreEqual(CellKind.Wall, level.Layout[1, 1].Kind);
            Assert.AreEqual(CellKind.Bin, level.Layout[2, 2].Kind);
            Assert.AreEqual(BlockColor.Red, level.Layout[2, 2].Color);
            Assert.AreEqual(1, level.Start.BlockCount);
            Assert.AreEqual(new GridPos(0, 0), level.Start.PositionOf(0));
            Assert.AreEqual(2, level.Par);
            Assert.AreEqual(8, level.Limit);
            Assert.AreEqual(LevelKind.Teach, level.Kind);
        }

        [Test]
        public void KindDefaultsToNormal()
        {
            Assert.AreEqual(LevelKind.Normal, LevelParser.Parse(With(@", ""kind"": ""teach""", "")).Kind);
        }

        [Test] public void RejectsBadJson() => AssertRejected("{ \"id\": ", "invalid JSON");
        [Test] public void RejectsMissingField() => AssertRejected(With(@"""par"": 2, ", ""), "missing \"par\"");
        [Test] public void RejectsTooWide() => AssertRejected(With(@"""w"": 3", @"""w"": 8"), "allowed sizes");
        [Test] public void RejectsTooSmall() => AssertRejected(With(@"""h"": 3", @"""h"": 2"), "allowed sizes");
        [Test] public void RejectsWrongRowCount() => AssertRejected(With(@"""h"": 3", @"""h"": 4"), "rows but h is 4");
        [Test] public void RejectsWrongRowLength() => AssertRejected(With(@""".#.""", @""".#..."""), "row 1 has 5");
        [Test] public void RejectsUnknownCellChar() => AssertRejected(With(@""".#.""", @"""@#."""), "unknown cell character '@'");
        [Test] public void RejectsLaterWorldCell() => AssertRejected(With(@""".#.""", @""">#."""), "not supported yet");
        [Test] public void RejectsLaterWorldBlockFeature() =>
            AssertRejected(With(@"""c"": ""r"" }", @"""c"": ""r"", ""ice"": true }"), "\"ice\"");
        [Test] public void RejectsBlockOnWall() => AssertRejected(With(@"""x"": 0, ""y"": 0", @"""x"": 1, ""y"": 1"), "on a wall");
        [Test] public void RejectsBlockOnBin() => AssertRejected(With(@"""x"": 0, ""y"": 0", @"""x"": 2, ""y"": 2"), "on a bin");
        [Test] public void RejectsBlockOutside() => AssertRejected(With(@"""x"": 0, ""y"": 0", @"""x"": 3, ""y"": 0"), "outside");
        [Test] public void RejectsColourWithoutBin() => AssertRejected(With(@"""c"": ""r""", @"""c"": ""b"""), "no Blue bin");
        [Test] public void RejectsUnknownColour() => AssertRejected(With(@"""c"": ""r""", @"""c"": ""x"""), "unknown colour");
        [Test] public void RejectsLimitBelowPar() => AssertRejected(With(@"""limit"": 8", @"""limit"": 1"), "lower than par");
        [Test] public void RejectsUnknownKind() => AssertRejected(With(@"""teach""", @"""boss"""), "unknown kind");
        [Test] public void RejectsFractionalNumber() => AssertRejected(With(@"""par"": 2", @"""par"": 2.5"), "whole number");

        [Test]
        public void RejectsSharedCell()
        {
            AssertRejected(With(@"{ ""x"": 0, ""y"": 0, ""c"": ""r"" }",
                @"{ ""x"": 0, ""y"": 0, ""c"": ""r"" }, { ""x"": 0, ""y"": 0, ""c"": ""r"" }"), "share cell");
        }

        [Test]
        public void RejectsTooManyBlocks()
        {
            string many = With(@"""w"": 3, ""h"": 3", @"""w"": 7, ""h"": 3")
                .Replace(@"""..."", // a comment", @"""......."",")
                .Replace(@""".#.""", @""".#....."" ")
                .Replace(@"""..R""", @"""......R""");
            var blocks = new System.Text.StringBuilder();
            for (int i = 0; i < 11; i++)
            {
                if (i > 0) blocks.Append(", ");
                int x = i % 7, y = i < 7 ? 0 : 2;
                blocks.Append("{ \"x\": " + x + ", \"y\": " + y + ", \"c\": \"r\" }");
            }
            many = many.Replace(@"{ ""x"": 0, ""y"": 0, ""c"": ""r"" }", blocks.ToString());
            AssertRejected(many, "maximum is 10");
        }

        [Test]
        public void ErrorMessage_NamesTheLevel()
        {
            AssertRejected(With(@"""limit"": 8", @"""limit"": 1"), "t_01:");
        }
    }
}
