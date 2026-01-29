using Microsoft.Extensions.Logging;
using Moq;

namespace SmbSharp.Tests.Util
{
    public static class LogHelper
    {
        public static void VerifyLog<T>(this Mock<ILogger<T>> loggerMock, LogLevel level, string message = "",
            Func<Times>? times = null)
        {
            times ??= Times.Once;

            loggerMock.Verify(logger => logger.Log(
                    It.Is<LogLevel>(logLevel => logLevel == level),
                    It.Is<EventId>(eventId => eventId.Id == 0),
                    It.Is<It.IsAnyType>((@object, @type) =>
                        @object.ToString()!
                            .Contains(message)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
                times);
        }

        public static void VerifyLogException<T>(this Mock<ILogger<T>> loggerMock, LogLevel level,
            string exceptionMessage = "",
            Func<Times>? times = null)
        {
            times ??= Times.Once;

            loggerMock.Verify(logger => logger.Log(
                    It.Is<LogLevel>(logLevel => logLevel == level),
                    It.Is<EventId>(eventId => eventId.Id == 0),
                    It.Is<It.IsAnyType>((@object, @type) => true),
                    It.Is<Exception>((@object, @type) => ((Exception)@object).Message.Contains(exceptionMessage)),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
                times);
        }
    }
}