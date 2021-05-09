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
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using FNFBRServer.Packet;

namespace FNFBRServer
{
    class NetworkPlayer
    {
        private readonly Server _server;
        private readonly TcpClient _client;
        private readonly Config _config;
        private readonly NetworkStream _stream;
        private readonly byte[] _buffer = new byte[0x10010];
        private MemoryStream _readStream = new();

        public Player Player { get; private set; }

        private static int _idCounter;
        private readonly int _id;

        private bool _tmpAuthenticated;
        private bool _tmpAdmin;
        private string _tmpNickname;

        private int _isDead; // must be int for cmpxchg
        public bool IsDead => _isDead == 1;

        private string GetConnecter() => _client.Client.RemoteEndPoint.ToString();

        public IPacket ChartPacket;
        public IPacket InstPacket;
        public IPacket VoicesPacket;

        public NetworkPlayer(Server server,TcpClient client)
        {
            _id = Interlocked.Increment(ref _idCounter);

            _server = server;
            _client = client;

            _config = server.Config;
            _stream = client.GetStream();

            Console.WriteLine($"Connection #{_id} from {GetConnecter()}");

            _stream.BeginRead(_buffer, 0, _buffer.Length, ReadCallback, null);
        }

        public void Disconnect()
        {
            var val = Interlocked.CompareExchange(ref _isDead, 1, 0);
            if (val == 1)
                return;

            Console.WriteLine($"Connection #{_id} disconnecting.");
            try
            {
                if(_client.Connected)
                {
                    if (_stream.CanWrite)
                    {
                        // write in sync
                        //Utils.WritePacket(new BinaryWriter(_stream), new Disconnect());
                        //_stream.Flush();
                        _stream.Close();
                    }
                    _client.Close();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Connection #{_id} graceful disconnect failed: {e}");
            }
        }

        public void Heartbeat()
        {
            SendPacket(new KeepAlive());
        }
        
        public void SendPacket(IPacket packet)
        {
            if (IsDead)
                return;
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);
            Utils.WritePacket(writer, packet);
            var array = stream.ToArray();
            try
            {
                _stream.BeginWrite(array, 0, array.Length, WriteCallback, null);
            }
            catch (IOException)
            {
                Disconnect();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Connection #{_id} BeginWrite failed: {e}");
                Disconnect();
            }
        }

        private void WriteCallback(IAsyncResult ar)
        {
            if (IsDead)
                return;

            try
            {
                _stream.EndWrite(ar);
            }
            catch (IOException)
            {
                Disconnect();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Connection #{_id} EndWrite failed: {e}");
                Disconnect();
            }
        }

        private void ReadCallback(IAsyncResult ar)
        {
            if (IsDead)
                return;

            try
            {
                var bytesRead = _stream.EndRead(ar);

                var newStream = new MemoryStream();
                _readStream.CopyTo(newStream);
                newStream.Write(_buffer, 0, bytesRead);
                newStream.Seek(0, SeekOrigin.Begin);
                var oldStream = _readStream;
                _readStream = newStream;
                oldStream.Dispose();
            }
            catch (IOException)
            {
                Disconnect();
                return;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Connection #{_id} EndRead failed: {e}");
                Disconnect();
                return;
            }

            var lastValid = _readStream.Position;
            while (true)
            {
                if (_readStream.Length - lastValid == 0)
                    break; // if we know it's empty don't bother trying to parse anything
                try
                {
                    var reader = new BinaryReader(_readStream);
                    var packet = Utils.ReadPacket(reader);
                    lastValid = _readStream.Position;
                    ProcessPacket(packet);
                }
                catch (EndOfStreamException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Connection #{_id} reading or processing packet failed: {e}");
                    //_readStream.Seek(0, SeekOrigin.End);
                    Disconnect();
                    return;
                }
            }
            _readStream.Seek(lastValid, SeekOrigin.Begin);

            try
            {
                _stream.BeginRead(_buffer, 0, _buffer.Length, ReadCallback, null);
            }
            catch (IOException)
            {
                Disconnect();
                return;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Connection #{_id} BeginRead failed: {e}");
                Disconnect();
            }
        }
        
