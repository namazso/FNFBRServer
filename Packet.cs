//  FNFBRServer - A server reimplementation for FunkinBattleRoyale
//  Copyright (C) 2021  namazso < admin@namazso.eu >
//
//   This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Affero General Public License as
//  published by the Free Software Foundation, either version 3 of the
//  License, or (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//   but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU Affero General Public License for more details.
//
//  You should have received a copy of the GNU Affero General Public License
//  along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.IO;
using System.Linq;

namespace FNFBRServer
{
    interface IPacket {}

    class Utils
    {
        public static string ReadString(BinaryReader s) => System.Text.Encoding.UTF8.GetString(s.ReadBytes(s.ReadUInt16()));

        public static void WriteString(BinaryWriter s, string str)
        {
            var buf = System.Text.Encoding.UTF8.GetBytes(str);
            s.Write((ushort)buf.Length);
            s.Write(buf);
        }

        public static byte[] ReadFile(BinaryReader s) => s.ReadBytes(s.ReadInt32());

        public static void WriteFile(BinaryWriter s, byte[] buf)
        {
            s.Write((uint)buf.Length);
            s.Write(buf);
        }

        private static readonly Type[] Packets = {
            typeof(Packet.SendClientToken),
            typeof(Packet.SendServerToken),
            typeof(Packet.SendPassword),
            typeof(Packet.PasswordConfirm),
            typeof(Packet.SendNickname),
            typeof(Packet.NicknameConfirm),

            typeof(Packet.BroadcastNewPlayer),
            typeof(Packet.EndPrevPlayers),
            typeof(Packet.JoinedLobby),
            typeof(Packet.PlayerLeft),
            typeof(Packet.GameStart),

            typeof(Packet.GameReady),
            typeof(Packet.PlayersReady),
            typeof(Packet.EveryoneReady),
            typeof(Packet.SendScore),
            typeof(Packet.BroadcastScore),
            typeof(Packet.GameEnd),
            typeof(Packet.ForceGameEnd),

            typeof(Packet.SendChatMessage),
            typeof(Packet.RejectChatMessage),
            typeof(Packet.Muted),
            typeof(Packet.BroadcastChatMessage),
            typeof(Packet.ServerChatMessage),

            typeof(Packet.ReadyDownload),
            typeof(Packet.SendChart),
            typeof(Packet.SendVoices),
            typeof(Packet.SendInst),
            typeof(Packet.RequestVoices),
            typeof(Packet.RequestInst),
            typeof(Packet.Deny),

            typeof(Packet.KeepAlive),

            typeof(Packet.Disconnect)
        };

        public static IPacket ReadPacket(BinaryReader s)
        {
            var id = s.ReadByte();
            if (id >= Packets.Length)
                throw new InvalidDataException();
            var packet = (IPacket) Activator.CreateInstance(Packets[id]);
            if (packet == null)
                throw new InvalidCastException();
            DeserializePacket(s, packet);
            return packet;
        }

        public static void WritePacket(BinaryWriter s, IPacket packet)
        {
            var id = Array.FindIndex(Packets, type => type == packet.GetType());
            s.Write((byte)id);
            SerializePacket(s, packet);
        }

        static void SerializePacket(BinaryWriter s, IPacket packet)
        {
            var type = packet.GetType();
            var props = type.GetProperties();
            foreach (var prop in props)
            {
                var propType = prop.PropertyType;
                var propUnderlyingType = propType;
                if (propUnderlyingType.IsEnum)
                    propUnderlyingType = propUnderlyingType.GetEnumUnderlyingType();

                byte[] bytes;
                object val = Convert.ChangeType(prop.GetValue(packet), propUnderlyingType);
                switch (val)
                {
                    case sbyte v:
                        bytes = new[] {(byte) v};
                        break;
                    case byte v:
                        bytes = new[] {v};
                        break;
                    case short v:
                        bytes = BitConverter.GetBytes(v);
                        break;
                    case ushort v:
                        bytes = BitConverter.GetBytes(v);
                        break;
                    case int v:
                        bytes = BitConverter.GetBytes(v);
                        break;
                    case uint v:
                        bytes = BitConverter.GetBytes(v);
                        break;
                    case long v:
                        bytes = BitConverter.GetBytes(v);
                        break;
                    case ulong v:
                        bytes = BitConverter.GetBytes(v);
                        break;
                    case string v:
                        bytes = new[]
                        {
                            BitConverter.GetBytes((ushort)v.Length),
                            System.Text.Encoding.UTF8.GetBytes(v)
                        }.SelectMany(arr => arr).ToArray();
                        break;
                    case byte[] v:
                        bytes = new[]
                        {
                            BitConverter.GetBytes((uint)v.Length),
                            v
                        }.SelectMany(arr => arr).ToArray();
                        break;
                    default:
                        throw new ArgumentException("Packet type cannot be serialized");
                }
                s.Write(bytes);
            }
        }

