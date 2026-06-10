using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AntennaMonitoring.Tests;

public abstract class TestBase
{
    protected Mock<ILogger<T>> CreateMockLogger<T>()
    {
        return new Mock<ILogger<T>>();
    }

    protected IOptions<TOptions> CreateOptions<TOptions>(TOptions value)
        where TOptions : class
    {
        return Options.Create(value);
    }

    protected void VerifyLog<T>(Mock<ILogger<T>> mockLogger, LogLevel level, string containsMessage, Times times)
    {
        mockLogger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString() != null && v.ToString().Contains(containsMessage)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            times);
    }
}
