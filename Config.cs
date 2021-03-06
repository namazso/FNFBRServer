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

namespace FNFBRServer
{
    class Config
    {
        public int Port { get; set; }
        public string Password { get; set; }
        public string AdminPassword { get; set; }
        public int SafeFrames { get; set; }
        public int WaitNominate { get; set; }
        public int WaitVote { get; set; }
        public int WaitPrepare { get; set; }
        public int WaitFinish { get; set; }
        public int MinimumPlayers { get; set; }
        public int MaximumNominations { get; set; }
        public string Motd { get; set; }
    }
}