        static void DeserializePacket(BinaryReader s, IPacket packet)
        {
            var type = packet.GetType();
            var props = type.GetProperties();
            foreach (var prop in props)
            {
                var propType = prop.PropertyType;
                var propUnderlyingType = propType;
                if (propUnderlyingType.IsEnum)
                    propUnderlyingType = propUnderlyingType.GetEnumUnderlyingType();

                object value;
                switch (propUnderlyingType)
                {
                    case Type t when t == typeof(sbyte):
                        value = Convert.ChangeType(s.ReadSByte(), propType);
                        break;
                    case Type t when t == typeof(byte):
                        value = Convert.ChangeType(s.ReadByte(), propType);
                        break;
                    case Type t when t == typeof(short):
                        value = Convert.ChangeType(BitConverter.ToInt16(s.ReadBytes(2).Reverse().ToArray()), propType);
                        break;
                    case Type t when t == typeof(ushort):
                        value = Convert.ChangeType(BitConverter.ToUInt16(s.ReadBytes(2).Reverse().ToArray()), propType);
                        break;
                    case Type t when t == typeof(int):
                        value = Convert.ChangeType(BitConverter.ToInt32(s.ReadBytes(4).Reverse().ToArray()), propType);
                        break;
                    case Type t when t == typeof(uint):
                        value = Convert.ChangeType(BitConverter.ToUInt32(s.ReadBytes(4).Reverse().ToArray()), propType);
                        break;
                    case Type t when t == typeof(long):
                        value = Convert.ChangeType(BitConverter.ToInt64(s.ReadBytes(8).Reverse().ToArray()), propType);
                        break;
                    case Type t when t == typeof(ulong):
                        value = Convert.ChangeType(BitConverter.ToUInt64(s.ReadBytes(8).Reverse().ToArray()), propType);
                        break;
                    case Type t when t == typeof(string): {
                        var len = BitConverter.ToUInt16(s.ReadBytes(2).Reverse().ToArray());
                        value = System.Text.Encoding.UTF8.GetString(s.ReadBytes(len));
                        break;
                    }
                    case Type t when t == typeof(byte[]): {
                        var len = BitConverter.ToInt32(s.ReadBytes(4).Reverse().ToArray());
                        value = s.ReadBytes(len);
                        break;
                    }
                    default:
                        throw new ArgumentException("Packet type cannot be deserialized");
                }
                prop.SetValue(packet, value);
            }
        }
    }

    namespace Packet
    {
        class SendClientToken : IPacket
        {
            public static uint TokenV1 = 93724324;
            public uint Token { get; set; }
        }

        class SendServerToken : IPacket
        {
            public static uint TokenV101 = 38371058;
            public uint Token { get; set; }
        }

        class SendPassword : IPacket
        {
            public string Password { get; set; }
        }

        class PasswordConfirm : IPacket
        {
            public enum ReplyType : byte
            {
                Correct = 0,
                GameInProgress,
                Incorrect
            };

            public ReplyType Reply { get; set; }
        }

        class SendNickname : IPacket
        {
            public string Nick { get; set; }
        }

        class NicknameConfirm : IPacket
        {
            public enum ReplyType : byte
            {
                Accepted = 0,
                AlreadyInUse,
                GameInProgress,
                Invalid
            };

            public ReplyType Reply { get; set; }
        }

        class BroadcastNewPlayer : IPacket
        {
            public byte Id { get; set; }
            public string Nickname { get; set; }
        }

        class EndPrevPlayers : IPacket {}

        class JoinedLobby : IPacket {}

        class PlayerLeft : IPacket
        {
            public byte Id { get; set; }
        }

        class GameStart : IPacket
        {
            public string Song { get; set; }
            public string Folder { get; set; }
        }

        class GameReady : IPacket {}
        
        class PlayersReady : IPacket
        {
            public byte Count { get; set; }
        }

        class EveryoneReady : IPacket
        {
            public byte SafeFrames { get; set; }
        }

        class SendScore : IPacket
        {
            public int Score { get; set; }
        }

        class BroadcastScore : IPacket
        {
            public byte Player { get; set; }
            public int Score { get; set; }
        }

        class GameEnd : IPacket {}

        class ForceGameEnd : IPacket {}

        class SendChatMessage : IPacket
        {
            public byte Id { get; set; }
            public string Message { get; set; }
        }

        class RejectChatMessage : IPacket
        {
            public byte Id { get; set; }
        }

        class Muted : IPacket {}

        class BroadcastChatMessage : IPacket
        {
            public byte Player { get; set; }
            public string Message { get; set; }
        }

        class ServerChatMessage : IPacket
        {
            public string Message { get; set; }
        }

        class ReadyDownload : IPacket {}

        class SendChart : IPacket
        {
            public byte[] File { get; set; }
        }

        class SendVoices : IPacket
        {
            public byte[] File { get; set; }
        }

        class SendInst : IPacket
        {
            public byte[] File { get; set; }
        }

        class RequestVoices : IPacket {}

        class RequestInst : IPacket {}

        class Deny : IPacket {}

        class KeepAlive : IPacket {}

        class Disconnect : IPacket {}
    }
}
