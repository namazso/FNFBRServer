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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using FNFBRServer.Packet;

namespace FNFBRServer
{
    class Server
    {
        private IPacket _chartPacket;
        private IPacket _instPacket;
        private IPacket _voicesPacket;
        private string _folder;
        private string _file;

        public Config Config;

        private bool _run = true;

        private readonly TcpListener _listener;

        private readonly AutoResetEvent _connectedEvent = new(false);
        
        private readonly List<Player> _players = new();
        private readonly List<NetworkPlayer> _networkPlayers = new();


        private readonly Timer _heartbeat;
        private readonly Timer _forceStartTimer;


        public Server(Config config)
        {
            Config = config;
            
            _listener = new TcpListener(IPAddress.Any, config.Port);
            _listener.Start();
            
            Console.WriteLine($"Listening on: {((IPEndPoint)_listener.LocalEndpoint).Port}");

            _heartbeat = new Timer(HeartbeatCallback);
            _forceStartTimer = new Timer(ForceStartCallback);
        }

        public void Run()
        {
            _heartbeat.Change(0, 1000);
            // Bind the socket to the local endpoint and listen for incoming connections.  
            try
            {
                while (_run)
                {
                    _listener.BeginAcceptTcpClient(AcceptCallback, null);

                    _connectedEvent.WaitOne();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception while running: " + e);
            }
            _heartbeat.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void HeartbeatCallback(object state)
        {
            lock (this)
            {
                foreach (var connection in _networkPlayers)
                    connection.Heartbeat();

                var deadConnections = _networkPlayers.Where(c => c.IsDead).ToImmutableArray();

                var deadPlayers = deadConnections
                    .Where(c => c.Player != null)
                    .Select(c => c.Player).ToImmutableArray();

                if(deadConnections.Length > 0 || deadPlayers.Length > 0)
                    Console.WriteLine($"Pruned {deadConnections.Length} dead connections and {deadPlayers.Length} dead players");

                _players.RemoveAll(p => deadPlayers.Contains(p));

                foreach (var player in _players)
                    foreach (var deadPlayer in deadPlayers)
                        player.NotifyPlayerLeave(deadPlayer);

                _networkPlayers.RemoveAll(c => deadConnections.Contains(c));

                UpdateReady();
            }
        }
        
        public Player CreatePlayer(NetworkPlayer networkPlayer, bool admin, string nick)
        {
            if (_players.Any(p => p.Nick == nick))
                throw new ArgumentException("Nickname already in use");

            Player player = null;

            for (var i = 0; i < 256; i++)
            {
                if(_players.FirstOrDefault(p => p.Id == i) != null)
                    continue;
                
                player = new Player(this, networkPlayer, i, admin, nick);
                _players.Add(player);
                break;
            }

            if (player == null)
                throw new OverflowException("Server full"); // fuck off we're full
            
            foreach (var p in _players.Where(p => p != player))
            {
                player.NotifyPlayerJoin(p, p.Nick);
                p.NotifyPlayerJoin(player, player.Nick);
            }

            return player;
        }

        public int PlayersInGame()
        {
            return _players.Count(player => player.State != Player.PlayerState.Lobby);
        }

        public void Say(string message)
        {
            foreach (var p in _players)
                p.NotifyServerChat(message);
        }

        public void SetSong(string folder, string file)
        {
            const string safetyFilter = "[a-zA-Z0-9_\\-]+";
            if (!Regex.IsMatch(folder, safetyFilter) || !Regex.IsMatch(file, safetyFilter))
                throw new ArgumentException("Invalid name");

            var chartPath = $"charts{Path.DirectorySeparatorChar}{folder}{Path.DirectorySeparatorChar}{file}.json";
            var instPath = $"charts{Path.DirectorySeparatorChar}{folder}{Path.DirectorySeparatorChar}Inst.ogg";
            var voicesPath = $"charts{Path.DirectorySeparatorChar}{folder}{Path.DirectorySeparatorChar}Voices.ogg";

            var chart = File.ReadAllBytes(chartPath);
            byte[] inst = null;
            try
            {
                inst = File.ReadAllBytes(instPath);
            }
            catch (Exception)
            {
                // ignored
            }

            byte[] voices = null;
            try
            {
                voices = File.ReadAllBytes(voicesPath);
            }
            catch (Exception)
            {
                // ignored
            }

            _chartPacket = new SendChart {File = chart};
            _instPacket = inst != null ? new SendInst {File = inst} : new Deny();
            _voicesPacket = voices != null ? new SendVoices {File = voices} : new Deny();
            _file = file;
            _folder = folder;
        }

        public void StartSong()
        {
            if (_chartPacket == null)
                throw new InvalidDataException("A song was not set");
            var inGame = _players.Count(player => player.State != Player.PlayerState.Lobby);
            if (inGame > 0)
                throw new InvalidDataException($"{inGame} players are still in game!");
            foreach (var p in _players)
                p.StartSong(_folder, _file, _chartPacket, _instPacket, _voicesPacket);
            _forceStartTimer.Change(Config.PrepareWait, Timeout.Infinite);
        }

        public void ForceStart()
        {
            if (_players.Any(p => p.SentStart))
                return; // game already started

            var startedCount = 0;
            foreach (var p in _players)
                switch (p.State)
                {
                    case Player.PlayerState.InGame:
                        ++startedCount;
                        p.NotifyStart();
                        break;
                    case Player.PlayerState.Preparing:
                        p.NotifyForceEnd(); // sometimes this seems to not get processed by client?
                        break;
                }
            if(startedCount > 0)
                Console.WriteLine($"Started game for {startedCount} players. Song: {_folder} {_file}");
        }

        private void ForceStartCallback(object state)
        {
            lock (this)
            {
                ForceStart();
            }
        }
        
        public Player FindPlayerByName(string nick) => _players.FirstOrDefault(p => p.Nick == nick);

        public void BroadcastChat(Player player, string message)
        {
            foreach (var p in _players)
                p.NotifyChat(player, message);
        }
        
        public void UpdateReady()
        {
            var preparing = _players.Count(p => p.State == Player.PlayerState.Preparing);
            var ready = _players.Count(p => p.State == Player.PlayerState.InGame);
            if (ready == 0)
                return;

            if (preparing == 0)
            {
                // everyone is ready
                ForceStart();
            }
            else
            {
                // notify readyness state
                foreach (var p in _players)
                    p.NotifyReady(preparing + ready, ready);
            }
        }

        public void ForceEnd()
        {
            foreach (var p in _players)
                p.NotifyForceEnd();
        }

        public void BroadcastScore(Player player, int score)
        {
            foreach (var p in _players)
                p.NotifyScore(player, score);
        }
        
        private void AcceptCallback(IAsyncResult ar)
        {
            try
            {
                var client = _listener.EndAcceptTcpClient(ar);
                lock (this)
                {
                    _networkPlayers.Add(new NetworkPlayer(this, client));
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed accepting player: " + e);
            }

            _connectedEvent.Set();
        }
    }
}
