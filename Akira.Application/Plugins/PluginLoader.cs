using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Akira.Contracts.Bridges;
using Akira.Contracts.Plugins;
using Akira.Contracts.Subtitles;

namespace Akira.Server.Plugins;

/// <summary>
/// Registers the built-in plugins and loads the plugin DLLs from Plugins:Path: each DLL directly in it, and in each
/// subfolder the DLL named like the folder, next to its own dependencies.
/// </summary>
public static partial class PluginLoader
{
    public const string SectionName = "Plugins";

    private static readonly IReadOnlyDictionary<Type, string> Kinds = new Dictionary<Type, string>
    {
        [typeof(ISubtitleSource)] = "subtitleSource",
        [typeof(ISubtitleTranslator)] = "subtitleTranslator",
        [typeof(IClientBridge)] = LoadedPlugin.BridgeKind
    };

    public static PluginCatalog AddPlugins(this IServiceCollection services, IConfiguration configuration,
        IEnumerable<(Type Plugin, string Section)> builtIns)
    {
        var path = configuration[$"{SectionName}:Path"] is { Length: > 0 } configured ? configured : "plugins";
        var catalog = new PluginCatalog(Path.GetFullPath(path, AppContext.BaseDirectory));
        services.AddSingleton(catalog);

        foreach (var (type, section) in builtIns)
        {
            // A built-in that does not register is a bug, so it stops the server
            if (Register(services, configuration, catalog, type, section, file: null) is { } problem)
            {
                throw new InvalidOperationException(problem);
            }
        }

        foreach (var file in Files(catalog.Path))
        {
            var name = Path.GetFileName(file);
            try
            {
                LoadFile(services, configuration, catalog, file, name);
            }
            catch (Exception exception) when (exception is not PluginSettingsException)
            {
                catalog.Skipped.Add(new SkippedPlugin(name, PluginCatalog.Reason(exception)));
            }
        }

        return catalog;
    }

    private static IEnumerable<string> Files(string path)
    {
        if (!Directory.Exists(path))
        {
            return [];
        }

        var inFolders = Directory.EnumerateDirectories(path)
            .Select(folder => Path.Combine(folder, Path.GetFileName(folder) + ".dll"))
            .Where(File.Exists);
        return Directory.EnumerateFiles(path, "*.dll").Concat(inFolders).Order(StringComparer.Ordinal);
    }

    private static void LoadFile(IServiceCollection services, IConfiguration configuration, PluginCatalog catalog, string file, string name)
    {
        var assemblyName = AssemblyName.GetAssemblyName(file);

        // A copy of the contract or another assembly the server has already is a dependency, not a plugin
        if (AssemblyLoadContext.Default.Assemblies.Any(a => string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var assembly = new PluginLoadContext(file).LoadFromAssemblyPath(file);
        if (assembly.GetCustomAttribute<AkiraPluginApiVersionAttribute>() is not { } version)
        {
            return;
        }

        // A newer minor may call what this server does not have, which would only show at the first request
        if (version.Major != AkiraPluginApi.Major || version.Minor > AkiraPluginApi.Minor)
        {
            catalog.Skipped.Add(new SkippedPlugin(name,
                $"It was built for plugin API {version.Major}.{version.Minor}, this server has {AkiraPluginApi.Major}.{AkiraPluginApi.Minor}."));
            return;
        }

        var plugins = assembly.GetExportedTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false } && type.GetCustomAttribute<AkiraPluginAttribute>() is not null)
            .ToList();
        if (plugins.Count == 0)
        {
            catalog.Skipped.Add(new SkippedPlugin(name, "It has no class marked [AkiraPlugin]."));
        }

        foreach (var type in plugins)
        {
            var key = type.GetCustomAttribute<AkiraPluginAttribute>()!.Key;
            if (Register(services, configuration, catalog, type, $"{SectionName}:{key}", name) is { } problem)
            {
                catalog.Skipped.Add(new SkippedPlugin(name, problem));
            }
        }
    }

