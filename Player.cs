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
using System.IO;
using System.Linq;
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

        public PlayerState State { get; private set; } = PlayerState.Lobby;

        public bool SentStart { get; private set; }

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
            if(inGame != 0)
                NotifyServerChat($"{inGame} players are currently playing a song.");
        }
        
        public void Kick()
        {
            _server.Say($"Kicked {Nick}");
            _networkPlayer.Disconnect();
        }

        public void StartSong(string folder, string file, IPacket chart, IPacket inst, IPacket voices)
        {
            _networkPlayer.ChartPacket = chart;
            _networkPlayer.InstPacket = inst;
            _networkPlayer.VoicesPacket = voices;
            _networkPlayer.SendPacket(new GameStart {Folder = folder, Song = file});
            State = PlayerState.Preparing;
        }

        public void NotifyReady(int total, int ready)
        {
            if (State != PlayerState.InGame)
                return;
            _networkPlayer.SendPacket(new PlayersReady {Count = (byte)ready});
        }

        public void NotifyStart()
        {
            if (State != PlayerState.InGame)
                return;
            if (SentStart)
                return;
            _networkPlayer.SendPacket(new PlayersReady {Count = 255});
            _networkPlayer.SendPacket(new EveryoneReady {SafeFrames = (byte) _server.Config.SafeFrames});
            SentStart = true;
        }

        public void NotifyScore(Player player, int score)
        {
            if (player != this)
                _networkPlayer.SendPacket(new BroadcastScore {Player = (byte) player.Id, Score = score});
        }

        public void NotifyForceEnd()
        {
            _networkPlayer.SendPacket(new ForceGameEnd());
            SentStart = false;
            State = PlayerState.Lobby; // force set to lobby
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
                SentStart = false;
                State = PlayerState.Lobby;
            }
        }

        public void OnGameEnd()
        {
            _endTimer.Change(1000, Timeout.Infinite);
        }

        public void PrintMultiLine(string str)
        {
            foreach (var line in str.Split('\n'))
                NotifyServerChat(line);
        }

        public void PrintMotd() => PrintMultiLine(_server.Config.Motd);

        public void PrintVersion() => PrintMultiLine(Constants.VersionInfo);

        private class Command
        {
            public readonly string Name;
            public readonly string Description;
            public readonly bool AdminOnly;

            public delegate void OnExecuteDelegate(Server server, Player player, string fullArgs, string[] args);

            public readonly OnExecuteDelegate OnExecute;

            public Command(string name, string description, bool adminOnly, OnExecuteDelegate onExecute)
            {
                Name = name;
                Description = description;
                AdminOnly = adminOnly;
                OnExecute = onExecute;
            }
        }

        private static readonly Command[] Commands =
        {
            new("/help", "print this help", false, (_, player, _, _) =>
            {
                player.NotifyServerChat("Supported commands: ");
                foreach (var cmd in Commands)
                    if (!cmd.AdminOnly || player.IsAdmin)
                        player.NotifyServerChat($"{cmd.Name} - {cmd.Description}");
            }),
            new("/motd", "print server motd", false, (_, player, _, _) =>
            {
                player.PrintMotd();
            }),
            new("/say", "chat as the server", true, (server, _, fullArgs, _) =>
            {
                server.Say(fullArgs);
            }),
            new("/kick", "kick a player", true, (server, _, fullArgs, _) =>
            {
                var target = server.FindPlayerByName(fullArgs);
                if (target == null)
                    throw new ArgumentException("No such player");

                target.Kick();
            }),
            new("/setsong", "set next song", true, (server, _, _, args) =>
            {
                if (args.Length != 2 && args.Length != 1)
                    throw new ArgumentException("Usage: /setsong [folder] <file without .json>");

                string file;
                string folder;
                if (args.Length == 1)
                {
                    file = args[0];
                    if (file.EndsWith("-hard"))
                        folder = file.Remove(file.Length - "-hard".Length);
                    else if (file.EndsWith("-easy"))
                        folder = file.Remove(file.Length - "-easy".Length);
                    else
                        folder = file;
                    
                }
                else
                {
                    folder = args[0];
                    file = args[1];
                }

                server.SetSong(folder, file);
                server.Say($"Song set to: {folder} {file}");
            }),
            new("/start", "start song", true, (server, _, _, _) =>
            {
                server.StartSong();
            }),
            new("/forceend", "force end song", true, (server, _, _, _) =>
            {
                server.ForceEnd();
                server.Say("force ended song");
            }),
            new("/forcestart", "force start song for people preparing", true, (server, _, _, _) =>
            {
                server.ForceStart();
            }),
            new("/search", "search for a song", false, (_, player, args, _) =>
            {
                args = args.ToLowerInvariant();
                Directory.EnumerateDirectories(Constants.ChartsFolder)
                    .Where(d => d.ToLowerInvariant().Contains(args))
                    .ToList()
                    .ForEach(player.NotifyServerChat);
            })
        };

        private void ProcessCommand(string message)
        {
            foreach (var command in Commands)
            {
                if (!message.StartsWith(command.Name))
                    continue;

                if (command.AdminOnly && !IsAdmin)
                {
                    NotifyServerChat("You don't have permission for this!");
                    return;
                }

                var rest = message.Substring(command.Name.Length).TrimStart();
                var args = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                try
                {
                    command.OnExecute(_server, this, rest, args);
                }
                catch (Exception e)
                {
                    NotifyServerChat($"Failed: {e.Message}");
                }
                
                return;
            }
            NotifyServerChat("Unknown command, try /help");
        }

        public void OnChat(int id, string message)
        {
            if (message.Length == 0)
                return;
            Console.WriteLine($"[{Id}|{Nick}] {message}");
            if (message.StartsWith('/'))
                ProcessCommand(message);
            else
                _server.BroadcastChat(this, message);
        }
    }
}
