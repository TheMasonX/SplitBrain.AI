using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodeClient.LlamaCpp;
using Orchestrator.Core.Models;

namespace Orchestrator.Tests.NodeClient;

/// <summary>
/// Unit tests for LlamaCppClient SSE parsing, health probing, and non-streaming execution.
/// Uses a FakeHttpMessageHandler to inject crafted HTTP responses without a real server.
/// Tests verify: normal SSE completion, [DONE] sentinel, finish_reason exit, mid-stream
/// truncation, event: line handling, blank line skipping, malformed JSON tolerance,
/// health state parsing, and model listing.
/// </summary>
[TestFixture]
public sealed class LlamaCppClientSseTests
{
    // ── Factory helpers ───────────────────────────────────────────────────

    private static LlamaCppClient MakeClient(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHttpMessageHandler(respond);
        var http    = new HttpClient(handler);
        var opts    = Options.Create(new LlamaCppClientOptions { BaseUrl = "http://test:8080" });
        var logger  = Substitute.For<ILogger<LlamaCppClient>>();
        return new LlamaCppClient(http, opts, logger);
    }

    private static HttpResponseMessage MakeSseResponse(string body,
        HttpStatusCode status = HttpStatusCode.OK)
    {
        var content = new StringContent(body, Encoding.UTF8, "text/event-stream");
        return new HttpResponseMessage(status) { Content = content };
    }

    private static HttpResponseMessage MakeJsonResponse(string body,
        HttpStatusCode status = HttpStatusCode.OK)
    {
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        return new HttpResponseMessage(status) { Content = content };
    }

    private static InferenceRequest MakeRequest() =>
        new() { Prompt = "hello", Model = "test", Stream = true };

    // ── StreamChunksAsync — normal paths ──────────────────────────────────

    [Test]
    public async Task StreamChunksAsync_YieldsDeltas_OnNormalCompletion()
    {
        var sseBody =
            "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\",\"content\":\"Hello\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\" world\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"!\"},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n\n";

        var client = MakeClient(_ => MakeSseResponse(sseBody));
        var deltas = new List<string>();
        await foreach (var d in client.StreamChunksAsync(MakeRequest()))
            deltas.Add(d);

        deltas.Should().Equal("Hello", " world", "!");
    }

    [Test]
    public async Task StreamChunksAsync_Stops_OnDoneSentinel_WithoutFinishReason()
    {
        var sseBody =
            "data: {\"choices\":[{\"delta\":{\"content\":\"A\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"B\"}}]}\n\n" +
            "data: [DONE]\n\n";

        var client = MakeClient(_ => MakeSseResponse(sseBody));
        var deltas = new List<string>();
        await foreach (var d in client.StreamChunksAsync(MakeRequest()))
            deltas.Add(d);

        deltas.Should().Equal("A", "B");
    }

    [Test]
    public async Task StreamChunksAsync_Stops_OnFinishReason_BeforeDoneSentinel()
    {
        // Some server variants emit finish_reason without a subsequent [DONE]
        var sseBody =
            "data: {\"choices\":[{\"delta\":{\"content\":\"X\"},\"finish_reason\":\"length\"}]}\n\n";

        var client = MakeClient(_ => MakeSseResponse(sseBody));
        var deltas = new List<string>();
        await foreach (var d in client.StreamChunksAsync(MakeRequest()))
            deltas.Add(d);

        deltas.Should().Equal("X");
    }

    [Test]
    public async Task StreamChunksAsync_YieldsEmptyCollection_WhenOnlyDoneSent()
    {
        var sseBody = "data: [DONE]\n\n";

        var client = MakeClient(_ => MakeSseResponse(sseBody));
        var deltas = new List<string>();
        await foreach (var d in client.StreamChunksAsync(MakeRequest()))
            deltas.Add(d);

        deltas.Should().BeEmpty();
    }

    // ── StreamChunksAsync — truncation & resilience ───────────────────────

    [Test]
    public async Task StreamChunksAsync_DeliverPartialContent_OnPrematureStreamClose()
    {
        // Stream ends without [DONE] — simulates server crash mid-response
        var sseBody =
            "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n";
        // No [DONE] — stream simply ends here

        var client = MakeClient(_ => MakeSseResponse(sseBody));
        var deltas = new List<string>();
        await foreach (var d in client.StreamChunksAsync(MakeRequest()))
            deltas.Add(d);

        // Partial content is still delivered (caller gets what was received)
        deltas.Should().Equal("partial");
    }

    [Test]
    public async Task StreamChunksAsync_ContinuesAfterMalformedJson()
    {
        // Malformed chunk should be skipped; valid chunks still delivered
        var sseBody =
            "data: {invalid json here}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"good\"}}]}\n\n" +
            "data: [DONE]\n\n";

        var client = MakeClient(_ => MakeSseResponse(sseBody));
        var deltas = new List<string>();
        await foreach (var d in client.StreamChunksAsync(MakeRequest()))
            deltas.Add(d);

        deltas.Should().Equal("good");
    }

    // ── StreamChunksAsync — line parsing ─────────────────────────────────

    [Test]
    public async Task StreamChunksAsync_SkipsEventLines_ProcessesDataLines()
    {
        // event: lines are SSE metadata; only data: lines carry content
        var sseBody =
            "event: ping\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"pong\"}}]}\n\n" +
            "event: done\n" +
            "data: [DONE]\n\n";

        var client = MakeClient(_ => MakeSseResponse(sseBody));
        var deltas = new List<string>();
        await foreach (var d in client.StreamChunksAsync(MakeRequest()))
            deltas.Add(d);

        deltas.Should().Equal("pong");
    }

