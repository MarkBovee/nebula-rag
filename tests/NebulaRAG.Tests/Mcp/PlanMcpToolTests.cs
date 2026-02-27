using System.Text.Json;
using NebulaRAG.Core.Exceptions;
using NebulaRAG.Core.Services;
using NebulaRAG.Mcp.Services;
using Xunit;

namespace NebulaRAG.Tests.Mcp;

public class PlanMcpToolTests
{
    private readonly PlanService _planService;
    private readonly TaskService _taskService;
    private readonly PlanValidator _planValidator;
    private readonly SessionValidator _sessionValidator;
    private readonly PlanMcpTool _planMcpTool;

    public PlanMcpToolTests()
    {
        // Setup test services
        _planService = new PlanService(new PostgresRagStore("Host=localhost;Database=nebularag;Username=postgres;Password=postgres"), null);
        _taskService = new TaskService(new PostgresRagStore("Host=localhost;Database=nebularag;Username=postgres;Password=postgres"), null);
        _planValidator = new PlanValidator();
        _sessionValidator = new SessionValidator(_planService);
        _planMcpTool = new PlanMcpTool(_planService, _taskService, _planValidator, _sessionValidator);
    }

    [Fact]
    public async Task HandleCreatePlanAsync_ValidRequest_CreatesPlan()
    {
        var request = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planName"] = "Test Plan",
            ["projectId"] = "test-project-1",
            ["initialTasks"] = new JsonArray { "Task 1", "Task 2" }
        };

        var result = await _planMcpTool.HandleCreatePlanAsync(request);