        public void ProcessPacket(IPacket packet)
        {
            switch (packet)
            {
                case KeepAlive:
                    SendPacket(new KeepAlive());
                    break;
                case SendClientToken p:
                    if (p.Token == SendClientToken.TokenV1)
                        SendPacket(new SendServerToken { Token = SendServerToken.TokenV101 });
                    else
                        Disconnect();
                    break;
                case SendPassword p:
                    if (_tmpAuthenticated)
                        break; // client seems to spam this repeatedly

                    if (p.Password == _config.AdminPassword)
                    {
                        _tmpAdmin = true;
                        _tmpAuthenticated = true;
                        Console.WriteLine($"Connection #{_id} authenticated as admin!");
                        SendPacket(new PasswordConfirm { Reply = PasswordConfirm.ReplyType.Correct });
                    }
                    else if (_config.Password.Length != 0 && p.Password != _config.Password)
                    {
                        SendPacket(new PasswordConfirm { Reply = PasswordConfirm.ReplyType.Incorrect });
                        Disconnect();
                    }
                    else
                    {
                        // We accept any password if no password is set
                        _tmpAuthenticated = true;
                        SendPacket(new PasswordConfirm { Reply = PasswordConfirm.ReplyType.Correct });
                    }
                    break;
                case SendNickname p:
                    var nick = p.Nick.Normalize().Trim();
                    if(Regex.IsMatch(nick, "[A-Za-z0-9\\.\\-]{1,12}"))
                    {
                        _tmpNickname = nick;
                        SendPacket(new NicknameConfirm {Reply = NicknameConfirm.ReplyType.Accepted});
                    }
                    else
                    {
                        SendPacket(new NicknameConfirm { Reply = NicknameConfirm.ReplyType.Invalid });
                        Disconnect();
                    }
                    break;
                case JoinedLobby:
                    //if (_player != null)
                        //break; // already joined, just swallow it

                    if (!_tmpAuthenticated)
                        throw new InvalidDataException("Tried joining lobby without authenticating");
                    try
                    {
                        var admin = _tmpAdmin;
                        var nickname = _tmpNickname;
                        lock (_server)
                        {
                            Player = _server.CreatePlayer(this, admin, nickname);
                            Console.WriteLine($"Connection #{_id} joined as nickname \"{Player.Nick}\" and ID {Player.Id}");

                            SendPacket(new EndPrevPlayers());
                            Player.OnJoin();
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Connection #{_id} failed CreatePlayer: {e}");
                        Disconnect();
                    }
                    break;
                case GameReady:
                    if (Player == null)
                        throw new InvalidDataException("Sent game ready before joining lobby");
                    lock (_server)
                        Player.OnGameReady();
                    break;
                case SendScore p:
                    if (Player == null)
                        throw new InvalidDataException("Sent game score before joining lobby");
                    lock (_server)
                        Player.OnScore(p.Score);
                    break;
                case GameEnd:
                    if (Player == null)
                        throw new InvalidDataException("Sent game end before joining lobby");
                    lock (_server)
                        Player.OnGameEnd();
                    break;
                case SendChatMessage p:
                    if (Player == null)
                        throw new InvalidDataException("Sent chat message before joining lobby");
                    lock (_server)
                        Player.OnChat(p.Id, p.Message.Normalize().Trim());
                    break;
                case ReadyDownload:
                    if (ChartPacket == null)
                        throw new InvalidDataException("Requested chart, but a chart packet isn't set");
                    SendPacket(ChartPacket);
                    break;
                case RequestVoices:
                    if (VoicesPacket == null)
                    {
                        SendPacket(new Deny());
                        throw new InvalidDataException("Requested voices, but a voices packet isn't set");
                    }
                    SendPacket(VoicesPacket);
                    break;
                case RequestInst:
                    if (InstPacket == null)
                    {
                        SendPacket(new Deny());
                        throw new InvalidDataException("Requested inst, but an inst packet isn't set");
                    }
                    SendPacket(InstPacket);
                    break;
                default:
                    throw new InvalidDataException("Sent unknown or unhandled packet");
            }
        }
    }
}

