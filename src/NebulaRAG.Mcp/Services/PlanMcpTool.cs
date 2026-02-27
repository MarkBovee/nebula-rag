using System.Text.Json;
using System.Text.Json.Nodes;
using NebulaRAG.Core.Exceptions;
using NebulaRAG.Core.Services;

namespace NebulaRAG.Mcp.Services;

public class PlanMcpTool
{
    private readonly PlanService _planService;
    private readonly TaskService _taskService;
    private readonly PlanValidator _planValidator;
    private readonly SessionValidator _sessionValidator;

    public PlanMcpTool(PlanService planService, TaskService taskService, PlanValidator planValidator, SessionValidator sessionValidator)
    {
        _planService = planService;
        _taskService = taskService;
        _planValidator = planValidator;
        _sessionValidator = sessionValidator;
    }

    public async Task<JsonObject> HandleCreatePlanAsync(JsonObject request)
    {
        var sessionId = request["sessionId"]?.GetValue<string>();
        var planName = request["planName"]?.GetValue<string>();
        var projectId = request["projectId"]?.GetValue<string>();
        var initialTasks = request["initialTasks"]?.AsArray();

        if (string.IsNullOrEmpty(sessionId))
            throw new PlanException(PlanException.SessionValidation.SessionRequired, "Session ID is required");

        await _sessionValidator.ValidateCreatePlanSessionAsync(sessionId);

        if (string.IsNullOrEmpty(planName))
            throw new PlanException("Plan name is required");
        if (string.IsNullOrEmpty(projectId))
            throw new PlanException("Project ID is required");

        var tasks = new List<string>();
        if (initialTasks != null)
        {
            foreach (var task in initialTasks)
            {
                if (task?.GetValue<string>() is string taskName)
                {
                    tasks.Add(taskName);
                }
            }
        }

        var plan = await _planService.CreatePlanAsync(sessionId, planName, projectId, tasks);
        return plan.ToJson();
    }

    public async Task<JsonObject> HandleGetPlanAsync(JsonObject request)
    {
        var sessionId = request["sessionId"]?.GetValue<string>();
        var planId = request["planId"]?.GetValue<string>();

        if (string.IsNullOrEmpty(sessionId))
            throw new PlanException(PlanException.SessionValidation.SessionRequired, "Session ID is required");
        if (string.IsNullOrEmpty(planId))
            throw new PlanException("Plan ID is required");

        await _sessionValidator.ValidateSessionOwnershipAsync(sessionId, planId);

        var plan = await _planService.GetPlanAsync(sessionId, planId);
        return plan.ToJson();
    }

    public async Task<JsonObject> HandleListPlansAsync(JsonObject request)
    {
        var sessionId = request["sessionId"]?.GetValue<string>();

        if (string.IsNullOrEmpty(sessionId))
            throw new PlanException(PlanException.SessionValidation.SessionRequired, "Session ID is required");

        await _sessionValidator.ValidateCreatePlanSessionAsync(sessionId);

        var plans = await _planService.ListPlansAsync(sessionId);
        var result = new JsonObject
        {
            ["plans"] = new JsonArray(plans.Select(p => p.ToJson()).ToArray())
        };
        return result;
    }

    public async Task<JsonObject> HandleUpdatePlanAsync(JsonObject request)
    {
        var sessionId = request["sessionId"]?.GetValue<string>();
        var planId = request["planId"]?.GetValue<string>();
        var planName = request["planName"]?.GetValue<string>();
        var status = request["status"]?.GetValue<string>();

        if (string.IsNullOrEmpty(sessionId))
            throw new PlanException(PlanException.SessionValidation.SessionRequired, "Session ID is required");
        if (string.IsNullOrEmpty(planId))
            throw new PlanException("Plan ID is required");

        await _sessionValidator.ValidateSessionOwnershipAsync(sessionId, planId);

        var plan = await _planService.UpdatePlanAsync(sessionId, planId, planName, status);
        return plan.ToJson();
    }

    public async Task<JsonObject> HandleCompleteTaskAsync(JsonObject request)
    {
        var sessionId = request["sessionId"]?.GetValue<string>();
        var planId = request["planId"]?.GetValue<string>();
        var taskId = request["taskId"]?.GetValue<string>();

        if (string.IsNullOrEmpty(sessionId))
            throw new PlanException(PlanException.SessionValidation.SessionRequired, "Session ID is required");
        if (string.IsNullOrEmpty(planId))
            throw new PlanException("Plan ID is required");
        if (string.IsNullOrEmpty(taskId))
            throw new PlanException("Task ID is required");

        await _sessionValidator.ValidateSessionOwnershipAsync(sessionId, planId);

        var task = await _taskService.CompleteTaskAsync(sessionId, planId, taskId);
        return task.ToJson();
    }

    public async Task<JsonObject> HandleUpdateTaskAsync(JsonObject request)
    {
        var sessionId = request["sessionId"]?.GetValue<string>();
        var planId = request["planId"]?.GetValue<string>();
        var taskId = request["taskId"]?.GetValue<string>();
        var taskName = request["taskName"]?.GetValue<string>();
        var status = request["status"]?.GetValue<string>();

        if (string.IsNullOrEmpty(sessionId))
            throw new PlanException(PlanException.SessionValidation.SessionRequired, "Session ID is required");
        if (string.IsNullOrEmpty(planId))
            throw new PlanException("Plan ID is required");
        if (string.IsNullOrEmpty(taskId))
            throw new PlanException("Task ID is required");

        await _sessionValidator.ValidateSessionOwnershipAsync(sessionId, planId);

        var task = await _taskService.UpdateTaskAsync(sessionId, planId, taskId, taskName, status);
        return task.ToJson();
    }

    public async Task<JsonObject> HandleArchivePlanAsync(JsonObject request)
    {
        var sessionId = request["sessionId"]?.GetValue<string>();
        var planId = request["planId"]?.GetValue<string>();

        if (string.IsNullOrEmpty(sessionId))
            throw new PlanException(PlanException.SessionValidation.SessionRequired, "Session ID is required");
        if (string.IsNullOrEmpty(planId))
            throw new PlanException("Plan ID is required");

        await _sessionValidator.ValidateSessionOwnershipAsync(sessionId, planId);

        var plan = await _planService.ArchivePlanAsync(sessionId, planId);
        return plan.ToJson();
    }
}