        Assert.NotNull(result);
        Assert.Contains("planId", result);
        Assert.Contains("name", result);
        Assert.Contains("status", result);
        Assert.Contains("tasks", result);
    }

    [Fact]
    public async Task HandleGetPlanAsync_ValidRequest_ReturnsPlan()
    {
        // First create a plan
        var createRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planName"] = "Test Plan",
            ["projectId"] = "test-project-1",
            ["initialTasks"] = new JsonArray { "Task 1", "Task 2" }
        };

        var createdPlan = await _planMcpTool.HandleCreatePlanAsync(createRequest);
        var planId = createdPlan["planId"]?.GetValue<string>();

        var getRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planId"] = planId
        };

        var result = await _planMcpTool.HandleGetPlanAsync(getRequest);

        Assert.NotNull(result);
        Assert.Equal(planId, result["planId"]?.GetValue<string>());
        Assert.Contains("tasks", result);
    }

    [Fact]
    public async Task HandleListPlansAsync_ValidRequest_ReturnsPlans()
    {
        // Create a plan first
        var createRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planName"] = "Test Plan",
            ["projectId"] = "test-project-1",
            ["initialTasks"] = new JsonArray { "Task 1", "Task 2" }
        };

        await _planMcpTool.HandleCreatePlanAsync(createRequest);

        var listRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1"
        };

        var result = await _planMcpTool.HandleListPlansAsync(listRequest);

        Assert.NotNull(result);
        Assert.Contains("plans", result);
        var plans = result["plans"]?.AsArray();
        Assert.NotNull(plans);
        Assert.True(plans.Count > 0);
    }

    [Fact]
    public async Task HandleUpdatePlanAsync_ValidRequest_UpdatesPlan()
    {
        // Create a plan first
        var createRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planName"] = "Test Plan",
            ["projectId"] = "test-project-1",
            ["initialTasks"] = new JsonArray { "Task 1", "Task 2" }
        };

        var createdPlan = await _planMcpTool.HandleCreatePlanAsync(createRequest);
        var planId = createdPlan["planId"]?.GetValue<string>();

        var updateRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planId"] = planId,
            ["planName"] = "Updated Plan Name",
            ["status"] = "In Progress"
        };

        var result = await _planMcpTool.HandleUpdatePlanAsync(updateRequest);

        Assert.NotNull(result);
        Assert.Equal("Updated Plan Name", result["name"]?.GetValue<string>());
        Assert.Equal("In Progress", result["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task HandleCompleteTaskAsync_ValidRequest_CompletesTask()
    {
        // Create a plan with tasks first
        var createRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planName"] = "Test Plan",
            ["projectId"] = "test-project-1",
            ["initialTasks"] = new JsonArray { "Task 1", "Task 2" }
        };

        var createdPlan = await _planMcpTool.HandleCreatePlanAsync(createRequest);
        var planId = createdPlan["planId"]?.GetValue<string>();
        var tasks = createdPlan["tasks"]?.AsArray();
        var taskId = tasks?[0]?["taskId"]?.GetValue<string>();

        var completeRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planId"] = planId,
            ["taskId"] = taskId
        };

        var result = await _planMcpTool.HandleCompleteTaskAsync(completeRequest);

        Assert.NotNull(result);
        Assert.Equal("Completed", result["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task HandleUpdateTaskAsync_ValidRequest_UpdatesTask()
    {
        // Create a plan with tasks first
        var createRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planName"] = "Test Plan",
            ["projectId"] = "test-project-1",
            ["initialTasks"] = new JsonArray { "Task 1", "Task 2" }
        };

        var createdPlan = await _planMcpTool.HandleCreatePlanAsync(createRequest);
        var planId = createdPlan["planId"]?.GetValue<string>();
        var tasks = createdPlan["tasks"]?.AsArray();
        var taskId = tasks?[0]?["taskId"]?.GetValue<string>();

        var updateRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planId"] = planId,
            ["taskId"] = taskId,
            ["taskName"] = "Updated Task Name",
            ["status"] = "In Progress"
        };

        var result = await _planMcpTool.HandleUpdateTaskAsync(updateRequest);

        Assert.NotNull(result);
        Assert.Equal("Updated Task Name", result["name"]?.GetValue<string>());
        Assert.Equal("In Progress", result["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task HandleArchivePlanAsync_ValidRequest_ArchivesPlan()
    {
        // Create a plan first
        var createRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planName"] = "Test Plan",
            ["projectId"] = "test-project-1",
            ["initialTasks"] = new JsonArray { "Task 1", "Task 2" }
        };

        var createdPlan = await _planMcpTool.HandleCreatePlanAsync(createRequest);
        var planId = createdPlan["planId"]?.GetValue<string>();

        var archiveRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planId"] = planId
        };

        var result = await _planMcpTool.HandleArchivePlanAsync(archiveRequest);

        Assert.NotNull(result);
        Assert.Equal("Archived", result["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task HandleCreatePlanAsync_InvalidSession_ThrowsException()
    {
        var request = new JsonObject
        {
            ["sessionId"] = "",
            ["planName"] = "Test Plan",
            ["projectId"] = "test-project-1",
            ["initialTasks"] = new JsonArray { "Task 1", "Task 2" }
        };

        await Assert.ThrowsAsync<PlanException>(() => _planMcpTool.HandleCreatePlanAsync(request));
    }

    [Fact]
    public async Task HandleGetPlanAsync_InvalidSession_ThrowsException()
    {
        var request = new JsonObject
        {
            ["sessionId"] = "",
            ["planId"] = "non-existent-plan"
        };

        await Assert.ThrowsAsync<PlanException>(() => _planMcpTool.HandleGetPlanAsync(request));
    }

    [Fact]
    public async Task HandleListPlansAsync_InvalidSession_ThrowsException()
    {
        var request = new JsonObject
        {
            ["sessionId"] = ""
        };

        await Assert.ThrowsAsync<PlanException>(() => _planMcpTool.HandleListPlansAsync(request));
    }

    [Fact]
    public async Task HandleUpdatePlanAsync_InvalidSession_ThrowsException()
    {
        var request = new JsonObject
        {
            ["sessionId"] = "",
            ["planId"] = "non-existent-plan",
            ["planName"] = "Updated Plan Name",
            ["status"] = "In Progress"
        };

        await Assert.ThrowsAsync<PlanException>(() => _planMcpTool.HandleUpdatePlanAsync(request));
    }

    [Fact]
    public async Task HandleCompleteTaskAsync_InvalidSession_ThrowsException()
    {
        var request = new JsonObject
        {
            ["sessionId"] = "",
            ["planId"] = "non-existent-plan",
            ["taskId"] = "non-existent-task"
        };

        await Assert.ThrowsAsync<PlanException>(() => _planMcpTool.HandleCompleteTaskAsync(request));
    }

    [Fact]
    public async Task HandleUpdateTaskAsync_InvalidSession_ThrowsException()
    {
        var request = new JsonObject
        {
            ["sessionId"] = "",
            ["planId"] = "non-existent-plan",
            ["taskId"] = "non-existent-task",
            ["taskName"] = "Updated Task Name",
            ["status"] = "In Progress"
        };

        await Assert.ThrowsAsync<PlanException>(() => _planMcpTool.HandleUpdateTaskAsync(request));
    }

    [Fact]
    public async Task HandleArchivePlanAsync_InvalidSession_ThrowsException()
    {
        var request = new JsonObject
        {
            ["sessionId"] = "",
            ["planId"] = "non-existent-plan"
        };

        await Assert.ThrowsAsync<PlanException>(() => _planMcpTool.HandleArchivePlanAsync(request));
    }

    [Fact]
    public async Task HandleGetPlanAsync_InvalidPlan_ThrowsException()
    {
        var request = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planId"] = "non-existent-plan"
        };

        await Assert.ThrowsAsync<PlanException>(() => _planMcpTool.HandleGetPlanAsync(request));
    }

    [Fact]
    public async Task HandleUpdatePlanAsync_InvalidPlan_ThrowsException()
    {
        var request = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planId"] = "non-existent-plan",
            ["planName"] = "Updated Plan Name",
            ["status"] = "In Progress"
        };

        await Assert.ThrowsAsync<PlanException>(() => _planMcpTool.HandleUpdatePlanAsync(request));
    }

    [Fact]
    public async Task HandleCompleteTaskAsync_InvalidPlan_ThrowsException()
    {
        var request = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planId"] = "non-existent-plan",
            ["taskId"] = "non-existent-task"
        };

        await Assert.ThrowsAsync<PlanException>(() => _planMcpTool.HandleCompleteTaskAsync(request));
    }

    [Fact]
    public async Task HandleUpdateTaskAsync_InvalidPlan_ThrowsException()
    {
        var request = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planId"] = "non-existent-plan",
            ["taskId"] = "non-existent-task",
            ["taskName"] = "Updated Task Name",
            ["status"] = "In Progress"
        };

        await Assert.ThrowsAsync<PlanException>(() => _planMcpTool.HandleUpdateTaskAsync(request));
    }

    [Fact]
    public async Task HandleArchivePlanAsync_InvalidPlan_ThrowsException()
    {
        var request = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planId"] = "non-existent-plan"
        };

        await Assert.ThrowsAsync<PlanException>(() => _planMcpTool.HandleArchivePlanAsync(request));
    }

    [Fact]
    public async Task HandleCompleteTaskAsync_InvalidTask_ThrowsException()
    {
        // Create a plan first
        var createRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planName"] = "Test Plan",
            ["projectId"] = "test-project-1",
            ["initialTasks"] = new JsonArray { "Task 1", "Task 2" }
        };

        var createdPlan = await _planMcpTool.HandleCreatePlanAsync(createRequest);
        var planId = createdPlan["planId"]?.GetValue<string>();

        var completeRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planId"] = planId,
            ["taskId"] = "non-existent-task"
        };

        await Assert.ThrowsAsync<PlanException>(() => _planMcpTool.HandleCompleteTaskAsync(completeRequest));
    }

    [Fact]
    public async Task HandleUpdateTaskAsync_InvalidTask_ThrowsException()
    {
        // Create a plan first
        var createRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planName"] = "Test Plan",
            ["projectId"] = "test-project-1",
            ["initialTasks"] = new JsonArray { "Task 1", "Task 2" }
        };

        var createdPlan = await _planMcpTool.HandleCreatePlanAsync(createRequest);
        var planId = createdPlan["planId"]?.GetValue<string>();

        var updateRequest = new JsonObject
        {
            ["sessionId"] = "test-session-1",
            ["planId"] = planId,
            ["taskId"] = "non-existent-task",
            ["taskName"] = "Updated Task Name",
            ["status"] = "In Progress"
        };

        await Assert.ThrowsAsync<PlanException>(() => _planMcpTool.HandleUpdateTaskAsync(updateRequest));
    }
}