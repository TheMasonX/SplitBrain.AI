using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Infrastructure.Logging;

namespace Orchestrator.Tests.Logging;

public sealed class FileLoggingServiceTests : IDisposable
{
    private readonly ILogger<FileLoggingService> _logger = Substitute.For<ILogger<FileLoggingService>>();
    private readonly FileLoggingOptions _options;
    private readonly FileLoggingService _sut;

    public FileLoggingServiceTests()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-logs-{Guid.NewGuid()}");
        _options = new FileLoggingOptions { Enabled = true, LogDirectory = tempDir };
        var optionsWrapper = Substitute.For<IOptions<FileLoggingOptions>>();
        optionsWrapper.Value.Returns(_options);
        _sut = new FileLoggingService(_logger, optionsWrapper);
    }

    public void Dispose()
    {
        _sut.Dispose();
        if (Directory.Exists(_options.LogDirectory))
        {
            Directory.Delete(_options.LogDirectory, recursive: true);
        }
    }

    [Test]
    public async Task LogInferenceAsync_WhenDisabled_ReturnsEarly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"disabled-test-{Guid.NewGuid()}");
        var disabledOptions = new FileLoggingOptions { Enabled = false, LogDirectory = tempDir };
        var optionsWrapper = Substitute.For<IOptions<FileLoggingOptions>>();
        optionsWrapper.Value.Returns(disabledOptions);
        var service = new FileLoggingService(_logger, optionsWrapper);

        await service.LogInferenceAsync("task1", "prompt", "response", "gpt-4o", "nodeA", 100);

        // When disabled, no directory or files should be created
        Directory.Exists(tempDir).Should().BeFalse();
    }

    [Test]
    public async Task LogInferenceAsync_WhenEnabled_WritesInferenceLog()
    {
        await _sut.LogInferenceAsync("task123", "test prompt", "test response", "gpt-4o", "nodeA", 250);

        var files = Directory.GetFiles(_options.LogDirectory, "inference-*.jsonl");
        files.Should().NotBeEmpty();
        
        var content = await File.ReadAllTextAsync(files[0]);
        content.Should().Contain("task123");
        content.Should().Contain("test prompt");
        content.Should().Contain("test response");
        content.Should().Contain("gpt-4o");
        content.Should().Contain("nodeA");
        content.Should().Contain("250");
    }

    [Test]
    public async Task LogInferenceAsync_WhenEnabled_CreatesCorrectFileName()
    {
        await _sut.LogInferenceAsync("task456", "prompt", "response", "gpt-4o", "nodeB", 100);

        var expectedFileName = $"inference-{DateTimeOffset.UtcNow:yyyy-MM-dd}.jsonl";
        var expectedPath = Path.Combine(_options.LogDirectory, expectedFileName);
        
        File.Exists(expectedPath).Should().BeTrue();
    }

    [Test]
    public async Task LogInferenceAsync_WhenEnabled_CalculatesPromptAndResponseLength()
    {
        var prompt = "This is a test prompt";
        var response = "This is a test response";

        await _sut.LogInferenceAsync("task789", prompt, response, "gpt-4o", "nodeC", 150);

        var files = Directory.GetFiles(_options.LogDirectory, "inference-*.jsonl");
        var content = await File.ReadAllTextAsync(files[0]);
        
        content.Should().Contain($"\"PromptLength\": {prompt.Length}");
        content.Should().Contain($"\"ResponseLength\": {response.Length}");
    }

    [Test]
    public async Task LogChatMessageAsync_WhenDisabled_ReturnsEarly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"disabled-chat-test-{Guid.NewGuid()}");
        var disabledOptions = new FileLoggingOptions { Enabled = false, LogDirectory = tempDir };
        var optionsWrapper = Substitute.For<IOptions<FileLoggingOptions>>();
        optionsWrapper.Value.Returns(disabledOptions);
        var service = new FileLoggingService(_logger, optionsWrapper);

        await service.LogChatMessageAsync("conv1", "user", "sender1", "Hello", DateTimeOffset.UtcNow);

        Directory.Exists(tempDir).Should().BeFalse();
    }

    [Test]
    public async Task LogChatMessageAsync_WhenEnabled_WritesChatLog()
    {
        var timestamp = DateTimeOffset.UtcNow;

        await _sut.LogChatMessageAsync("conv123", "user", "sender456", "Hello, world!", timestamp);

        var files = Directory.GetFiles(_options.LogDirectory, "chats-*.jsonl");
        files.Should().NotBeEmpty();
        
        var content = await File.ReadAllTextAsync(files[0]);
        content.Should().Contain("conv123");
        content.Should().Contain("user");
        content.Should().Contain("sender456");
        content.Should().Contain("Hello, world!");
    }

    [Test]
    public async Task LogChatMessageAsync_WhenEnabled_CreatesCorrectFileName()
    {
        await _sut.LogChatMessageAsync("conv789", "assistant", "bot1", "Response", DateTimeOffset.UtcNow);

        var expectedFileName = $"chats-{DateTimeOffset.UtcNow:yyyy-MM-dd}.jsonl";
        var expectedPath = Path.Combine(_options.LogDirectory, expectedFileName);
        
        File.Exists(expectedPath).Should().BeTrue();
    }

    [Test]
    public async Task LogChatMessageAsync_WhenEnabled_IncludesAllFields()
    {
        var timestamp = new DateTimeOffset(2024, 1, 15, 10, 30, 45, TimeSpan.Zero);

        await _sut.LogChatMessageAsync("conversation1", "system", "sys", "System message", timestamp);

        var files = Directory.GetFiles(_options.LogDirectory, "chats-*.jsonl");
        var content = await File.ReadAllTextAsync(files[0]);
        
        content.Should().Contain("\"ConversationId\"");
        content.Should().Contain("conversation1");
        content.Should().Contain("\"Role\"");
        content.Should().Contain("system");
        content.Should().Contain("\"SenderId\"");
        content.Should().Contain("sys");
        content.Should().Contain("\"Message\"");
        content.Should().Contain("System message");
        content.Should().Contain("\"Timestamp\"");
    }

    [Test]
    public async Task LogChatMessageAsync_WhenEnabled_AppendsToExistingFile()
    {
        await _sut.LogChatMessageAsync("conv1", "user", "user1", "First message", DateTimeOffset.UtcNow);
        await _sut.LogChatMessageAsync("conv1", "assistant", "bot1", "Second message", DateTimeOffset.UtcNow);

        var files = Directory.GetFiles(_options.LogDirectory, "chats-*.jsonl");
        var content = await File.ReadAllTextAsync(files[0]);
        
        content.Should().Contain("First message");
        content.Should().Contain("Second message");
    }

    [Test]
    public async Task LogInferenceAsync_WhenEnabled_AppendsToExistingFile()
    {
        await _sut.LogInferenceAsync("task1", "prompt1", "response1", "gpt-4o", "nodeA", 100);
        await _sut.LogInferenceAsync("task2", "prompt2", "response2", "gpt-4o", "nodeB", 200);

        var files = Directory.GetFiles(_options.LogDirectory, "inference-*.jsonl");
        var content = await File.ReadAllTextAsync(files[0]);
        
        content.Should().Contain("task1");
        content.Should().Contain("task2");
    }

    [Test]
    public async Task LogInferenceAsync_WithCancellationToken_PassesTokenThrough()
    {
        using var cts = new CancellationTokenSource();

        await _sut.LogInferenceAsync("task1", "prompt", "response", "gpt-4o", "nodeA", 100, cts.Token);

        var files = Directory.GetFiles(_options.LogDirectory, "inference-*.jsonl");
        files.Should().NotBeEmpty();
    }

    [Test]
    public async Task LogChatMessageAsync_WithCancellationToken_PassesTokenThrough()
    {
        using var cts = new CancellationTokenSource();

        await _sut.LogChatMessageAsync("conv1", "user", "sender1", "message", DateTimeOffset.UtcNow, cts.Token);

        var files = Directory.GetFiles(_options.LogDirectory, "chats-*.jsonl");
        files.Should().NotBeEmpty();
    }

}
