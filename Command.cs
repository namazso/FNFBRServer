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
using System.Linq;

namespace FNFBRServer
{
    class Command
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

        private static Server.ChartEntry FindChart(Server server, string song, string difficulty)
        {
            if (!server.Charts.TryGetValue(song, out var charts))
                throw new ArgumentException("Song not found");

            Server.ChartEntry chart = null;

            if (difficulty != null)
            {
                chart = charts.FirstOrDefault(d => d.DifficultyNiceName == difficulty);
            }
            else
            {
                foreach (var diff in new[] { "hard", "normal", "easy" })
                {
                    chart = charts.FirstOrDefault(d => d.DifficultyNiceName == diff);
                    if (chart != null)
                        break;
                }
            }

            if (chart == null)
                throw new ArgumentException("Difficulty not found");

            return chart;
        }

        public static void ProcessCommand(Server server, Player player, string message)
        {
            foreach (var command in Commands)
            {
                if (!(message.StartsWith(command.Name + " ") || message.Equals(command.Name)))
                    continue;

                if (command.AdminOnly && !player.IsAdmin)
                {
                    player.NotifyServerChat("You don't have permission for this!");
                    return;
                }

                var rest = message[command.Name.Length..].TrimStart();
                var args = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                try
                {
                    command.OnExecute(server, player, rest, args);
                }
                catch (Exception e)
                {
                    player.NotifyServerChat($"Failed: {e.Message}");
                }

                return;
            }
            player.NotifyServerChat("Unknown command, try /help");
        }

        public static readonly Command[] Commands =
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
            new("/version", "print server version", false, (_, player, _, _) =>
            {
                player.PrintVersion();
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
                    throw new ArgumentException("Usage: /setsong <song> [difficulty]");

                var chart = FindChart(server, args[0], args.Length == 2 ? args[1] : null);

                server.SetSong(chart);
                server.Say($"Song set to: {chart.SongName} {chart.DifficultyNiceName}");
            }),
            new("/nom", "nominate for next song", false, (server, player, _, args) =>
            {
                if (server.State != Server.ServerState.Nomination || !server.VotingEnabled)
                    throw new ArgumentException("You cannot nominate now");

                if (args.Length != 2 && args.Length != 1)
                    throw new ArgumentException("Usage: /nom <song> [difficulty]");

                var chart = FindChart(server, args[0], args.Length == 2 ? args[1] : null);

                server.Nominate(chart);
                server.Say($"{player.Nick} nominated: {chart.SongName} {chart.DifficultyNiceName}");
            }),
            new("/start", "start song", true, (server, _, _, _) =>
            {
                server.ManualStartSong();
            }),
            new("/forceend", "force end song", true, (server, _, _, _) =>
            {
                server.ForceEnd();
                server.Say("Force ended song");
            }),
            new("/voteon", "enable voting", true, (server, _, _, _) =>
            {
                server.VotingEnabled = true;
            }),
            new("/voteoff", "disable voting", true, (server, _, _, _) =>
            {
                server.VotingEnabled = false;
            }),
            new("/loadcharts", "load new charts", true, (server, _, _, _) =>
            {
                server.LoadCharts(false);
            }),
            new("/reloadcharts", "reload charts", true, (server, _, _, _) =>
            {
                server.LoadCharts();
            }),
            new("/search", "search for a song", false, (server, player, args, _) =>
            {
                var search = args.ToLowerInvariant();
                foreach (var (key, value) in server.Charts.Where(s => s.Key.Contains(search)))
                {
                    var msg = key + " (";
                    msg += string.Join(" ", value.Select(v => v.DifficultyNiceName));
                    msg += ")";
                    player.NotifyServerChat(msg);
                }
            })
        };
    }
}
