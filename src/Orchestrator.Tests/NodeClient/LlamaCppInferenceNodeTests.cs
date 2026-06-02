using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodeClient.LlamaCpp;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Models;

namespace Orchestrator.Tests.NodeClient;

[TestFixture]
public sealed class LlamaCppInferenceNodeTests
{
    private static readonly LlamaCppClientOptions DefaultOpts = new()
    {
        NodeId         = "B",
        ModelLabel     = "qwen3-coder-30b-a3b",
        VramMb         = 8192,
        BaseUrl        = "http://192.168.1.20:8080",
        TimeoutSeconds = 180
    };

    private readonly ILlamaCppClient _client =
        Substitute.For<ILlamaCppClient>();
    private readonly ILogger<LlamaCppInferenceNode> _logger =
        Substitute.For<ILogger<LlamaCppInferenceNode>>();
    private readonly LlamaCppInferenceNode _sut;

    public LlamaCppInferenceNodeTests()
    {
        _sut = new LlamaCppInferenceNode(_client, Options.Create(DefaultOpts), _logger);
    }

    // ── Identity & Capabilities ───────────────────────────────────────────

    [Test]
    public void NodeId_MatchesOptions()
        => _sut.NodeId.Should().Be("B");

    [Test]
    public void Provider_IsLlamaCpp()
        => _sut.Provider.Should().Be(NodeProviderType.LlamaCpp);

    [Test]
    public void Capabilities_ReflectOptions()
    {
        _sut.Capabilities.NodeId.Should().Be("B");
        _sut.Capabilities.Model.Should().Be("qwen3-coder-30b-a3b");
        _sut.Capabilities.VramMb.Should().Be(8192);
        _sut.Capabilities.SupportsStreaming.Should().BeTrue();
    }

    [Test]
    public void Capabilities_VramMb_IsConfigurable()
    {
        var customOpts = Options.Create(new LlamaCppClientOptions { NodeId = "C", VramMb = 6144 });
        var sut = new LlamaCppInferenceNode(_client, customOpts, _logger);
        sut.Capabilities.VramMb.Should().Be(6144);
    }

    [Test]
    public void InitialHealth_IsUnavailable()
        => _sut.Health.State.Should().Be(HealthState.Unavailable);

    // ── ExecuteAsync ──────────────────────────────────────────────────────

    [Test]
    public async Task ExecuteAsync_ReturnsResult_WithCorrectMetadata()
    {
        const string text = "the answer";
        var request = new InferenceRequest { Prompt = "the question", Model = "test" };
        _client.ExecuteAsync(request, Arg.Any<CancellationToken>()).Returns(text);

        var result = await _sut.ExecuteAsync(request);

        result.Text.Should().Be(text);
        result.NodeId.Should().Be("B");
        result.Model.Should().Be("qwen3-coder-30b-a3b");
        result.LatencyMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_PassesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var request = new InferenceRequest { Prompt = "test" };
        _client.ExecuteAsync(Arg.Any<InferenceRequest>(), cts.Token).Returns("ok");

        await _sut.ExecuteAsync(request, cts.Token);

        await _client.Received(1).ExecuteAsync(Arg.Any<InferenceRequest>(), cts.Token);
    }

    // ── StreamAsync ───────────────────────────────────────────────────────

    [Test]
    public async Task StreamAsync_YieldsIntermediateDeltasThenFinalChunk()
    {
        var request = new InferenceRequest { Prompt = "stream" };
        _client.StreamChunksAsync(request, Arg.Any<CancellationToken>())
               .Returns(YieldAll("tok1", " tok2", " tok3"));

        var chunks = new List<InferenceChunk>();
        await foreach (var chunk in _sut.StreamAsync(request))
            chunks.Add(chunk);

        var intermediate = chunks.Where(c => !c.IsFinal).ToList();
        intermediate.Should().HaveCount(3);
        intermediate.Select(c => c.Content).Should().Equal("tok1", " tok2", " tok3");

        var final = chunks.Single(c => c.IsFinal);
        final.Content.Should().BeEmpty();
        final.FinalResult.Should().NotBeNull();
        final.FinalResult!.Text.Should().Be("tok1 tok2 tok3");
        final.FinalResult.NodeId.Should().Be("B");
        final.FinalResult.Model.Should().Be("qwen3-coder-30b-a3b");
    }

