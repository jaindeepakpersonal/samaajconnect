using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sangam.Gateway.Security;
using Xunit;

namespace Sangam.Gateway.Tests;

/// <summary>
/// The headers every gateway response carries, against terminal endpoints that
/// stand in for a proxied service: JSON, an HTML page with and without its own
/// policy, and an upstream announcing its software.
/// </summary>
public sealed class SecurityHeadersMiddlewareTests : IAsyncLifetime
{
    private IHost _host = null!;

    private bool _hsts;

    private async Task StartAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services => services.AddSingleton(new SecurityHeadersOptions { Hsts = _hsts }))
                .Configure(app =>
                {
                    app.UseMiddleware<SecurityHeadersMiddleware>();

                    app.Map("/api", api => api.Run(context =>
                    {
                        // What a service behind the proxy would send.
                        context.Response.Headers["Server"] = "Kestrel";
                        context.Response.Headers["X-Powered-By"] = "Express";
                        return context.Response.WriteAsJsonAsync(new { ok = true });
                    }));

                    app.Map("/page", page => page.Run(context =>
                    {
                        context.Response.ContentType = "text/html";
                        return context.Response.WriteAsync("<html></html>");
                    }));

                    app.Map("/own-policy", page => page.Run(context =>
                    {
                        context.Response.ContentType = "text/html";
                        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; the-portal's-own";
                        return context.Response.WriteAsync("<html></html>");
                    }));

                    app.Run(context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                        return Task.CompletedTask;
                    });
                }))
            .StartAsync();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _host.StopAsync();

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values)
        : response.Content.Headers.TryGetValues(name, out var content) ? string.Join(",", content)
        : string.Empty;

    [Fact]
    public async Task An_API_response_gets_every_header_a_locked_down_policy_and_no_server_names()
    {
        await StartAsync();

        var response = await _host.GetTestClient().GetAsync("/api");

        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
        Header(response, "X-Frame-Options").Should().Be("DENY");
        Header(response, "Referrer-Policy").Should().Be("strict-origin-when-cross-origin");
        Header(response, "Content-Security-Policy").Should().Be(SecurityHeadersMiddleware.ApiContentSecurityPolicy);
        Header(response, "Server").Should().BeEmpty();
        Header(response, "X-Powered-By").Should().BeEmpty();
        Header(response, "Strict-Transport-Security").Should().BeEmpty("HSTS is opt-in, for where TLS terminates");
    }

    [Fact]
    public async Task A_page_without_its_own_policy_gets_the_portals_and_one_with_keeps_its_own()
    {
        await StartAsync();
        var client = _host.GetTestClient();

        Header(await client.GetAsync("/page"), "Content-Security-Policy")
            .Should().Be(SecurityHeadersMiddleware.PageContentSecurityPolicy);
        Header(await client.GetAsync("/own-policy"), "Content-Security-Policy")
            .Should().Be("default-src 'self'; the-portal's-own");
    }

    [Fact]
    public async Task A_refusal_carries_them_too_and_HSTS_when_switched_on()
    {
        _hsts = true;
        await StartAsync();

        var response = await _host.GetTestClient().GetAsync("/anything-else");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        Header(response, "X-Frame-Options").Should().Be("DENY");
        Header(response, "Strict-Transport-Security").Should().StartWith("max-age=31536000");
    }
}
