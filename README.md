# BotController

A web page and backend that run a bot for [Badger Brawl (NetRTS)](https://github.com/SnowSE/NetRTS).
The page talks to the backend (`Bot.UI`), and the backend plays matches on a NetRTS game server.
The bot's strategy is in [`Bot.UI/game/Strategy.cs`](Bot.UI/game/Strategy.cs).

## Getting the code

The NetRTS game server is included as a git submodule in `NetRTS/`, so clone with:

```bash
git clone --recurse-submodules https://github.com/kirah-cox/BotController.git
```

If you already cloned without it, run `git submodule update --init` in the repo.

## Playing on the competition server

```bash
dotnet run --project Bot.UI
```

Open http://localhost:5114, register a bot name, and start a match. Matches on
https://netrts.snowse.io are public.

## Practising privately on a local server

Run the game server in one terminal:

```bash
cd NetRTS
dotnet run --project src/NetRts.Server
```

and the bot controller, pointed at it, in another:

```bash
dotnet run --project Bot.UI --launch-profile local
```

Each server needs its own registration, so register again on the local one.

## API keys

Registering saves the bot's API key to `Bot.UI/netrts-key.<server>.json`. These files are in
`.gitignore`: never commit them. Back up the competition server's key file (it is shown only once),
and copy it to `Bot.UI/` on any other machine you play from.

## Updating NetRTS

```bash
git submodule update --remote NetRTS
git add NetRTS
git commit -m "Update NetRTS"
```