    [Test]
    public async Task StreamChunksAsync_SkipsBlankLines()
    {
        var sseBody =
            "\n" +
            "  \n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\n" +
            "data: [DONE]\n\n";

        var client = MakeClient(_ => MakeSseResponse(sseBody));
        var deltas = new List<string>();
        await foreach (var d in client.StreamChunksAsync(MakeRequest()))
            deltas.Add(d);

        deltas.Should().Equal("ok");
    }

    [Test]
    public async Task StreamChunksAsync_SkipsChunksWithNullContent()
    {
        // Final chunk often carries finish_reason with empty/absent content
        var sseBody =
            "data: {\"choices\":[{\"delta\":{\"content\":\"text\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n\n";

        var client = MakeClient(_ => MakeSseResponse(sseBody));
        var deltas = new List<string>();
        await foreach (var d in client.StreamChunksAsync(MakeRequest()))
            deltas.Add(d);

        // The empty-delta chunk should not yield an empty string
        deltas.Should().Equal("text");
    }

    [Test]
    public async Task StreamChunksAsync_TrimsDataLineWhitespace()
    {
        var sseBody = "data:   {\"choices\":[{\"delta\":{\"content\":\"trimmed\"}}]}   \n\ndata: [DONE]\n\n";

        var client = MakeClient(_ => MakeSseResponse(sseBody));
        var deltas = new List<string>();
        await foreach (var d in client.StreamChunksAsync(MakeRequest()))
            deltas.Add(d);

        deltas.Should().Equal("trimmed");
    }

    // ── ExecuteAsync (non-streaming) ──────────────────────────────────────

    [Test]
    public async Task ExecuteAsync_ReturnsMessageContent_OnSuccess()
    {
        var body = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"answer\"}}]}";
        var client = MakeClient(_ => MakeJsonResponse(body));
        var request = new InferenceRequest { Prompt = "question", Model = "test", Stream = false };

        var result = await client.ExecuteAsync(request);

        result.Should().Be("answer");
    }

    [Test]
    public async Task ExecuteAsync_ReturnsEmpty_WhenChoicesIsNull()
    {
        var client = MakeClient(_ => MakeJsonResponse("{\"choices\":null}"));

        var result = await client.ExecuteAsync(MakeRequest());

        result.Should().BeEmpty();
    }

    [Test]
    public void ExecuteAsync_Throws_OnHttpError()
    {
        var client = MakeClient(_ =>
            MakeJsonResponse("{}", HttpStatusCode.ServiceUnavailable));

        Func<Task> act = () => client.ExecuteAsync(MakeRequest());

        act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── IsHealthyAsync / GetServerStatusAsync ──────────────────────────────

    [Test]
    public async Task IsHealthyAsync_ReturnsTrue_WhenStatusIsOk()
    {
        var client = MakeClient(_ => MakeJsonResponse("{\"status\":\"ok\"}"));
        (await client.IsHealthyAsync()).Should().BeTrue();
    }

    [Test]
    public async Task IsHealthyAsync_ReturnsFalse_WhenStatusIsLoadingModel()
    {
        var client = MakeClient(_ => MakeJsonResponse("{\"status\":\"loading model\"}"));
        (await client.IsHealthyAsync()).Should().BeFalse();
    }

    [Test]
    public async Task IsHealthyAsync_ReturnsFalse_WhenStatusIsError()
    {
        var client = MakeClient(_ => MakeJsonResponse("{\"status\":\"error\"}"));
        (await client.IsHealthyAsync()).Should().BeFalse();
    }

    [Test]
    public async Task IsHealthyAsync_ReturnsFalse_OnNetworkFailure()
    {
        var client = MakeClient(_ => throw new HttpRequestException("refused"));
        (await client.IsHealthyAsync()).Should().BeFalse();
    }

    [Test]
    public async Task GetServerStatusAsync_ReturnsStatusString()
    {
        var client = MakeClient(_ => MakeJsonResponse("{\"status\":\"loading model\"}"));
        (await client.GetServerStatusAsync()).Should().Be("loading model");
    }

    [Test]
    public async Task GetServerStatusAsync_ReturnsNull_OnNetworkFailure()
    {
        var client = MakeClient(_ => throw new HttpRequestException("refused"));
        (await client.GetServerStatusAsync()).Should().BeNull();
    }

    [Test]
    public async Task GetServerStatusAsync_ReturnsNull_OnNonSuccessStatus()
    {
        var client = MakeClient(_ =>
            MakeJsonResponse("{}", HttpStatusCode.InternalServerError));
        (await client.GetServerStatusAsync()).Should().BeNull();
    }

    // ── ListModelsAsync ───────────────────────────────────────────────────

    [Test]
    public async Task ListModelsAsync_ReturnsModels_FromOaiFormat()
    {
        var body = "{\"object\":\"list\",\"data\":[{\"id\":\"qwen3-coder-30b-a3b\",\"object\":\"model\"}]}";
        var client = MakeClient(_ => MakeJsonResponse(body));

        var models = await client.ListModelsAsync();

        models.Should().HaveCount(1);
        models[0].ModelId.Should().Be("qwen3-coder-30b-a3b");
    }

    [Test]
    public async Task ListModelsAsync_ReturnsEmpty_OnNetworkFailure()
    {
        var client = MakeClient(_ => throw new HttpRequestException("refused"));
        (await client.ListModelsAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task ListModelsAsync_ReturnsEmpty_OnNonSuccessStatus()
    {
        var client = MakeClient(_ => MakeJsonResponse("{}", HttpStatusCode.NotFound));
        (await client.ListModelsAsync()).Should().BeEmpty();
    }

    // ── FakeHttpMessageHandler ─────────────────────────────────────────────

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond(request));
    }
}