    private static string? Register(IServiceCollection services, IConfiguration configuration, PluginCatalog catalog, Type type, string section,
        string? file)
    {
        var key = type.GetCustomAttribute<AkiraPluginAttribute>()?.Key;
        if (key is null || !KeyPattern().IsMatch(key) || key == "path")
        {
            return $"{type.FullName} needs [AkiraPlugin] with a key of lowercase letters, digits and dashes, at most 32.";
        }

        if (catalog.Plugins.Any(p => p.Key == key))
        {
            return $"The key {key} is taken.";
        }

        if (!typeof(IAkiraPlugin).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
        {
            return $"{type.FullName} must implement IAkiraPlugin and have a constructor without parameters.";
        }

        var plugin = (IAkiraPlugin)Activator.CreateInstance(type)!;
        var settings = new ConfigurationPluginSettings(configuration.GetSection(section), section);
        Validate(plugin.Manifest, settings);

        var before = services.Count;
        try
        {
            plugin.ConfigureServices(services, settings);
        }
        catch (Exception exception) when (file is not null)
        {
            // Half a registration is worse than none
            while (services.Count > before)
            {
                services.RemoveAt(services.Count - 1);
            }

            return $"Registering its services failed: {PluginCatalog.Reason(exception)}";
        }

        var provides = services
            .Skip(before)
            .Select(d => Kinds.GetValueOrDefault(d.ServiceType))
            .OfType<string>()
            .Distinct()
            .ToList();
        if (KeyBridge(services, before, key) is { } bridgeProblem)
        {
            while (services.Count > before)
            {
                services.RemoveAt(services.Count - 1);
            }

            return bridgeProblem;
        }

        if (file is not null)
        {
            GuardHostedServices(services, before, key);
        }

        var assembly = type.Assembly;
        var version = VersionOf(assembly);
        catalog.Plugins.Add(new LoadedPlugin(key, plugin.Manifest, version, file, settings, provides, assembly));
        return null;
    }

    private static string? VersionOf(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (informational is not null)
        {
            return informational.InformationalVersion.Split('+')[0];
        }

        return assembly.GetName().Version?.ToString();
    }

    // The host finds a bridge by its plugin's key, so the plugin registers it as a plain singleton and it is keyed here
    private static string? KeyBridge(IServiceCollection services, int from, string key)
    {
        var bridges = Enumerable.Range(from, services.Count - from)
            .Where(i => services[i].ServiceType == typeof(IClientBridge))
            .ToList();
        if (bridges.Count > 1)
        {
            return "A plugin can register one bridge.";
        }

        foreach (var index in bridges)
        {
            var descriptor = services[index];
            services[index] = descriptor switch
            {
                { ImplementationInstance: { } instance } => new ServiceDescriptor(typeof(IClientBridge), key, instance),
                { ImplementationFactory: { } factory } => new ServiceDescriptor(typeof(IClientBridge), key,
                    (provider, _) => factory(provider), descriptor.Lifetime),
                _ => new ServiceDescriptor(typeof(IClientBridge), key, descriptor.ImplementationType!, descriptor.Lifetime)
            };
        }

        return null;
    }

    // The host stops when a hosted service throws, at its start or later. A plugin's own are wrapped, so one that fails is
    // logged and the server runs on
    private static void GuardHostedServices(IServiceCollection services, int from, string key)
    {
        for (var index = from; index < services.Count; index++)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType != typeof(IHostedService) || descriptor.IsKeyedService)
            {
                continue;
            }

            services[index] = ServiceDescriptor.Singleton<IHostedService>(provider => new PluginHostedService(key,
                () => Create(provider, descriptor), provider.GetRequiredService<PluginCatalog>(),
                provider.GetRequiredService<ILogger<PluginHostedService>>()));
        }
    }

    private static IHostedService Create(IServiceProvider provider, ServiceDescriptor descriptor) => descriptor switch
    {
        { ImplementationInstance: IHostedService instance } => instance,
        { ImplementationFactory: { } factory } => (IHostedService)factory(provider),
        _ => (IHostedService)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)
    };

    // Like every option: a plugin set up halfway stops the server with the key named, one not set up at all is just not used
    private static void Validate(PluginManifest manifest, IPluginSettings settings)
    {
        var required = manifest.Settings.Where(s => s.Required).ToList();
        var missing = required.Where(s => settings.Get(s.Key) is null).ToList();
        if (missing.Count > 0 && (settings.Enabled || missing.Count < required.Count))
        {
            throw new PluginSettingsException($"{settings.Section}:{missing[0].Key} is required for {manifest.Name}.");
        }

        foreach (var setting in manifest.Settings)
        {
            if (settings.Get(setting.Key) is not { } value)
            {
                continue;
            }

            var valid = setting.Kind switch
            {
                PluginSettingKind.Url => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https",
                PluginSettingKind.Number => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
                PluginSettingKind.Toggle => bool.TryParse(value, out _),
                _ => true
            };
            if (!valid)
            {
                throw new PluginSettingsException($"{settings.Section}:{setting.Key} is not a valid {setting.Kind.ToString().ToLowerInvariant()}.");
            }
        }
    }

    [GeneratedRegex("^[a-z0-9-]{1,32}$")]
    private static partial Regex KeyPattern();
}
