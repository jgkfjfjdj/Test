using System;
using Kindling.Content;
using NUnit.Framework;

namespace Kindling.Tests.Pure
{
    public class ContentKeyTests
    {
        [TestCase("item.tool.iron_axe", ContentKind.Item)]
        [TestCase("recipe.iron_axe", ContentKind.Recipe)]
        [TestCase("piece.wall", ContentKind.Piece)]
        [TestCase("loot.ranger_station.crate", ContentKind.LootTable)]
        [TestCase("season.winter", ContentKind.Season)]
        [TestCase("cos.hat.knit_red", ContentKind.Cosmetic)]
        [TestCase("map.reach_2k", ContentKind.Map)]
        public void ParsesValidKeys(string text, ContentKind kind)
        {
            var key = ContentKey.Parse(text);
            Assert.That(key.Value, Is.EqualTo(text));
            Assert.That(key.Kind, Is.EqualTo(kind));
            Assert.That(key.IsValid, Is.True);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("item")]
        [TestCase("item.")]
        [TestCase(".item.axe")]
        [TestCase("item..axe")]
        [TestCase("Item.axe")]
        [TestCase("item.Axe")]
        [TestCase("item.1axe")]
        [TestCase("item.iron-axe")]
        [TestCase("item.iron axe")]
        [TestCase("weapon.axe")]
        [TestCase("item.a.b.c.d.e.f")]
        public void RejectsInvalidKeys(string text)
        {
            Assert.That(ContentKey.TryParse(text, out _, out var error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
            Assert.Throws<FormatException>(() => ContentKey.Parse(text));
        }

        [Test]
        public void RejectsKeysLongerThanTheLimit()
        {
            string longKey = "item." + new string('a', ContentKey.MaxLength);
            Assert.That(ContentKey.TryParse(longKey, out _), Is.False);
        }

        [Test]
        public void DefaultKeyIsInvalid()
        {
            ContentKey key = default;
            Assert.That(key.IsValid, Is.False);
            Assert.That(key.Value, Is.EqualTo(string.Empty));
        }

        [Test]
        public void EqualityIsOrdinal()
        {
            Assert.That(ContentKey.Parse("item.stone"), Is.EqualTo(ContentKey.Parse("item.stone")));
            Assert.That(ContentKey.Parse("item.stone") != ContentKey.Parse("item.wood"), Is.True);
            Assert.That(ContentKey.Parse("item.stone").GetHashCode(), Is.EqualTo(ContentKey.Parse("item.stone").GetHashCode()));
        }
    }
}