    [Test]
    public async Task StreamAsync_FinalChunk_AccumulatesAllDeltas()
    {
        var request = new InferenceRequest { Prompt = "acc" };
        _client.StreamChunksAsync(request, Arg.Any<CancellationToken>())
               .Returns(YieldAll("A", "B", "C"));

        var chunks = new List<InferenceChunk>();
        await foreach (var chunk in _sut.StreamAsync(request))
            chunks.Add(chunk);

        chunks.Last().FinalResult!.Text.Should().Be("ABC");
    }

    [Test]
    public async Task StreamAsync_EmptyStream_YieldsJustFinalChunk()
    {
        var request = new InferenceRequest { Prompt = "empty" };
        _client.StreamChunksAsync(request, Arg.Any<CancellationToken>())
               .Returns(YieldAll());

        var chunks = new List<InferenceChunk>();
        await foreach (var chunk in _sut.StreamAsync(request))
            chunks.Add(chunk);

        chunks.Should().HaveCount(1);
        chunks[0].IsFinal.Should().BeTrue();
        chunks[0].FinalResult!.Text.Should().BeEmpty();
    }

    // ── GetHealthAsync ────────────────────────────────────────────────────

    [Test]
    public async Task GetHealthAsync_ReturnsHealthy_WhenStatusIsOk()
    {
        _client.GetServerStatusAsync(Arg.Any<CancellationToken>()).Returns("ok");
        _client.ListModelsAsync(Arg.Any<CancellationToken>())
               .Returns(new List<ModelInfo> { new() { ModelId = "qwen3-coder-30b-a3b" } });

        var health = await _sut.GetHealthAsync();

        health.State.Should().Be(HealthState.Healthy);
        health.AvailableModels.Should().Contain("qwen3-coder-30b-a3b");
        health.LatencyMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task GetHealthAsync_ReturnsDegraded_WhenStatusIsLoadingModel()
    {
        _client.GetServerStatusAsync(Arg.Any<CancellationToken>()).Returns("loading model");

        var health = await _sut.GetHealthAsync();

        health.State.Should().Be(HealthState.Degraded);
        health.AvailableModels.Should().BeEmpty();
    }

    [Test]
    public async Task GetHealthAsync_ReturnsUnavailable_WhenStatusIsError()
    {
        _client.GetServerStatusAsync(Arg.Any<CancellationToken>()).Returns("error");

        var health = await _sut.GetHealthAsync();

        health.State.Should().Be(HealthState.Unavailable);
    }

    [Test]
    public async Task GetHealthAsync_ReturnsUnavailable_WhenStatusIsNull()
    {
        _client.GetServerStatusAsync(Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<string?>(null));

        var health = await _sut.GetHealthAsync();

        health.State.Should().Be(HealthState.Unavailable);
        health.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task GetHealthAsync_ReturnsUnavailable_WhenClientThrows()
    {
        _client.GetServerStatusAsync(Arg.Any<CancellationToken>())
               .Returns(Task.FromException<string?>(
                   new HttpRequestException("connection refused")));

        var health = await _sut.GetHealthAsync();

        health.State.Should().Be(HealthState.Unavailable);
        health.ErrorMessage.Should().Contain("refused");
    }

    [Test]
    public async Task GetHealthAsync_UpdatesCachedHealthProperty()
    {
        _client.GetServerStatusAsync(Arg.Any<CancellationToken>()).Returns("ok");
        _client.ListModelsAsync(Arg.Any<CancellationToken>())
               .Returns(Array.Empty<ModelInfo>());

        var result = await _sut.GetHealthAsync();

        _sut.Health.State.Should().Be(result.State);
        _sut.Health.LastChecked.Should().Be(result.LastChecked);
    }

    [Test]
    public async Task GetHealthAsync_DoesNotCallListModels_WhenNotOk()
    {
        _client.GetServerStatusAsync(Arg.Any<CancellationToken>()).Returns("loading model");

        await _sut.GetHealthAsync();

        await _client.DidNotReceive().ListModelsAsync(Arg.Any<CancellationToken>());
    }

    // ── ListModelsAsync ───────────────────────────────────────────────────

    [Test]
    public async Task ListModelsAsync_DelegatesToClient()
    {
        var models = new List<ModelInfo> { new() { ModelId = "qwen3-coder-30b-a3b" } };
        _client.ListModelsAsync(Arg.Any<CancellationToken>()).Returns(models);

        var result = await _sut.ListModelsAsync();

        result.Should().HaveCount(1);
        result[0].ModelId.Should().Be("qwen3-coder-30b-a3b");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<string> YieldAll(params string[] items)
    {
        foreach (var item in items)
            yield return item;
        await Task.CompletedTask;
    }
}
