using MailSweep.Api;
using Microsoft.Extensions.Configuration;

namespace MailSweep.Api.Tests;

public sealed class FrontendOriginTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost:5173")]
    [InlineData("https://localhost:5173/path")]
    [InlineData("https://localhost:5173?next=elsewhere")]
    [InlineData("https://user@localhost:5173")]
    [InlineData("https://localhost:5173/")]
    public void RejectsInvalidFrontendOrigins(string? origin)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Frontend:Origin"] = origin }).Build();

        Assert.Throws<InvalidOperationException>(() => FrontendOrigin.Read(configuration));
    }

    [Fact]
    public void AcceptsHttpsOrigin()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Frontend:Origin"] = "https://localhost:5173" }).Build();

        Assert.Equal("https://localhost:5173", FrontendOrigin.Read(configuration));
    }
}
