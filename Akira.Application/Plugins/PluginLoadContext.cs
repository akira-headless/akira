using System.Reflection;
using System.Runtime.Loader;

namespace Akira.Server.Plugins;

// The contract and everything the server already has come from the server, so their types are shared; the plugin's own
// dependencies come from its folder
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string path)
        : base(Path.GetFileNameWithoutExtension(path))
    {
        _resolver = new AssemblyDependencyResolver(path);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        try
        {
            return Default.LoadFromAssemblyName(assemblyName);
        }
        catch (FileNotFoundException)
        {
            var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
            if (resolved is null)
            {
                return null;
            }

            return LoadFromAssemblyPath(resolved);
        }
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var resolved = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (resolved is null)
        {
            return IntPtr.Zero;
        }

        return LoadUnmanagedDllFromPath(resolved);
    }
}
