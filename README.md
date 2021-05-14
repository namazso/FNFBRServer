# FNFBRServer

A server reimplementation for [FunkinBattleRoyale](https://github.com/XieneDev/FunkinBattleRoyale), more fit for headless usage.

## User commands

### /help

Lists available commands and their descriptions

### /motd

Prints the MOTD

### /version

Prints the server version

### /nom <song> [difficulty]

Only usable during nomination period. Nominate a song. If no difficulty is specified the hardest recognized will be used.

### /search [substring]

Search for songs containing the specified substring. Songs matching with available difficulties are displayed

### Voting

During voting period just chat a number to vote

## Admin commands

### /say <message>

Chat as the server

### /kick <nick>

Kicks a player

### /setsong <song> [difficulty]

Set a song, overriding votes

### /start

Start current selected song. To be used when voting is off

### /forceend

Force ends current song for everyone playing.

### /voteon

Enable voting for next song

### /voteoff

Disable voting for next song

### /loadcharts

Find new folders, load all charts found in them

### /reloadcharts

Throw away current song list and rescan everything. WARNING: this is terribly slow and usually not a good idea.

## License

    FNFBRServer - A server reimplementation for FunkinBattleRoyale
    Copyright (C) 2021  namazso <admin@namazso.eu>

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
