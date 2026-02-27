using System.Text.Json;
using System.Text.Json.Nodes;
using NebulaRAG.Core.Exceptions;
using NebulaRAG.Core.Services;

namespace NebulaRAG.Mcp.Services;

public class SessionValidator
{
    private readonly PlanService _planService;

    public SessionValidator(PlanService planService)
    {
        _planService = planService;
    }

    public async Task ValidateSessionOwnershipAsync(string sessionId, string planId)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new PlanException("Session ID is required");

        if (!string.IsNullOrEmpty(planId))
        {
            var plan = await _planService.GetPlanAsync(sessionId, planId);
            if (plan.SessionId != sessionId)
            {
                throw new PlanException($"Access denied: Plan {planId} belongs to session {plan.SessionId}, not {sessionId}");
            }
        }
    }

    public async Task ValidateCreatePlanSessionAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new PlanException("Session ID is required");

        // Check if session already has an active plan
        var activePlan = await _planService.GetActivePlanAsync(sessionId);
        if (activePlan != null)
        {
            throw new PlanException($"Session {sessionId} already has an active plan. Only one active plan per session is allowed.");
        }
    }
}