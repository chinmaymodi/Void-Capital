using Microsoft.AspNetCore.Mvc;
using VoidCapital.Api.Modules.Signals.DTOs;
using VoidCapital.Api.Modules.Signals.Services;
using VoidCapital.Api.Shared;

namespace VoidCapital.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class SignalsController : ControllerBase
{
    private readonly ISignalService _signalService;

    public SignalsController(ISignalService signalService)
    {
        _signalService = signalService;
    }

    [HttpGet("today/{userId:int}")]
    public async Task<ActionResult<ApiResponse<IEnumerable<SignalDto>>>> GetToday(int userId)
    {
        var signals = await _signalService.GetTodaySignalsAsync(userId);
        return Ok(ApiResponse<IEnumerable<SignalDto>>.Ok(signals));
    }

    [HttpPost("{signalId:int}/approve")]
    public async Task<ActionResult<ApiResponse<SignalDto>>> Approve(int signalId)
    {
        var signal = await _signalService.ApproveSignalAsync(signalId);
        return Ok(ApiResponse<SignalDto>.Ok(signal));
    }

    [HttpPost("{signalId:int}/reject")]
    public async Task<ActionResult<ApiResponse<SignalDto>>> Reject(int signalId)
    {
        var signal = await _signalService.RejectSignalAsync(signalId);
        return Ok(ApiResponse<SignalDto>.Ok(signal));
    }

    [HttpPost("batch-approve")]
    public async Task<ActionResult<ApiResponse<IEnumerable<SignalBatchResult>>>> BatchApprove(
        [FromBody] BatchSignalRequest request)
    {
        var results = await _signalService.BatchApproveAsync(request.Ids);
        return Ok(ApiResponse<IEnumerable<SignalBatchResult>>.Ok(results));
    }

    [HttpPost("batch-reject")]
    public async Task<ActionResult<ApiResponse<IEnumerable<SignalBatchResult>>>> BatchReject(
        [FromBody] BatchSignalRequest request)
    {
        var results = await _signalService.BatchRejectAsync(request.Ids);
        return Ok(ApiResponse<IEnumerable<SignalBatchResult>>.Ok(results));
    }
}
