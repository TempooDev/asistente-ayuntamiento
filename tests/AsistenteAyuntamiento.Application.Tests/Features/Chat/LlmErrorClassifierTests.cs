using System.Net;
using AsistenteAyuntamiento.Application.Features.Chat;
using FluentAssertions;
using Microsoft.SemanticKernel;
using Xunit;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat;

public class LlmErrorClassifierTests
{
    [Fact]
    public void Http_429_from_semantic_kernel_is_quota_exceeded() =>
        LlmErrorClassifier.IsQuotaExceeded(new HttpOperationException(HttpStatusCode.TooManyRequests, null, "x", null)).Should().BeTrue();

    [Fact]
    public void Nested_http_429_is_quota_exceeded() =>
        LlmErrorClassifier.IsQuotaExceeded(new InvalidOperationException("wrapper",
            new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests))).Should().BeTrue();

    [Theory]
    [InlineData("context length limit exceeded")]
    [InlineData("insufficient permissions")]
    [InlineData("quota of something unrelated")]
    public void Message_text_alone_is_never_quota_exceeded(string message) =>
        LlmErrorClassifier.IsQuotaExceeded(new InvalidOperationException(message)).Should().BeFalse();

    [Fact]
    public void Other_http_errors_are_not_quota_exceeded() =>
        LlmErrorClassifier.IsQuotaExceeded(new HttpOperationException(HttpStatusCode.ServiceUnavailable, null, "x", null)).Should().BeFalse();

    [Fact]
    public void User_messages_never_contain_provider_details() =>
        LlmErrorCodes.UserMessage(LlmErrorCodes.Unavailable).Should().StartWith($"[{LlmErrorCodes.Unavailable}]");
}
