using Kindling.Core.Json;
using NUnit.Framework;

namespace Kindling.Tests.Pure.Content
{
    public class JsonTests
    {
        [Test]
        public void ParsesAllValueKindsAndKeepsMemberOrder()
        {
            JsonValue v = JsonReader.Parse(@"// comment
{ ""b"": [1, -2.5, 3e2, true, false, null], /* block */ ""a"": { ""s"": ""x\""\né"" } }");
            Assert.That(v.Members[0].Key, Is.EqualTo("b"));
            Assert.That(v.Get("b").Items[1].AsDouble(), Is.EqualTo(-2.5));
            Assert.That(v.Get("b").Items[2].AsInt(), Is.EqualTo(300));
            Assert.That(v.Get("b").Items[5].IsNull, Is.True);
            Assert.That(v.Get("a").GetString("s"), Is.EqualTo("x\"\né"));
            Assert.That(v.Get("missing"), Is.Null);
            Assert.That(v.GetInt("missing", 7), Is.EqualTo(7));
        }

        [TestCase("{\"a\": 1,}", 1)]
        [TestCase("{\"a\": 1, \"a\": 2}", 1)]
        [TestCase("[1 2]", 1)]
        [TestCase("{\n  \"a\": tru\n}", 2)]
        [TestCase("\"unterminated", 1)]
        [TestCase("01", 1)]
        [TestCase("{} extra", 1)]
        public void RejectsInvalidJsonWithALineNumber(string text, int line)
        {
            var e = Assert.Throws<JsonException>(() => JsonReader.Parse(text));
            Assert.That(e.Line, Is.EqualTo(line));
        }

        [Test]
        public void TypeErrorsNameThePosition()
        {
            JsonValue v = JsonReader.Parse("{\n \"n\": \"text\"\n}");
            var e = Assert.Throws<JsonException>(() => v.GetInt("n"));
            Assert.That(e.Line, Is.EqualTo(2));
            Assert.Throws<JsonException>(() => JsonReader.Parse("{\"x\": 1.5}").GetInt("x"));
        }

        [Test]
        public void LimitsNesting()
        {
            Assert.Throws<JsonException>(() => JsonReader.Parse(new string('[', 100) + new string(']', 100)));
        }

        [Test]
        public void WriterRoundTrips()
        {
            JsonValue o = JsonValue.NewObject().Set("name", "a\"b\n").Set("n", 12).Set("f", 0.25).Set("ok", true);
            o.Set("list", JsonValue.NewArray().Add(JsonValue.Number(1)).Add(JsonValue.Null));
            string text = JsonWriter.Write(o);
            JsonValue back = JsonReader.Parse(text);
            Assert.That(back.GetString("name"), Is.EqualTo("a\"b\n"));
            Assert.That(back.GetInt("n"), Is.EqualTo(12));
            Assert.That(back.GetDouble("f"), Is.EqualTo(0.25));
            Assert.That(JsonWriter.Write(back), Is.EqualTo(text), "stable output");
        }
    }
}
