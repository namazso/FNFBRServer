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

using System.IO;
using System.Text.Json;

namespace FNFBRServer
{
    /// <summary>
    /// Console app that allows telnet client to connect and chat on the port 10000.
    /// <para>Minimum sdk requirements: C# 5 and .Net 4.5 </para> 
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            var config = JsonSerializer.Deserialize<Config>(File.ReadAllBytes("config.json"));
            var server = new Server(config);
            server.Run();
        }
    }
}
