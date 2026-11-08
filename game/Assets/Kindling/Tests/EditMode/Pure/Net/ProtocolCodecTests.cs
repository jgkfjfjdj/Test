using System.Collections.Generic;
using System.Numerics;
using Kindling.Net.Protocol;
using Kindling.Simulation.Movement;
using NUnit.Framework;
using Kindling.Core.Serialization;

namespace Kindling.Tests.Pure.Net
{
    public class ProtocolCodecTests
    {
        [Test]
        public void PrimitivesRoundTrip()
        {
            var w = new PacketWriter(4);
            w.WriteByte(250);
            w.WriteSByte(-5);
            w.WriteBool(true);
            w.WriteUShort(65000);
            w.WriteShort(-1234);
            w.WriteInt24(-8388608);
            w.WriteInt24(8388607);
            w.WriteUInt(0xDEADBEEF);
            w.WriteInt(-42);
            w.WriteFloat(3.25f);
            foreach (uint v in new uint[] { 0, 127, 128, 16383, 16384, uint.MaxValue })
            {
                w.WriteVarUInt(v);
            }

            w.WriteString("héllo");
            w.WriteBlob(new byte[] { 1, 2, 3 });

            var r = new PacketReader(w.ToArray());
            Assert.That(r.ReadByte(), Is.EqualTo(250));
            Assert.That(r.ReadSByte(), Is.EqualTo(-5));
            Assert.That(r.ReadBool(), Is.True);
            Assert.That(r.ReadUShort(), Is.EqualTo(65000));
            Assert.That(r.ReadShort(), Is.EqualTo(-1234));
            Assert.That(r.ReadInt24(), Is.EqualTo(-8388608));
            Assert.That(r.ReadInt24(), Is.EqualTo(8388607));
            Assert.That(r.ReadUInt(), Is.EqualTo(0xDEADBEEF));
            Assert.That(r.ReadInt(), Is.EqualTo(-42));
            Assert.That(r.ReadFloat(), Is.EqualTo(3.25f));
            foreach (uint v in new uint[] { 0, 127, 128, 16383, 16384, uint.MaxValue })
            {
                Assert.That(r.ReadVarUInt(), Is.EqualTo(v));
            }

            Assert.That(r.ReadString(), Is.EqualTo("héllo"));
            Assert.That(r.ReadBlob(16), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(r.AtEnd, Is.True);
        }

        [Test]
        public void ReadingPastTheEndOrBadValuesThrows()
        {
            Assert.Throws<ProtocolException>(() => new PacketReader(new byte[] { 1 }).ReadUShort());
            Assert.Throws<ProtocolException>(() => new PacketReader(new byte[] { 2 }).ReadBool());
            Assert.Throws<ProtocolException>(() => new PacketReader(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 }).ReadVarUInt());
            Assert.Throws<ProtocolException>(() => new PacketReader(new byte[] { 0x00, 0x00, 0xC0, 0x7F }).ReadFloat(), "NaN");

            var w = new PacketWriter();
            w.WriteString(new string('x', 300), 400);
            Assert.Throws<ProtocolException>(() => new PacketReader(w.ToArray()).ReadString(256));
        }

        [Test]
        public void LongStringsAreTruncatedOnACharacterBoundary()
        {
            var w = new PacketWriter();
            w.WriteString("ééééé", 5); // 2 bytes each: only 2 fit
            Assert.That(new PacketReader(w.ToArray()).ReadString(), Is.EqualTo("éé"));
        }

        [Test]
        public void InputQuantizationIsStableAndSafe()
        {
            var input = new PlayerInput { Sequence = 7, Move = new Vector2(0.333f, -1.7f), Yaw = -30.1234f, Pitch = 120f, Buttons = InputButtons.Jump | InputButtons.Sprint };
            PlayerInput q = InputCodec.Quantize(input);
            Assert.That(InputCodec.Quantize(q), Is.EqualTo(q), "quantizing twice changes nothing");
            Assert.That(q.Move.Y, Is.EqualTo(-1f));
            Assert.That(q.Yaw, Is.EqualTo(329.8766f).Within(0.01f));
            Assert.That(q.Pitch, Is.EqualTo(89f));

            var w = new PacketWriter();
            InputCodec.Write(w, input);
            Assert.That(w.Length, Is.EqualTo(InputCodec.EncodedSize));
            var r = new PacketReader(w.ToArray());
            Assert.That(InputCodec.Read(ref r, 7), Is.EqualTo(q), "the wire gives exactly the quantized input");

            PlayerInput nan = InputCodec.Quantize(new PlayerInput { Move = new Vector2(float.NaN, 0), Yaw = float.PositiveInfinity, Pitch = float.NaN });
            Assert.That(nan.Move, Is.EqualTo(Vector2.Zero));
            Assert.That(nan.Yaw, Is.EqualTo(0f));
            Assert.That(nan.Pitch, Is.EqualTo(0f));
        }

