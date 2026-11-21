using System.Net;
using Microsoft.AspNetCore.Hosting;
using Akira.Server.Extensions;
using Akira.Server.Testing;
using Xunit;

namespace Akira.Server.Tests;

public class WebUiTests
{
    [Fact]
    public async Task EveryPage_IsTheWebUi_ButAnUnknownApiPathStaysAnApiProblem()
    {
        var webRoot = Directory.CreateTempSubdirectory("akira_webroot_").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html><title>Akira</title>", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(webRoot, "app.js"), "console.log(1)", TestContext.Current.CancellationToken);
            await using var factory = new AkiraApiFactory { Settings = { [WebHostDefaults.WebRootKey] = webRoot } };
            using var anonymous = factory.CreateAnonymousClient();

            var root = await anonymous.GetAsync("/", TestContext.Current.CancellationToken);
            var page = await anonymous.GetAsync("/users/42", TestContext.Current.CancellationToken);
            var asset = await anonymous.GetAsync("/app.js", TestContext.Current.CancellationToken);
            var api = await anonymous.GetAsync("/api/v1/nothing-here", TestContext.Current.CancellationToken);
            var openApi = await anonymous.GetAsync("/openapi/nothing.json", TestContext.Current.CancellationToken);

            Assert.Equal((HttpStatusCode.OK, "text/html"), (root.StatusCode, root.Content.Headers.ContentType?.MediaType));
            Assert.Equal((HttpStatusCode.OK, "text/html"), (page.StatusCode, page.Content.Headers.ContentType?.MediaType));
            Assert.Equal((HttpStatusCode.OK, "text/javascript"), (asset.StatusCode, asset.Content.Headers.ContentType?.MediaType));

            // A page from before an upgrade names files that are gone, so it is never taken from a cache
            Assert.Equal("no-cache", page.Headers.CacheControl?.ToString());
            Assert.Equal(ApplicationBuilderExtensions.WebUiContentSecurityPolicy, root.Headers.GetValues("Content-Security-Policy").Single());
            Assert.Equal(ApplicationBuilderExtensions.WebUiContentSecurityPolicy, page.Headers.GetValues("Content-Security-Policy").Single());
            Assert.False(api.Headers.Contains("Content-Security-Policy"));
            Assert.Equal("nosniff", api.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
            Assert.Equal("application/problem+json", api.Content.Headers.ContentType?.MediaType);
            Assert.NotEqual("text/html", openApi.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }
}
