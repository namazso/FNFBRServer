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
        public IPacket ChartPacket { get; private set; }
        public IPacket InstPacket { get; private set; }
        public IPacket VoicesPacket { get; private set; }
        public string Folder { get; private set; }
        public string File { get; private set; }

        private long _length;

        public Config Config;

        private bool _run = true;

        public bool VotingEnabled { get; set; } = true;

        private readonly TcpListener _listener;

        private readonly AutoResetEvent _connectedEvent = new(false);
        
        private readonly List<Player> _players = new();
        private readonly List<NetworkPlayer> _networkPlayers = new();

        public class ChartEntry
        {
            public string DifficultyName { get; set; }
            public string DifficultyNiceName => DifficultyName == "" ? "normal" : DifficultyName;
            public string LocalFolder { get; set; }
            public string SongName { get; set; }
            public byte[] Data { get; set; }
        }

        public Dictionary<string, List<ChartEntry>> Charts { get; } = new();

        private readonly List<ChartEntry> _nominations = new();

        private readonly Timer _heartbeat;

        public enum ServerState
        {
            // Players are free to chat and nominate maps
            Nomination,

            // Nominated maps are printed, chat is suppressed, numbers chatted are considered the vote
            Voting,

            // People are downloading or waiting for other players
            Preparing,

            // In game / playing a song
            Playing,

            // Server has below threshold number of players
            Dead
        };

        private readonly Timer _stateAdvanceTimer;
        private ServerState _state = ServerState.Dead;

        public ServerState State
        {
            get => _state;
            private set
            {
                switch (value)
                {
                    case ServerState.Nomination:
                        _nominations.Clear();
                        if (VotingEnabled)
                        {
                            Say("Nominations have started. Use /nom to nominate, /search to search");
                            _stateAdvanceTimer.Change(Config.WaitNominate, Timeout.Infinite);
                        }
                        break;
                    case ServerState.Voting:
                        ChartPacket = null;
                        Say("Voting has started. Type the number of your vote. Nominated songs:");
                        if (_nominations.Count < 5)
                        {
                            var allCharts = Charts.SelectMany(p => p.Value).ToImmutableArray();
                            var random = new Random();
                            while (_nominations.Count < 5)
                            {
                                try
                                {
                                    Nominate(allCharts[random.Next(allCharts.Length)]);
                                }
                                catch (Exception)
                                {
                                    // ignored
                                }
                            }
                        }
                        for (var index = 0; index < _nominations.Count; index++)
                        {
                            var nomination = _nominations[index];
                            Say($"{index}. {nomination.SongName} {nomination.DifficultyNiceName}");
                        }
                        _stateAdvanceTimer.Change(Config.WaitVote, Timeout.Infinite);
                        break;
                    case ServerState.Preparing:
                        if (ChartPacket == null)
                        {
                            var val = _players
                                .Select(p => p.Vote)
                                .Where(i => i >= 0 && i < _nominations.Count)
                                .GroupBy(i => i)
                                .OrderByDescending(g => g.Count())
                                .FirstOrDefault();
                            SetSong(_nominations[val?.Key ?? 0]);
                        }
                        _stateAdvanceTimer.Change(Config.WaitPrepare, Timeout.Infinite);
                        break;
                    case ServerState.Playing:
                        _stateAdvanceTimer.Change(_length + Config.WaitFinish, Timeout.Infinite);
                        break;
                    case ServerState.Dead:
                        _stateAdvanceTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(value), value, null);
                }
                _state = value;

                foreach (var player in _players)
                    player.OnServerStateChange(value);
            }
        }

        private void StateAdvanceCallback(object state)
        {
            lock (this)
            {
                State = State switch
                {
                    ServerState.Nomination  => ServerState.Voting,
                    ServerState.Voting      => ServerState.Preparing,
                    ServerState.Preparing   => ServerState.Playing,
                    ServerState.Playing     => ServerState.Nomination,
                    _                       => throw new ArgumentOutOfRangeException(nameof(State), State, null)
                };
            }
        }

        public Server(Config config)
        {
            Config = config;

            LoadCharts();
            
            _listener = new TcpListener(IPAddress.Any, config.Port);
            _listener.Start();
            
            Console.WriteLine($"Listening on: {((IPEndPoint)_listener.LocalEndpoint).Port}");

            _heartbeat = new Timer(HeartbeatCallback);
            _stateAdvanceTimer = new Timer(StateAdvanceCallback);
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

                var deadConnections = _networkPlayers
                    .Where(c => c.IsDead)
                    .ToImmutableArray();

                var deadPlayers = deadConnections
                    .Where(c => c.Player != null)
                    .Select(c => c.Player)
                    .ToImmutableArray();

                if(deadConnections.Length > 0 || deadPlayers.Length > 0)
                    Console.WriteLine($"Pruned {deadConnections.Length} dead connections and {deadPlayers.Length} dead players");

                _players.RemoveAll(p => deadPlayers.Contains(p));

                foreach (var player in _players)
                    foreach (var deadPlayer in deadPlayers)
                        player.NotifyPlayerLeave(deadPlayer);

                _networkPlayers.RemoveAll(c => deadConnections.Contains(c));

                if (_players.Count < Config.MinimumPlayers && State != ServerState.Dead)
                    State = ServerState.Dead;

                if (_players.Count >= Config.MinimumPlayers && State == ServerState.Dead)
                    State = ServerState.Nomination;

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

        public int PlayersInGame() => _players.Count(player => player.State != Player.PlayerState.Lobby);

        public void Say(string message) => _players.ForEach(p => p.NotifyServerChat(message));

        public void Nominate(ChartEntry chart)
        {
            if (_nominations.Count >= Config.MaximumNominations)
                throw new ArgumentException("Maximum number of nominations already reached");
            if (_nominations.Contains(chart))
                throw new ArgumentException("Chart already nominated");
            _nominations.Add(chart);
        }

        public void LoadCharts(bool full = true)
        {
            if(full)
                Charts.Clear();
            
            var normalize = new Func<string, string>(s => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9\\-]", "-").Trim('-'));
            
            foreach (var folder in Directory.EnumerateDirectories(Constants.ChartsFolder))
            {
                var folderName = Path.GetFileName(folder);
                if(folderName == null)
                    continue;
                var songName = normalize(folderName);
                if(!Charts.TryGetValue(songName, out var difficultyCharts))
                {
                    difficultyCharts = new();
                    Charts.Add(songName, difficultyCharts);
                }
                else if (!full)
                {
                    continue; // if we aren't doing a full reload just skip folders we already know of
                }


                foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if(fileName is null or "config")
                        continue;
                    var difficulty = normalize(fileName).Replace(songName, null).Trim('-');
                    byte[] data;
                    try
                    {
                        data = Chart.FixChart(System.IO.File.ReadAllBytes(file), songName);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"WARNING: map {file} is invalid: {e.Message}");
                        continue;
                    }

                    difficultyCharts.Add(new ChartEntry {Data = data, DifficultyName = difficulty, LocalFolder = folder, SongName = songName});
                }
            }
        }

        public void SetSong(ChartEntry chartEntry)
        {
            var instPath = Path.Join(chartEntry.LocalFolder, "Inst.ogg");
            var voicesPath = Path.Join(chartEntry.LocalFolder, "Voices.ogg");
            
            // Note: This means that even default songs must have their instrumentals available
            var inst = System.IO.File.ReadAllBytes(instPath);

            // This also verifies if the song is a valid OGG Vorbis
            _length = (long)(new NVorbis.VorbisReader(instPath).TotalTime).TotalMilliseconds;

            byte[] voices;
            try
            {
                voices = System.IO.File.ReadAllBytes(voicesPath);
            }
            catch (Exception)
            {
                // client b_u_g: voices are requested for charts that don't need voices
                voices = System.IO.File.ReadAllBytes("silence.ogg");
            }

            ChartPacket = new SendChart { File = chartEntry.Data };
            InstPacket = inst != null ? new SendInst { File = inst } : new Deny();
            VoicesPacket = new SendVoices { File = voices };
            File = chartEntry.SongName + chartEntry.DifficultyName == "" ? chartEntry.DifficultyName : "-" + chartEntry.DifficultyName;
            Folder = chartEntry.SongName;
        }

        public void ManualStartSong()
        {
            if (ChartPacket == null)
                throw new InvalidDataException("A song was not set");
            State = ServerState.Preparing;
        }
        
        public Player FindPlayerByName(string nick) => _players.FirstOrDefault(p => p.Nick == nick);

        public void BroadcastChat(Player player, string message)
        {
            foreach (var p in _players)
                p.NotifyChat(player, message);
        }
        
        public void UpdateReady()
        {
            if (State != ServerState.Preparing)
                return;

            var preparing = _players.Count(p => p.State == Player.PlayerState.Preparing);
            var ready = _players.Count(p => p.State == Player.PlayerState.InGame);
            if (ready == 0)
                return;
            
            if (preparing == 0)
            {
                // everyone is ready
                State = ServerState.Playing;
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
            State = ServerState.Nomination;
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