        [Test]
        public void InputsMessageCarriesRedundantInputsAndSurvivesSequenceStart()
        {
            var inputs = new List<PlayerInput>
            {
                new PlayerInput { Sequence = 2, Move = new Vector2(0, 1) },
                new PlayerInput { Sequence = 1, Move = new Vector2(1, 0) },
            };
            var w = new PacketWriter();
            InputsMessage.Write(w, inputs, 3);
            var r = new PacketReader(w.ToArray());
            Assert.That((MessageType)r.ReadByte(), Is.EqualTo(MessageType.Inputs));
            var read = new List<PlayerInput>();
            InputsMessage.Read(ref r, read);
            Assert.That(read.Count, Is.EqualTo(2));
            Assert.That(read[0].Sequence, Is.EqualTo(2u));
            Assert.That(read[1].Move.X, Is.EqualTo(1f));
        }

        [Test]
        public void WelcomeAndHelloRoundTrip()
        {
            var state = PlayerState.SpawnAt(new Vector3(1.5f, 2f, -3f), 45f);
            state.Velocity = new Vector3(0.1f, -0.2f, 0.3f);
            state.Crouched = true;
            state.Stamina = 55.5f;
            var w = new PacketWriter();
            new WelcomeMessage { PlayerId = 9, ServerTick = 1234, TickRate = 30, MapSeed = -5, ResumeToken = new byte[16], Resumed = true, LastInputSequence = 77, State = state }.Write(w);
            var r = new PacketReader(w.ToArray());
            r.ReadByte();
            WelcomeMessage m = WelcomeMessage.Read(ref r);
            Assert.That(m.PlayerId, Is.EqualTo(9));
            Assert.That(m.MapSeed, Is.EqualTo(-5));
            Assert.That(m.Resumed, Is.True);
            Assert.That(m.LastInputSequence, Is.EqualTo(77u));
            Assert.That(m.State.Position, Is.EqualTo(state.Position));
            Assert.That(m.State.Velocity, Is.EqualTo(state.Velocity));
            Assert.That(m.State.Crouched, Is.True);
            Assert.That(m.State.Stamina, Is.EqualTo(55.5f));

            w.Reset();
            new HelloMessage { ProtocolVersion = 999, DisplayName = "x" }.Write(w);
            r = new PacketReader(w.ToArray());
            r.ReadByte();
            Assert.That(HelloMessage.Read(ref r).ProtocolVersion, Is.EqualTo(999), "a future version is still readable enough to reject it");
        }

        [Test]
        public void EntitySnapshotsAreCompactAndAccurateToHalfACentimetre()
        {
            var e = new EntitySnapshot { Id = 300, Position = new Vector3(-1234.567f, 12.345f, 2000.004f), Yaw = 359.9f, Pitch = -45f, Flags = EntityFlags.Crouched | EntityFlags.Away };
            var w = new PacketWriter();
            e.Write(w);
            Assert.That(w.Length, Is.LessThanOrEqualTo(EntitySnapshot.MaxEncodedSize));
            var r = new PacketReader(w.ToArray());
            EntitySnapshot d = EntitySnapshot.Read(ref r);
            Assert.That(d.Id, Is.EqualTo(300));
            Assert.That(Vector3.Distance(d.Position, e.Position), Is.LessThan(0.009f));
            Assert.That(d.Yaw, Is.EqualTo(359.9f).Within(0.01f));
            Assert.That(d.Pitch, Is.EqualTo(-45f).Within(0.5f));
            Assert.That(d.Flags, Is.EqualTo(e.Flags));
        }

        [Test]
        public void DisconnectPayloadRoundTripsAndToleratesGarbage()
        {
            byte[] payload = DisconnectPayload.Encode(DisconnectCode.ServerFull, "full");
            Assert.That(DisconnectPayload.Decode(payload, 0, payload.Length, DisconnectCode.None, out string message), Is.EqualTo(DisconnectCode.ServerFull));
            Assert.That(message, Is.EqualTo("full"));
            Assert.That(DisconnectPayload.Decode(new byte[] { 3, 200 }, 0, 2, DisconnectCode.None, out _), Is.EqualTo(DisconnectCode.None));
            Assert.That(DisconnectPayload.Decode(null, 0, 0, DisconnectCode.Timeout, out _), Is.EqualTo(DisconnectCode.Timeout));
        }
    }
}
