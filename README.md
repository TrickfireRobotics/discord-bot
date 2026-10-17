# discord-bot

This is the bot we use to track when our members come in and out of our lab.

Its primary purpose is so people know when members of respective teams are in
the shop and actively working. It also doubles as a way for members to see that
work is actually being done on the robot, which we think can increase a sense of
community and motivation.

# Contributing

This project is built using [DSharpPlus](https://dsharpplus.github.io/). A
devcontainer is set up under `.devcontainer/` with the .NET SDK and C#
tooling pre-installed — open the repo in VS Code and reopen in container to
get a working environment without installing anything locally.

Before running, ensure that there is a `secrets.txt` file in the build
directory with the Discord bot token on the first line and the Notion secret
on the second. Additionally, create a `config.json` file in the build
directory with the properties defined in [`config.json`](DiscordBot/config.json).

See `docs/` for setup, configuration, deployment, and architecture details.