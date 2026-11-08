using System.Collections.Generic;
using System.Numerics;
using Kindling.Core.Numerics;
using Kindling.Simulation.Collision;
using Kindling.Simulation.World;
using NUnit.Framework;

namespace Kindling.Tests.Pure.Simulation
{
    public class CollisionWorldTests
    {
        [Test]
        public void ClipsEachAxisAgainstBoxesAndGround()
        {
            var world = new CollisionWorld(new[] { new Aabb(new Vector3(2, 0, -1), new Vector3(3, 2, 1)) });
            var scratch = new List<int>();
            var box = new Aabb(new Vector3(0, 0.5f, -0.3f), new Vector3(0.6f, 2.3f, 0.3f));
            world.Gather(box.Expand(new Vector3(5, -5, 0)), scratch);
            Assert.That(world.ClipX(box, 5f, scratch), Is.EqualTo(2f - 0.6f).Within(CollisionWorld.Skin));
            Assert.That(world.ClipX(box, -5f, scratch), Is.EqualTo(-5f));
            Assert.That(world.ClipY(box, -5f, scratch), Is.EqualTo(-0.5f).Within(CollisionWorld.Skin), "stops at ground plane");
            Assert.That(world.ClipZ(box, 5f, scratch), Is.EqualTo(5f), "no box along Z");
        }

        [Test]
        public void OverlapDetectsBoxesAndBelowGround()
        {
            var world = new CollisionWorld(new[] { new Aabb(new Vector3(0, 0, 0), new Vector3(1, 1, 1)) });
            var scratch = new List<int>();
            Assert.That(world.Overlaps(new Aabb(new Vector3(0.5f, 0.5f, 0.5f), new Vector3(2, 2, 2)), scratch), Is.True);
            Assert.That(world.Overlaps(new Aabb(new Vector3(1, 0, 0), new Vector3(2, 1, 1)), scratch), Is.False, "touching faces");
            Assert.That(world.Overlaps(new Aabb(new Vector3(5, -1, 5), new Vector3(6, 1, 6)), scratch), Is.True, "below ground");
        }

        [Test]
        public void GreyboxLayoutIsDeterministicPerSeed()
        {
            var a = GreyboxLayout.Generate(7);
            var b = GreyboxLayout.Generate(7);
            var c = GreyboxLayout.Generate(8);
            Assert.That(a.Pieces.Count, Is.EqualTo(b.Pieces.Count));
            for (int i = 0; i < a.Pieces.Count; i++)
            {
                Assert.That(a.Pieces[i].Box.Min, Is.EqualTo(b.Pieces[i].Box.Min));
                Assert.That(a.Pieces[i].Box.Max, Is.EqualTo(b.Pieces[i].Box.Max));
            }

            Assert.That(c.Pieces[^1].Box.Min, Is.Not.EqualTo(a.Pieces[^1].Box.Min));
        }

        [Test]
        public void SpawnPointsAreClear()
        {
            var layout = GreyboxLayout.Generate(1);
            var world = layout.BuildCollisionWorld();
            var scratch = new List<int>();
            foreach (var spawn in layout.SpawnPoints)
            {
                Assert.That(world.Overlaps(Aabb.FromFeet(spawn + new Vector3(0, 0.01f, 0), 0.3f, 1.8f), scratch), Is.False, spawn.ToString());
            }
        }

        [Test]
        public void DeterministicRandomHasAFixedSequence()
        {
            var r = new DeterministicRandom(42);
            ulong first = r.NextULong();
            Assert.That(new DeterministicRandom(42).NextULong(), Is.EqualTo(first));
            for (int i = 0; i < 1000; i++)
            {
                float f = r.NextFloat();
                Assert.That(f, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                Assert.That(r.Range(3, 7), Is.InRange(3, 6));
            }
        }
    }
}
