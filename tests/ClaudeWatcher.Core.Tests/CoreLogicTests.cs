using ClaudeWatcher.Core;
using Xunit;

namespace ClaudeWatcher.Core.Tests;

public class ClassifierTests
{
    [Theory]
    [InlineData("waiting", AgentState.Waiting)]
    [InlineData("busy", AgentState.Working)]
    [InlineData("idle", AgentState.Idle)]
    [InlineData("shell", AgentState.Idle)]
    [InlineData(null, AgentState.Idle)]
    public void Classify_maps_status_to_state(string? status, AgentState expected) =>
        Assert.Equal(expected, StatusClassifier.Classify(new Session { Status = status }));

    [Fact]
    public void Dominant_is_most_urgent_present_state()
    {
        Assert.Equal(AgentState.Waiting, new StatusCounts(1, 2, 3).Dominant);
        Assert.Equal(AgentState.Working, new StatusCounts(0, 2, 3).Dominant);
        Assert.Equal(AgentState.Idle, new StatusCounts(0, 0, 3).Dominant);
        Assert.Null(new StatusCounts(0, 0, 0).Dominant);
    }

    [Fact]
    public void Summary_reads_in_urgency_order()
    {
        Assert.Equal("No running agents", SummaryText.For(new StatusCounts(0, 0, 0)));
        Assert.Equal("1 needs you · 2 working · 3 idle", SummaryText.For(new StatusCounts(1, 2, 3)));
        Assert.Equal("2 working", SummaryText.For(new StatusCounts(0, 2, 0)));
    }

    [Fact]
    public void WaitingReason_phrases_permission_prompt_neutrally() =>
        Assert.Equal("awaiting your response", StatusClassifier.WaitingReason("permission prompt"));
}

public class ContextWindowTests
{
    [Theory]
    [InlineData("claude-opus-4-8", "Opus 4.8")]
    [InlineData("claude-haiku-4-5-20251001", "Haiku 4.5")]
    [InlineData("claude-opus-4-8[1m]", "Opus 4.8")]
    [InlineData("claude-sonnet-5", "Sonnet 5")]
    public void HumanModel_formats_api_ids(string raw, string expected) =>
        Assert.Equal(expected, ContextWindow.HumanModel(raw));

    [Theory]
    [InlineData(142_000, "142K")]
    [InlineData(1_000_000, "1M")]
    [InlineData(1_500_000, "1.5M")]
    [InlineData(950, "950")]
    public void FormatTokens_is_compact(int n, string expected) =>
        Assert.Equal(expected, ContextWindow.FormatTokens(n));

    [Fact]
    public void Opus4_gets_a_million_token_window() =>
        Assert.Equal(1_000_000, ContextWindow.For(observedTokens: 50_000, model: "claude-opus-4-8"));

    [Fact]
    public void Unknown_model_defaults_to_200k_until_usage_proves_larger()
    {
        Assert.Equal(200_000, ContextWindow.For(observedTokens: 50_000, model: "claude-sonnet-5"));
        Assert.Equal(1_000_000, ContextWindow.For(observedTokens: 250_000, model: "claude-sonnet-5"));
    }
}
