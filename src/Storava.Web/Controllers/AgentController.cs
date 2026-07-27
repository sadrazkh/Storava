using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Storava.Web.Models;
using Storava.Web.Services;

namespace Storava.Web.Controllers;

/// <summary>
/// The only surface a companion Agent talks to, and it is deliberately tiny: an Agent pairs, and
/// that is all. Everything the Agent goes on to do — reading drives, walking a tree, acting on a
/// folder — happens between the Agent and the browser on the same machine, never through here.
/// <para>
/// No scan data, no path, and no file ever arrives at this controller. That is the same boundary
/// the rest of the server keeps, and pairing does not open a hole in it.
/// </para>
/// </summary>
[ApiController]
[AllowAnonymous]
// The Agent is a native process, not a browser: it holds no antiforgery cookie and could not
// produce a token. Redemption is protected by the code itself, which is single-use and expires.
[IgnoreAntiforgeryToken]
[Route("api/agent")]
public sealed class AgentController(
    IDevicePairingService pairing,
    ILogger<AgentController> logger) : ControllerBase
{
    [HttpPost("pair")]
    [EnableRateLimiting("account")]
    [ProducesResponseType<AgentPairResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<AgentPairProblem>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Pair(
        [FromBody] AgentPairRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AgentPairProblem("malformed_request", "The pairing request was malformed."));

        var result = await pairing.RedeemAsync(
            request.Code,
            request.PublicKey,
            request.DeviceName,
            cancellationToken);

        if (result.Succeeded)
        {
            return Ok(new AgentPairResponse(
                result.DeviceId,
                result.DisplayName,
                result.ChannelSecret,
                DateTimeOffset.UtcNow));
        }

        // Logged without the code: a rejected code is still a secret someone typed.
        logger.LogInformation("A pairing attempt was refused: {Reason}.", result.Failure);
        return BadRequest(Describe(result.Failure));
    }

    private static AgentPairProblem Describe(PairingFailure failure) => failure switch
    {
        PairingFailure.Expired => new(
            "expired",
            "That pairing code has expired. Generate a new one on your account page."),
        PairingFailure.AlreadyUsed => new(
            "already_used",
            "That pairing code has already been used. Each code pairs one computer."),
        PairingFailure.InvalidPublicKey => new(
            "invalid_key",
            "The agent presented a key this server cannot read."),
        PairingFailure.DuplicateDevice => new(
            "already_paired",
            "This computer is already paired. Remove it from your account page before pairing again."),
        _ => new(
            "unknown_code",
            "That pairing code was not recognised. Check it and try again.")
    };
}
