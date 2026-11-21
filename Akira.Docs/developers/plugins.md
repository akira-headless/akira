# Plugin API

A plugin adds a subtitle source, a translator or a bridge to Akira, without a change to the server. A plugin is a .NET DLL. It references only `Akira.Contracts`.

## Project

Make a class library for `net10.0`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <EnableDynamicLoading>true</EnableDynamicLoading>
    </PropertyGroup>
    <ItemGroup>
        <!-- Add this for a bridge, which has controllers -->
        <FrameworkReference Include="Microsoft.AspNetCore.App" />
        <!-- The server has the contract, so it is not copied -->
        <ProjectReference Include="..\server\Akira.Contracts\Akira.Contracts.csproj" Private="false" ExcludeAssets="runtime" />
    </ItemGroup>
</Project>
```

## Entry class

Each plugin has one entry class with a parameterless constructor, and the contract version on the assembly:

```csharp
[assembly: AkiraPluginApiVersion(AkiraPluginApi.Major, AkiraPluginApi.Minor)]

[AkiraPlugin("example")]
public sealed class ExamplePlugin : IAkiraPlugin
{
    public PluginManifest Manifest { get; } = new("Example", "Finds subtitles on example.org.",
    [
        new PluginSetting("ApiKey", "API key", PluginSettingKind.Secret) { Required = true }
    ]);

    public void ConfigureServices(IServiceCollection services, IPluginSettings settings)
    {
        services.AddSingleton<ISubtitleSource>(new ExampleSource(settings.Get("ApiKey")));
    }
}
```

The key has lowercase letters, digits and dashes, at most 32.

## Settings

Settings come from the configuration section `Plugins:{key}`, for example the environment variable `Plugins__example__ApiKey`. The manifest describes them: a key, a label, a kind (`Text`, `Secret`, `Number`, `Toggle`, `Url`, `User`), whether it is required, and a description.

- A plugin with a missing required setting stops the server at start, with the setting named.
- Akira never logs or returns the value of a setting. The API only says whether it is set.
- A bridge only runs with `Plugins:{key}:Enabled` set to `true`.

## Subtitle sources and translators

Register one or both in `ConfigureServices`.

| Interface | Does |
|-----------|------|
| `ISubtitleSource` | `SearchAsync` finds subtitles for a movie or episode in one language, best first. `DownloadAsync` gets one. |
| `ISubtitleTranslator` | `StartAsync` starts a translation of a subtitle file and returns a job id. `GetAsync` reports its progress and, when done, the path of the result. `CancelAsync` stops it. |

`IsConfigured` tells Akira whether the provider can be used. Languages are ISO 639-2/B, for example `dut`.

To report a failure, throw `ProviderException`. Set `transient` when a retry later can work, such as a timeout. The message of any other failure goes to the user.

## Bridges

A bridge gives apps of other servers an API they know. Register an `IClientBridge` as a singleton and answer with controllers that derive from `BridgeController<TBridge>`.

- Akira answers the bridge under `/bridges/{key}`, whatever routes the controllers declare.
- `Connect` tells the user how an app connects: with a link (`Link`), an address and a pairing code (`Address`), or by itself on the network (`Discovery`).
- The bridge controllers are anonymous to Akira. Use `IAccountAccess` to find out who calls.
- Reach the library through the facades, never through Akira's own types:

| Facade | For |
|--------|-----|
| `IAccountAccess` | Callers, device tokens, links and pairing |
| `ILibraryAccess` | Items, versions, tracks and artwork |
| `IPlaybackAccess` | Playback plans for the client's capabilities, and the session of each |
| `IWatchStateAccess` | Progress and watched state |
| `IMediaAccess` | Signed media urls |
| `IEventFeed` | A user's library and watch state changes after a sequence |
| `IServerInfo` | The server's stable id and its local addresses |

A plan from `IPlaybackAccess.PlanAsync` does not start a playback. Call `StartAsync` when the app really plays; a progress report also starts it. Then end it with `StopAsync` at a position, or with `EndAsync` when nobody knows the position. A report returns `false` when the session is gone.

Make a call to `ILibraryAccess.VersionsAsync` with all the items of a listing, not one call for each item.

## Loading

Akira looks in `Plugins:Path` once at start. Default is `plugins` next to the server; in Docker, mount a folder at `/app/plugins`.

- A DLL directly in the folder is a plugin.
- In each subfolder, the DLL with the name of the folder is a plugin. Put its own dependencies next to it.

A plugin that does not load, has another major version or a newer minor version, or has a bridge that cannot be made, is skipped and logged. A background service of a plugin that fails is logged. A plugin never stops the server. `GET /api/v1/plugins` lists every plugin and why one did not load.

Plugins run in the server's process, with its rights. Only add DLLs you trust.

## Versions

`AkiraPluginApi` has a major and a minor version. Akira loads a plugin of the same major version and the same or an older minor version. Until the first release the contract stays 1.0 and can change.

A minor version only adds: a member to an interface that Akira implements, an `init` property to a record, or a method with a default body to an interface that a plugin implements. Any other change makes a new major version. So set new record values with an initializer, for example `new ItemQuery(ItemKind.Movie) { Limit = 50 }`.
