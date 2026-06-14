using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Common;
using TransitInfoAPI.Services.Otp;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("otp")]
public class OtpController : ControllerBase
{
    private readonly OtpManagerService _otpManager;

    public OtpController(OtpManagerService otpManager)
    {
        _otpManager = otpManager;
    }

    [HttpPost("restart")]
    public async Task<ActionResult<OperationResult>> Restart(CancellationToken ct = default)
    {
        await _otpManager.RestartOtpAsync(ct);
        return Ok(OperationResult.Ok("OTP restart initiated. Graph build will take several minutes."));
    }
}
