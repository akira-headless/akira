# Building

The server repository holds the .NET 10 server and the admin page (`admin`).

The design and its reasons are in `docs`, next to the repositories.

## Server

Needs the .NET 10 SDK.

```bash
dotnet build Akira.slnx
dotnet test Akira.slnx --filter "Category!=Integration"
dotnet run --project Akira.Server
```

The server answers on `http://localhost:8484`, with Scalar at `/scalar`. The integration tests need Docker (MariaDB) and ffprobe.

## Admin page

```bash
cd Akira.Client
npm install
npm run dev     # http://localhost:5173, against the server on 8484
npm run build   # into Akira.Server/wwwroot
```
