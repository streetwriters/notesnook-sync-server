# Notesnook Sync Server

This repo contains the full source code of the Notesnook Sync Server licensed under AGPLv3.

## Building

### From source

Requirements:

1. [.NET 8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
2. [git](https://git-scm.com/downloads)

The first step is to `clone` the repository:

```bash
git clone https://github.com/streetwriters/notesnook-sync-server.git

# change directory
cd notesnook-sync-server
```

Once you are inside the `./notesnook-sync-server` directory, run:

```bash
# this might take a while to complete
dotnet restore Notesnook.sln
```

Then build all projects:

```bash
dotnet build Notesnook.sln
```

To run the `Notesnook.API` project:

```bash
dotnet run --project Notesnook.API/Notesnook.API.csproj
```

To run the `Streetwriters.Messenger` project:

```bash
dotnet run --project Streetwriters.Messenger/Streetwriters.Messenger.csproj
```

To run the `Streetwriters.Identity` project:

```bash
dotnet run --project Streetwriters.Identity/Streetwriters.Identity.csproj
```

### Using docker

The sync server can easily be started using Docker.

```bash
wget https://raw.githubusercontent.com/streetwriters/notesnook-sync-server/master/docker-compose.yml
```

And then use Docker Compose to start the servers:

```bash
docker compose up
```

This takes care of setting up everything including MongoDB, Minio etc.

For detailed self-hosting instructions, see the [self-hosting documentation](docs/self-hosting.md).

## Self-hosting

Self-hosting the Notesnook Sync Server is now possible using Docker. For complete setup instructions, see the [self-hosting documentation](docs/self-hosting.md).

## License

```
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2023 Streetwriters (Private) Limited

This program is free software: you can redistribute it and/or modify
it under the terms of the Affero GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
Affero GNU General Public License for more details.

You should have received a copy of the Affero GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
```
