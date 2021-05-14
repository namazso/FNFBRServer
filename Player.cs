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
using System.Collections.Generic;
using System.Threading;
using FNFBRServer.Packet;

namespace FNFBRServer
{
    class Player
    {
        public enum PlayerState
        {
            Lobby,
            Preparing,
            InGame
        };

        public readonly int Id;
        public readonly bool IsAdmin;
        public readonly string Nick;

        public int Vote { get; private set; } = -1;

        public PlayerState State { get; private set; } = PlayerState.Lobby;

        private readonly Server _server;
        private readonly NetworkPlayer _networkPlayer;

        private readonly List<IPacket> _pendingPackets = new();

        private readonly Timer _endTimer;

        public Player(Server server, NetworkPlayer networkPlayer, int id, bool isAdmin, string nick)
        {
            _server = server;
            _networkPlayer = networkPlayer;
            Id = id;
            IsAdmin = isAdmin;
            Nick = nick;

            _endTimer = new Timer(EndTimerCallback);
        }

        private void LobbyPacket(IPacket packet)
        {

            if (State == PlayerState.Lobby)
            {
                foreach (var pendingPacket in _pendingPackets)
                    _networkPlayer.SendPacket(pendingPacket);
                _pendingPackets.Clear();
                _networkPlayer.SendPacket(packet);
            }
            else
            {
                _pendingPackets.Add(packet);
            }
        }

        public void NotifyPlayerJoin(Player player, string nick)
        {
            if (player == this)
                return;
            LobbyPacket(new BroadcastNewPlayer {Id = (byte) player.Id, Nickname = nick});
        }

        public void NotifyPlayerLeave(Player player)
        {
            if (player == this)
                return;
            LobbyPacket(new PlayerLeft {Id = (byte) player.Id});
        }

        public void NotifyChat(Player player, string message)
        {
            if (player == this)
                return;
            LobbyPacket(new BroadcastChatMessage {Player = (byte) player.Id, Message = message});
        }

        public void NotifyServerChat(string message) => LobbyPacket(new ServerChatMessage {Message = message});

        public void OnJoin()
        {
            PrintVersion();
            PrintMotd();
            var inGame = _server.PlayersInGame();
            if (inGame != 0)
                NotifyServerChat($"{inGame} players are currently playing a song.");
            if (_server.State == Server.ServerState.Dead)
                NotifyServerChat("Waiting for more players to join...");
        }
        
        public void Kick()
        {
            _server.Say($"Kicked {Nick}");
            _networkPlayer.Disconnect();
        }
        
        public void NotifyReady(int total, int ready)
        {
            if (State != PlayerState.InGame)
                return;
            _networkPlayer.SendPacket(new PlayersReady {Count = (byte)ready});
        }
        
        public void NotifyScore(Player player, int score)
        {
            if (player != this)
                _networkPlayer.SendPacket(new BroadcastScore {Player = (byte) player.Id, Score = score});
        }
        
        public void OnServerStateChange(Server.ServerState newState)
        {
            switch (newState)
            {
                case Server.ServerState.Nomination:
                    if (State is PlayerState.InGame or PlayerState.Preparing)
                    {
                        _networkPlayer.SendPacket(new ForceGameEnd());
                        OnGameEnd();
                    }
                    break;
                case Server.ServerState.Voting:
                    Vote = -1;
                    if (State is PlayerState.InGame or PlayerState.Preparing)
                        _networkPlayer.Disconnect();
                    break;
                case Server.ServerState.Preparing:
                    _networkPlayer.ChartPacket = _server.ChartPacket;
                    _networkPlayer.InstPacket = _server.InstPacket;
                    _networkPlayer.VoicesPacket = _server.VoicesPacket;
                    _networkPlayer.SendPacket(new GameStart { Folder = _server.Folder, File = _server.File });
                    State = PlayerState.Preparing;
                    break;
                case Server.ServerState.Playing:
                    if (State != PlayerState.InGame)
                        return;
                    _networkPlayer.SendPacket(new PlayersReady { Count = 255 });
                    _networkPlayer.SendPacket(new EveryoneReady { SafeFrames = (byte)_server.Config.SafeFrames });
                    break;
                case Server.ServerState.Dead:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(newState), newState, null);
            }
        }

        public void OnGameReady()
        {
            State = PlayerState.InGame;
            _server.UpdateReady();
        }

        public void OnScore(int score) => _server.BroadcastScore(this, score);
        
        private void EndTimerCallback(object state)
        {
            lock (_server)
            {
                if (State is PlayerState.InGame or PlayerState.Preparing)
                    State = PlayerState.Lobby;
            }
        }

        // Need to delay this because if we just dump the queued messages instantly client crashes
        public void OnGameEnd() => _endTimer.Change(1000, Timeout.Infinite);

        public void PrintMultiLine(string str)
        {
            foreach (var line in str.Split('\n'))
                NotifyServerChat(line);
        }

        public void PrintMotd() => PrintMultiLine(_server.Config.Motd);

        public void PrintVersion() => PrintMultiLine(Constants.VersionInfo);
        
        public void OnChat(int id, string message)
        {
            if (message.Length == 0)
                return;

            Console.WriteLine($"[{Id}|{Nick}] {message}");

            if (message.StartsWith('/'))
            {
                Command.ProcessCommand(_server, this, message);
                return;
            }

            if (_server.State == Server.ServerState.Voting)
            {
                if (Vote == -1 && int.TryParse(message, out var vote))
                {
                    Vote = vote;
                    NotifyServerChat("Vote successful!");
                }
                return;
            }
            
            _server.BroadcastChat(this, message);
        }
    }
}
