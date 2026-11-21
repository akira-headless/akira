using System.Reflection;
using Akira.Contracts.Bridges;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Akira.Server.Bridges;

namespace Akira.Server.Controllers;

/// <summary>
/// Limits the controllers of a bridge to /bridges/{key} while the bridge is switched on, anonymous to the host and left
/// out of the OpenAPI document. A plugin has no other controllers.
/// </summary>
public sealed class BridgeControllerConvention : IApplicationModelConvention
{
    private readonly IReadOnlyList<ActiveBridge> _bridges;
    private readonly IReadOnlySet<Assembly> _plugins;

    public BridgeControllerConvention(
        IReadOnlyList<ActiveBridge> bridges,
        IReadOnlySet<Assembly> plugins)
    {
        _bridges = bridges;
        _plugins = plugins;
    }

    /// <inheritdoc />
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers.ToList())
        {
            var type = controller.ControllerType;
            if (BridgeOf(type) is not { } bridgeType)
            {
                // A plugin may not add routes to the API, also not one whose bridge could not be made
                if (_plugins.Contains(type.Assembly))
                {
                    application.Controllers.Remove(controller);
                }

                continue;
            }

            // Only for a switched on bridge, and only for the plugin's own, so one plugin cannot answer under another's
            // path
            if (_bridges.FirstOrDefault(bridge => bridge.Bridge == bridgeType && bridge.Assembly == type.Assembly) is not { } owner)
            {
                application.Controllers.Remove(controller);
                continue;
            }

            LockIn(controller, owner.Key);
        }
    }

    private static Type? BridgeOf(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(BridgeController<>))
            {
                return current.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static void LockIn(ControllerModel controller, string key)
    {
        var prefix = new AttributeRouteModel { Template = $"{BridgeService.BasePath.TrimStart('/')}/{key}" };
        if (controller.Selectors.Count == 0)
        {
            controller.Selectors.Add(new SelectorModel());
        }

        foreach (var selector in controller.Selectors)
        {
            selector.AttributeRouteModel = AttributeRouteModel.CombineAttributeRouteModel(prefix, Relative(selector.AttributeRouteModel));
        }

        controller.ApiExplorer.IsVisible = false;
        controller.Filters.Add(new OwnBodyFilter());
        foreach (var action in controller.Actions)
        {
            action.ApiExplorer.IsVisible = false;
            foreach (var selector in action.Selectors)
            {
                selector.AttributeRouteModel = Relative(selector.AttributeRouteModel);
                selector.EndpointMetadata.Add(new AllowAnonymousAttribute());
            }
        }
    }

    // MVC skips the prefix for a template that starts with / or ~/, so a plugin could answer outside /bridges/{key}.
    // Make it relative, so it is combined like any other.
    private static AttributeRouteModel? Relative(AttributeRouteModel? route)
    {
        if (route is not { IsAbsoluteTemplate: true, Template: { } template })
        {
            return route;
        }

        return new AttributeRouteModel(route) { Template = template.TrimStart('~').TrimStart('/') };
    }

    // Bridges read their bodies themselves, as JSON or SOAP, so MVC never reads a form before they do
    private sealed class OwnBodyFilter : IResourceFilter
    {
        /// <inheritdoc />
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            context.ValueProviderFactories.RemoveType<FormValueProviderFactory>();
            context.ValueProviderFactories.RemoveType<FormFileValueProviderFactory>();
            context.ValueProviderFactories.RemoveType<JQueryFormValueProviderFactory>();
        }

        /// <inheritdoc />
        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }
}
