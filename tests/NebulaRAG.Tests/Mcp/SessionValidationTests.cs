using System.Text.Json;
using NebulaRAG.Core.Exceptions;
using NebulaRAG.Core.Services;
using NebulaRAG.Mcp.Services;
using Xunit;

namespace NebulaRAG.Tests.Mcp;

public class SessionValidationTests
{
    private readonly PlanService _planService;
    private readonly SessionValidator _sessionValidator;

    public SessionValidationTests()
    {
        // Setup test services
        _planService = new PlanService(new PostgresRagStore("Host=localhost;Database=nebularag;Username=postgres;Password=postgres"), null);
        _sessionValidator = new SessionValidator(_planService);
    }

    [Fact]
    public async Task ValidateSessionOwnershipAsync_ValidSessionAndPlan_Passes()
    {
        // Create a plan first
        var createRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planName"] = "Test Plan",
            ["projectId"] = "test-project-1",
            ["initialTasks"] = new JsonArray { "Task 1", "Task 2" }
        };

        var planMcpTool = new PlanMcpTool(new PlanService(new PostgresRagStore("Host=localhost;Database=nebularag;Username=postgres;Password=postgres"), null),
                                       new TaskService(new PostgresRagStore("Host=localhost;Database=nebularag;Username=postgres;Password=postgres"), null),
                                       new PlanValidator(),
                                       _sessionValidator);

        await planMcpTool.HandleCreatePlanAsync(createRequest);
        var planId = createRequest["planId"]?.GetValue<string>();

        await _sessionValidator.ValidateSessionOwnershipAsync("test-session-1", planId);
    }

    [Fact]
    public async Task ValidateSessionOwnershipAsync_InvalidSession_ThrowsException()
    {
        await Assert.ThrowsAsync<PlanException>(() => _sessionValidator.ValidateSessionOwnershipAsync("", "plan-123"));
    }

    [Fact]
    public async Task ValidateSessionOwnershipAsync_InvalidPlan_ThrowsException()
    {
        await Assert.ThrowsAsync<PlanException>(() => _sessionValidator.ValidateSessionOwnershipAsync("test-session-1", "non-existent-plan"));
    }

    [Fact]
    public async Task ValidateSessionOwnershipAsync_WrongSession_ThrowsException()
    {
        // Create a plan with session-1
        var createRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planName"] = "Test Plan",
            ["projectId"] = "test-project-1",
            ["initialTasks"] = new JsonArray { "Task 1", "Task 2" }
        };

        var planMcpTool = new PlanMcpTool(new PlanService(new PostgresRagStore("Host=localhost;Database=nebularag;Username=postgres;Password=postgres"), null),
                                       new TaskService(new PostgresRagStore("Host=localhost;Database=nebularag;Username=postgres;Password=postgres"), null),
                                       new PlanValidator(),
                                       _sessionValidator);

        await planMcpTool.HandleCreatePlanAsync(createRequest);
        var planId = createRequest["planId"]?.GetValue<string>();

        await Assert.ThrowsAsync<PlanException>(() => _sessionValidator.ValidateSessionOwnershipAsync("test-session-2", planId));
    }

    [Fact]
    public async Task ValidateCreatePlanSessionAsync_ValidSession_Passes()
    {
        await _sessionValidator.ValidateCreatePlanSessionAsync("test-session-1");
    }

    [Fact]
    public async Task ValidateCreatePlanSessionAsync_InvalidSession_ThrowsException()
    {
        await Assert.ThrowsAsync<PlanException>(() => _sessionValidator.ValidateCreatePlanSessionAsync(""));
    }

    [Fact]
    public async Task ValidateCreatePlanSessionAsync_MultipleActivePlans_ThrowsException()
    {
        // Create first plan
        var createRequest1 = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planName"] = "Test Plan 1",
            ["projectId"] = "test-project-1",
            ["initialTasks"] = new JsonArray { "Task 1", "Task 2" }
        };

        var planMcpTool1 = new PlanMcpTool(new PlanService(new PostgresRagStore("Host=localhost;Database=nebularag;Username=postgres;Password=postgres"), null),
                                        new TaskService(new PostgresRagStore("Host=localhost;Database=nebularag;Username=postgres;Password=postgres"), null),
                                        new PlanValidator(),
                                        _sessionValidator);

        await planMcpTool1.HandleCreatePlanAsync(createRequest1);

        // Try to create second plan with same session - should fail
        var createRequest2 = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planName"] = "Test Plan 2",
            ["projectId"] = "test-project-1",
            ["initialTasks"] = new JsonArray { "Task 3", "Task 4" }
        };

        var planMcpTool2 = new PlanMcpTool(new PlanService(new PostgresRagStore("Host=localhost;Database=nebularag;Username=postgres;Password=postgres"), null),
                                        new TaskService(new PostgresRagStore("Host=localhost;Database=nebularag;Username=postgres;Password=postgres"), null),
                                        new PlanValidator(),
                                        _sessionValidator);

        await Assert.ThrowsAsync<PlanException>(() => planMcpTool2.HandleCreatePlanAsync(createRequest2));
    }
}