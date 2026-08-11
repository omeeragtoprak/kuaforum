using Kuaforum.Api.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// Koyeb free-tier test deploy hazırlığı (2026-08): DevTools:Enabled/AccessKey gating
/// mantığının pure-unit seviyesinde doğrulanması — gerçek HTTP/DB gerekmez.
/// </summary>
public sealed class DevToolsGateTests
{
    private static IConfiguration BuildConfig(params (string Key, string? Value)[] entries)
        => new ConfigurationBuilder().AddInMemoryCollection(
            entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value))).Build();

    [Fact]
    public void IsEnabled_true_in_development_regardless_of_flag()
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(Environments.Development);
        var config = BuildConfig();

        Assert.True(DevToolsGate.IsEnabled(env, config));
    }

    [Fact]
    public void IsEnabled_false_in_production_when_flag_unset()
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(Environments.Production);
        var config = BuildConfig();

        Assert.False(DevToolsGate.IsEnabled(env, config));
    }

    [Fact]
    public void IsEnabled_true_in_production_when_flag_set()
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(Environments.Production);
        var config = BuildConfig(("DevTools:Enabled", "true"));

        Assert.True(DevToolsGate.IsEnabled(env, config));
    }

    [Fact]
    public void IsEnabledOutsideDevelopment_false_when_environment_is_development_even_if_flag_set()
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(Environments.Development);
        var config = BuildConfig(("DevTools:Enabled", "true"));

        Assert.False(DevToolsGate.IsEnabledOutsideDevelopment(env, config));
    }

    [Fact]
    public void IsEnabledOutsideDevelopment_true_when_production_and_flag_set()
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(Environments.Production);
        var config = BuildConfig(("DevTools:Enabled", "true"));

        Assert.True(DevToolsGate.IsEnabledOutsideDevelopment(env, config));
    }

    [Fact]
    public void CheckAccessKey_no_op_when_access_key_not_configured()
    {
        var config = BuildConfig();
        var ctx = new DefaultHttpContext();

        Assert.Null(DevToolsGate.CheckAccessKey(ctx, config));
    }

    [Fact]
    public void CheckAccessKey_rejects_missing_header_when_access_key_configured()
    {
        var config = BuildConfig(("DevTools:AccessKey", "s3cr3t"));
        var ctx = new DefaultHttpContext();

        var result = DevToolsGate.CheckAccessKey(ctx, config);
        Assert.NotNull(result);
    }

    [Fact]
    public void CheckAccessKey_rejects_wrong_header_value()
    {
        var config = BuildConfig(("DevTools:AccessKey", "s3cr3t"));
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Dev-Access-Key"] = "wrong";

        var result = DevToolsGate.CheckAccessKey(ctx, config);
        Assert.NotNull(result);
    }

    [Fact]
    public void CheckAccessKey_accepts_matching_header_value()
    {
        var config = BuildConfig(("DevTools:AccessKey", "s3cr3t"));
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Dev-Access-Key"] = "s3cr3t";

        var result = DevToolsGate.CheckAccessKey(ctx, config);
        Assert.Null(result);
    }
}
